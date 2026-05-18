using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XivToolsUI.Models;

public class AppSettings
{
    private static readonly string ConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xivtools", "settings.json");

    public string GamePath { get; set; } = "";
    public string ModsPath { get; set; } = DefaultModsPath();
    public string Language { get; set; } = "en";

    public static AppSettings Load()
    {
        try {
            if (File.Exists(ConfigPath)) {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath));
                if (s != null) return s;
            }
        } catch { }

        var settings = new AppSettings();
        settings.GamePath = DetectGamePath();
        return settings;
    }

    private static string DefaultModsPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher", "pluginConfigs", "Penumbra");
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".xlcore/wineprefix/drive_c/XIVMODCONFIG/FFXIV Mods");
    }

    private static string DetectGamePath()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? DetectGamePathWindows()
            : DetectGamePathLinux();
    }

    private static string DetectGamePathWindows()
    {
        try {
            var config = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher", "launcherConfig.json");
            if (!File.Exists(config)) return "";

            var doc = JsonDocument.Parse(File.ReadAllText(config));
            if (doc.RootElement.TryGetProperty("GamePath", out var gp))
            {
                var path = gp.GetString() ?? "";
                var sqpack = Path.Combine(path, "game", "sqpack", "ffxiv");
                if (Directory.Exists(sqpack)) return sqpack;
                if (Directory.Exists(path))   return path;
            }
        } catch { }
        return "";
    }

    private static string DetectGamePathLinux()
    {
        try {
            var launcherIni = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".xlcore/launcher.ini");
            if (!File.Exists(launcherIni)) return "";

            foreach (var line in File.ReadAllLines(launcherIni)) {
                var m = Regex.Match(line, @"^GamePath=(.+)$");
                if (m.Success) {
                    var path = m.Groups[1].Value.Trim();
                    var sqpack = Path.Combine(path, "game", "sqpack", "ffxiv");
                    if (Directory.Exists(sqpack)) return sqpack;
                    if (Directory.Exists(path))   return path;
                }
            }
        } catch { }
        return "";
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
