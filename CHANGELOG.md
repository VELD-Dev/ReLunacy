# Changelog

This file lists all the changes of every version. This file is edited constantly during the development, in order not to forget what have been done for X or Y version. It's better to keep it up to date on dev branch.
Because the European date format is better, please keep date format like this: `DD-MM-YYYY`.

## [v0.03](https://github.com/VELD-Dev/ReLunacy/releases/0.03) - DD-MM-2025

[View diff](https://github.com/VELD-Dev/ReLunacy/compare/0.02..0.03)
- Rewrote entirely the file reading library. Reading speeds have been significantly improved, especially on old-engine levels.
- Made a new rendering engine, running under OpenGL 4.4.0, based on Replanetizer's rendering engine.
- Updated ImGui.NET version from 1.89 to 1.91, fixing some bugs and improving the UI.
- Rewrote the ImGuiController for the new version of ImGui.NET, credits to @NogginBops for the code, it greatly helped, because the original one was totally broken.
- Updated .NET version from 8.0 to 9.0, improving performances.
- Added Frustrum Culling, improving performances on levels.
- Made a new transform system, fixing previous rotation bugs.
- Added a new logging system, logging everything to a file (except the errors, will be fixed in a future release)
- Added a new `Logs` frame, showing the logs output.
- Created internal Entity types based off their real types, increasing editor flexibility and reliability.
- [TO BE DONE] Added a new `Asset view` frame, allowing to isolate an entity and its data on a separate frame, showing its model in its own frame. It will also allow to export objects as `.gltf`, `.obj` and `.dae` models in the future.
- Added bases for animations and animations viewing in the Asset view.
- Added basic transform tools to interact with assets directly from the 3D View.
- Added frames rounding and removed frames borders, making the UI more modern and pleasant for the eyes.
- [TO BE DONE] Edited Update frame, it will now show a frame telling that there is no newer version too.
- Added a loading modal when loading a level, showing the precise progress of the level loading, with all the detailed steps.
- Added UFrags support for old-engine levels (ToD, QfB) (no gaps on the old engine, good news !)
- Modified the rendering technique, will now render elements individually instead of using instancing. Slightly decreases performances but allows for more flexibility, especially when moving objects or when selecting objects.
- Fixed a bug that was showing a padding or a black border around the whole viewport.
- Fixed black borders around the 3D view inside the frame.

## [v0.02](https://github.com/VELD-Dev/ReLunacy/releases/0.02) - 24-08-2024

[View diff](https://github.com/VELD-Dev/ReLunacy/compare/0.01.2..0.02)
- Added selection system, when clicking an entity, it will be selected and highlighted in the 3D View.
- Added a new `Instance Properties` frame that allows to move, rotate, rescale and teleport camera to entity in the 3D View. It also shows the name of the entity and its internal instance ID.
- Added a new `Entity Explorer` frame that allows to search through all entities. Clicking an element will select it in 3D View and show its properties on Instance Properties frame.
- New logo, made by **Nooga** !
- New resource manager, allowing for future implementation of editor assets like icons, textures, gizmos, etc.
- Added a simple shortcut support.
- Changed camera rotation system
- Changed rendering method, allowing for entity selection
- Rewrote entirely the input system. Navigating in 3D space is now less messy, and inputs won't be detected for movements if the 3D View is not focused.
- Fixed overlay showing wrong camera position (inverted)

## [v0.01.2](https://github.com/VELD-Dev/ReLunacy/releases/0.01.2) - 22-08-2024

[View diff](https://github.com/VELD-Dev/ReLunacy/compare/0.01.1..0.01.2)
- Fixed Update Checker that was previously failing to check updates. Now it works.

## [v0.01.1](https://github.com/VELD-Dev/ReLunacy/releases/0.01.1) - 22-08-2024

[View diff](https://github.com/VELD-Dev/ReLunacy/compare/0.01..0.01.1)
- Added a counter for the loading of zones of the old engine because some levels have really, really lots of zones, resulting in a very long loading, so while I have no solution to speed up the loading, i'll keep it like that.
- Fixed previous level's volumes not clearing when loading a new level, resulting in a shitton of volumes visualized that had nothing to do there
- Fixed the "Total Entities" stats not clearing correctly when loading or closing a level, resulting in an always-growing number that does not reflect the true amount of entities the engine actually handles.
- Fixed old-engine expensive texture assignment; We still haven't reversed them afaik, so the level editor was crashing when loading old-engine levels as there's no expensive textures loaded in the database, despite having references to them.
- Fixed using commands to load levels on `ReLunacy.exe`: Previously, loading a level from a command argument when launching the level editor was not working, now it does.
- Changed the colour of volumes from yellowish to white.
- Removed debug leftovers on stats overlay.

## [v0.01](https://github.com/VELD-Dev/ReLunacy/releases/0.01) - 22-08-2024

[View diff](https://github.com/VELD-Dev/ReLunacy/commits/0.01)
- Rewrote entirely Lunacy: ReLunacy has born!
- It's now possible to see UFrags on post-ACIT games.
- It's now possible to see Volumes on post-ACIT games.