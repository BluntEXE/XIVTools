using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xivModdingFramework.Cache;
using xivModdingFramework.Items.Categories;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using XivToolsUI.Services;

namespace XivToolsUI.ViewModels;

public partial class GameFileEntry : ObservableObject
{
    public string GamePath  { get; init; } = "";
    public string FileName  => Path.GetFileName(GamePath);
    public string Extension => Path.GetExtension(GamePath).TrimStart('.').ToUpper();
    public string Directory => Path.GetDirectoryName(GamePath) ?? "";
}

public partial class SearchResultEntry : ObservableObject
{
    public string Name     { get; init; } = "";
    public string Category { get; init; } = "";
    public string RootId   { get; init; } = "";
    public IItem? Item     { get; init; }
}

public partial class ItemBrowserViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private string  _nameQuery    = "";
    [ObservableProperty] private string  _rootQuery    = "";
    [ObservableProperty] private bool    _isBusy;
    [ObservableProperty] private string  _statusMsg    = "Search by item name or enter a root ID directly";
    [ObservableProperty] private GameFileEntry?    _selectedFile;
    [ObservableProperty] private SearchResultEntry? _selectedResult;
    [ObservableProperty] private bool    _showResults  = true;

    public ObservableCollection<SearchResultEntry> SearchResults { get; } = new();
    public ObservableCollection<GameFileEntry>     Files         { get; } = new();

    public ItemBrowserViewModel(MainViewModel main) => _main = main;

    // ── Name search ───────────────────────────────────────────
    [RelayCommand]
    private async Task SearchByNameAsync()
    {
        if (string.IsNullOrWhiteSpace(NameQuery)) return;
        if (!FrameworkService.Instance.IsInitialized) {
            StatusMsg = "Game cache not initialized - check Settings"; return;
        }

        IsBusy = true;
        ShowResults = true;
        SearchResults.Clear();
        Files.Clear();
        _main.SetStatus($"Searching for '{NameQuery}'...");

        try {
            var q = NameQuery.Trim();
            await Task.Run(async () => {
                var results = new System.Collections.Generic.List<SearchResultEntry>();

                // Search across categories in parallel
                await System.Threading.Tasks.Task.WhenAll(
                    SearchCategory(() => new Gear().GetGearList(q), "Gear", results),
                    SearchCategory(() => new Character().GetCharacterList(q), "Character", results),
                    SearchCategory(() => new Companions().GetMinionList(q), "Minion", results),
                    SearchCategory(() => new Companions().GetMountList(q), "Mount", results),
                    SearchCategory(() => new Housing().GetFurnitureList(q), "Housing", results)
                );

                var sorted = results.OrderBy(r => r.Category).ThenBy(r => r.Name).ToList();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    foreach (var r in sorted) SearchResults.Add(r);
                    StatusMsg = sorted.Count > 0
                        ? $"{sorted.Count} results for '{q}'"
                        : $"No results for '{q}'";
                    _main.SetStatus(StatusMsg);
                });
            });
        } catch (Exception ex) {
            StatusMsg = $"Search error: {ex.Message}";
        }
        IsBusy = false;
    }

    private static async Task SearchCategory<T>(
        Func<Task<System.Collections.Generic.List<T>>> getter,
        string category,
        System.Collections.Generic.List<SearchResultEntry> results) where T : IItem
    {
        try {
            var items = await getter();
            lock (results) {
                foreach (var item in items) {
                    results.Add(new SearchResultEntry {
                        Name     = item.Name,
                        Category = category,
                        RootId   = TryGetRootString(item),
                        Item     = item,
                    });
                }
            }
        } catch { /* category may not be available */ }
    }

    private static string TryGetRootString(IItem item)
    {
        try {
            var rootInfo = xivModdingFramework.Cache.ItemRootExtensions.GetRootInfo(item);
            if (rootInfo.IsValid()) return rootInfo.ToString();
        } catch { }
        return "";
    }

    // When a search result is selected, load its files via the proper GetRootInfo extension
    partial void OnSelectedResultChanged(SearchResultEntry? value)
    {
        if (value == null) return;
        ShowResults = false;
        _ = LoadFilesForItemAsync(value);
    }

    private async Task LoadFilesForItemAsync(SearchResultEntry result)
    {
        IsBusy = true;
        Files.Clear();
        _main.SetStatus($"Loading files for {result.Name}...");
        try {
            await Task.Run(async () => {
                var files = new System.Collections.Generic.List<string>();
                if (result.Item != null) {
                    // Use the framework's proper GetRootInfo extension method
                    var rootInfo = xivModdingFramework.Cache.ItemRootExtensions.GetRootInfo(result.Item);
                    if (rootInfo.IsValid()) {
                        var root = new XivDependencyRoot(rootInfo);
                        files = (await root.GetAllFiles()).ToList();
                    }
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    foreach (var f in files.OrderBy(x => x))
                        Files.Add(new GameFileEntry { GamePath = f });
                    StatusMsg = files.Count > 0
                        ? $"{files.Count} files for {result.Name}"
                        : $"Could not resolve files for {result.Name} — try root ID lookup";
                    _main.SetStatus(StatusMsg);
                });
            });
        } catch (Exception ex) {
            StatusMsg = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    // ── Direct root lookup ────────────────────────────────────
    [RelayCommand]
    private async Task LookupRootAsync()
    {
        if (string.IsNullOrWhiteSpace(RootQuery)) return;
        if (!FrameworkService.Instance.IsInitialized) {
            StatusMsg = "Game cache not initialized - check Settings"; return;
        }

        IsBusy = true;
        ShowResults = false;
        Files.Clear();
        _main.SetStatus($"Looking up {RootQuery}...");
        try {
            await Task.Run(async () => {
                var rootInfo = XivCache.GetFileNameRootInfo(RootQuery.Trim(), true);
                if (!rootInfo.IsValid()) {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        StatusMsg = $"'{RootQuery}' is not a valid root ID");
                    return;
                }
                var root  = new XivDependencyRoot(rootInfo);
                var files = await root.GetAllFiles();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    foreach (var f in files.OrderBy(x => x))
                        Files.Add(new GameFileEntry { GamePath = f });
                    StatusMsg = $"{Files.Count} files for {RootQuery}";
                    _main.SetStatus(StatusMsg);
                });
            });
        } catch (Exception ex) {
            StatusMsg = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    [RelayCommand] public void BackToResults() { ShowResults = true; SelectedResult = null; }

    // ── Quick open ────────────────────────────────────────────
    [RelayCommand]
    private async Task OpenInTextureViewerAsync()
    {
        if (SelectedFile == null) return;
        var tmpPath = await ExtractToTempAsync(SelectedFile.GamePath);
        if (tmpPath == null) return;
        _main.TextureViewModel.TexturePath = tmpPath;
        _main.NavigateToTextureCommand.Execute(null);
        await _main.TextureViewModel.LoadTextureCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task OpenInMaterialEditorAsync()
    {
        if (SelectedFile == null || !SelectedFile.GamePath.EndsWith(".mtrl")) return;
        var tmpPath = await ExtractToTempAsync(SelectedFile.GamePath);
        if (tmpPath == null) return;
        _main.MaterialEditorViewModel.FilePath = tmpPath;
        _main.NavigateToMaterialEditorCommand.Execute(null);
        // LoadFromFile is internal, trigger via setting FilePath and calling load
        await _main.MaterialEditorViewModel.LoadFromFilePublicAsync();
    }

    private async Task<string?> ExtractToTempAsync(string gamePath)
    {
        if (!FrameworkService.Instance.IsInitialized) return null;
        try {
            return await Task.Run(async () => {
                var rtx  = ModTransaction.BeginReadonlyTransaction();
                var data = await rtx.ReadFile(gamePath);
                var tmp  = Path.Combine(Path.GetTempPath(), "xivtools_" + Path.GetFileName(gamePath));
                File.WriteAllBytes(tmp, data);
                return tmp;
            });
        } catch (Exception ex) {
            _main.SetStatus($"Extract error: {ex.Message}");
            return null;
        }
    }

    // ── File actions ──────────────────────────────────────────
    [RelayCommand]
    private async Task ExtractFileAsync()
    {
        if (SelectedFile == null || !FrameworkService.Instance.IsInitialized) return;
        var ext  = Path.GetExtension(SelectedFile.GamePath).TrimStart('.');
        var name = Path.GetFileNameWithoutExtension(SelectedFile.FileName);
        var (defaultExt, filter) = ext switch {
            "tex"  => ("png",  new FilePickerFileType("PNG Image")     { Patterns = new[] { "*.png" } }),
            "mdl"  => ("mdl",  new FilePickerFileType("MDL Model")     { Patterns = new[] { "*.mdl" } }),
            "mtrl" => ("mtrl", new FilePickerFileType("MTRL Material") { Patterns = new[] { "*.mtrl" } }),
            _      => (ext,    new FilePickerFileType("Raw File")      { Patterns = new[] { $"*.{ext}" } })
        };
        var file = await GetTopLevel()?.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = $"Extract {SelectedFile.FileName}", SuggestedFileName = $"{name}.{defaultExt}",
            DefaultExtension = defaultExt, FileTypeChoices = new[] { filter }
        });
        if (file == null) return;
        IsBusy = true;
        _main.SetStatus($"Extracting {SelectedFile.FileName}...");
        try {
            await Task.Run(async () => {
                var rtx    = ModTransaction.BeginReadonlyTransaction();
                var srcExt = Path.GetExtension(SelectedFile.GamePath).ToLower();
                if (srcExt == ".tex") {
                    var data = await rtx.ReadFile(SelectedFile.GamePath);
                    var tex  = xivModdingFramework.Textures.DataContainers.XivTex.FromUncompressedTex(data);
                    await tex.SaveAs(file.Path.LocalPath);
                } else {
                    File.WriteAllBytes(file.Path.LocalPath, await rtx.ReadFile(SelectedFile.GamePath));
                }
            });
            _main.SetStatus($"Extracted → {Path.GetFileName(file.Path.LocalPath)}");
            StatusMsg = $"Extracted → {file.Path.LocalPath}";
        } catch (Exception ex) {
            StatusMsg = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedFile == null) return;
        var clipboard = GetTopLevel()?.Clipboard;
        if (clipboard != null) _ = clipboard.GetType().GetMethod("SetTextAsync")
            ?.Invoke(clipboard, new object[] { SelectedFile.GamePath });
        _main.SetStatus($"Copied: {SelectedFile.GamePath}");
    }

    private Avalonia.Controls.TopLevel? GetTopLevel() =>
        Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            ? Avalonia.Controls.TopLevel.GetTopLevel(d.MainWindow) : null;
}
