# Changelog

This file lists all the changes of every version. This file is edited constantly during the development, in order not to forget what have been done for X or Y version. It's better to keep it up to date on dev branch.
Because the European date format is better, please keep date format like this: `DD-MM-YYYY`.

## [v0.06-alpha.2](https://github.com/VELD-Dev/ReLunacy/releases/0.06-alpha.2) - 22-11-2026

- Added **Resistance: Fall of Man** support.
  - Currently supported: Ties, UFrags, Mobys, Volumes, Paths/Splines, Clue, Controllers, Baked Lightmaps, Directional Lighting, Reflection Cubemaps, Instance names (when `ps3debug.dat` has the required data) and Instance Parameters basic support.
  - Currently **NOT** supported: Level Editing, Effects, Foliages, Shrubs, Sound Banks, Model Importation, Collision Importation.
- New level editing and saving on A Crack in Time, Full Frontal Assault, All 4 One and Into the Nexus: Mobys, Ties, UFrags, volumes, cameras, paths, and Clue and Moby settings.
- New project system (`.lunaproj`): File > Save keeps your edits in a project, and Install to Game writes them into the game.
- New Asset Explorer (View menu): drag Mobys and Ties into the 3D view to place them. Favourites are listed first.
- New Assets Browser (View menu) with cached thumbnails, asset references, and dedicated texture and shader viewers. Preview shaders and swap them in the Asset Viewer.
- New Create menu: volumes, paths, controllers, Pods, Areas and Scents.
- New model import (File > Import > Model...) turns a glTF model into a new Tie with collision in A Crack in Time. Import textures and generate shaders, with manual texture assignments available.
- New deletion and renaming of instances, Pods, Areas and Scents.
- New layers and zones can be added to a level.
- New Create > Moon and moon edit mode in A Crack in Time's space sectors.
- New collision viewer and editor on every game (viewport toolbar): colours, filters, and editable materials and flags.
- New planet occlusion tools: Bake > Bake occlusion for placed and moved Ties and UFrags, a "Never culled" option, and a view of the occlusion cells.
- New undo and redo (Ctrl+Z, Ctrl+Y).
- New multiple selection (Shift+click, Ctrl+click, Ctrl+A in the hierarchy).
- New hierarchy: one tree by layer, with a show/hide eye on every row.
- New volume display: real shapes (box, sphere, cylinder, capsule), real size, and colours by kind (death, lava, water...).
- New path editing: keys, handles, adding and removing keys.
- New camera display, with Camera POV to look through a camera.
- New Property Inspector fields: named and typed for A Crack in Time's classes, with links to the instances they name.
- New Pods window (View menu).
- New Config Tables (View menu): edit the game's configuration spreadsheets.
- New Text Tables (View menu): edit the game's text in every language.
- New Sound Banks (View menu) for supported old-engine games and A Crack in Time. Browse sounds by mixer group, seek through full songs with waveform markers, and export audio in up to nine formats depending on the platform.
- New effects playing in the 3D view, and an Effects tab in the Asset Viewer.
- New Backups window (File > Backups...).
- New startup modal and button to create desktop shortcut (compatible Linux & Windows)
- Added a gizmo toolbar: move, rotate, scale, World/Local space, snapping.
- Added viewport toolbar buttons for lighting, game shaders, navmesh, collision and each object type.
- Added the space sector boundary and the Aphelion's fly height to the 3D view.
- Added a space sector's boundary radius and the level's settings to Level Data.
- Added radius previews for space moons.
- Added colours by role for volumes and paths.
- Added icons for controllers in the 3D view.
- Added Lua scripts of A Crack in Time levels to the Lua environment.
- Added Lua API documentation for A Crack in Time, and an RPCS3 patch that brings back its Lua text output.
- Added bloom and fog to A Crack in Time.
- Added a Linux desktop launcher and fixed the taskbar icon.
- Improved level loading: one region at a time (Render > Region switches region).
- Improved texture loading: much faster, and textures stream in at the size the view needs, using far less video memory.
- Changed: every write to a game file keeps a dated backup.
- Changed: shorter interface text.
- Fixed a crash on GPUs without enough video memory for a level's textures; textures are reduced instead.
- Fixed levels edited by ReLunacy loading forever in game: ReLunacy now offers to clear RPCS3's disk cache after a save.
- Fixed missing reflections, normal maps and detail maps on A Crack in Time's Ties.
- Fixed reflections turning black on dark surfaces in A Crack in Time.
- Fixed Moby shading on old-engine games and Moby scale in the Asset Viewer.
- Fixed glTF and GLB model export.
- Fixed Lua scripts installed into A Crack in Time not running.
- Fixed the Lua environment listing no level scripts from A Crack in Time on.
- Fixed some scripts showing as a single line in the Lua editor.
- Fixed saving a script opened from an archive.
- Fixed a crash when pasting into the Lua editor.

