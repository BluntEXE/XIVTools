using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Materials.FileTypes;
using XivToolsUI.Services;

namespace XivToolsUI.ViewModels;

/// <summary>Flattened mesh ready to upload to GPU.</summary>
public class GlMeshData
{
    /// <summary>pos(3)+normal(3)+uv(2) = 8 floats/vertex, stride=32</summary>
    public float[] Vertices  { get; init; } = Array.Empty<float>();
    public int[]   Indices   { get; init; } = Array.Empty<int>();
    public float[] Color     { get; init; } = new float[] { 0.7f, 0.7f, 0.8f };
    public string  Name      { get; init; } = "";
    /// <summary>BGRA pixel data for diffuse texture, null if none loaded.</summary>
    public (byte[] Data, int W, int H)? Texture { get; init; }
}

public partial class ModelViewerViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private string  _filePath   = "";
    [ObservableProperty] private string  _gamePath   = "";
    [ObservableProperty] private bool    _isBusy;
    [ObservableProperty] private string  _statusMsg  = "Open an .mdl file or extract one from the game";
    [ObservableProperty] private bool    _hasModel;
    [ObservableProperty] private int     _vertexCount;
    [ObservableProperty] private int     _triangleCount;
    [ObservableProperty] private int     _meshCount;

    public event Action<List<GlMeshData>>? ModelLoaded;

    private static readonly float[][] _meshColors = {
        new[] { 0.65f, 0.75f, 0.95f }, new[] { 0.95f, 0.75f, 0.65f },
        new[] { 0.65f, 0.95f, 0.75f }, new[] { 0.95f, 0.95f, 0.65f },
        new[] { 0.85f, 0.65f, 0.95f }, new[] { 0.65f, 0.95f, 0.95f },
    };

    public ModelViewerViewModel(MainViewModel main) => _main = main;

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var files = await GetTopLevel()?.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Open Model File", AllowMultiple = false,
            FileTypeFilter = new[] {
                new FilePickerFileType("MDL Model") { Patterns = new[] { "*.mdl" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
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
                ? "Enter a game path first"
                : "Game cache not initialized";
            return;
        }
        IsBusy = true;
        _main.SetStatus($"Extracting {Path.GetFileName(GamePath)}...");
        try {
            await Task.Run(async () => {
                var rtx  = ModTransaction.BeginReadonlyTransaction();
                var data = await rtx.ReadFile(GamePath.Trim());
                var tmp  = Path.Combine(Path.GetTempPath(), "xivtools_" + Path.GetFileName(GamePath));
                File.WriteAllBytes(tmp, data);
                FilePath = tmp;
            });
            await LoadFromFileAsync();
        } catch (Exception ex) {
            StatusMsg = $"Extract error: {ex.Message}";
            _main.SetStatus(StatusMsg);
        }
        IsBusy = false;
    }

    private async Task LoadFromFileAsync()
    {
        if (!File.Exists(FilePath)) return;
        IsBusy = true;
        StatusMsg = $"Loading {Path.GetFileName(FilePath)}...";
        _main.SetStatus(StatusMsg);
        try {
            List<GlMeshData> meshes = new();
            await Task.Run(async () => {
                var data    = File.ReadAllBytes(FilePath);
                var xivMdl  = Mdl.GetXivMdl(data);
                var ttModel = await TTModel.FromRaw(xivMdl);
                meshes = await BuildGlMeshesAsync(ttModel);
            });
            HasModel       = true;
            MeshCount      = meshes.Count;
            VertexCount    = meshes.Sum(m => m.Vertices.Length / 8);
            TriangleCount  = meshes.Sum(m => m.Indices.Length / 3);
            var textured   = meshes.Count(m => m.Texture.HasValue);
            StatusMsg      = $"{Path.GetFileName(FilePath)}  ·  {MeshCount} meshes  ·  {VertexCount:N0} verts  ·  {textured}/{MeshCount} textured";
            _main.SetStatus(StatusMsg);
            ModelLoaded?.Invoke(meshes);
        } catch (Exception ex) {
            StatusMsg = $"Load error: {ex.Message}";
            _main.SetStatus(StatusMsg);
        }
        IsBusy = false;
    }

    private async Task<List<GlMeshData>> BuildGlMeshesAsync(TTModel model)
    {
        var result   = new List<GlMeshData>();
        int colorIdx = 0;

        // Determine game path prefix for resolving texture paths
        var modelGamePath = GamePath.Trim();

        foreach (var group in model.MeshGroups) {
            var verts   = new List<float>();
            var indices = new List<int>();
            int baseVtx = 0;

            foreach (var part in group.Parts) {
                foreach (var v in part.Vertices) {
                    // pos(3) + normal(3) + uv(2) = 8 floats
                    verts.Add(v.Position.X); verts.Add(v.Position.Y); verts.Add(v.Position.Z);
                    verts.Add(v.Normal.X);   verts.Add(v.Normal.Y);   verts.Add(v.Normal.Z);
                    verts.Add(v.UV1.X);      verts.Add(1f - v.UV1.Y); // flip V for GL
                }
                foreach (var idx in part.TriangleIndices) indices.Add(baseVtx + idx);
                baseVtx += part.Vertices.Count;
            }
            if (verts.Count == 0 || indices.Count == 0) continue;

            // Try to load diffuse texture for this mesh group
            (byte[] Data, int W, int H)? tex = null;
            if (!string.IsNullOrEmpty(group.Material) && FrameworkService.Instance.IsInitialized) {
                tex = await TryLoadDiffuseAsync(group.Material, modelGamePath);
            }

            result.Add(new GlMeshData {
                Vertices = verts.ToArray(),
                Indices  = indices.ToArray(),
                Color    = _meshColors[colorIdx % _meshColors.Length],
                Name     = group.Name ?? $"Mesh {result.Count}",
                Texture  = tex,
            });
            colorIdx++;
        }
        return result;
    }

    private static async Task<(byte[] Data, int W, int H)?> TryLoadDiffuseAsync(
        string mtrlPath, string modelGamePath)
    {
        try {
            var rtx = ModTransaction.BeginReadonlyTransaction();

            // Resolve relative material path if needed
            if (!mtrlPath.StartsWith("chara") && !string.IsNullOrEmpty(modelGamePath)) {
                var dir = Path.GetDirectoryName(modelGamePath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
                // Materials are usually in ../../material/
                var parts = dir.Split('/');
                if (parts.Length >= 2) {
                    var basePath = string.Join("/", parts[..^2]);
                    mtrlPath = $"{basePath}/material/v0001/{mtrlPath}";
                }
            }

            // Get texture paths from material
            var texPaths = await Mtrl.GetTexturePathsFromMtrlPath(mtrlPath, false, false, rtx);
            // Find the diffuse/normal texture (prefer _d.tex > _n.tex > first .tex)
            var diffuse = texPaths.FirstOrDefault(t => t.EndsWith("_d.tex"))
                       ?? texPaths.FirstOrDefault(t => t.EndsWith("_n.tex"))
                       ?? texPaths.FirstOrDefault(t => t.EndsWith(".tex"));
            if (diffuse == null) return null;

            // Load and convert to BGRA bytes
            var rawData = await rtx.ReadFile(diffuse);
            var xivTex  = XivTex.FromUncompressedTex(rawData);
            var tmpPng  = Path.Combine(Path.GetTempPath(), $"xivtools_model_tex_{Path.GetFileName(diffuse)}.png");
            await xivTex.SaveAs(tmpPng);

            using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(tmpPng);
            var pixels = new byte[img.Width * img.Height * 4];
            img.CopyPixelDataTo(pixels);
            return (pixels, img.Width, img.Height);
        } catch {
            return null;
        }
    }

    private Avalonia.Controls.TopLevel? GetTopLevel() =>
        Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            ? Avalonia.Controls.TopLevel.GetTopLevel(d.MainWindow) : null;
}
