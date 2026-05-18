# XIVTools

Native desktop mod manager for FFXIV on Linux and Windows. Reads and writes Penumbra collections and game files directly through xivModdingFramework. No TexTools installation needed. No Wine on Windows.

## Features

- **Mod List** — enable and disable mods in any Penumbra collection
- **Modpack Operations** — install `.ttmp2` / `.pmp` files, upgrade pre-Dawntrail mods, convert between formats
- **Texture Viewer** — preview any `.tex` file from the game or a mod, export as PNG or DDS
- **Texture Import** — replace game textures by importing PNG or DDS, written into a mod
- **Material Editor** — edit `.mtrl` files: colorset rows, dye templates, color pickers for all channels
- **Model Viewer** — OpenGL preview of any `.mdl` file with textures applied
- **Model Import** — replace game models by importing `.obj`, `.fbx`, `.glb`, `.gltf`, or `.dae`
- **Item Browser** — search the full FFXIV item database, open any item's textures, materials, or model
- **File Search** — search all game file paths in the sqpack index
- **File Override Inspector** — see every file in the active collection, which mod provides it, and at what priority
- **Collection Switcher** — switch Penumbra collections without opening the game

---

## Requirements

### Windows

No additional dependencies. All runtime libraries including the model import backend are bundled.

### Linux

- FFXIV running via XLCore (the Linux version of XIVLauncher) with the Penumbra Dalamud plugin installed
- FBX, glTF, and DAE model import requires the `libassimp` system library:
  - Arch/Manjaro: `sudo pacman -S assimp`
  - Ubuntu/Debian: `sudo apt install libassimp5`
  - Fedora: `sudo dnf install assimp`
- OBJ import has no additional dependencies

---

## Installation

### Windows

