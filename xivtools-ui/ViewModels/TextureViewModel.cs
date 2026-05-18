using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xivModdingFramework.Mods;
using xivModdingFramework.Textures.DataContainers;

namespace XivToolsUI.ViewModels;

public partial class TextureViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private string   _texturePath = "";
    [ObservableProperty] private Bitmap?  _preview;
    [ObservableProperty] private string   _info = "No texture loaded";
    [ObservableProperty] private bool     _isBusy;

    public TextureViewModel(MainViewModel main) => _main = main;

    [RelayCommand]
    private async Task BrowseTextureAsync()
    {
        var files = await GetTopLevel()?.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Open Texture",
            AllowMultiple = false,
            FileTypeFilter = new[] {
                new FilePickerFileType("Textures") { Patterns = new[] { "*.tex", "*.png", "*.dds", "*.bmp" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        }) ?? Array.Empty<IStorageFile>();
        if (files.Count > 0) {
            TexturePath = files[0].Path.LocalPath;
            await LoadTextureAsync();
        }
    }

    [RelayCommand]
    private async Task ExtractFromGameAsync()
    {
        // TODO: open a path entry dialog for game internal path
        _main.SetStatus("Game extraction - enter path in text field and press Load");
    }

    [RelayCommand]
    private async Task LoadTextureAsync()
    {
        if (string.IsNullOrWhiteSpace(TexturePath)) return;
        IsBusy = true;
        _main.SetStatus($"Loading {Path.GetFileName(TexturePath)}...");
        try {
            Bitmap? bmp = null;
            string infoText = "";

            await Task.Run(async () => {
                if (TexturePath.EndsWith(".tex", StringComparison.OrdinalIgnoreCase)) {
                    var data = File.ReadAllBytes(TexturePath);
                    var tex  = XivTex.FromUncompressedTex(data);
                    infoText = $"Format: {tex.TextureFormat}  Size: {tex.Width}x{tex.Height}  Mips: {tex.MipMapCount}";
                    // Convert to PNG in memory
                    var pngPath = Path.Combine(Path.GetTempPath(), "xivtools_preview.png");
                    await tex.SaveAs(pngPath);
                    bmp = new Bitmap(pngPath);
                } else {
                    bmp = new Bitmap(TexturePath);
                    infoText = $"Size: {bmp.PixelSize.Width}x{bmp.PixelSize.Height}";
                }
            });

            Preview = bmp;
            Info    = infoText;
            _main.SetStatus($"Loaded {Path.GetFileName(TexturePath)}");
        } catch (Exception ex) {
            Info = $"Error: {ex.Message}";
            _main.SetStatus($"Error: {ex.Message}");
        }
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ExportTextureAsync()
    {
        if (string.IsNullOrWhiteSpace(TexturePath) || !TexturePath.EndsWith(".tex")) return;
        var file = await GetTopLevel()?.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = "Export Texture",
            DefaultExtension = "png",
            FileTypeChoices = new[] {
                new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("DDS") { Patterns = new[] { "*.dds" } },
            }
        });
        if (file == null) return;
        IsBusy = true;
        try {
            await Task.Run(async () => {
                var data = File.ReadAllBytes(TexturePath);
                var tex  = XivTex.FromUncompressedTex(data);
                await tex.SaveAs(file.Path.LocalPath);
            });
            _main.SetStatus($"Exported to {Path.GetFileName(file.Path.LocalPath)}");
        } catch (Exception ex) {
            _main.SetStatus($"Export error: {ex.Message}");
        }
        IsBusy = false;
    }

    private Avalonia.Controls.TopLevel? GetTopLevel() =>
        Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow)
            : null;
}
