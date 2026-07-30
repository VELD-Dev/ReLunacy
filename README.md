<h1>
    <p align="center">
        <img src="media/logo.svg" alt="Logo" width="200" height="200" title="Logo made by Nooga.">
        <p align="center" style="font-weight: bold">ReLunacy</p>
    </p>
</h1>
<h3>
    <p align="center">
        Level Editor for the Ratchet and Clank: Future Series and Resistance PS3 opuses
    </p>
</h3>
<p align="center" style="font-weight: 300;">
    <a href="#compatibility">Compatibility</a> •
    <a href="#features">Features</a> •
    <a href="#prerequisites">Prerequisites</a> •
    <a href="#usage">Usage</a> •
    <a href="#clone_build">Clone & Build</a> •
    <a href="#running">Running</a> •
    <a href="#notes">Notes</a> •
    <a href="./LICENSE">License</a> •
    <a href="#credits">Credits</a>
    <a href="CONTRIBUTING.md">Contributing</a>
</p>

<p align="center">
    <video src="https://github.com/user-attachments/assets/9031135c-a13b-4e37-a829-c0ffb7aa1ce6" autoplay muted loop playsinline></video>
</p>

<p align="center">
    <img alt="GitHub License" src="https://img.shields.io/github/license/VELD-Dev/ReLunacy?style=for-the-badge">
    <img alt="GitHub Release" src="https://img.shields.io/github/v/release/VELD-Dev/ReLunacy?style=for-the-badge&color=00DD00">
    <img alt="GitHub Downloads (all assets, all releases)" src="https://img.shields.io/github/downloads/VELD-Dev/ReLunacy/total?style=for-the-badge&label=tot.%20downloads&color=7E7EDD">
</p>

<h2 id="compatibility">✅ Compatibility</h2>

| Game                                  | Engine Version | Lighting | Textures | Shaders/Materials | Mobys | Ties | UFrags | Shrubs | Foliages | Particles | Volumes & Triggers |
|---------------------------------------|:--------------:|:--------:|:--------:|:-----------------:|:-----:|:----:|:------:|:------:|:--------:|:---------:|:------------------:|
| Resistance: Fall Of Man               |      Old       |    ✅    |    ✅    |         ✅         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |
| Ratchet & Clank: Tools of Destruction |      Old       |    ✅    |    ✅    |         ✅         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |
| Ratchet & Clank: Quest for Booty      |      Old       |    ✅    |    ✅    |         ✅         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |
| Resistance 2                          |      New       |   ⚠³    |   ✅¹    |         🚧         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |
| Ratchet & Clank: A Crack In Time      |      New       |   ⚠³    |   ✅¹    |         🚧         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |
| Resistance: Retribution               |      New       |   ⚠³    |   ✅¹    |         🚧         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |
| Ratchet & Clank: All 4 One            |      New+      |   ⚠³    |   ✅²    |         🚧         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |
| Resistance 3                          |      New+      |   ⚠³    |   ✅²    |         🚧         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |
| Ratchet & Clank: Full Frontal Assault |      New+      |   ⚠³    |   ✅²    |         🚧         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |
| Resistance: Burning Skies             |      New+      |   ⚠³    |   ✅²    |         🚧         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |
| Ratchet & Clank: Into the Nexus       |      New+      |   ⚠³    |   ✅²    |         🚧         |   ✅   |  ✅   |   ✅    |   ❌    |    ❌     |     ❌     |         ✅          |

Notes:
- **¹** : Textures are supported but some artifacts remain.
- **²** : Textures are supported but some artifacts remain and some meshes have wrong UVs
- **³** : Lighting/Shading will "work" but it will NOT look correct at all. Lighting is currently only supported on ToD, QfB and R:FoM.

<h2 id="features">🌙 Features</h2>

ReLunacy is still in early development, but it already comes with a solid set of features, modders and speedrunners will really like it.

