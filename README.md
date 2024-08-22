<h1>
<p align="center">
  <!--<img src="media/ReplanetizerIcon500px.ico" alt="Logo" width="110" height="110" title="Logo made by Nooga.">  Replanetizer leftovers :p-->
  <p align="center" style="font-weight: bold">ReLunacy</p>
</h1>
  <p align="center">
    Level Editor for the Ratchet and Clank: Future Series and Resistance PS3 opuses
    <br/>
    </p>
</p>
<p align="center">
  <a href="#features">Features</a> •
  <a href="#usage">Usage</a> •
  <!--<a href="#building">Building</a> •
  <a href="#technology">Technology</a> •
  <a href="#licensing">Licensing</a> •
  <a href="CONTRIBUTING.md">Contributing</a>-->
</p>

<p align="center">
  <img src="media/preview.gif" alt="Preview">
</p>

## Features

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

## Building
* cd into the directory with the `Lunacy.sln` file
* Run `dotnet build`

## Running

### ReLunacy

- Run `ReLunacy.exe` by double-clicking it, or execute `./ReLunacy.exe <path to level folder>`.
- From the menu bar, access `File > Load Level` and enter the path to the level folder.
  - The folder is the folder that contains either `main.dat` or `assetlookup.dat` file.
  - If `assetlookup.dat` is there, `highmips.dat` from `level_uncached.psarc` must be included as well.
- Controls:
  - `[RMB]+[Move]` to look around
  - `[W][A][S][D]` or `[Z][Q][S][D]` to move arond (depending on locale)
  - `[SHIFT]` to move faster
  - `[E][Q]` or `[E][A]` to move up and down (depending on locale)

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
