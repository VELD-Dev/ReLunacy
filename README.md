<h1>
    <p align="center">
        <!--<img src="media/Logo.svg" alt="Logo" width="110" height="110" title="Logo made by Nooga.">  Replanetizer leftovers :p-->
        <p align="center" style="font-weight: 1000">ReLunacy</p>
    </p>
</h1>
<p align="center" style="font-size: 20px; font-weight: 500;">
    Level Editor for the Ratchet and Clank: Future Series and Resistance PS3 opuses
</p>
<hr style="margin: 0 auto 1em auto; width: 40%; height: 1px;">
<p align="center" style="font-weight: 300;">
    <a href="#features">Features</a> •
    <a href="#prerequisites">Prerequisites</a> •
    <a href="#usage">Usage</a> •
    <a href="#building">Building</a> •
    <a href="#running">Running</a> •
    <a href="#notes">Notes</a> •
    <a href="./LICENSE">License</a> •
    <a href="#credits">Credits</a>
    <!--<a href="#technology">Technology</a> •
    <a href="CONTRIBUTING.md">Contributing</a>-->
</p>

<p align="center">
    <img alt="Demo" src="media/demo.gif">
    <img alt="GitHub License" src="https://img.shields.io/github/license/VELD-Dev/ReLunacy?style=for-the-badge">
    <img alt="GitHub Release" src="https://img.shields.io/github/v/release/VELD-Dev/ReLunacy?style=for-the-badge&color=00DD00">
    <img alt="GitHub Downloads (all assets, all releases)" src="https://img.shields.io/github/downloads/VELD-Dev/ReLunacy/total?style=for-the-badge&label=tot.%20downloads&color=7E7EDD">
</p>

<h2 id="features">🌙 Features</h2>

ReLunacy is still in early development, but it comes with very interesting features, modders and speedrunners will really like it.

- Visualize levels
  - Mobys, Ties and UFrags (terrain)
  - Triggers & Volumes
  - Light sources (WIP)
- Editing (WIP)
- Export (WIP)
  - Thanks to [@NefariousTechSupport](https://github.com/NefariousTechSupport?tab=repositories), the AssetExtractor tool works for the essential things, but I will soon be implementing a better exportation tool directly in the level editor.
- Flexible editor
  - Almost everything in the editor can be edited in Editor settings; camera speed, field of view, overlay, stats profiler, renderer...
  - Dockable UI made for people who like to organise their work.
  - Comfortable and user-friendly; the editor is made to be easy to use.
  - (WIP) Customize the rendering by adding your own shaders to the editor.

<h2 id="prerequisites">⚠️ Prerequisites</h2>

Here are essential things you need to run **ReLunacy**, without those, the app might be slow or could just not run at all.
- (Windows) [**.NET 8.0 Desktop Runtime**](https://download.visualstudio.microsoft.com/download/pr/907765b0-2bf8-494e-93aa-5ef9553c5d68/a9308dc010617e6716c0e6abd53b05ce/windowsdesktop-runtime-8.0.8-win-x64.exe)
- (Linux/Mac) [**.NET 8.0 Runtime**](https://dotnet.microsoft.com/fr-fr/download/dotnet/8.0#runtime-8.0.8)
- A 64bits (x64) OS... I mean... 32bits (x32) OS doesn't exist anymore right ?

<h2 id="usage">⌨️ Usage</h2>

In first place, you need to extract the game you want to inspect the level of, with a tool like **PS3GameExtractor** or simply by using **RPCS3**.  

Then, you need a tool to extract the `.psarc` files, **PS3GameExtractor** can do it, but otherwise use [**PSArcTool**](https://github.com/periander/PSArcTool) by Periander.  

Reach the level of your choice inside `/packed/levels/<level_name>/` and extract `level_cached.psarc` and `level_uncached.psarc` with one of the previous tools.  

Finally, go to the extracted files `/packed/levels/<level_name>/built/levels/<level_name>/` and copy the full address, and paste it in the `File > Open File` dialog frame.  
  
Controls:
- Keybindings:
  - `[W][A][S][D]` / `[Z][Q][S][D]` to move around, depending on your keyboard.
  - `[E][Q]` / `[E][A]` to go up and down, depending on your keyboard.
  - `[SHIFT]` to move faster (sets move speed to Editor Settings's max speed).
  - `[RMB]`+`[Move Mouse]` to look around.
- Miscellaneous:
  - `File > Open Level` to open a level.
  - `File > Close Level` to close a level.
  - `Edit > Editor Settings` to open settings of the editor.
  - `Tools > Translation` to select the translation tool (move objects).
  - `Tools > Rotation` to select the rotation tool (rotate objects).
  - `Tools > Scale` to select the scale tool (rescale objects).
  - `Tools > Deselect Object(s)` to deselect all the selected objects.
  - `View > Show Overlay` to show the stats overlay (FPS, level stats, camera info...).
  - `View > View 3D` to open or close the 3D View window.
  - `Render > Mobys` to render or not Mobys.
  - `Render > Ties` to render or not Ties.
  - `Render > UFrags` to render or not UFrags.
  - `Render > Volumes` to render or not volumes.
  - `About > Official Github` to reach this github page.
  - `About > Check for update` to check for updates. If nothing pops up, then you're up to date.
  - The Stats Overlay can be customized inside `Edit > Editor Settings > Overlay settings`.
  - The interface language can be changed inside `Edit > Editor Settings > Visual settings`.

## Building
* cd into the directory with the `Lunacy.sln` file
* Run `dotnet build`

## Running

### ReLunacy

- Run `ReLunacy.exe` by double-clicking it, or execute `./ReLunacy.exe <path to level folder>`.
- From the menu bar, access `File > Load Level` and enter the path to the level folder.
  - The folder is the folder that contains either `main.dat` or `assetlookup.dat` file.
  - If `assetlookup.dat` is there, `highmips.dat` from `level_uncached.psarc` must be included as well.

### Lunacy [Deprecated]

- Run `Lunacy.exe <path to folder>`, where the folder contains either the `main.dat` file or the `assetlookup.dat` file
- if `assetlookup.dat` is there, `highmips.dat` from `level_uncached.psarc` must be included as well
- Controls are as such:
  - `[RMB]`+`[Move]` to look around
  - `[W][A][S][D]` or `[Z][Q][S][D]`to move, `[SHIFT]` to move faster
  - `[P]` shows the names of all objects that the mouse is hovering over
  - Select an object in either the regions or zones windows to teleport to that object
- Add the command argument `--load-ufrags` to load UFrags in Lunacy Legacy.

### AssetExtractor

* Run `AssetExtractor.exe <path to folder>`, where the folder contains either the `main.dat` file or the `assetlookup.dat` file
* If `assetlookup.dat` is there, `highmips.dat` from `level_uncached.psarc` must be included as well
* Assets will be found in the inputted folder

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