## [v0.06-alpha.1](https://github.com/VELD-Dev/ReLunacy/releases/0.06-alpha.1) - 06-11-2026

- New Lua environment: browse, edit, compile and install Tools of Destruction's level scripts.
  - Code editor with completion, documentation on hover, rename, find references and Ctrl+F.
  - Shareable colour themes (`ReLunacy/Themes/`).
  - API documentation in `ReLunacy/LuaApi/<game name>/api.lua`; see `ReLunacy/LuaApi/README.md` before editing it.
- New sky rendering on both engines (Render > Sky), by @Fianshoo.
- New animation support, by @Fianshoo in PRs #59 and #60.
- Improved rendering, much closer to the games on both engines, by @Fianshoo:
  - Textures sampled like on the console.
  - The game's own shaders (needs a decrypted EBOOT in the game's install; Render > Game shaders).
  - Bloom.
  - Fog (Render > Fog).
  - Baked lighting.
  - Reflections (Render > Reflections).
  - Two-layer surfaces (grass, snow, moss over stone).
  - Animated textures.
- New navigation mesh view (Render > Navmesh), with selectable chunks.
- New Cubemap Viewer panel.
- New layouts: save your own; the last one comes back on start.
- New status bar showing what is running; the loading window can be dismissed.
- Added every region of a level; the 3D view uses the environment of the region you are in.
- Added per-zone render toggles (Render > Zones).
- Added HDR rendering with an exposure setting (Render > Tonemap).
- Added lighting and reflection debug views (Render menu).
- Added Ties, UFrags and navmesh grouped by zone in the hierarchy.
- Added each region's environment to Level Data.
- Added Moby animation controls, a Tie's or UFrag's cubemap, and a volume's id and group to the Property Inspector.
- Added Moby asset names to the Asset Explorer.
- Added a raw vertex view to the Asset Viewer.
- Added a warning when a game has no decrypted EBOOT.
- Added a robots.txt and the AI / PC ports disclaimers.
- Improved level loading: the progress follows the real work and the window no longer freezes.
- Changed: ReLunacy starts with a docked layout, and panels open docked.
- Changed: the window opens maximized and remembers its size.
- Changed: the Game Browser remembers the last USRDIR folder.
- Changed: All 4 One starts with a lower reflection intensity.
- Changed: Ties, UFrags and shaders are named the same way everywhere.
- Fixed zone names on every new-engine game.
- Fixed bright surfaces turning blue, green or black.
- Fixed levels shared by several games (lobby, level_transitions) opening as the wrong game.
- Fixed "Open in Asset Viewer" opening the wrong UFrag on new-engine levels.
- Fixed the Asset Viewer preview looking different from the 3D view.
- Fixed an Asset Viewer crash during a level reload, by @Fianshoo in PR #58.
- Fixed some Mobys and new-engine materials drawing wrong.
- Fixed a crash while loading some levels.

## [v0.05.1](https://github.com/VELD-Dev/ReLunacy/releases/0.05.1) - 28-08-2026

