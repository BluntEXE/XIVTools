using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivToolsUI.Services;

namespace XivToolsUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private string _gamePath;
    [ObservableProperty] private string _modsPath;
    [ObservableProperty] private string _statusMsg = "";

    public SettingsViewModel(MainViewModel main)
    {
        _main    = main;
        GamePath = main.Settings.GamePath;
        ModsPath = main.Settings.ModsPath;
    }

    [RelayCommand]
    private async Task BrowseGamePathAsync()
    {
        var dirs = await GetTopLevel()?.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select FFXIV Game Folder", AllowMultiple = false })
            ?? System.Array.Empty<IStorageFolder>();
        if (dirs.Count > 0) GamePath = dirs[0].Path.LocalPath;
    }

    [RelayCommand]
    private async Task BrowseModsPathAsync()
    {
        var dirs = await GetTopLevel()?.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Penumbra Mods Folder", AllowMultiple = false })
            ?? System.Array.Empty<IStorageFolder>();
        if (dirs.Count > 0) ModsPath = dirs[0].Path.LocalPath;
    }

    [RelayCommand]
    private async Task SaveAndApplyAsync()
    {
        _main.Settings.GamePath = GamePath;
        _main.Settings.ModsPath = ModsPath;
        _main.Settings.Save();
        StatusMsg = "Saved - reinitializing game cache...";
        FrameworkService.Instance.Reset();
        await _main.Reinitialize();
        StatusMsg = FrameworkService.Instance.IsInitialized
            ? "Settings saved and game cache initialized."
            : $"Saved, but: {FrameworkService.Instance.InitError}";
    }

    private Avalonia.Controls.TopLevel? GetTopLevel() =>
        Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            ? Avalonia.Controls.TopLevel.GetTopLevel(d.MainWindow)
            : null;
}
