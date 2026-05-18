using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XivToolsUI.Services;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Mods;
using XivToolsUI.Services;

namespace XivToolsUI.ViewModels;

public partial class ColorsetRowViewModel : ObservableObject
{
    public int Index { get; init; }

    [ObservableProperty] private Color _diffuse;
    [ObservableProperty] private Color _specular;
    [ObservableProperty] private Color _emissive;
    [ObservableProperty] private float _specularPower;
    [ObservableProperty] private float _gloss;
    [ObservableProperty] private float _metalness;
    [ObservableProperty] private bool  _isExpanded;

    // Dye data (from ColorSetDyeData - 8 bytes per row)
    [ObservableProperty] private ushort _dyeTemplate;   // bits 0-4 = template ID
    [ObservableProperty] private bool   _dyeDiffuse;
    [ObservableProperty] private bool   _dyeSpecular;
    [ObservableProperty] private bool   _dyeEmissive;
    [ObservableProperty] private bool   _dyeGloss;
    [ObservableProperty] private bool   _dyeSpecularPower;
    [ObservableProperty] private bool   _dyeMetalness;

    public string Label     => $"Row {Index + 1}";
    public string DyeLabel  => DyeTemplate > 0 ? $"Template {DyeTemplate}" : "No dye";

    public SolidColorBrush DiffuseBrush  => new(Diffuse);
    public SolidColorBrush SpecularBrush => new(Specular);
    public SolidColorBrush EmissiveBrush => new(Emissive);

    public string DiffuseHex
    {
        get => $"#{Diffuse.R:X2}{Diffuse.G:X2}{Diffuse.B:X2}";
        set { if (TryParseHex(value, out var c)) Diffuse = c; }
    }
    public string SpecularHex
    {
        get => $"#{Specular.R:X2}{Specular.G:X2}{Specular.B:X2}";
        set { if (TryParseHex(value, out var c)) Specular = c; }
    }
    public string EmissiveHex
    {
        get => $"#{Emissive.R:X2}{Emissive.G:X2}{Emissive.B:X2}";
        set { if (TryParseHex(value, out var c)) Emissive = c; }
    }

    partial void OnDiffuseChanged(Color v)  { OnPropertyChanged(nameof(DiffuseHex));  OnPropertyChanged(nameof(DiffuseBrush));  }
    partial void OnSpecularChanged(Color v) { OnPropertyChanged(nameof(SpecularHex)); OnPropertyChanged(nameof(SpecularBrush)); }
    partial void OnEmissiveChanged(Color v) { OnPropertyChanged(nameof(EmissiveHex)); OnPropertyChanged(nameof(EmissiveBrush)); }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb)) {
            color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF));
            return true;
        }
        return false;
    }
}

public partial class ShaderKeyViewModel : ObservableObject
{
    public string Category  { get; init; } = "";
    public string CategoryHex => $"0x{Category}";
    [ObservableProperty] private string _value = "";
    public string ValueHex => $"0x{Value}";
    partial void OnValueChanged(string v) => OnPropertyChanged(nameof(ValueHex));
}

