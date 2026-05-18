using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace XivToolsUI.Services;

public class PenumbraService
{
    private static readonly string PenumbraDir = ResolvePenumbraDir();

    private static string ResolvePenumbraDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher", "pluginConfigs", "Penumbra");
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".xlcore/pluginConfigs/Penumbra");
    }

    private static readonly HttpClient Http = new() { BaseAddress = new Uri("http://localhost:42069/"), Timeout = TimeSpan.FromSeconds(2) };

    // ── Collection loading ────────────────────────────────────

    private string GetActiveCollectionPath()
    {
        var activeJson = Path.Combine(PenumbraDir, "active_collections.json");
        if (!File.Exists(activeJson)) return "";
        var doc = JsonDocument.Parse(File.ReadAllText(activeJson));
        var id  = doc.RootElement.TryGetProperty("Current", out var cur) ? cur.GetString() : "";
        if (string.IsNullOrEmpty(id) || id == "00000000-0000-0000-0000-000000000000") {
            id = doc.RootElement.TryGetProperty("Default", out var def) ? def.GetString() : "";
        }
        return string.IsNullOrEmpty(id) ? "" : Path.Combine(PenumbraDir, "collections", $"{id}.json");
    }

    /// <summary>Returns all available collection names (id → name).</summary>
    public Dictionary<string, string> GetCollections()
    {
        var result = new Dictionary<string, string>();
        var collectionsDir = Path.Combine(PenumbraDir, "collections");
        if (!Directory.Exists(collectionsDir)) return result;
        foreach (var file in Directory.GetFiles(collectionsDir, "*.json")) {
            if (file.EndsWith(".bak")) continue;
            try {
                var doc  = JsonDocument.Parse(File.ReadAllText(file));
                var id   = Path.GetFileNameWithoutExtension(file);
                var name = doc.RootElement.TryGetProperty("Name", out var n) ? n.GetString() ?? id : id;
                result[id] = name;
            } catch { }
        }
        return result;
    }

    /// <summary>Returns the active (Current) collection id and name.</summary>
    public (string Id, string Name) GetActiveCollection()
    {
        var activeJson = Path.Combine(PenumbraDir, "active_collections.json");
        if (!File.Exists(activeJson)) return ("", "Unknown");
        try {
            var doc  = JsonDocument.Parse(File.ReadAllText(activeJson));
            var id   = doc.RootElement.TryGetProperty("Current", out var c) ? c.GetString() ?? "" : "";
            var cols = GetCollections();
            var name = cols.TryGetValue(id, out var n) ? n : id;
            return (id, name);
        } catch { return ("", "Unknown"); }
    }

    /// <summary>Sets all collection slots to the given collection id.</summary>
    public void SetActiveCollection(string collectionId)
    {
        var activeJson = Path.Combine(PenumbraDir, "active_collections.json");
        if (!File.Exists(activeJson)) return;
        var text = File.ReadAllText(activeJson);
        var doc  = JsonDocument.Parse(text);
        using var ms     = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        foreach (var prop in doc.RootElement.EnumerateObject()) {
            // Update all guid-style fields to the new collection
            if (prop.Value.ValueKind == JsonValueKind.String &&
                prop.Value.GetString()?.Length == 36 &&
                prop.Name != "Individuals") {
                writer.WriteString(prop.Name, collectionId);
            } else {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
        writer.Flush();
        File.WriteAllBytes(activeJson, ms.ToArray());
    }

    /// <summary>Returns folder name → priority for all mods in the active collection.</summary>
    public Dictionary<string, int> LoadPriorities()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path   = GetActiveCollectionPath();
        if (!File.Exists(path)) return result;
        try {
            var doc      = JsonDocument.Parse(File.ReadAllText(path));
            var settings = doc.RootElement.GetProperty("Settings");
            foreach (var mod in settings.EnumerateObject()) {
                var priority = mod.Value.TryGetProperty("Priority", out var p) ? p.GetInt32() : 0;
                result[mod.Name] = priority;
            }
        } catch { }
        return result;
    }

    public Dictionary<string, bool> LoadEnabledStates()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var path   = GetActiveCollectionPath();
        if (!File.Exists(path)) return result;
        try {
            var doc      = JsonDocument.Parse(File.ReadAllText(path));
            var settings = doc.RootElement.GetProperty("Settings");
            foreach (var mod in settings.EnumerateObject()) {
                var enabled = mod.Value.TryGetProperty("Enabled", out var e) && e.GetBoolean();
                result[mod.Name] = enabled;
            }
        } catch { }
        return result;
    }

    public bool SetModEnabled(string modFolderName, bool enabled)
    {
        var path = GetActiveCollectionPath();
        if (!File.Exists(path)) return false;
        try {
            // Read raw JSON
            var text  = File.ReadAllText(path);
            var doc   = JsonDocument.Parse(text);
            var root  = doc.RootElement;

            // Rebuild JSON with updated Enabled flag
            using var ms     = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject()) {
                if (prop.Name == "Settings") {
                    writer.WritePropertyName("Settings");
                    writer.WriteStartObject();
                    foreach (var mod in prop.Value.EnumerateObject()) {
                        writer.WritePropertyName(mod.Name);
                        if (mod.Name == modFolderName) {
                            // Rewrite this mod's entry with updated Enabled
                            writer.WriteStartObject();
                            foreach (var field in mod.Value.EnumerateObject()) {
                                if (field.Name == "Enabled") {
                                    writer.WriteBoolean("Enabled", enabled);
                                } else {
                                    field.WriteTo(writer);
                                }
                            }
                            writer.WriteEndObject();
                        } else {
                            mod.Value.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                } else {
                    prop.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
            writer.Flush();

            File.WriteAllBytes(path, ms.ToArray());
            return true;
        } catch {
            return false;
        }
    }

    // ── HTTP API ──────────────────────────────────────────────

    /// <summary>Tell Penumbra to reload its state (if game is running).</summary>
    public async Task TriggerRedrawAsync()
    {
        try { await Http.PostAsync("api/redraw", null); } catch { }
    }

    /// <summary>Get the live mod name → display name map from Penumbra API.</summary>
    public async Task<Dictionary<string, string>> GetApiModsAsync()
    {
        try {
            var json = await Http.GetStringAsync("api/mods");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        } catch {
            return new Dictionary<string, string>();
        }
    }

    public static PenumbraService Instance { get; } = new();
}