- **Level viewing**
  - Renders Mobys, Ties, UFrags (terrain) and Volumes, each independently toggleable from the `Render` menu.
  - Experimental backface culling and experimental decal-aware translucent rendering (depth-tested but not depth-written, so decals like moss/vines don't z-fight with the terrain underneath).
  - Frustum culling with an optional bounding-sphere debug overlay.
- **Asset browsing & inspection**
  - **Asset Viewer**: a dedicated 3D preview for individual Mobys/Ties, with a searchable/filterable asset tree, per-bangle visibility toggles, skeleton overlay for skinned Mobys, GPU mesh/bangle picking, a read-only per-vertex data inspector, and one-click "Find Usages" to jump to placed instances in the level.
  - **Textures Explorer**: browse every loaded texture, isolate individual R/G/B/A channels, inspect format/dimensions, and find which shaders/assets use a given texture.
  - **Shader Browser**: inspect every parsed material — decoded texture references (Albedo/Normal/Expensive/Detail Map), rendering mode, and a hex dump of still-unidentified metadata for reverse-engineering.
  - **PSARC Explorer**: browse, search and extract files directly out of `.psarc` archives, without extracting the whole archive first.
  - **Game Browser**: scans a game install (folder or `.psarc` archives) and lists every level it finds, auto-detecting old-engine (Tools of Destruction, Quest for Booty) vs new-engine (A Crack in Time, Full Frontal Assault, All 4 One, Into the Nexus) titles.
- **Editing**
  - Translate/Rotate/Scale gizmo (world or local space, with configurable snapping) for placed Mobys, Ties and Volumes.
  - Direct numeric Position/Rotation/Scale editing from the Property Inspector.
  - This is placement editing for the current session/export, not a level format writer yet. There's no "Save Level" that writes changes back into the game's own files.
- **Export**
  - Export individual Mobys/Ties, or a whole level, to **glTF** (`.glb`), whole-level export uses true mesh instancing (each unique asset's geometry is stored once, referenced by every placed instance reducing file size) and includes skeletons/skinning for animated Mobys, as well as pre-configured materials for exportation.
  - Export individual Mobys/Ties to **Wavefront OBJ** (`.obj`) (with materials and textures; no skeleton support).
  - Export individual textures as PNG or raw pixel data.
- **Format support**
  - Both old-engine and new-engine PS3 titles, loaded either from a pre-extracted folder or directly out of `.psarc` archives.
  - Broad texture format coverage: R8, R5G6B5, A1R5G5B5, A8R8G8B8, DXT1, DXT3, DXT5, BC4, BC5, G8B8, RGBA4 and RGBA16F.
  - Optional `texstream.dat`/`debug.dat` side-loading for higher-resolution textures and asset/instance names (see [Notes](#notes)).
- **Flexible editor**
  - Dockable UI you can rearrange to fit how you work.
  - Deep Editor Settings: graphics backend (thanks to Bliss framework), VSync/framerate cap, MSAA, camera speed/FOV/sensitivity, gizmo size/snap, stats overlay (FPS, profiler, level/camera info), and more.
  - Localization-ready (currently ships with English).
  - Built-in update checker with Stable and Nightly channels.

<h2 id="prerequisites">⚠️ Prerequisites</h2>

Here are essential things you need to run **ReLunacy**, without those, the app might be slow or could just not run at all.
- (Windows) [**.NET 10.0 Desktop Runtime**](https://dotnet.microsoft.com/fr-fr/download/dotnet/thank-you/runtime-desktop-10.0.10-windows-x64-installer)
- (Linux/Mac) [**.NET 10.0 Runtime**](https://dotnet.microsoft.com/fr-fr/download/dotnet/10.0#runtime-10.0.10)
- A 64bits (x64) OS... I mean... who uses a x32 device in 2026 ?

<h2 id="usage">⌨️ Usage</h2>

In first place, you need to extract the game you want to inspect the level of, with a tool like **PS3GameExtractor** or simply by using **RPCS3**. This gives you the game's `USRDIR` folder.

From there, you don't need to extract anything else by hand anymore. Open ReLunacy, go to `File > Game Browser`, paste (or browse to) the path to the `USRDIR` folder and click `Scan`. It reads levels straight out of the packed `.psarc` archives (or from an already extracted folder, both work), lists everything it finds, and you just click `Load` next to the level you want.

If you know exactly which level you want and prefer to type a path yourself, `File > Open level` still works too, it takes a path to a level's `main.dat` (old engine) or `assetlookup.dat` (new engine), inside an extracted folder or a `.psarc` archive.

Controls:
- Keybindings:
  - `[W][A][S][D]` / `[Z][Q][S][D]` to move around, depending on your keyboard.
  - `[E][Q]` / `[E][A]` to go up and down, depending on your keyboard.
  - `[SHIFT]` to move faster (sets move speed to Editor Settings's max speed).
  - `[RMB]`+`[Move Mouse]` to look around.
  - `[MMB]`+`[Move Mouse]` to pan the camera around (drags the orbit point with it).
  - `[W]` `[E]` `[R]` to switch between the Translation, Rotation and Scale tools.
  - `[ESC]` to deselect the current object(s).
- Miscellaneous:
  - `File > Game Browser` to scan a game's `USRDIR` and pick a level to load from the list.
  - `File > Open level` to load a level by typing or pasting its path directly.
  - `File > Export Level...` to export the whole loaded level to glTF.
  - `File > Close level` to close the level.
  - `Edit > Editor Settings` to open settings of the editor.
  - `Tools > Translation` / `Rotation` / `Scale` to select the transform tool for the gizmo.
  - `Tools > Deselect Object(s)` to deselect all the selected objects.
  - `View > Show Overlay` to show the stats overlay (FPS, level stats, camera info...).
  - `View > 3D View` to open or close the 3D View window.
  - `View > Entity Explorer`, `Asset Viewer`, `Textures Explorer`, `Shader Browser`, `Properties Inspector`, `PSArc Explorer` and `Logs` to open the other editor windows.
  - `Render > Mobys` to render or not Mobys.
  - `Render > Ties` to render or not Ties.
  - `Render > UFrags` to render or not UFrags.
  - `Render > Volumes` to render or not volumes.
  - `Render > Bounding Spheres` to show or hide the debug bounding spheres.
  - `About > Official Github` to reach this github page.
  - `About > Check for update` to check for updates. If nothing pops up, then you're up to date.
  - The Stats Overlay can be customized inside `Edit > Editor Settings > Overlay settings`.
  - The interface language can be changed inside `Edit > Editor Settings > Visual settings`.

<h2 id="clone_build">Clone & Build</h2>

- Clone the repo with `git clone https://github.com/VELD-Dev/ReLunacy.git --recursive` (add `-b dev` if you want to use branch dev)
- After cloning, make sure to run `git submodules update --recursive` to update the external dependencies (LibreFios).
- cd into the directory with the `ReLunacy.sln` file
- Run `dotnet build ReLunacy`

## Running

- Run `ReLunacy.exe` by double-clicking it, or execute `./ReLunacy.exe <path to level folder or .psarc file>`, it will load that level right on startup.
- Or just run it with no argument and use `File > Game Browser` or `File > Open level` from the menu bar, as described above in Usage.
  - If you point it at a folder, that folder needs to contain either `main.dat` or `assetlookup.dat`.
  - If `assetlookup.dat` is there, `highmips.dat` from `level_uncached.psarc` must be included as well, unless you're loading the level's `.psarc` directly, in which case ReLunacy picks up `level_uncached.psarc` on its own.

Lunacy (the old standalone viewer) and AssetExtractor have both been retired. Everything they used to do now lives in ReLunacy itself.

## Notes

* Including the `texstream.dat` file in the same place as `main.dat` will improve texture resolution (found in `level_textures.psarc`)
* Including the `debug.dat` file for a level in the same place as `main.dat` will include asset and instance names

## Credits

Of course, I haven't been working alone on this, I wrote ReLunacy, but it would be very impolite to not credit everyone who's involved!
- [**@NefariousTechSupport**](https://github.com/NefariousTechSupport?tab=repositories) is the original developer of Lunacy, they are also an important reverse engineer for the PS3 games. The new renderer is inspired by their 7th [igRewrite](https://github.com/NefariousTechSupport/igRewrite7) (a Skylander level editor)
- [**@chaoticgd**](https://github.com/chaoticgd) greatly helped me with the 3D rendering
- [**@MilchRatchet**](https://github.com/MilchRatchet): I strongly inspired the global level editor from [Replanetizer](https://github.com/RatchetModding/Replanetizer), of which they are the maintainer, mostly for the Frames systems.
- [**@PredatorCZ**](https://github.com/PredatorCZ) is one of the pioneer of Ratchet & Clank: Future Series reverse engineer, it makes sense that a lot of the code is based on their [InsomniaToolset](https://github.com/PredatorCZ/InsomniaToolset).
- **@Nooga** is the artist that made ReLunacy's Logo
- [**@Neigy**](https://github.com/Neigy) Made huge reverse engineering progresses especially regarding textures, volumes and triggers. Also helped a lot for ReLunacy's ultimate rewrite.

## Contributing

Contributions are welcome but they must follow a few rules !
1. Follow the repository codestyle
2. Make pull requests with small features !
3. Rewrites, "19k+ edited lines" and such pull requests will be **INSTANTLY REJECTED**
4. If code is written by AI, control it enough to avoid making breaking changes, and **DO NOT MAKE HUGE CHANGES**. Those will be instantly rejected as well.
5. AI is okay as long as **you know what you're doing**. If you have 0 knowledge of the codebase or of the game architecture, please **refrain from making pull requests**. We do not need sloppy code that none's able to debug.