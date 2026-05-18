using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods;
using XivToolsUI.Services;

namespace XivToolsUI.ViewModels;

public partial class ModelImportViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private string  _sourcePath   = "";
    [ObservableProperty] private string  _gamePath     = "";
    [ObservableProperty] private string  _targetMod    = "";
    [ObservableProperty] private string  _statusMsg    = "Select a source model and destination";
    [ObservableProperty] private bool    _isBusy;
    [ObservableProperty] private bool    _canImport;
    [ObservableProperty] private string  _formatNote   = "";

    public ObservableCollection<string> InstalledMods { get; } = new();
    public ObservableCollection<string> SupportedFormats { get; } = new() { "OBJ", "FBX (requires assimp)" };

    public ModelImportViewModel(MainViewModel main)
    {
        _main = main;
        LoadMods();
    }

    private void LoadMods()
    {
        InstalledMods.Clear();
        var modsDir = _main.Settings.ModsPath;
        if (!Directory.Exists(modsDir)) return;
        foreach (var d in Directory.GetDirectories(modsDir).OrderBy(Path.GetFileName))
            InstalledMods.Add(Path.GetFileName(d)!);
        if (InstalledMods.Count > 0) TargetMod = InstalledMods[0];
    }

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var files = await GetTopLevel()?.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Select Source Model",
            AllowMultiple = false,
            FileTypeFilter = new[] {
                new FilePickerFileType("3D Models") { Patterns = new[] { "*.obj", "*.fbx", "*.db" } },
                new FilePickerFileType("OBJ") { Patterns = new[] { "*.obj" } },
                new FilePickerFileType("FBX") { Patterns = new[] { "*.fbx" } },
                new FilePickerFileType("DB (intermediate)") { Patterns = new[] { "*.db" } },
            }
        }) ?? Array.Empty<IStorageFile>();
        if (files.Count == 0) return;

        SourcePath = files[0].Path.LocalPath;
        var ext = Path.GetExtension(SourcePath).ToLower();
        FormatNote = ext switch {
            ".obj" => "OBJ format supported natively",
            ".fbx" => "FBX requires: sudo pacman -S assimp python-pyassimp",
            ".db"  => "Intermediate DB format - imports directly",
            _      => ""
        };
        UpdateCanImport();
    }

    partial void OnGamePathChanged(string v)  => UpdateCanImport();
    partial void OnSourcePathChanged(string v) => UpdateCanImport();

    private void UpdateCanImport() =>
        CanImport = File.Exists(SourcePath) && !string.IsNullOrWhiteSpace(GamePath);

    [RelayCommand]
    private async Task ImportToGameAsync()
    {
        if (!CanImport || !FrameworkService.Instance.IsInitialized) {
            StatusMsg = "Game cache not initialized or missing fields"; return;
        }
        IsBusy = true;
        StatusMsg = $"Importing {Path.GetFileName(SourcePath)}...";
        _main.SetStatus(StatusMsg);
        try {
            await Task.Run(async () => {
                var tx = await ModTransaction.BeginTransaction(writeEnabled: true);
                try {
                    await Mdl.ImportModel(SourcePath, GamePath.Trim(), null, tx);
                    await ModTransaction.CommitTransaction(tx);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        StatusMsg = $"Imported to game: {GamePath}";
                        _main.SetStatus(StatusMsg);
                    });
                } catch {
                    await ModTransaction.CancelTransaction(tx, true);
                    throw;
                }
            });
        } catch (Exception ex) {
            StatusMsg = $"Import error: {ex.Message}";
            _main.SetStatus(StatusMsg);
        }
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ImportToModAsync()
    {
        if (!CanImport) return;
        if (string.IsNullOrWhiteSpace(TargetMod)) { StatusMsg = "Select a target mod"; return; }

        IsBusy = true;
        StatusMsg = $"Converting {Path.GetFileName(SourcePath)} for mod...";
        _main.SetStatus(StatusMsg);
        try {
            await Task.Run(async () => {
                // Export to DB first, then save the DB into the mod
                var tmpDb = Path.Combine(Path.GetTempPath(), "xivtools_import_model.db");

                // Use the framework to load the OBJ and convert to the game's MDL format
                // then save as uncompressed MDL into the mod folder
                var rtx = ModTransaction.BeginReadonlyTransaction();
                var tx  = await ModTransaction.BeginTransaction(writeEnabled: false);

                // Get existing MDL data to use as template for import settings
                byte[]? existingMdl = null;
                try { existingMdl = await rtx.ReadFile(GamePath.Trim()); } catch { }

                // Import model - this uses the converter under the hood
                // Save result into mod folder
                var modsDir   = _main.Settings.ModsPath;
                var outPath   = Path.Combine(modsDir, TargetMod,
                    GamePath.Trim().Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                await ModTransaction.CancelTransaction(tx, false);

                // Load the source OBJ/FBX via TTModel, then export as uncompressed MDL to mod folder
                var ttm = await xivModdingFramework.Models.DataContainers.TTModel.LoadFromFile(SourcePath);
                ttm.Source = GamePath.Trim();
                await Mdl.ExportTTModelToFile(ttm, outPath, 1, null, rtx);

                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    StatusMsg = $"Saved to {TargetMod}/{Path.GetFileName(outPath)}";
                    _main.SetStatus(StatusMsg);
                });
            });
            _ = PenumbraService.Instance.TriggerRedrawAsync();
        } catch (Exception ex) {
            StatusMsg = $"Import error: {ex.Message}";
            _main.SetStatus(StatusMsg);
        }
        IsBusy = false;
    }

    private Avalonia.Controls.TopLevel? GetTopLevel() =>
        Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            ? Avalonia.Controls.TopLevel.GetTopLevel(d.MainWindow) : null;
}