[View diff](https://github.com/VELD-Dev/ReLunacy/compare/0.05..0.05.1)

- Fixed a crash when loading a level while one was already loaded (or loading), from the previous level's GPU resources being freed mid-frame while a panel like the 3D View still had an already-queued draw command referencing them this same frame
- Fixed the shaderc/spirv-cross native libraries not being found on published builds (Windows and Linux alike), from checking only the layout a RID-agnostic build uses when the actual release builds are RID-specific and lay the files out differently
- Fixed a Windows-specific stack overflow when loading a level with many materials, from a Vulkan descriptor-building loop doing a `stackalloc` per material instead of once outside the loop - C# doesn't reclaim `stackalloc`'d memory between loop iterations, so it kept growing for as long as the loop ran, and a level with thousands of materials was enough to exceed Windows's much smaller default thread stack (this had gone unnoticed on Linux, whose default stack is several times larger)

## [v0.05](https://github.com/VELD-Dev/ReLunacy/releases/0.05) - 28-08-2026

[View diff](https://github.com/VELD-Dev/ReLunacy/compare/0.04.1..0.05)

### Overview

Lighting is finally **FULLY OPERATIONAL** on Pre-ACIT games ! As detailed in the previous patchnotes, the
shader is 100% faithful to the original game, it's because it's actually based off the actual game shader,
extracted thanks to RenderDoc. This update comes with a brand new renderer (again), working only in Vulkan
this time (no OpenGL, sorry old PCs). Thanks to Vulkan I was able to add the exact same render pipeline than
the original game (threaded Frustum Culling, multi passes for transparency types, etc.), and it comes with HUGE
performance improvements thanks to that. For more details, see under !

### Details

- Added lighting for Ties and Mobys too
- Added the game's own analytic level lighting (ambient + two directional lights, straight from the level data) as the fallback light rig for surfaces with no bake of their own
- Added cubemaps support (for reflections). Works at least for ToD.
- Added Foliages support for Pre-ACIT games
- New render system working on Vulkan:
  - Better transparency, Anti-aliasing, threaded rendering & culling, register-once scene...
  - Huge performances improvements (over 560% gain)
  - Render pipeline of the original engine (+ custom ones for ReLunacy specific stuff), including the game's real 7 render modes (Opaque/Cutout/Overlay/Additive/Scunge/Soft-Edge/Blended) with their actual blend and alpha-test states, reversed straight from the EBOOT - this also fixed Scunge (glass, decals) rendering fully solid instead of blending
  - Threaded asset loading: cut the post-disk-load freeze on a large level from ~14s down to ~5s
  - Frame pipelining: CPU recording and GPU execution now overlap across frames, instead of the CPU immediately waiting on the previous frame's fence right after submitting it
  - Unified viewport input handling (which of the overlay/gizmo/picking/camera gets a click) across the 3D View and Asset Viewer
  - Fixed a large Tie frustum-culling error that could clip visible geometry at the edges of the screen
  - Migrated off Veldrith (a single-maintainer Veldrid fork) onto NeoVeldrid, the actively-maintained Silk.NET-based continuation, so the project no longer depends on a custom graphics backend
- Added a better `Frame Profiler` frame (performances debug)
- Added `Level Data` frame (including instances count, assets count, and light sources)
- Added an in-app Markdown renderer, used to show this changelog straight from the Update Info frame
- Added Font Awesome icons to parts of the UI
- Added Moby Distance culling and Moby Update distance (used by Luna Engine, not ReLunacy)
- Added Texture, Assets and Shaders browse filter (Filter by usage - used/unused - or other asset-specific filters)
- Selection is now pixel-perfect (easier to select volumes), and foliage instances can now be picked too
- Changed `.obj` export to write into its own per-asset subfolder, same as the separate `.gltf` export already did, instead of dumping every export into one shared folder
- Fixed freeze when loading level by threading assets upload to GPU.
- Fixed textures being read only from `textures.dat` (low-res) instead of `texstream.dat`. It was essentially impacting mobys.
- Fixed UVs for model exportation to `.obj`. They were upside down.
- Fixed vertex alpha on transparent materials: it's now multiplied with the texture's own alpha instead of replacing it, applies to every transparent render mode instead of only textures with no alpha channel of their own, and Mobys no longer misread their bone index as vertex alpha the way Ties/UFrags legitimately do (Mobys carry a real bone index there)
- Fixed foliage sprites on old-engine levels not showing their texture (was resolved indirectly instead of through a direct index into the old texture table)
- Fixed a segfault when opening certain assets in the Asset Viewer
- Fixed GPU buffer picking (clicking to select in the 3D View) silently not working, due to an input event-ordering bug
- Fixed Moby Cull Distance and Update Distance being read from swapped file offsets
- Fixed `3D View` being reset when closed and reopened.
- Fixed the 3D scene renderer segfaulting on construction after the NeoVeldrid migration, from a Vulkan function-pointer table that was never explicitly initialized (previously only worked by accident, as a side effect of Veldrith initializing the same shared state internally)

## [v0.04.1](https://github.com/VELD-Dev/ReLunacy/releases/0.04.1) - 30-07-2026

[View diff](https://github.com/VELD-Dev/ReLunacy/compare/0.04..0.04.1)

### Lighting (the big one)

ReLunacy can finally render levels **lit**, instead of showing raw unlit textures. The new shader is a
reproduction of the game's own fragment shader, reverse-engineered from a real capture of Tools of
Destruction running in RPCS3, so it's not an approximation of "what looks nice", it's the game's
actual maths. **It's a first step to a replica of ToD** *(and QfB)* **visuals !**

- Added an optional **lit renderer**, off by default: enable it in `Editor Settings` > `Lighting (experimental)`.
- **Baked lightmaps on UFrags**: the game's baked light colour and light direction atlases are
  now loaded, bound and sampled, through the UFrags' own second UV set. This is what carries all of
  the level's baked shadows and coloured bounce light.
- **Normal maps** are now composed the way the game does it (as partial derivatives, added together
  rather than blended), and their inverted orientation has been fixed, in the renderer *and* in glTF export.
- **Parallax / height offset**, using the scale and bias values read from each shader's own metadata.
- **Detail maps** are now applied, contributing both normal detail and specular, with the per-shader
  tiling factor read from the shader metadata. Worth noting that the detail map is still experimental and might look
  very weird in some cases.
- **Environment/ambient approximation** averaged from the level's own cubemap. (very experimental)
- Materials are now cached per instance rather than per shader, so UFrags that share a
  shader but have different bakes no longer collapse onto one shared lightmap.

#### Lighting limitations, please read

- **Lighting is currently only supported on Ratchet & Clank: Tools of Destruction and Quest For Booty.**
  Post-ACiT levels store their baked lighting data somewhere else, which hasn't been reversed yet, so
  using lighting on them will definitely look wrong. Resistance: Fall of Man is untested but should work.
- **Ties are not lightmapped yet.** Tie instances do carry a bake index, but their lightmap UVs haven't
  been located yet (they live outside the Tie vertex record, in a separate vertex stream), so Ties
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
- `Asset View`: added a new **UFrags** tab. UFrags can now be browsed, previewed, exported, and
  their baked lighting inspected individually; lightmap index, UV rectangle, and both atlases with the
  fragment's UV island drawn on top of them. UFrags are the only asset type whose bake can be inspected
  out of context, since each one is its own single placement.
- `Instance Properties`: "teleport camera to entity" now places the camera just short of the selected
  entity instead of pushing it past/away from it.
- Fixed Tie instance data not being read at all on old-engine levels, which silently discarded each
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
- Implemented a custom `DecalAwareForwardRenderer`, fixing z-fighting on alpha-blended decal textures (moss/vines painted onto UFrags) by disabling depth *writes* (while keeping depth *testing*) for translucent geometry instead of Bliss's default renderer, which hardcoded depth writes for everything.
- Implemented Moby per-instance render/display distance culling (reverse-engineered from real gameplay data), toggleable from the Render menu - off by default since the free-fly editor camera doesn't share the game's player-anchored camera assumptions.
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