public partial class MaterialEditorViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private XivMtrl? _mtrl;

    [ObservableProperty] private string  _filePath   = "";
    [ObservableProperty] private string  _gamePath   = "";
    [ObservableProperty] private string  _shaderName = "";
    [ObservableProperty] private bool    _isBusy;
    [ObservableProperty] private string  _statusMsg  = "Open a .mtrl file or extract one from the game";
    [ObservableProperty] private bool    _hasFile;

    public ObservableCollection<ColorsetRowViewModel> ColorsetRows { get; } = new();
    public ObservableCollection<ShaderKeyViewModel>   ShaderKeys   { get; } = new();

    public MaterialEditorViewModel(MainViewModel main) => _main = main;

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var files = await GetTopLevel()?.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Open Material File",
            AllowMultiple = false,
            FileTypeFilter = new[] {
                new FilePickerFileType("MTRL Material") { Patterns = new[] { "*.mtrl" } },
                new FilePickerFileType("All Files")     { Patterns = new[] { "*" } }
            }
        }) ?? Array.Empty<IStorageFile>();
        if (files.Count == 0) return;
        FilePath = files[0].Path.LocalPath;
        await LoadFromFileAsync();
    }

    [RelayCommand]
    private async Task ExtractFromGameAsync()
    {
        if (string.IsNullOrWhiteSpace(GamePath) || !FrameworkService.Instance.IsInitialized) {
            StatusMsg = string.IsNullOrWhiteSpace(GamePath)
                ? "Enter a game path first (e.g. chara/equipment/e6071/material/v0001/mt_c0201e6071_top_a.mtrl)"
                : "Game cache not initialized - check Settings";
            return;
        }
        IsBusy = true;
        _main.SetStatus($"Extracting {Path.GetFileName(GamePath)}...");
        try {
            await Task.Run(async () => {
                var rtx  = ModTransaction.BeginReadonlyTransaction();
                var data = await rtx.ReadFile(GamePath.Trim());
                var tmpPath = Path.Combine(Path.GetTempPath(), "xivtools_" + Path.GetFileName(GamePath));
                File.WriteAllBytes(tmpPath, data);
                FilePath = tmpPath;
            });
            await LoadFromFileAsync();
        } catch (Exception ex) {
            StatusMsg = $"Extract error: {ex.Message}";
        }
        IsBusy = false;
    }

    public async Task LoadFromFilePublicAsync() => await LoadFromFileAsync();

    private async Task LoadFromFileAsync()
    {
        if (!File.Exists(FilePath)) return;
        IsBusy = true;
        StatusMsg = $"Loading {Path.GetFileName(FilePath)}...";
        ColorsetRows.Clear();
        ShaderKeys.Clear();
        try {
            await Task.Run(() => {
                var data = File.ReadAllBytes(FilePath);
                _mtrl = Mtrl.GetXivMtrl(data, FilePath);
            });
            if (_mtrl != null) PopulateFromMtrl(_mtrl);
            HasFile = true;
            StatusMsg = $"Loaded {Path.GetFileName(FilePath)} — {ColorsetRows.Count} colorset rows, {ShaderKeys.Count} shader keys";
            _main.SetStatus(StatusMsg);
        } catch (Exception ex) {
            StatusMsg = $"Load error: {ex.Message}";
            _main.SetStatus(StatusMsg);
        }
        IsBusy = false;
    }

    private void PopulateFromMtrl(XivMtrl mtrl)
    {
        ShaderName = mtrl.ShaderPackRaw ?? "";

        // Shader keys - explicit uint→string conversion avoids the TexTools
        // "Unable to cast UInt32 to String" bug (Error TT2) which occurs when
        // UI code does (string)shaderKeyObject instead of .ToString()
        if (mtrl.ShaderKeys != null) {
            foreach (var key in mtrl.ShaderKeys) {
                try {
                    ShaderKeys.Add(new ShaderKeyViewModel {
                        Category = key.KeyId.ToString("X8"),
                        Value    = key.Value.ToString("X8"),
                    });
                } catch {
                    // Malformed shader key - add as raw hex placeholder
                    ShaderKeys.Add(new ShaderKeyViewModel { Category = "????????", Value = "00000000" });
                }
            }
        }

        // Colorset rows (16 rows × 16 halfs each)
        if (mtrl.ColorSetData != null && mtrl.ColorSetData.Count > 0) {
            const int rowSize = 16;
            const int dyeSize = 8;
            int numRows = Math.Min(mtrl.ColorSetData.Count / rowSize, 16);
            for (int i = 0; i < numRows; i++) {
                int off = i * rowSize;
                var row = new ColorsetRowViewModel {
                    Index         = i,
                    Diffuse       = HalfToColor(mtrl.ColorSetData, off + 0),
                    Specular      = HalfToColor(mtrl.ColorSetData, off + 4),
                    Emissive      = HalfToColor(mtrl.ColorSetData, off + 8),
                    SpecularPower = HalfToFloat(mtrl.ColorSetData, off + 12),
                    Gloss         = HalfToFloat(mtrl.ColorSetData, off + 13),
                    Metalness     = HalfToFloat(mtrl.ColorSetData, off + 14),
                };

                // Dye data: 8 bytes per row, first 2 bytes = flags + template
                if (mtrl.ColorSetDyeData != null && (i + 1) * dyeSize <= mtrl.ColorSetDyeData.Length) {
                    int doff = i * dyeSize;
                    // Byte 0: channel flags; Byte 1 bits 0-4: template ID (1-indexed, 0 = no template)
                    byte flags    = mtrl.ColorSetDyeData[doff];
                    byte tmplByte = mtrl.ColorSetDyeData[doff + 1];
                    row.DyeTemplate      = (ushort)(tmplByte & 0x1F);
                    row.DyeDiffuse       = (flags & 0x01) != 0;
                    row.DyeSpecular      = (flags & 0x02) != 0;
                    row.DyeEmissive      = (flags & 0x04) != 0;
                    row.DyeGloss         = (flags & 0x08) != 0;
                    row.DyeSpecularPower = (flags & 0x10) != 0;
                    row.DyeMetalness     = (flags & 0x20) != 0;
                }
                ColorsetRows.Add(row);
            }
        }
    }

    private static Color HalfToColor(System.Collections.Generic.List<SharpDX.Half> data, int offset)
    {
        if (offset + 3 >= data.Count) return Colors.Black;
        float r = Clamp01((float)data[offset]);
        float g = Clamp01((float)data[offset + 1]);
        float b = Clamp01((float)data[offset + 2]);
        return Color.FromArgb(255, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static float HalfToFloat(System.Collections.Generic.List<SharpDX.Half> data, int offset) =>
        offset < data.Count ? (float)data[offset] : 0f;

    private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));

    [RelayCommand]
    private async Task SaveFileAsync()
    {
        if (_mtrl == null || !HasFile) return;
        var file = await GetTopLevel()?.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = "Save Material",
            SuggestedFileName = Path.GetFileName(FilePath),
            DefaultExtension  = "mtrl",
            FileTypeChoices = new[] { new FilePickerFileType("MTRL") { Patterns = new[] { "*.mtrl" } } }
        });
        if (file == null) return;
        IsBusy = true;
        try {
            ApplyToMtrl(_mtrl);
            File.WriteAllBytes(file.Path.LocalPath, Mtrl.XivMtrlToUncompressedMtrl(_mtrl));
            StatusMsg = $"Saved → {file.Path.LocalPath}";
            _main.SetStatus(StatusMsg);
            ToastService.Instance.Success($"Saved {Path.GetFileName(file.Path.LocalPath)}");
        } catch (Exception ex) {
            StatusMsg = $"Save error: {ex.Message}";
        }
        IsBusy = false;
    }

    // ── Save to Penumbra mod ──────────────────────────────────
    [ObservableProperty] private bool   _showSaveToMod;
    [ObservableProperty] private string _saveToModTarget = "";

    public System.Collections.ObjectModel.ObservableCollection<string> InstalledMods { get; } = new();

    public void LoadModList()
    {
        InstalledMods.Clear();
        var modsDir = _main.Settings.ModsPath;
        if (!Directory.Exists(modsDir)) return;
        foreach (var d in Directory.GetDirectories(modsDir).OrderBy(Path.GetFileName))
            InstalledMods.Add(Path.GetFileName(d)!);
        if (InstalledMods.Count > 0) SaveToModTarget = InstalledMods[0];
    }

    [RelayCommand]
    private void OpenSaveToMod()
    {
        if (_mtrl == null || !HasFile) return;
        LoadModList();
        ShowSaveToMod = true;
    }

    [RelayCommand]
    private async Task SaveToModConfirmedAsync()
    {
        ShowSaveToMod = false;
        if (_mtrl == null || string.IsNullOrWhiteSpace(GamePath) || string.IsNullOrWhiteSpace(SaveToModTarget)) {
            StatusMsg = string.IsNullOrWhiteSpace(GamePath)
                ? "Set the Game Path field first (e.g. chara/equipment/e6071/material/...)"
                : "Select a target mod";
            return;
        }
        IsBusy = true;
        try {
            ApplyToMtrl(_mtrl);
            var bytes     = Mtrl.XivMtrlToUncompressedMtrl(_mtrl);
            var modsDir   = _main.Settings.ModsPath;
            var outPath   = Path.Combine(modsDir, SaveToModTarget,
                                GamePath.Trim().Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllBytes(outPath, bytes);
            StatusMsg = $"Saved to {SaveToModTarget}/{Path.GetFileName(outPath)}";
            _main.SetStatus(StatusMsg);
            ToastService.Instance.Success(StatusMsg);
            _ = PenumbraService.Instance.TriggerRedrawAsync();
        } catch (Exception ex) {
            StatusMsg = $"Save error: {ex.Message}";
        }
        IsBusy = false;
    }

    [RelayCommand]
    private void SaveToModCancelled() => ShowSaveToMod = false;

    private void ApplyToMtrl(XivMtrl mtrl)
    {
        // Write colorset rows back
        const int rowSize = 16;
        for (int i = 0; i < ColorsetRows.Count && i * rowSize + 15 < mtrl.ColorSetData.Count; i++) {
            var row = ColorsetRows[i];
            int off = i * rowSize;
            WriteColor(mtrl.ColorSetData, off + 0, row.Diffuse);
            WriteColor(mtrl.ColorSetData, off + 4, row.Specular);
            WriteColor(mtrl.ColorSetData, off + 8, row.Emissive);
            mtrl.ColorSetData[off + 12] = (SharpDX.Half)row.SpecularPower;
            mtrl.ColorSetData[off + 13] = (SharpDX.Half)row.Gloss;
            mtrl.ColorSetData[off + 14] = (SharpDX.Half)row.Metalness;
        }
        // Write dye data back
        if (mtrl.ColorSetDyeData != null) {
            const int dyeSize = 8;
            for (int i = 0; i < ColorsetRows.Count && (i + 1) * dyeSize <= mtrl.ColorSetDyeData.Length; i++) {
                var row  = ColorsetRows[i];
                int doff = i * dyeSize;
                byte flags = 0;
                if (row.DyeDiffuse)       flags |= 0x01;
                if (row.DyeSpecular)      flags |= 0x02;
                if (row.DyeEmissive)      flags |= 0x04;
                if (row.DyeGloss)         flags |= 0x08;
                if (row.DyeSpecularPower) flags |= 0x10;
                if (row.DyeMetalness)     flags |= 0x20;
                mtrl.ColorSetDyeData[doff]     = flags;
                mtrl.ColorSetDyeData[doff + 1] = (byte)(row.DyeTemplate & 0x1F);
            }
        }
        // Write shader keys back
        if (mtrl.ShaderKeys != null) {
            for (int i = 0; i < Math.Min(ShaderKeys.Count, mtrl.ShaderKeys.Count); i++) {
                if (uint.TryParse(ShaderKeys[i].Value, System.Globalization.NumberStyles.HexNumber, null, out var val))
                    mtrl.ShaderKeys[i] = new ShaderKey { KeyId = mtrl.ShaderKeys[i].KeyId, Value = val };
            }
        }
    }

    private static void WriteColor(System.Collections.Generic.List<SharpDX.Half> data, int off, Color c)
    {
        data[off]     = (SharpDX.Half)(c.R / 255f);
        data[off + 1] = (SharpDX.Half)(c.G / 255f);
        data[off + 2] = (SharpDX.Half)(c.B / 255f);
    }

    private Avalonia.Controls.TopLevel? GetTopLevel() =>
        Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            ? Avalonia.Controls.TopLevel.GetTopLevel(d.MainWindow) : null;
}
