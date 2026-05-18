using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xivModdingFramework.Mods;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.FileTypes;
using XivToolsUI.Services;

namespace XivToolsUI.ViewModels;

public partial class TextureImportViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    // Source (new texture to import)
    [ObservableProperty] private string  _sourcePath  = "";
    [ObservableProperty] private Bitmap? _sourcePreview;
    [ObservableProperty] private string  _sourceInfo  = "";

    // Destination
    [ObservableProperty] private string  _gamePath    = "";  // e.g. chara/equipment/e6071/texture/...
    [ObservableProperty] private string  _targetMod   = "";  // mod folder name to write into
    [ObservableProperty] private string  _targetMode  = "mod"; // "mod" or "game"

    // State
    [ObservableProperty] private bool    _isBusy;
    [ObservableProperty] private string  _statusMsg   = "Select a source image and a destination to import";
    [ObservableProperty] private bool    _canImport;

    public ObservableCollection<string> InstalledMods { get; } = new();

    public TextureImportViewModel(MainViewModel main)
    {
        _main = main;
        LoadInstalledMods();
    }

    private void LoadInstalledMods()
    {
        InstalledMods.Clear();
        var modsDir = _main.Settings.ModsPath;
        if (!Directory.Exists(modsDir)) return;
        foreach (var dir in Directory.GetDirectories(modsDir).OrderBy(Path.GetFileName))
            InstalledMods.Add(Path.GetFileName(dir));
        if (InstalledMods.Count > 0) TargetMod = InstalledMods[0];
    }

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var files = await GetTopLevel()?.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Select Source Image",
            AllowMultiple = false,
            FileTypeFilter = new[] {
                new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.dds", "*.bmp", "*.tga" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        }) ?? Array.Empty<IStorageFile>();
        if (files.Count == 0) return;
        SourcePath = files[0].Path.LocalPath;
        await LoadSourcePreviewAsync();
    }

    private async Task LoadSourcePreviewAsync()
    {
        if (!File.Exists(SourcePath)) return;
        IsBusy = true;
        try {
            await Task.Run(async () => {
                var ext = Path.GetExtension(SourcePath).ToLower();
                if (ext == ".dds") {
                    // Convert DDS preview via framework
                    var texData = Tex.DDSToUncompressedTex(SourcePath);
                    var xivTex  = XivTex.FromUncompressedTex(texData);
                    var tmpPng  = Path.Combine(Path.GetTempPath(), "xivtools_import_preview.png");
                    await xivTex.SaveAs(tmpPng);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        SourcePreview = new Bitmap(tmpPng);
                        SourceInfo = $"{xivTex.Width}×{xivTex.Height} {xivTex.TextureFormat} {xivTex.MipMapCount} mips";
                    });
                } else {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        SourcePreview = new Bitmap(SourcePath);
                        SourceInfo    = $"{SourcePreview.PixelSize.Width}×{SourcePreview.PixelSize.Height} {ext.TrimStart('.').ToUpper()}";
                    });
                }
            });
        } catch (Exception ex) {
            SourceInfo = $"Preview error: {ex.Message}";
        }
        IsBusy = false;
        UpdateCanImport();
    }

    partial void OnGamePathChanged(string v)   => UpdateCanImport();
    partial void OnSourcePathChanged(string v) => UpdateCanImport();
    partial void OnTargetModChanged(string v)  => UpdateCanImport();

    private void UpdateCanImport() =>
        CanImport = File.Exists(SourcePath) && !string.IsNullOrWhiteSpace(GamePath) &&
                    (TargetMode == "game" || !string.IsNullOrWhiteSpace(TargetMod));

    // ── Import to Penumbra mod folder ─────────────────────────
    [RelayCommand]
    private async Task ImportToModAsync()
    {
        if (!CanImport) return;
        IsBusy = true;
        StatusMsg = "Converting texture...";
        _main.SetStatus(StatusMsg);
        try {
            await Task.Run(async () => {
                // Convert source to uncompressed .tex
                var tmpDds     = Path.Combine(Path.GetTempPath(), "xivtools_import.dds");
                var ddsPath    = await Tex.ConvertToDDS(SourcePath, GamePath.Trim());
                var texBytes   = Tex.DDSToUncompressedTex(ddsPath);

                // Write to mod folder, mirroring the game path
                var modsDir    = _main.Settings.ModsPath;
                var modFolder  = Path.Combine(modsDir, TargetMod);
                var outputPath = Path.Combine(modFolder, GamePath.Trim().Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, texBytes);

                // Cleanup temp DDS if it was created
                if (ddsPath != SourcePath && File.Exists(ddsPath)) File.Delete(ddsPath);

                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    StatusMsg = $"Imported to {TargetMod}/{Path.GetFileName(outputPath)}";
                    ToastService.Instance.Success(StatusMsg);
                    _main.SetStatus(StatusMsg);
                });
            });

            // Tell Penumbra to reload
            _ = PenumbraService.Instance.TriggerRedrawAsync();
        } catch (Exception ex) {
            StatusMsg = $"Import error: {ex.Message}";
            _main.SetStatus(StatusMsg);
        }
        IsBusy = false;
    }

    // ── Import to game sqpack (via ModTransaction) ────────────
    [RelayCommand]
    private async Task ImportToGameAsync()
    {
        if (!CanImport || !FrameworkService.Instance.IsInitialized) return;
        if (!ShowGameWriteConfirm()) return;

        IsBusy = true;
        StatusMsg = "Writing to game data...";
        _main.SetStatus(StatusMsg);
        try {
            await Task.Run(async () => {
                var tx = await ModTransaction.BeginTransaction(writeEnabled: true);
                try {
                    await Tex.ImportTex(GamePath.Trim(), SourcePath, null, "XIVTools", tx);
                    await ModTransaction.CommitTransaction(tx);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        StatusMsg = $"Written to game: {GamePath}";
                        _main.SetStatus(StatusMsg);
                    });
                } catch {
                    await ModTransaction.CancelTransaction(tx, true);
                    throw;
                }
            });
        } catch (Exception ex) {
            StatusMsg = $"Write error: {ex.Message}";
            _main.SetStatus(StatusMsg);
        }
        IsBusy = false;
    }

    [RelayCommand] public void SetModeMod()  => TargetMode = "mod";
    [RelayCommand] public void SetModeGame() => TargetMode = "game";

    private bool ShowGameWriteConfirm()
    {
        // For now just return true - TODO: add confirmation dialog
        StatusMsg = "Warning: writing directly to game data. Penumbra mod import is safer.";
        return true;
    }

    private Avalonia.Controls.TopLevel? GetTopLevel() =>
        Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            ? Avalonia.Controls.TopLevel.GetTopLevel(d.MainWindow) : null;
}