1. Download `XIVTools-windows-v{version}.zip` from the [Releases](../../releases) page
2. Extract to any folder, e.g. `C:\Tools\XIVTools\`
3. Run `xivtools-ui.exe`

XIVTools auto-detects your game path from XIVLauncher's config on first launch. If detection fails, set it in **Settings**.

No administrator privileges needed. SmartScreen may prompt on first launch; click **More info**, then **Run anyway**.

### Linux

1. Download `XIVTools-linux-v{version}.zip` from the [Releases](../../releases) page
2. Extract:
   ```bash
   unzip XIVTools-linux-v1.0.0.zip -d ~/XIVTools
   ```
3. Make the binary executable:
   ```bash
   chmod +x ~/XIVTools/app/xivtools-ui
   ```
4. Run:
   ```bash
   ~/XIVTools/app/xivtools-ui
   ```
   On Wayland:
   ```bash
   WAYLAND_DISPLAY=wayland-1 ~/XIVTools/app/xivtools-ui
   ```

XIVTools auto-detects your game path from `~/.xlcore/launcher.ini` on first launch.

To add XIVTools to your application launcher, see [Linux Desktop Integration](#linux-desktop-integration) below.

---

## First-Time Setup

Open **Settings** (bottom of the sidebar) and set these two paths:

| Field | Description |
|-------|-------------|
| **Game Path** | The FFXIV `game` folder containing `ffxiv_dx11.exe`. Auto-detected on first launch: Windows reads XIVLauncher's config, Linux reads `~/.xlcore/launcher.ini`. Example: `.../FINAL FANTASY XIV Online/game` |
| **Mods Directory** | The folder where Penumbra stores installed mods. Windows default: `%AppData%\XIVLauncher\pluginConfigs\Penumbra`. Linux default: inside your XLCore Wine prefix at `.xlcore/wineprefix/drive_c/XIVMODCONFIG/FFXIV Mods`. |

Click **Save and Apply**. XIVTools reinitializes the game cache immediately.

---

## Usage

### Mod List

Shows every mod in the active Penumbra collection. Each row displays the mod name, folder name, enabled state, and priority.

Toggle the switch on the right to enable or disable a mod. XIVTools writes the change to Penumbra's collection file immediately. If the game is running with Penumbra active, the change applies live via Penumbra's HTTP API.

**Open mod folder** opens the mod directory in your file manager. **Refresh** reloads the list from disk.

### Collection Switcher

Click the collection name at the bottom of the sidebar. Selecting a collection rewrites Penumbra's `active_collections.json` and reloads the mod list. If the game is running, Penumbra redraws all active mods.

### Modpack Operations

| Operation | Input | Output |
|-----------|-------|--------|
| **Install Modpack** | `.ttmp2` or `.pmp` | Extracts into the current Penumbra mods directory |
| **Upgrade to Dawntrail** | Pre-7.0 `.ttmp2` | `.pmp` compatible with the current game |
| **Convert .ttmp2 to .pmp** | `.ttmp2` | `.pmp` in the same folder |
| **Convert .pmp to .ttmp2** | `.pmp` | `.ttmp2` in the same folder |

Drag a modpack file onto the window, or use **Browse** to open one.

### Texture Viewer

Open any `.tex` file to preview it. The viewer shows a full-resolution preview with zoom and pan, the format, dimensions, and mip count. Export using the **Export PNG** or **Export DDS** buttons.

Click **Open File** to browse for a `.tex` file, or reach a texture through the **Item Browser**.

### Texture Import

1. Click **Open .tex** and select the game texture to replace
2. Click **Import Image** and select your PNG or DDS replacement
3. Choose the target mod from the dropdown, or create a new one
4. Click **Import**

XIVTools handles format conversion. If your image dimensions differ from the original, a warning appears before import.

### Material Editor

Open a `.mtrl` file to edit:

- **Colorset** — all 16 rows, each with diffuse, specular, emissive, and gloss values. Click any color swatch to open the color picker.
- **Dye Template** — per-row dye channel assignments
- **Shader flags** — shader feature bit toggles

Use **Open .mtrl** to load a file. Use **Save to Mod** to write the result into a mod.

### Model Viewer

Preview any `.mdl` file with its textures applied.

| Control | Action |
|---------|--------|
| Left drag | Orbit |
| Right drag | Pan |
| Scroll | Zoom |
| `R` | Reset camera |

Click **Open .mdl** to load a model. XIVTools loads textures from the same mod directory automatically.

### Model Import

1. Click **Open .mdl** and select the game model to replace
2. Click **Import Model** and select your file (`.obj`, `.fbx`, `.glb`, `.gltf`, or `.dae`)
3. Review the mesh list. Each group in your file maps to one mesh in the game model.
4. Choose the target mod and click **Import**

Name your groups or objects to match the original model's mesh names. If names don't match, XIVTools maps by order.

Bone weights are read from the file if present. Skinned characters require bone names to match the original skeleton exactly.

OBJ import has no extra dependencies. FBX, glTF, and DAE files require `libassimp` on Linux; on Windows the library is bundled.

### Item Browser

1. Type in the search box to filter by item name
2. Select an item from the list
3. Click the texture, material, or model button to open it in the relevant viewer

XIVTools builds the item database from the game's Excel sheets on first initialization. Search is instant once the index loads.

### File Search

Search all sqpack file paths by keyword or partial path. Results show the full internal path and containing dat file. Click a result to copy the path.

Use **Rebuild Index** if the index becomes stale after a game update.

### File Override Inspector

Lists every file override in the active Penumbra collection: the game file path, the mod providing it, that mod's priority, and whether the mod is enabled.

If two mods override the same file, the one with the higher priority wins.

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+1` through `Ctrl+0` | Navigate sidebar pages in order |
| `R` | Reset camera (Model Viewer) |

---

## Linux Desktop Integration

Create `~/.local/share/applications/xivtools.desktop`:

```ini
[Desktop Entry]
Name=XIVTools
Comment=FFXIV Mod Manager
Exec=/home/YOUR_USER/XIVTools/app/xivtools-ui
Type=Application
Categories=Game;Utility;
```

Replace `/home/YOUR_USER/XIVTools/` with your install path.

---

## Config File

| Platform | Path |
|----------|------|
| Windows | `%AppData%\xivtools\settings.json` |
| Linux | `~/.config/xivtools/settings.json` |

```json
{
  "GamePath": "/path/to/FINAL FANTASY XIV Online/game",
  "ModsPath": "/path/to/FFXIV Mods",
  "Language": "en"
}
```

Delete this file to reset to defaults.

---

## Building from Source

**Prerequisites:** .NET 10 SDK. On Linux, `libassimp` for FBX/glTF/DAE support.

```bash
git clone https://github.com/BluntEXE/XIVTools
cd XIVTools
cd xivtools-ui
dotnet run -c Release
```

The patched xivModdingFramework source is at `../lib/xivModdingFramework`. The build references it via a local `ProjectReference` — no separate setup needed.

**Build release packages:**

```bash
bash build-release.sh 1.0.0
# xivtools-release/XIVTools-linux-v1.0.0.zip
# xivtools-release/XIVTools-windows-v1.0.0.zip
```

---

## Credits

[xivModdingFramework](https://github.com/TexTools/xivModdingFramework) by the TexTools team. UI: [Avalonia](https://avaloniaui.net/).

---

## License

MIT
