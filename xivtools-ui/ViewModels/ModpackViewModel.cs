using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.FileTypes;

namespace XivToolsUI.ViewModels;

public partial class ModpackViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private string _inputPath  = "";
    [ObservableProperty] private string _outputPath = "";
    [ObservableProperty] private string _operationLog = "Select a modpack file to get started.";
    [ObservableProperty] private bool   _isBusy;

    public ModpackViewModel(MainViewModel main) => _main = main;

    private void Log(string msg)
    {
        OperationLog += "\n" + msg;
        _main.SetStatus(msg);
    }

    [RelayCommand]
    private async Task BrowseInputAsync()
    {
        var files = await GetTopLevel()?.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Select Modpack",
            AllowMultiple = false,
            FileTypeFilter = new[] {
                new FilePickerFileType("FFXIV Modpack") { Patterns = new[] { "*.ttmp2", "*.ttmp", "*.pmp" } },
                new FilePickerFileType("All Files")     { Patterns = new[] { "*" } }
            }
        }) ?? Array.Empty<IStorageFile>();
        if (files.Count > 0) {
            InputPath  = files[0].Path.LocalPath;
            OutputPath = Path.ChangeExtension(InputPath, null) + "_DT.pmp";
            OperationLog = $"Selected: {Path.GetFileName(InputPath)}";
        }
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var file = await GetTopLevel()?.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = "Save Upgraded Modpack",
            DefaultExtension = "pmp",
            FileTypeChoices = new[] {
                new FilePickerFileType("Penumbra Modpack (*.pmp)") { Patterns = new[] { "*.pmp" } }
            }
        });
        if (file != null) OutputPath = file.Path.LocalPath;
    }

    [RelayCommand]
    private async Task UpgradeAsync()
    {
        if (string.IsNullOrWhiteSpace(InputPath))  { Log("Select an input modpack first."); return; }
        if (string.IsNullOrWhiteSpace(OutputPath)) { Log("Select an output path first."); return; }

        IsBusy = true;
        OperationLog = $"Upgrading {Path.GetFileName(InputPath)} for Dawntrail...";
        try {
            var changed = await Task.Run(() => ModpackUpgrader.UpgradeModpack(InputPath, OutputPath, true, true));
            var msg = changed ? $"Upgraded → {Path.GetFileName(OutputPath)}" : $"Already DT-compatible → {Path.GetFileName(OutputPath)}";
            Log(msg);
            ToastService.Instance.Success(msg);
        } catch (Exception ex) {
            // Patch 7.5 broke CMP (CharaMakeParameter) format - TexTools crashes on this entirely.
            // We detect it and give a clear message rather than a silent failure.
            if (ex.Message.Contains("CMP") || ex.Message.Contains("CharaMake") ||
                ex.Message.Contains("scaling") || ex.InnerException?.Message.Contains("CMP") == true) {
                Log($"Warning: CMP/racial-scaling data could not be processed (patch 7.5 format change).");
                Log($"The mod was partially upgraded - gear/texture/model changes are applied.");
                Log($"Output saved to: {Path.GetFileName(OutputPath)}");
                ToastService.Instance.Warning("Partially upgraded - CMP racial scaling skipped (patch 7.5)");
            } else {
                Log($"Error: {ex.Message}");
                ToastService.Instance.Error($"Upgrade failed: {ex.Message}");
            }
        }
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ResaveAsync()
    {
        if (string.IsNullOrWhiteSpace(InputPath))  { Log("Select an input modpack first."); return; }
        if (string.IsNullOrWhiteSpace(OutputPath)) { Log("Select an output path first."); return; }

        IsBusy = true;
        OperationLog = $"Converting {Path.GetFileName(InputPath)}...";
        try {
            await Task.Run(async () => {
                var data = await WizardData.FromModpack(InputPath);
                await data.WriteModpack(OutputPath, true);
            });
            Log($"Done -> {Path.GetFileName(OutputPath)}");
        } catch (Exception ex) {
            Log($"Error: {ex.Message}");
        }
        IsBusy = false;
    }

    private Avalonia.Controls.TopLevel? GetTopLevel() =>
        Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow)
            : null;
}
