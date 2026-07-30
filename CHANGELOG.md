# Changelog

This file lists all the changes of every version. This file is edited constantly during the development, in order not to forget what have been done for X or Y version. It's better to keep it up to date on dev branch.
Because the European date format is better, please keep date format like this: `DD-MM-YYYY`.

## [v0.04.1](https://github.com/VELD-Dev/ReLunacy/releases/0.04.1) - 30-07-2026

[View diff](https://github.com/VELD-Dev/ReLunacy/compare/0.04..0.04.1)

### Lighting (the big one)

ReLunacy can finally render levels **lit**, instead of showing raw unlit textures. The new shader is a
reproduction of the game's own fragment shader, reverse-engineered from a real capture of Tools of
Destruction running in RPCS3, so it's not an approximation of "what looks nice", it's the game's
actual maths. **It's a first step to a replica of ToD** *(and QfB)* **visuals !**

- Added an optional **lit renderer**, off by default: enable it in `Editor Settings` > `Lighting (experimental)`.
- **Baked lightmaps on terrain (UFrags)**: the game's baked light colour and light direction atlases are
  now loaded, bound and sampled, through the terrain's own second UV set. This is what carries all of
  the level's baked shadows and coloured bounce light.
- **Normal maps** are now composed the way the game does it (as partial derivatives, added together
  rather than blended), and their inverted orientation has been fixed, in the renderer *and* in glTF export.
- **Parallax / height offset**, using the scale and bias values read from each shader's own metadata.
- **Detail maps** are now applied, contributing both normal detail and specular, with the per-shader
  tiling factor read from the shader metadata. Worth noting that the detail map is still experimental and might look
  very weird in some cases.
- **Environment/ambient approximation** averaged from the level's own cubemap. (very experimental)
- Materials are now cached per instance rather than per shader, so terrain fragments that share a
  shader but have different bakes no longer collapse onto one shared lightmap.

#### Lighting limitations, please read

- **Lighting is currently only supported on Ratchet & Clank: Tools of Destruction and Quest For Booty.**
  Post-ACiT levels store their baked lighting data somewhere else, which hasn't been reversed yet, so
  using lighting on them will definitely look wrong. Resistance: Fall of Man is untested but should work.
- **Ties are not lightmapped yet.** Tie instances do carry a bake index, but their lightmap UVs haven't
  been located yet (they live outside the tie vertex record, in a separate vertex stream), so ties
  currently render without their baked lighting. Work in progress.
- **There are still a very few artifacts on UFrag lightmaps.** Some UFrags might be rendered unlit or with weird
  colors. I'm still investigating this.
- **There is no dynamic lighting, on purpose**: in this game every shadow is baked, so the scene light
  direction / colour / specular power settings only affect geometry that has no bake of its own. Once
  the lighting will be done, only the mobys will have dynamic shadows and yet, not all of them. (following
  the original game's shading)

### Other changes

- Added a **texture filtering** setting for the 3D view: bilinear by default (what the PS3 actually does),
  with point/nearest still selectable for pixel-peeping raw texel data.
- Texture previews in the UI (Texture Explorer, thumbnails) are now sampled linearly instead of looking blocky.
  (may change in the future)
- `Shader Browser`: added live-editable parallax scale/bias and detail map tiling & strengths, plus a raw
  shader metadata hex dump with a big-endian float view, to help reverse the remaining unknown fields.
- `Asset View`: added export buttons for **Moby and Tie** assets, and a separate `.gltf` export option
  (writing the `.gltf` + `.bin` + textures into its own folder) alongside the existing single-file `.glb`.
- `Asset View`: added a new **UFrags** tab. Terrain fragments can now be browsed, previewed, exported, and
  their baked lighting inspected individually; lightmap index, UV rectangle, and both atlases with the
  fragment's UV island drawn on top of them. UFrags are the only asset type whose bake can be inspected
  out of context, since each one is its own single placement.
- `Instance Properties`: "teleport camera to entity" now places the camera just short of the selected
  entity instead of pushing it past/away from it.
- Fixed tie instance data not being read at all on old-engine levels, which silently discarded each
  instance's baked lighting index.

## [v0.04](https://github.com/VELD-Dev/ReLunacy/releases/0.04) - 26-07-2026

[View diff](https://github.com/VELD-Dev/ReLunacy/compare/0.03..0.04)
- Rewrote entirely the file reading library. Reading speeds have been significantly improved, especially on old-engine levels.
- Switched rendering engine to [Bliss Rendering Framework](), running the app under **Vulkan**.
- Updated ImGui.NET version from 1.89 to 1.91.6, fixing some bugs and improving the UI.
- New ImGuiController, copied from Sparkle Engine.
- Updated .NET version from 8.0 to 9.0, improving performances.
- Added Frustrum Culling, improving performances on levels.
- Added a new logging system, logging everything to a file (except the errors, will be fixed in a future release)
- Added a new `Logs` frame, showing the logs output.
- Created internal Entity types based off their real types, increasing editor flexibility and reliability.
- Added a new `Asset View` frame, allowing to isolate an entity and its data on a separate frame, showing its model in its own frame. Objects can now be exported as `.gltf`/`.glb` and `.obj` (`.dae` still not supported).
- Added a new `Texture Explorer` frame, allowing to inspect a texture and its data on a separate frame, and export its raw data or export them as `.bmp` and `.png`
- Added bases for animations and animations viewing in the Asset view.
- Added transform tools (move/rotate/scale gizmos, via ImGuizmo) to interact with assets directly from the 3D View, including translation/rotation/scale snapping settings.
- Added frames rounding and removed frames borders, making the UI more modern and pleasant for the eyes.
- Edited Update frame: it now supports a Stable/Nightly update channel setting and properly notifies when no newer version is available.
- Added a loading modal when loading a level, showing the precise progress of the level loading, with all the detailed steps.
- A few fixes for UFrags on old engine.
- Rewrote Textures reading, it is much faster than before.
- Rewrote the Asset Loader, the new one loads levels much faster and has everything explicitly defined, making it easier to understand as contributor.
- Modified the rendering technique thanks to Bliss, will now render elements individually instead of using instancing. Slightly decreases performances but allows for more flexibility, especially when moving objects or when selecting objects.
- Fixed a bug that was showing a padding or a black border around the whole viewport.
- Fixed black borders around the 3D view inside the frame.
- Instance Properties frame now shows the vertices of the object (will be moved to Asset View).
- Reducing far clip distance should now increase performance as it now unloads objects that are further this distance (which was not the case before, it was just not showing them but they were still rendering)
- Updated Entity Explorer frame's search bar: It will now update the output only when pressing "enter", and above that the search results are now cached, improving considerably performances.
- Merged LibLunacy (asset-format library) and its archive I/O directly into ReLunacy.Engine instead of referencing them as separate assemblies, fully retargeted to Bliss 1.6.15/Veldrith; reorganized loading/asset code under clearer `Loading`/`Assets`/`Scene`/`Rendering`/`Games` namespaces and removed unused legacy engine code left over from the old architecture.
- Added `.gltf`/`.glb` and `.obj` model export (with `.mtl`/`.png`), including a new whole-level export (`Export Level`) that bundles every placed instance into a single scene file. Export runs in the background with a progress modal and an "open containing folder" action when done. Mobys with skeletons now always export as static/rigid meshes at level scope (this avoids a crash from colliding bone names across placed instances) and the exported level file is named after the level's own folder instead of the raw `level_cached`/`level_uncached` archive filename.
- Added skinned-mesh/skeleton export support (joint extraction, bone-weighted meshes) for single-asset exports.
- Implemented a custom `DecalAwareForwardRenderer`, fixing z-fighting on alpha-blended decal textures (moss/vines painted onto terrain) by disabling depth *writes* (while keeping depth *testing*) for translucent geometry instead of Bliss's default renderer, which hardcoded depth writes for everything.
- Implemented Moby per-instance render/display distance culling (reverse-engineered from real gameplay data), toggleable from the Render menu — off by default since the free-fly editor camera doesn't share the game's player-anchored camera assumptions.
- Added Volume selection in the 3D viewport: proper GPU-buffer picking against the volume's actual wireframe edges (not a solid hitbox, so clicking empty interior space no longer selects a volume), with configurable wire thickness and unselected/selected colors in Editor Settings, and volume metadata (ID/group) now shown in the Property Inspector.
- Added a configurable Selection Outline color in Editor Settings.
- Expanded supported texture formats (R8, A1R5G5B5, RGBA4, RGBA16F, BC4, BC5, G8B8), fixed imprecise RGB565 channel expansion, and added linearization handling for Morton-swizzled textures.
- Native vertex tangent decoding straight from source mesh data, replacing the previous derived/approximated tangents, for more accurate normal-mapped rendering.
- Added a nightly build pipeline (GitHub Actions, Windows + Linux artifacts, rolling release).
- Relicensed the project under the GNU GPL v3.
- README overhaul: replaced the demo GIF with an embedded, autoplaying video and cleaned up the licensing section.

## [v0.03](https://github.com/VELD-Dev/ReLunacy/releases/0.03) - 23-05-2025

[View diff](https://github.com/VELD-Dev/ReLunacy/compare/0.02..0.03)
- Greatly improved loading times for old engine levels
- Fixed some levels that couldn't load because of a texture format misimplemented
- Changed transform system, it has broken some rotations. This will be addressed in a future version.
- Fixed overlay causing stutters
- UFrags can now be selected in the 3D View
- Ties can now be moved (normally)
- Updated styles: smoother UI, round corners, and more.
- Added a loading popup for more convenience when loading levels.
- Added texture transparency support.
- UFrags now load on pre-ACIT games ! (Resistance: Fall of Man, Ratchet & Clank: Tools of Destruction & Quest For Booty)

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