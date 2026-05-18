using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivToolsUI.Services;

namespace XivToolsUI.ViewModels;

public partial class ModEntry : ObservableObject
{
    public string Name        { get; init; } = "";
    public string Author      { get; init; } = "";
    public string Version     { get; init; } = "";
    public string FolderPath  { get; init; } = "";
    public string FolderName  => Path.GetFileName(FolderPath);

    [ObservableProperty] private bool   _enabled;
    [ObservableProperty] private bool   _isToggling;

    public string EnabledLabel => Enabled ? "Enabled" : "Disabled";
    partial void OnEnabledChanged(bool v) => OnPropertyChanged(nameof(EnabledLabel));
}

public partial class ModListViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private Dictionary<string, bool> _enabledStates = new();

    public ObservableCollection<ModEntry> Mods { get; } = new();
    [ObservableProperty] private ModEntry? _selectedMod;
    [ObservableProperty] private string    _searchText  = "";
    [ObservableProperty] private bool      _isLoading;
    [ObservableProperty] private string    _enabledCount = "";

    public ModListViewModel(MainViewModel main) => _main = main;

    public async Task LoadModsAsync()
    {
        IsLoading = true;
        Mods.Clear();
        _main.SetStatus("Loading mods...");
        try {
            var modsDir = _main.Settings.ModsPath;
            if (!Directory.Exists(modsDir)) { IsLoading = false; return; }

            // Load enabled states from Penumbra collection
            _enabledStates = await Task.Run(() => PenumbraService.Instance.LoadEnabledStates());

            // Build all entries on background thread, then add to UI in one batch
            var entries = await Task.Run(() => {
                var list = new List<ModEntry>();
                foreach (var dir in Directory.GetDirectories(modsDir).OrderBy(d => Path.GetFileName(d))) {
                    var metaPath = Path.Combine(dir, "meta.json");
                    if (!File.Exists(metaPath)) continue;
                    try {
                        var meta    = JsonDocument.Parse(File.ReadAllText(metaPath));
                        var root    = meta.RootElement;
                        var folder  = Path.GetFileName(dir);
                        _enabledStates.TryGetValue(folder, out var enabled);
                        list.Add(new ModEntry {
                            Name       = root.TryGetProperty("Name",    out var n) ? n.GetString() ?? "" : folder,
                            Author     = root.TryGetProperty("Author",  out var a) ? a.GetString() ?? "" : "",
                            Version    = root.TryGetProperty("Version", out var v) ? v.GetString() ?? "" : "",
                            FolderPath = dir,
                            Enabled    = enabled,
                        });
                    } catch { }
                }
                return list;
            });

            // Add all to collection on UI thread in one go - no race condition
            foreach (var entry in entries) Mods.Add(entry);

            var on  = Mods.Count(m => m.Enabled);
            EnabledCount = $"{Mods.Count} mods · {on} enabled";
            _main.SetStatus(EnabledCount);
        } catch (Exception ex) {
            _main.SetStatus($"Error loading mods: {ex.Message}");
        }
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ToggleModAsync(ModEntry? mod)
    {
        mod ??= SelectedMod;
        if (mod == null || mod.IsToggling) return;

        mod.IsToggling = true;
        var newState = !mod.Enabled;
        _main.SetStatus($"{(newState ? "Enabling" : "Disabling")} {mod.Name}...");

        var ok = await Task.Run(() => PenumbraService.Instance.SetModEnabled(mod.FolderName, newState));
        if (ok) {
            mod.Enabled = newState;
            var on = Mods.Count(m => m.Enabled);
            EnabledCount = $"{Mods.Count} mods · {on} enabled";
            var statusMsg = $"{mod.Name} {(newState ? "enabled" : "disabled")}";
            _main.SetStatus(statusMsg);
            ToastService.Instance.Show(statusMsg, newState ? ToastKind.Success : ToastKind.Info);
            _ = PenumbraService.Instance.TriggerRedrawAsync();
        } else {
            _main.SetStatus($"Could not update {mod.Name} - collection file not found");
        }
        mod.IsToggling = false;
    }

    [RelayCommand]
    private async Task UpgradeModAsync(ModEntry? mod)
    {
        mod ??= SelectedMod;
        if (mod == null) return;
        // Find the mod's pmp equivalent or pass folder as modpack path
        _main.ModpackVm.InputPath = mod.FolderPath;
        _main.Modpack.OutputPath = mod.FolderPath + "_DT.pmp";
        _main.NavigateToModpackCommand.Execute(null);
        _main.SetStatus($"Ready to upgrade {mod.Name} - click Upgrade to Dawntrail");
    }

    // ── Delete with inline confirmation ──────────────────────
    [ObservableProperty] private bool   _showDeleteConfirm;
    [ObservableProperty] private string _deleteConfirmName = "";

    [RelayCommand]
    private void DeleteMod()
    {
        if (SelectedMod == null) return;
        DeleteConfirmName = SelectedMod.Name;
        ShowDeleteConfirm = true;
    }

    [RelayCommand]
    private async Task DeleteConfirmedAsync()
    {
        ShowDeleteConfirm = false;
        if (SelectedMod == null) return;
        var mod = SelectedMod;
        try {
            await Task.Run(() => System.IO.Directory.Delete(mod.FolderPath, true));
            Mods.Remove(mod);
            SelectedMod = null;
            var on = Mods.Count(m => m.Enabled);
            EnabledCount = $"{Mods.Count} mods · {on} enabled";
            _main.SetStatus($"Deleted '{mod.Name}'");
            _ = PenumbraService.Instance.TriggerRedrawAsync();
            ToastService.Instance.Success($"Deleted '{mod.Name}'");
        } catch (Exception ex) {
            _main.SetStatus($"Delete failed: {ex.Message}");
            ToastService.Instance.Error($"Delete failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteCancelled() => ShowDeleteConfirm = false;

    [RelayCommand]
    private async Task RefreshAsync() => await LoadModsAsync();

    [RelayCommand]
    private void OpenModFolder()
    {
        if (SelectedMod == null) return;
        try {
            var psi = new System.Diagnostics.ProcessStartInfo("xdg-open");
            psi.ArgumentList.Add(SelectedMod.FolderPath);
            psi.UseShellExecute = false;
            System.Diagnostics.Process.Start(psi);
        } catch (Exception ex) {
            _main.SetStatus($"Could not open folder: {ex.Message}");
        }
    }

    public ObservableCollection<ModEntry> FilteredMods =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Mods
            : new ObservableCollection<ModEntry>(Mods.Where(m =>
                m.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                m.Author.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredMods));
}
