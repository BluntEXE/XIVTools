using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivToolsUI.Services;

namespace XivToolsUI.ViewModels;

public partial class ConflictEntry : ObservableObject
{
    public string GamePath    { get; init; } = "";
    public string FileName    => Path.GetFileName(GamePath);

    /// <summary>Mods that provide this file, sorted highest priority first. (Name, Priority, FolderName)</summary>
    public List<(string Name, int Priority, string Folder)> ModsByPriority { get; init; } = new();

    public string Winner      => ModsByPriority.Count > 0 ? $"{ModsByPriority[0].Name} (p{ModsByPriority[0].Priority})" : "";
    public string Overridden  => string.Join(", ", ModsByPriority.Skip(1).Select(m => $"{m.Name} (p{m.Priority})"));
    public int    Count       => ModsByPriority.Count;

    /// <summary>True when 2+ mods share the same highest priority - genuinely undefined.</summary>
    public bool   IsTrueConflict =>
        ModsByPriority.Count >= 2 &&
        ModsByPriority[0].Priority == ModsByPriority[1].Priority;

    public string ConflictType => IsTrueConflict ? "Conflict" : "Override";
    public string AllMods      => string.Join(" → ", ModsByPriority.Select(m => $"{m.Name} (p{m.Priority})"));
}

public partial class ConflictViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _statusMsg = "Click Scan to detect mod conflicts";
    [ObservableProperty] private int    _trueConflictCount;
    [ObservableProperty] private int    _overrideCount;
    [ObservableProperty] private int    _affectedModCount;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool   _showOverridesOnly;
    [ObservableProperty] private ConflictEntry? _selectedConflict;

    public ObservableCollection<ConflictEntry> AllEntries { get; } = new();

    public ObservableCollection<ConflictEntry> FilteredEntries
    {
        get {
            var src = AllEntries.AsEnumerable();
            if (!ShowOverridesOnly) src = src.Where(e => e.IsTrueConflict);
            if (!string.IsNullOrWhiteSpace(SearchText))
                src = src.Where(e =>
                    e.GamePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    e.AllMods.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            return new ObservableCollection<ConflictEntry>(src);
        }
    }

    partial void OnSearchTextChanged(string v)     => OnPropertyChanged(nameof(FilteredEntries));
    partial void OnShowOverridesOnlyChanged(bool v) => OnPropertyChanged(nameof(FilteredEntries));

    public ConflictViewModel(MainViewModel main) => _main = main;

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true;
        AllEntries.Clear();
        StatusMsg = "Loading mod priorities from Penumbra collection...";
        _main.SetStatus(StatusMsg);

        try {
            var modsDir = _main.Settings.ModsPath;
            if (!Directory.Exists(modsDir)) {
                StatusMsg = "Mods path not configured - check Settings";
                IsBusy = false; return;
            }

            // Step 1: load priorities AND enabled state - only enabled mods matter
            // Penumbra silently ignores disabled mods entirely
            var priorities    = await Task.Run(() => PenumbraService.Instance.LoadPriorities());
            var enabledStates = await Task.Run(() => PenumbraService.Instance.LoadEnabledStates());

            StatusMsg = "Scanning mod file lists...";

            // Step 2: map game path → list of (modName, priority)
            var pathToMods = await Task.Run(() => {
                var map = new Dictionary<string, List<(string Name, int Priority, string Folder)>>(StringComparer.OrdinalIgnoreCase);

                foreach (var modDir in Directory.GetDirectories(modsDir)) {
                    var folderName = Path.GetFileName(modDir);

                    // Only include mods that are in Penumbra's collection AND enabled.
                    // If TryGetValue returns false the folder isn't tracked at all - skip it.
                    if (!enabledStates.TryGetValue(folderName, out bool enabled) || !enabled)
                        continue;

                    var metaPath = Path.Combine(modDir, "meta.json");
                    if (!File.Exists(metaPath)) continue;

                    // Use display name for UI but folder name as dedup key
                    string displayName;
                    try {
                        var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
                        displayName = doc.RootElement.TryGetProperty("Name", out var n)
                            ? n.GetString() ?? folderName : folderName;
                    } catch { displayName = folderName; }

                    priorities.TryGetValue(folderName, out int priority);

                    // Collect all game paths provided by this mod across ALL json files,
                    // then add to the map once - prevents options within the same mod
                    // being counted as conflicts with each other.
                    var thisModPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var jsonFile in Directory.GetFiles(modDir, "*.json")) {
                        try {
                            var doc = JsonDocument.Parse(File.ReadAllText(jsonFile));
                            CollectModPaths(doc.RootElement, thisModPaths);
                        } catch { }
                    }

                    // Now add this mod's files to the global map.
                    // Key for dedup = folderName (unique on disk).
                    // Value stored = displayName (UI label).
                    // We track added folderNames in a separate set to avoid the mismatch
                    // where folderName != displayName causes the check to always fail.
                    foreach (var gamePath in thisModPaths) {
                        if (!map.TryGetValue(gamePath, out var list)) {
                            list = new();
                            map[gamePath] = list;
                        }
                        // Dedup by folder name (unique) - two installs of same mod show as separate entries
                        if (!list.Any(m => m.Folder.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
                            list.Add((displayName, priority, folderName));
                    }
                }
                return map;
            });

            // Step 3: only entries with 2+ mods
            var entries = pathToMods
                .Where(kv => kv.Value.Count >= 2)
                .Select(kv => {
                    var sorted = kv.Value
                        .OrderByDescending(m => m.Priority)
                        .ThenBy(m => m.Name)
                        .ToList();
                    return new ConflictEntry {
                        GamePath       = kv.Key,
                        ModsByPriority = sorted,
                    };
                })
                .OrderByDescending(e => e.IsTrueConflict)   // true conflicts first
                .ThenByDescending(e => e.Count)
                .ThenBy(e => e.GamePath)
                .ToList();

            foreach (var e in entries) AllEntries.Add(e);

            TrueConflictCount = entries.Count(e => e.IsTrueConflict);
            OverrideCount     = entries.Count(e => !e.IsTrueConflict);
            AffectedModCount  = entries
                .SelectMany(e => e.ModsByPriority.Select(m => m.Folder))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();

            OnPropertyChanged(nameof(FilteredEntries));

            StatusMsg = TrueConflictCount == 0
                ? $"No true conflicts — {OverrideCount} deliberate overrides (different priorities)"
                : $"{TrueConflictCount} same-priority conflicts · {OverrideCount} deliberate overrides";
            _main.SetStatus(StatusMsg);
        } catch (Exception ex) {
            StatusMsg = $"Scan error: {ex.Message}";
        }
        IsBusy = false;
    }

    /// <summary>
    /// Recursively collect all game paths listed in "Files" dictionaries into a set.
    /// Using a set means options within the same mod that cover the same game file
    /// are deduplicated - only one entry per game path per mod.
    /// </summary>
    private static void CollectModPaths(JsonElement el, HashSet<string> paths)
    {
        if (el.ValueKind == JsonValueKind.Object) {
            foreach (var prop in el.EnumerateObject()) {
                if (prop.Name == "Files" && prop.Value.ValueKind == JsonValueKind.Object) {
                    foreach (var file in prop.Value.EnumerateObject())
                        if (!string.IsNullOrWhiteSpace(file.Name))
                            paths.Add(file.Name);
                } else {
                    CollectModPaths(prop.Value, paths);
                }
            }
        } else if (el.ValueKind == JsonValueKind.Array) {
            foreach (var item in el.EnumerateArray())
                CollectModPaths(item, paths);
        }
    }
}
