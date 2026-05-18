using System;
using System.IO;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using XivToolsUI.Models;

namespace XivToolsUI.Services;

public class FrameworkService
{
    private static FrameworkService? _instance;
    public static FrameworkService Instance => _instance ??= new FrameworkService();

    public bool IsInitialized { get; private set; }
    public string? InitError   { get; private set; }

    public void Reset() => IsInitialized = false;

    public async Task<bool> Initialize(AppSettings settings)
    {
        if (IsInitialized) return true;
        if (string.IsNullOrWhiteSpace(settings.GamePath)) {
            InitError = "Game path not set - go to Settings";
            return false;
        }
        try {
            var sqpackPath = Path.Combine(settings.GamePath, "game", "sqpack", "ffxiv");
            if (!Directory.Exists(sqpackPath))
                sqpackPath = settings.GamePath; // already pointing at sqpack/ffxiv

            await XivCache.SetGameInfo(new System.IO.DirectoryInfo(sqpackPath), XivLanguage.English, false);

            var tmp = Path.Combine(Path.GetTempPath(), "xivtools_ui_tmp");
            Directory.CreateDirectory(tmp);
            XivCache.FrameworkSettings.TempDirectory = tmp;

            IsInitialized = true;
            InitError = null;
            return true;
        } catch (Exception ex) {
            InitError = ex.Message;
            return false;
        }
    }
}
