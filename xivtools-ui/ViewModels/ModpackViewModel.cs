using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.Interfaces;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.SqPack.FileTypes;
using PMPClass = xivModdingFramework.Mods.FileTypes.PMP.PMP;

namespace XivToolsUI.ViewModels;

public partial class UpgradeQueueItem : ObservableObject
{
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    [ObservableProperty] private string _status = "Pending";

    public UpgradeQueueItem(string path) => Path = path;
}

public partial class ModpackViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    // ── Create modpack ────────────────────────────────────────────────────────
    [ObservableProperty] private string _sourceFolder  = "";
    [ObservableProperty] private string _packName      = "";
    [ObservableProperty] private string _packAuthor    = "";
    [ObservableProperty] private string _packVersion   = "1.0.0";
    [ObservableProperty] private string _packDesc      = "";
    [ObservableProperty] private string _createOutput  = "";
    [ObservableProperty] private string _createLog     = "Select a mod folder to get started.";
    [ObservableProperty] private bool   _isCreating;

    [RelayCommand]
    private async Task BrowseSourceFolderAsync()
    {
        var dirs = await GetTopLevel()?.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Mod Folder", AllowMultiple = false })
            ?? Array.Empty<IStorageFolder>();
        if (dirs.Count == 0) return;

        SourceFolder = dirs[0].Path.LocalPath;
        CreateOutput = Path.Combine(
            Path.GetDirectoryName(SourceFolder) ?? SourceFolder,
            Path.GetFileName(SourceFolder) + ".pmp");

        // Auto-populate metadata from Penumbra meta.json if present
        var metaPath = Path.Combine(SourceFolder, "meta.json");
        if (File.Exists(metaPath))
        {
            try
            {
                var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
                var r = doc.RootElement;
                if (r.TryGetProperty("Name",        out var n)) PackName   = n.GetString() ?? "";
                if (r.TryGetProperty("Author",      out var a)) PackAuthor = a.GetString() ?? "";
                if (r.TryGetProperty("Version",     out var v)) PackVersion= v.GetString() ?? "1.0.0";
                if (r.TryGetProperty("Description", out var d)) PackDesc   = d.GetString() ?? "";
                CreateLog = $"Loaded metadata from meta.json — {Path.GetFileName(SourceFolder)}";
            }
            catch { CreateLog = $"Selected: {Path.GetFileName(SourceFolder)} (meta.json parse failed)"; }
        }
        else
        {
            PackName  = Path.GetFileName(SourceFolder);
            CreateLog = $"Selected: {Path.GetFileName(SourceFolder)} — fill in metadata and click Create.";
        }
    }

    [RelayCommand]
    private async Task BrowseCreateOutputAsync()
    {
        var file = await GetTopLevel()?.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title            = "Save Modpack As",
            DefaultExtension = "pmp",
            SuggestedFileName= string.IsNullOrWhiteSpace(PackName) ? "modpack" : PackName,
            FileTypeChoices  = new[] {
                new FilePickerFileType("Penumbra Modpack (*.pmp)") { Patterns = new[] { "*.pmp" } }
            }
        });
        if (file != null) CreateOutput = file.Path.LocalPath;
    }

    [RelayCommand]
    private async Task CreateModpackAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceFolder)) { CreateLog = "Select a source folder first."; return; }
        if (string.IsNullOrWhiteSpace(PackName))     { CreateLog = "Enter a mod name."; return; }
        if (string.IsNullOrWhiteSpace(CreateOutput)) { CreateLog = "Choose an output path."; return; }

        IsCreating = true;
        CreateLog  = $"Scanning {Path.GetFileName(SourceFolder)}...";

        try
        {
            var fileInfos = await Task.Run(() => BuildFileInfos(SourceFolder));
            if (fileInfos.Count == 0)
            {
                CreateLog = "No game files found in that folder. Make sure it contains files under game-path subdirectories (e.g. chara/, ui/, bgcommon/).";
                IsCreating = false;
                return;
            }

            CreateLog = $"Packing {fileInfos.Count} file(s)...";

            var meta = new BaseModpackData
            {
                Name        = PackName,
                Author      = PackAuthor,
                Version     = Version.TryParse(PackVersion, out var ver) ? ver : new Version(1, 0, 0),
                Description = PackDesc,
            };

            await PMPClass.CreateSimplePmp(CreateOutput, meta, fileInfos);

            var size = new FileInfo(CreateOutput).Length / 1024;
            CreateLog = $"Created: {Path.GetFileName(CreateOutput)} ({fileInfos.Count} files, {size:N0} KB)";
            _main.SetStatus($"Modpack created: {Path.GetFileName(CreateOutput)}");
            ToastService.Instance.Success($"Created {Path.GetFileName(CreateOutput)}");
        }
        catch (Exception ex)
        {
            CreateLog = $"Error: {ex.Message}";
            ToastService.Instance.Error($"Create failed: {ex.Message}");
        }

        IsCreating = false;
    }

    private static Dictionary<string, FileStorageInformation> BuildFileInfos(string folder)
    {
        var result    = new Dictionary<string, FileStorageInformation>(StringComparer.OrdinalIgnoreCase);
        var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "meta.json", "default_option.json", "group_001.json", ".DS_Store" };

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (skipNames.Contains(name) || name.StartsWith(".")) continue;

            // Derive game path: relative path from source folder, forward-slash, lowercase
            var rel      = Path.GetRelativePath(folder, file);
            var gamePath = rel.Replace('\\', '/').ToLowerInvariant();

            // Skip files that aren't under a recognised game-path root
            if (!LooksLikeGamePath(gamePath)) continue;

            result[gamePath] = new FileStorageInformation
            {
                StorageType = EFileStorageType.UncompressedIndividual,
                RealPath    = file,
                RealOffset  = 0,
                FileSize    = (int)new FileInfo(file).Length
            };
        }
        return result;
    }

    private static bool LooksLikeGamePath(string path)
    {
        // Game files live under these top-level directories
        var roots = new[] { "chara/", "ui/", "bgcommon/", "bg/", "cut/", "music/", "sound/", "shader/", "vfx/", "exd/", "game/" };
        foreach (var r in roots)
            if (path.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return true;
        // Also accept any file with a recognised game extension at any depth
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".tex" or ".mdl" or ".mtrl" or ".avfx" or ".sklb" or ".pbd" or ".scd" or ".imc" or ".atex";
    }

    [ObservableProperty] private string _inputPath  = "";
    [ObservableProperty] private string _outputPath = "";
    [ObservableProperty] private string _operationLog = "Select a modpack file to get started.";
    [ObservableProperty] private bool   _isBusy;

    // ── Dawntrail upgrade (batch) ────────────────────────────────────────────
    public ObservableCollection<UpgradeQueueItem> UpgradeQueue { get; } = new();
    [ObservableProperty] private bool _isUpgrading;

    public ModpackViewModel(MainViewModel main) => _main = main;

    private void Log(string msg)
    {
        OperationLog += "\n" + msg;
        _main.SetStatus(msg);
    }

    [RelayCommand]
    private async Task BrowseUpgradeFilesAsync()
    {
        var files = await GetTopLevel()?.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Select Modpacks to Upgrade",
            AllowMultiple = true,
            FileTypeFilter = new[] {
                new FilePickerFileType("FFXIV Modpack") { Patterns = new[] { "*.ttmp2", "*.ttmp", "*.pmp" } },
                new FilePickerFileType("All Files")     { Patterns = new[] { "*" } }
            }
        }) ?? Array.Empty<IStorageFile>();

        var existing = new HashSet<string>(UpgradeQueue.Count > 0
            ? UpgradeQueue.Select(i => i.Path)
            : Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var f in files)
        {
            var path = f.Path.LocalPath;
            if (existing.Add(path)) UpgradeQueue.Add(new UpgradeQueueItem(path));
        }

        if (files.Count > 0) OperationLog = $"{UpgradeQueue.Count} file(s) queued.";
    }

    [RelayCommand]
    private void ClearUpgradeQueue() => UpgradeQueue.Clear();

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
            OutputPath = Path.ChangeExtension(InputPath, null) + "_converted" +
                (Path.GetExtension(InputPath).Equals(".pmp", StringComparison.OrdinalIgnoreCase) ? ".ttmp2" : ".pmp");
            OperationLog = $"Selected: {Path.GetFileName(InputPath)}";
        }
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var file = await GetTopLevel()?.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = "Save Converted Modpack",
            DefaultExtension = "pmp",
            FileTypeChoices = new[] {
                new FilePickerFileType("Penumbra Modpack (*.pmp)") { Patterns = new[] { "*.pmp" } },
                new FilePickerFileType("TexTools Modpack (*.ttmp2)") { Patterns = new[] { "*.ttmp2" } }
            }
        });
        if (file != null) OutputPath = file.Path.LocalPath;
    }

    [RelayCommand]
    private async Task UpgradeAllAsync()
    {
        if (UpgradeQueue.Count == 0) { Log("Select modpack files to upgrade first."); return; }

        IsUpgrading = true;
        int upgraded = 0, unchanged = 0, failed = 0;

        foreach (var item in UpgradeQueue)
        {
            item.Status = "Upgrading...";
            var outputPath = Path.ChangeExtension(item.Path, null) + "_DT.pmp";
            try
            {
                var changed = await Task.Run(() => ModpackUpgrader.UpgradeModpack(item.Path, outputPath, true, true));
                if (changed) { item.Status = "Upgraded"; upgraded++; }
                else         { item.Status = "Already DT-compatible"; unchanged++; }
            }
            catch (Exception ex)
            {
                // Patch 7.5 broke CMP (CharaMakeParameter) format - TexTools crashes on this entirely.
                // We detect it and give a clear message rather than a silent failure.
                if (ex.Message.Contains("CMP") || ex.Message.Contains("CharaMake") ||
                    ex.Message.Contains("scaling") || ex.InnerException?.Message.Contains("CMP") == true)
                {
                    item.Status = "Partial - CMP/racial-scaling skipped (patch 7.5)";
                    upgraded++;
                }
                else
                {
                    item.Status = $"Failed: {ex.Message}";
                    failed++;
                }
            }
        }

        IsUpgrading = false;
        var msg = $"Upgraded {upgraded}, unchanged {unchanged}, failed {failed}.";
        Log(msg);
        if (failed > 0) ToastService.Instance.Warning(msg);
        else            ToastService.Instance.Success(msg);
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
