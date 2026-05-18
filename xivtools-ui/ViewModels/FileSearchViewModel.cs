using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XivToolsUI.ViewModels;

public partial class FileSearchResult : ObservableObject
{
    public string ModName    { get; init; } = "";
    public string ModFolder  { get; init; } = "";
    public string GamePath   { get; init; } = "";
    public string LocalPath  { get; init; } = "";
    public int    Priority   { get; init; }
    public bool   Enabled    { get; init; }
    public string Status     => Enabled ? $"Enabled (p{Priority})" : "Disabled";
}

public partial class FileSearchViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    // Pre-built index: game path → list of (ModName, ModFolder, LocalPath, Priority, Enabled)
    private Dictionary<string, List<(string ModName, string Folder, string Local, int Priority, bool Enabled)>> _index = new();
    private bool _indexBuilt;

    [ObservableProperty] private string  _query       = "";
    [ObservableProperty] private bool    _isBusy;
    [ObservableProperty] private string  _statusMsg   = "Enter a game path or keyword to search";
    [ObservableProperty] private bool    _indexing;
    [ObservableProperty] private int     _indexedMods;

    public ObservableCollection<FileSearchResult> Results { get; } = new();

    public FileSearchViewModel(MainViewModel main) => _main = main;

    // Build index on first use or on demand
    [RelayCommand]
    private async Task RebuildIndexAsync()
    {
        _indexBuilt = false;
        await EnsureIndexAsync();
    }

    private async Task EnsureIndexAsync()
    {
        if (_indexBuilt) return;
        Indexing = true;
        StatusMsg = "Building file index...";
        _main.SetStatus(StatusMsg);

        var modsDir      = _main.Settings.ModsPath;
        var enabledMap   = await Task.Run(() => Services.PenumbraService.Instance.LoadEnabledStates());
        var priorityMap  = await Task.Run(() => Services.PenumbraService.Instance.LoadPriorities());

        _index = await Task.Run(() => {
            var idx = new Dictionary<string, List<(string, string, string, int, bool)>>(
                StringComparer.OrdinalIgnoreCase);
            int count = 0;

            foreach (var modDir in Directory.GetDirectories(modsDir)) {
                var folder = Path.GetFileName(modDir);
                enabledMap.TryGetValue(folder, out bool enabled);
                priorityMap.TryGetValue(folder, out int priority);

                string modName;
                try {
                    var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(modDir, "meta.json")));
                    modName = meta.RootElement.TryGetProperty("Name", out var n)
                        ? n.GetString() ?? folder : folder;
                } catch { modName = folder; }

                // Scan all group JSONs for Files dicts
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var json in Directory.GetFiles(modDir, "*.json")) {
                    try {
                        var doc = JsonDocument.Parse(File.ReadAllText(json));
                        CollectFiles(doc.RootElement, modName, folder, priority, enabled, idx, seen);
                    } catch { }
                }
                count++;
            }
            return idx;
        });

        IndexedMods = _index.Values.SelectMany(v => v).Select(v => v.Item2)
                           .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _indexBuilt = true;
        Indexing    = false;
        StatusMsg   = $"Index ready — {_index.Count:N0} files across {IndexedMods} mods";
        _main.SetStatus(StatusMsg);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) return;
        IsBusy = true;
        Results.Clear();
        await EnsureIndexAsync();

        var q = Query.Trim();
        await Task.Run(() => {
            // Match on game path substring (case-insensitive)
            var hits = _index
                .Where(kv => kv.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key)
                .Take(500)
                .ToList();

            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                foreach (var (path, mods) in hits) {
                    foreach (var m in mods.OrderByDescending(x => x.Priority)) {
                        Results.Add(new FileSearchResult {
                            ModName   = m.ModName,
                            ModFolder = m.Folder,
                            GamePath  = path,
                            LocalPath = m.Local,
                            Priority  = m.Priority,
                            Enabled   = m.Enabled,
                        });
                    }
                }
                StatusMsg = Results.Count > 0
                    ? $"{hits.Count} files matched · {Results.Count} entries"
                    : $"No files matched '{q}'";
                _main.SetStatus(StatusMsg);
            });
        });
        IsBusy = false;
    }

    private static void CollectFiles(JsonElement el, string modName, string folder,
        int priority, bool enabled,
        Dictionary<string, List<(string, string, string, int, bool)>> idx,
        HashSet<string> seen)
    {
        if (el.ValueKind == JsonValueKind.Object) {
            foreach (var prop in el.EnumerateObject()) {
                if (prop.Name == "Files" && prop.Value.ValueKind == JsonValueKind.Object) {
                    foreach (var file in prop.Value.EnumerateObject()) {
                        if (string.IsNullOrWhiteSpace(file.Name)) continue;
                        if (seen.Contains(file.Name)) continue;
                        seen.Add(file.Name);
                        var local = file.Value.ValueKind == JsonValueKind.String
                            ? file.Value.GetString() ?? "" : "";
                        if (!idx.TryGetValue(file.Name, out var list)) {
                            list = new();
                            idx[file.Name] = list;
                        }
                        list.Add((modName, folder, local, priority, enabled));
                    }
                } else {
                    CollectFiles(prop.Value, modName, folder, priority, enabled, idx, seen);
                }
            }
        } else if (el.ValueKind == JsonValueKind.Array) {
            foreach (var item in el.EnumerateArray())
                CollectFiles(item, modName, folder, priority, enabled, idx, seen);
        }
    }
}
