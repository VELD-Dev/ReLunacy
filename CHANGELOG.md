# Changelog

This file lists all the changes of every version. This file is edited constantly during the development, in order not to forget what have been done for X or Y version. It's better to keep it up to date on dev branch.
Because the European date format is better, please keep date format like this: `DD-MM-YYYY`.

## [v0.06](https://github.com/VELD-Dev/ReLunacy/releases/0.06) - ??-11-2026

### Highlights

- Added a level collision visualizer and editor (off by default; everything is in the panel the 3D viewport toolbar's collision button opens), on both engines. Tools of Destruction's parser comes from @sowelilili's ReLunacy-ToD-tools fork, extended here to Quest for Booty (same format) and decoded for QForce, All 4 One, Into the Nexus and A Crack in Time, where each region's `collision.dat` holds it in section 0x2900. See `docs/reverse/collision.md`.
  - The new engine stores the same header and triangle meshes as ToD, with the instance fields reordered, and adds primitive sets: boxes, spheres, capsules and cylinders, which make up 20-50% of the collision instances on QForce, All 4 One and Into the Nexus. Every mesh vertex lands inside its instance's stored box on all six games, and every primitive set's shapes fit their box.
  - Drawn like the navmesh, as an overlay over the level, but instanced (one draw per unique shape), since a level places up to 3.7 million collision triangles. Colour by type (world mesh, Tie mesh, box, sphere, capsule, cylinder), by material, by triangle flags or by instance flags, with a legend built from what the level holds. Opacity, triangle outlines, and per-type toggles; it follows the Zones menu's region toggles, and a Tie several regions share is drawn once.
  - Filters by type (world mesh, Tie mesh, and each primitive kind), material, triangle flags and instance flags, each listing the values the level holds with their colour and count, so the lists double as the colour modes' legends.
  - Edit mode: clicks select collision instead of entities, faces or whole instances. Shift adds, Ctrl removes, dragging selects a box (the faces showing in it, or with Through walls every face whose centre is in it), plus All visible, Same material, Same flags, Same shape. The selection's materials, triangle flags and instance flags can be changed (0x400, which marks a primitive set, stays read only), and reverted. Picking runs on the CPU against the shapes, rasterising the faces under the cursor or the box nearest first, so it honours the filters and what other collision hides.
  - A Tie's collision moves with it: moving or rotating a Tie (gizmo or Property Inspector) carries the collision instances placed with it. They are matched on the placement matrix, which the collision shares exactly (3852 of 3858 Ties on cobalia, 7974 of 9312 on krell_canyon, the rest having no collision), and every Tie collision instance of both levels finds its Tie. The match uses the file's matrix rather than the editor's, which a decompose does not rebuild exactly for mirrored placements (220 on cobalia).
  - Triangle flag bits are named where they are known: no bit set is the default walkable surface, bit 0 is "no wall jump"; bits 1-4 are not decoded yet and say so.
  - A shape is stored once and placed by every instance using it, and once in each region file listing it: an edit to a face changes it on all of those, and the panel says how many. Edits are in memory only for now; nothing is written back to the level files.
  - Volumes are hidden while editing collision: their wireframes sit on the walls and covered the faces being picked.
- The 3D viewport's toolbar now holds the switches that get flipped all the time, so they no longer need a trip through a menu:
  - Lit / unlit (was `Editor Settings > Lighting`), and next to it the game's RSX shaders (was `Render > Game shaders`). The RSX shaders belong to the lit path, so their button is greyed out while unlit; it keeps its state, on by default, for when lighting comes back.
  - Navmesh on/off (was `Render > Navmesh`) and the collision panel. Each is greyed out on a level that has none. The navmesh toggle is now also remembered between sessions.
  - Mobys, Ties, UFrags, volumes and bounding spheres (were `Render > Mobys` and so on). Foliage stays in the Render menu.
- Volumes are told apart on the new engine. gp_prius.dat holds eight instance families, named by the handle types of each region's instances.lua (and matching its counts one for one): Mobys, paths, volumes, **Clues**, controllers, **Pods**, **Areas** and **Scents**, every instance placed in one of the level's editor **layers** (0x25008: "gameplay", "cameras", "audio_reverb"...). A Clue is a volume with a behaviour class: fall to death, death, lava, water, ledge grab, camera field, fire point, hero spawn and some 70 more over the three games; its volume is the one named after it or "generatedVolumeForClue_" + its name. An Area groups paths and volumes, a Scent groups Clues (krell_canyon's death_clues), a Pod groups Mobys. A Crack in Time references instances by layer and index, All 4 One and Into the Nexus by TUID through a reference pool (0x25022), and every reference of four levels resolves. The class names come from what the designers named the Clues of each class over every level (0x127: 958 of 1,349 are ClueFallToDeath); a class not in that table takes its level's own name for it.
  - Volumes are coloured by kind: what kills in red, lava orange, water blue, every other Clue class a colour of its own, plain volumes in the volume colour. Or by layer.
  - Death volumes are drawn with translucent faces in their colour under their wireframe (box, sphere or cylinder, as their shape). The volume panel's Faces setting extends that to lava and water, to every Clue or to every volume, or turns it off, with the faces' opacity.
  - The hierarchy's Volumes tab lists them by layer, each name with a swatch of the colour it is drawn in.
  - A new filter button beside the volumes toggle on the viewport toolbar opens a panel: show or hide each kind and each layer (each list is its colour's legend, with counts), or show one Area's or one Scent's volumes alone.
  - The Property Inspector shows a volume's layer, its Clue (class and name) and the Areas and Scents it belongs to, and the Clue's settings: the fields naming another instance, resolved (a ClueGrindRail's rail path, a ClueCameraField's camera controller, a ClueEnemy's path, spawn volumes and Scents, a ClueRespawn's Area), then every other field raw, as hex and as flags (four 0/1 bytes), a float or an integer, until each class's fields are named. On All 4 One and Into the Nexus a Clue's settings are an IGHW of their own (the class struct in 0x2501C, its references in a 0x25022 pool); on A Crack in Time a flat struct. Every Clue's settings of the three games read, starting with its class number.
- Ported from Fianshoo's ReLunacy Resurgence research (its gp_prius readers, EBOOT class tables and camera notes), and checked here on A Crack in Time, All 4 One and Into the Nexus:
  - Paths (splines) are read with their points (position and distance along the path), their length, whether they loop, and a kind byte: krell_canyon 416 paths / 30,603 points, creature_collector 106, haunted_city 201, every path's last point exactly its stored length.
  - Pods (groups of Mobys) and Squadrons (groups of Tie placements, a family past the file's own reference types) are read.
  - All 4 One's classes go by their real names, recovered from its EBOOT's enum registrations: Clue classes (0x974 is NavClue_CustomEdge) and controller classes (CamArgBlendable, CamArgFixed, CamArgFollowPath...). Every game numbers its classes its own way (0x85B is ClueNoLedgeGrab on A Crack in Time, a projectile on All 4 One), so a game's table only names that game's classes.
  - A Crack in Time's instance references are the runtime handle the QForce loader builds, kind << 26 | layer << 16 | 0xF000 | index; the layer field is ten bits, not eight.
  - Cameras face the right way: a camera controller looks along its local +Z (CamArgFixed copies its controller's matrix to the view), so the camera model is turned half a turn. The Property Inspector shows a camera's class name and its settings, references resolved, as for Clues.
  - Cameras can be looked through: the Property Inspector's **Camera POV** puts the 3D view at the camera, looking along its +Z, and darkens everything outside the game's 16:9 frame. The FOV is the camera's own when its settings give one (Full Frontal Assault's CamArgFixed / FixedFollow / FollowPath / IndexedPath FieldOfView, after Fianshoo's header; retail keeps 0 = the game's default), else the editor's default (90° horizontal, Editor Settings; the game's own is not known). Esc goes back to the previous view; right-dragging flies on from the camera's place. Moving the camera with the gizmo moves the view with it.
  - The camera model is half its former size, and its size is an editor setting (Editor Settings > Tools).
- Paths are drawn and editable on the new engine. The game stores no tangents or curves: a path is its points joined by straight segments, each point's distance along the path being exactly the sum of the segments before it (every path of krell_canyon, creature_collector and haunted_city), so that is what is drawn - no resampling, nothing the game does not follow. All of a level's segments are one vertex buffer and one draw (krell_canyon: 30,187 segments), each widened in the vertex shader to a fixed width in pixels so a path reads the same near and far, depth-tested against the level and nudged towards the camera so one lying on the ground stays visible. Click a path to select it (picked in screen space, against whatever mesh is under the cursor, the nearer winning): it is highlighted and its keys get markers. The gizmo moves, turns or scales the whole path. Distances and the length are recomputed the game's way.
  - Most paths are cubic splines, and are edited as such. They were exported as 10 samples per span, evenly spaced in the curve's parameter: their point count is 10 x n + 1, and every block of 10 segments starting on a 10th point is one cubic to float precision (a millionth of the block's size; a block shifted by 5 points misses by a thousandth) - 302 of krell_canyon's 416 paths, 104 of creature_collector's 106, 120 of haunted_city's 201. Their keys (every 10th point) and Bezier handles are recovered exactly: sampling them again gives the file's points back to 0.0002 units. Click a key to move it (its handles move with it), then its handles to shape the two spans either side; each key's handles are Smooth, Aligned or Free (read from the data: handles in line through the key), and a key can be inserted (the span split in two, its shape unchanged) or removed (the two spans merged into the curve that follows them best). Every edit samples the spans it touched again, 10 per span, as the export did. The other paths are polylines whose every point is a key.
  - Selecting a path opens a path panel in the viewport: step from key to key, set a key's curvature to Bezier (smooth handles along its neighbours) or None (a corner, straight towards them), pick its handle mode, add a key before or after the selected one (between two keys without changing the curve, or past either end along the path's direction), remove it (Delete does too), and turn a polyline into a spline through its points. Keys, handles and points are dragged straight on the screen, in the horizontal plane through them, or up and down when Shift is held as they are grabbed; the gizmo still moves them along an axis. Edits are in memory only for now. Paths have a toolbar button and a tab in the hierarchy, by layer.
- The level's cameras are shown on the new engine, with a camera model, at every camera controller: the controller instances (gp_prius 0x2506C: position, rotation, scale and a class) whose class the level's cam... / CamArgBlendable / CamArgFixed controllers share (creature_collector 103, krell_canyon 66, haunted_city 33). They can be clicked, selected, moved with the gizmo and inspected, are listed in a Cameras tab of the hierarchy by layer, and switched with a camera button on the viewport toolbar. Most CamArgBlendable cameras share one placement rotation, so their real view direction comes from their settings, not decoded yet.
- Trigger volumes are drawn in their real shape on the new engine: boxes, cylinders, spheres and capsules, where every volume used to be drawn as a box. The shape was in the volume record all along, in the top byte of the matrix's last word (gp_prius 0x2505C +0x3C: 0 box, 1 cylinder, 2 sphere, 3 capsule), read as a float it is a tiny denormal, which is how it hid. Over the four games: 63172 boxes, 6556 cylinders, 656 spheres and 8 capsules. The Property Inspector shows the shape. Tools of Destruction's volumes have no such field and stay boxes.
- Textures stream. A level loads every texture at no more than 128 px, decoded from the small mips the game ships with it rather than from the whole image, and the textures the camera needs larger are then decoded at the size it needs, in the background, and swapped in a few times a second. The size comes from how large each object drawing the texture is on screen. Under the GPU's memory allowance, textures nothing has shown for a while are lowered again to make room, the least recently seen first, and when the textures on screen do not all fit at full size they share what there is, a step at a time, rather than the first ones taking it all. With room to spare nothing is ever lowered. krell_canyon's load now decodes 318 MiB of pixels in 0.23 s instead of about 2.4 GiB in 0.6 s, and six views of it raise 597 of its 1,461 large textures (653 MiB instead of 1,792 MiB at full size). The base decode is byte-for-byte the full decode's lower levels, and what reaches the GPU reads back identical to a full decode (checked on krell_canyon, creature_collector and metropolis). The texture explorer shows the selected texture at its full resolution, and the sky's textures are always at full resolution. `RELUNACY_TEXTURE_STREAMING=0` loads every texture at its own size again.
- Level loads are much faster to decode and upload textures. The decode spent most of its time not decoding but applying the RSX channel remap (CONTROL1), one function call per channel per pixel: it is now a precomputed byte shuffle, four pixels at a time, with byte-for-byte the same result (krell_canyon: 4.5 s to 1.1 s of work). The editor also uses the server garbage collector, which stops the decoding threads from waiting on each other's allocations. Together, krell_canyon's texture decode went from 1.85 s to 0.6 s, and a creature_collector region's from about 1 s to 0.23 s. Uploads used to submit one GPU command per mip level. They are now batched, one submission per frame: creature_collector's first region uploads in 0.48 s over 13 frames instead of 2.3 s over 73.
- A level is loaded one region at a time. All 4 One's levels are progression segments of one map that overlap in space and were never shown together, and ReLunacy used to read, decode and upload every region at once to show one. Now it reads the level's default region, and **Render > Region** switches to another: the current region's scene, models and textures are freed, and the new one is read from the level's files, which stay open (0.2-0.5 s to read on creature_collector, plus its textures). A region reads only its own zones, Moby instances, environment and collision, and only the Moby, Tie and Shrub classes those place. creature_collector needed 4,867 MiB of video memory for its textures; its largest region needs 1,651 MiB. A Crack in Time's levels have a single region, but they no longer read the classes nothing places either (krell_canyon: 70 of 165 Mobys). The other classes stay listed in the Asset Viewer under "Not in this region" and are read when picked.
- A level whose textures do not fit the GPU's memory no longer crashes it. A 4 GB card ran out of video memory part-way through the texture upload, and the driver failed with an access violation inside NeoVeldrid's allocator instead of returning an error. ReLunacy now reads the GPU's memory before decoding: the driver's own budget when it reports one (`VK_EXT_memory_budget`), otherwise the size of its video memory. When the level's textures would not fit, the largest are reduced until they do, and as many as the remaining memory allows keep one size larger. A dialog says by how much and why (krell_canyon on a 4 GB card: 55 of 17,509 textures down to 512-1024 px). The estimate counts what NeoVeldrid really uses: about 32 KiB of padding per texture on top of its pixels, which on a level of 29,262 mostly small textures is almost as much again. It matches the driver's own count to within 2 %. If a texture still cannot be allocated, it is drawn with a placeholder and the dialog says what was already on the GPU. `RELUNACY_TEXTURE_BUDGET_MIB` forces a budget, to see what a smaller card gets.
- A Crack in Time has bloom. Its levels carry no 0x34000 section, which is where the other new-engine games keep the bloom settings, so it never ran there. The same five settings are in ACiT's own region.dat 0x8100 record (+0x70..+0x80), different per level (most levels 0.54 / 2.5 / 0.4 / 0.55 / 0.8, axiom_city 1.12 / 2.33 / 0.21, tombli without history), and they are the values the game's own bloom shaders in the RPCS3 shader cache carry.
- A Crack in Time has fog. Its regions carry none of the other new-engine games' fog table (region.dat 0x34004), so nothing was drawn. ACiT keeps the old engine's linear fog instead, in the region.dat 0x8100 record that also holds its bloom: colour at +0x30, then near, far and amount at +0x50..+0x58, Tools of Destruction's main.dat 0x7100 fields moved by 0x10 (krell_canyon 50 / 500 / 0.3, zolar_forest 90 / 500 / 0.6; 12 of the 22 levels have one, the space sectors and arenas none). The colour is the one ACiT's own world shaders in the RPCS3 cache mix in, colour + (fog - colour) x amount, to the last digit. The amount comes from a vertex program nothing can read yet, so it rises over view depth from near to far up to the amount, as on Tools of Destruction. `Render > Fog` switches it, as on the old engine.
- A Crack in Time's reflections show on dark and shadowed surfaces again. ACiT is drawn with All 4 One's shaders (its own EBOOT is not decrypted), and those scale the reflection down by how brightly the surface is lit, using QForce's default when a level gives no setting - which blacked out the reflection on anything dimly lightmapped. None of ACiT's 27 reflecting shaders in the RPCS3 cache does that, so it is turned off for ACiT.
- A Crack in Time's Ties have reflections, normal maps, detail maps and parallax again. Every Tie primitive was drawn with none of them: the per-primitive feature mask the other new-engine games keep at +0x20 does not exist in ACiT's primitive record (+0x20 is 0 on every one of them, and nothing near it holds mask-like values), so ANDing the material's features with it removed them all. ACiT Ties now keep their material's features as they are.

## [v0.06-alpha.1](https://github.com/VELD-Dev/ReLunacy/releases/0.06-alpha.1) - 06-11-2026

### Highlights

- Added a Lua environment (`Lua Environment` panel): browse a level's scripts, read them, edit them, and write them back into the game.
  - A Lua 5.0 reader, disassembler and compiler for the bytecode Tools of Destruction actually runs. All 524 scripts it ships recompile byte for byte from their own sources, which is what the compiler is checked against.
  - A code editor with syntax colouring, completion, snippets, auto-closing brackets, rename, go to declaration, find references (results listed under the editor, as coloured code), Ctrl+F, and documentation on hover.
  - LuaLS annotations (`---@param`, `---@class`, `---@cast`...) are coloured and completed, with the parameter names of the function below offered after `---@param`. `---@cast` narrows a variable's type for everything after it. The documentation is under `ReLunacy/LuaApi/<game name>/api.lua`. Please follow `ReLunacy/LuaApi/README.md` before editing the docs.
  - Documentation is markdown, as LuaLS writes it, and is rendered as such - Lua examples included, coloured by the editor itself.
  - Editable colour themes, with the real editor as a live preview. Color themes can be easily shared across installs under `ReLunacy/Themes/`.
  - Saving writes the source and the compiled chunk back into `global_cached.psarc`, adds them to the game's file index when they are new, and keeps `game.toc`'s record of each archive's length in step. The original is backed up first, and the rebuilt archive is verified in full before anything is replaced; files the edit does not touch keep the exact compressed bytes they shipped with.
  - Adding new scripts. Not established yet, and worth knowing before relying on it: whether the engine accepts a file added through ReLunacy. Replacing an existing script needs none of that and is the safer path.
- Added animation support by @Zairata in PRs #59 and #60
- Added sky rendering on both engines (`Render menu > Sky`) by @Fianshoo: the dome and its gradient, the two scrolling cloud layers and the authored sky shells (panoramas, stars, planets), drawn in the game's pass order with each texture's own RSX sampler. Tools of Destruction reads main.dat 0x8150/0x8160/0x8170 and the shell mesh in vertices.dat 0x9000/0x9100; the new engine reads every sky of the region (region.dat 0x81B0, textures in sky.dat), so All 4 One's seaside (3 skies) and polar_sea (2) can show each one. The clear colour comes from the sky, as in game. Bloom shells stay off for now.
- Old-engine textures now keep their whole 0x5200 RSX descriptor (`TextureMetadataOld` fields 0x08-0x1C, `Texture.OldDescriptor`) instead of 16 unnamed bytes.
- Rendering closer to the games, on both engines, by @Fianshoo. Every item below was checked against the game's own code (EBOOT, RSX shaders, RPCS3 shader cache) or measured over the retail files:
  - Textures are sampled the way the RSX samples them: each texture's own descriptor drives wrap, filter, LOD, channel remap (CONTROL1), gamma and signed/biased decoding, on both engines.
  - The game's own fragment shaders: materials draw with the shaders they ship (holograms, shields, veils...) and, when a decrypted EBOOT (*.elf) lies in the level's install, with the game's standard shaders (QForce, All 4 One, Into the Nexus; A Crack in Time borrows a sibling's) and Tools of Destruction's Tie shaders. `Render > Game shaders` turns them off.
  - Bloom as the games run it (the level's own parameters: region.dat 0x34000 on the new engine, main.dat 0x7100 on ToD), with the sky writing its glow mask.
  - Fog: ToD's per-level fog (main.dat 0x7100), and the new engine's fog tables and atmosphere (Henyey-Greenstein sun scattering). `Render > Fog` hides ToD's.
  - Baked lighting: new-engine zone lightmaps from lighting.dat with their high-resolution half from highlight.dat, the per-placement baked-light constant, the unbaked Ties' prelight (both engines), ToD's per-vertex baked light multiplier, lightmap indices sanitized the way the game does.
  - Reflections: the new engine's cubemaps.dat cubes picked per placement, ToD's cube decoded as the console does (sRGB on all four channels, HDR exponent base 2) with the Tie shaders' distance fade. `Render > Reflections` hides ToD's.
  - Two-layer surfaces (grass, snow, moss over stone): the second material and its blend mask on Ties and UFrags, both engines.
  - Animated UVs (section 0x5C00): scrolling and turning textures move, stepped at the game's frame rate (60 on ToD, 30 on the new engine).
  - ToD back faces culled as the game culls them, its non-opaque polygon offset, the new engine's normal/reflection/detail fade distances.
  - Known level names tell the game apart (`GameDefinitions.FromLevelPath`), which selects A Crack in Time's data layout.
  - The skinning added in #60 is kept: the lit, picking and outline vertex shaders still skin, the bone palette now sits at bindings 9 and 11.
- Every region of a level is loaded, not just the first. On the new engine the cubemap, lighting, fog and sky belong to the REGION, not the level, and any of them can be shared between regions, so the 3D view follows the environment of the region you are in.
- Zone names now come from where the game actually keeps them. Every zone was misnamed on every new-engine game: the reader read section 0x74C0 of the zone's own data as a (TUID, name pointer) metadata record, but in a zone 0x74C0 is the SHRUB instance name table and 0x72C0 is the Tie one - so a zone that had shrubs was named after its first shrub placement (`nb_cr_tree_curved_2_5`), and a zone that had none read offset 0 of its own IGHW header and fell back to `Zone_<TUID>`. The real names are in the region: `region.dat` 0x1C000 holds one name offset per zone, positionally parallel to the 0x1C010 zone-TUID list already used for region membership. Measured over every level of All 4 One, Into the Nexus, QForce and A Crack in Time: 1010 of 1010 zones named, with no disagreement where several regions list the same zone. Old-engine levels have neither table and keep their single synthetic zone.
- Added a navigation-mesh visualizer (`Render > Navmesh`, off by default). The navmesh is decoded from zone sections 0x19000/0x19200 - chunk table and chunk data - which no art zone carries, and is drawn as a semi-transparent surface over the level with its unique edges as a solid black wireframe. Its colour and opacity are `Editor Settings > Navmesh Color`, beside the volume colour; the wireframe thickness follows the existing volume wire thickness, since both draw the same edge mesh.
  - The surface is double-sided and writes no depth, so overlapping islands both show and nothing behind the mesh is hidden. The wireframe is GPU-instanced - one draw call for every edge in the level - using the trigger volumes' thin-box edge geometry rather than `VK_POLYGON_MODE_LINE`: line mode would redraw every shared edge twice because it walks triangles, while the navmesh stores each edge once, and a thin box has a real world-space thickness.
  - Faces are triangles given as three edge indices. All 19681 faces across all 55 retail new-engine levels read as triangles, and all 21011 vertices of all 1062 chunks lie inside their own chunk's bounding sphere, which is what identifies the layout. See `docs/reverse/newengine_special_zones.md`.
- Navmesh chunks are selectable: a Navmesh tab in the hierarchy lists them per zone, selecting one highlights exactly that chunk's triangles in the viewport (in the selection colour), and the Property Inspector shows its zone, chunk index, vertex/triangle/edge counts, how many of its edges link to another chunk, and which chunks those are. A chunk rather than a whole navmesh because that is the unit the data is built around - it is what carries a bounding sphere and what the edges' neighbour fields point at. Selection is through the hierarchy; clicking a chunk in the viewport is not wired into the pick pass yet.
- The hierarchy now groups Ties, UFrags and navmesh chunks under the zone they belong to, instead of listing each kind flat. Levels carry no path or folder structure of their own, so the zone is the grouping the data actually gives us; Mobys and Volumes belong to a region rather than a zone and stay flat. Where a level has several regions the zone header names the region too, because a zone can be listed by more than one region and each builds its own copy of the entities - merging them by name would hide real duplicates. Searching filters within each zone and hides the zones left empty. Shrubs will join the grouped tabs once they are placed in the scene at all.
- Added per-zone render toggles (`Render menu > Zones`)
- Added a Cubemap Viewer panel: the level's reflection cubes projected as a free-look view, with the channel reconstruction, exposure, gamma and tonemap all adjustable, and controls for how a face's texels are stored and which way the assembled cube is turned. It is how the cubemap work below was judged, and `Apply to 3D view` pushes a candidate arrangement into the renderer so it can be checked on real geometry rather than in the projection alone; a sweep steps through the candidates rather than dialling each one in by hand.
- Measured where a new-engine cubemap face's texels are stored, over all 24 cubes of four games (All 4 One, Into the Nexus, QForce, A Crack in Time): the convention's `(u, v)` is at `(v, u)`, a plain transpose, which is the only one of the eight layouts whose six faces join at the seams - by a margin of 16x to 90x on seam discontinuity. A row flip is ruled out: it would have broken the seams.
- The cubemap arrangement is one setting for how the faces are stored and one for how the cube is turned (a signed axis permutation, 48 of them), and the per-face rotations are derived from it. Both the Cubemap Viewer's projection and the GPU upload read through it, so a candidate can be judged on real geometry; `Apply to 3D view` and the candidate sweep are in the viewer. Note that no smoothness measurement can choose between the 48 - re-orienting a cube that joins leaves it joining - so the cube's orientation is still a by-eye finding against the retail game.
- Assets are named one way throughout the engine instead of three. Ties were written TIE, TIEs, Tie and Ties; UFrags were called terrain in half the comments and UFrags in the other half, sometimes in one sentence; and shaders were called programs, which is RSX and Cg jargon that reads like something that runs on its own. 230 occurrences of TIE over 48 files, 23 terrain comments judged one at a time, `Assets.Terrain` renamed to `Assets.UFrags`, and 246 comments and 297 identifiers moved from program to shader.
- The Level Data panel now reports the level per region: each region's own cubemap, lighting, fog and sky, which on the new engine belong to the region and not to the level, so a level with thirteen regions no longer reports one environment for all of them.
- The Property Inspector shows more of what a placement actually is: animation controls for Mobys, the cubemap a Tie or UFrag reflects, a volume's id and group, and a navmesh chunk's counts and neighbours.
- Added Moby Assets names reading for the Asset Explorer.
- Added a layout system. You can create custom layouts and save them. The layout is automatically saved on exit and is reused directly when reopening ReLunacy.
- ReLunacy now starts with a default docked layout.
- Every panel now starts docked, and spawns/appears docked on first opening.
- Found and decoded vertex alpha offset and vertex alpha ranges in New Engine for UFragVertex, VertexFormat0 and VertexFormat1. Ranges: (1.0-0.0) 0xFF80-0xFFFF, 0x7F80-0x7FFF, 0x9080-0x9001, 0x8080-0x8001; Some are ascending, other descending, annoying shit I won't detail.
- Added a status bar under the dockspace showing what is running, and the loading window is now a frame you can dismiss instead of a modal that blocks the editor until it finishes.
- Level loading reports what it is actually doing. The progress bars follow real work (per reader, per stage) instead of advancing on a guess, and everything between "level data read" and "textures on the GPU" that can run off the main thread now does, so the window no longer freezes while a level finishes opening.

### Everything else

- Added the raw-vertex inspector in Asset Viewer for reverse-engineering purposes.
- Fixed the Asset Viewer disposing its preview while a level reload was still using it, by @Zairata in PR #58.
- Game Browser now remembers the last USRDIR path you scanned, both when reopening the frame AND relaunching ReLunacy.
- Fixed "Open in Asset Viewer" (from the Property Inspector) jumping to the wrong UFrag, or the wrong zone's UFrag entirely, whenever a level had more than one zone in New Engine.
- The editor window now opens maximized by default and remembers last size and maximization state between sessions.
- Partial shrub support on the new engine, loading only: the shrub asset format and the per-zone placements are read (`ShrubReader`, `Zone.ShrubInstances`), and loading their materials no longer re-packs the texture atlas every time. Nothing is drawn yet - no shrub mesh reaches the renderer, and shrubs do not appear in the entity hierarchy.
- Fixed a latent crash in level loading: `FileManager.LoadFile` registers a new entry and throws if one already exists, so any two readers wanting the same per-region file would take the load down. Those reads go through `GetOrLoadIGFile` now.
- The scene is rendered to a 16-bit float target and tonemapped at a settable exposure (`Render > Tonemap`) instead of being clamped to 0-1. The games' lighting is genuinely HDR: clamping it was what made bright surfaces blow out, and what made a large baked light scale look like a decoding bug.
- Added live lighting and reflection debug views (`Render` menu) that isolate one term of the lit shader at a time - lightmap, prelight, analytic rig, baked scale, cubemap tint, reflection occlusion - with a non-finite check that paints NaN and infinity rather than hiding them.
- Fixed the fluorescent blue/red/green and pitch-black patches on Ties and Mobys. The new engine's atmosphere in-scatter term is `c += c * L * (1 - c)`, which is what the RSX ran, but `(1 - c)` is only headroom while `c` stays inside 0-1: on the float target it turns into a downward parabola that crosses zero and runs away negative, so a bright surface went green, then blue, then black as its radiance rose. It now measures the headroom on the displayable colour, which is bit-identical to the console's result over 0-1 and monotonic above it.
- All 4 One starts at a reflection intensity of 0.04, which is a GUESS by eye, not a decoded constant (`GameDefinitions.DefaultReflectionIntensityGuess`). Every other game keeps 1.0, which is not a measurement either.
- Fixed levels whose name belongs to more than one game being attributed to the wrong one: `lobby` is shared by Full Frontal Assault, All 4 One and Into the Nexus, and `level_transitions` by Tools of Destruction and Quest for Booty, so All 4 One's lobby loaded as QForce - picking its shader table and data layout. The level's sibling folders settle it. Also filled in the known level lists for Full Frontal Assault, All 4 One and Into the Nexus, and added the missing `pvp_dlc_2`.
- ReLunacy now says so when a level's game has no decrypted EBOOT in its install, instead of quietly falling back to generic shaders.
- Fixed the Asset Viewer's preview drawing with a different shader table from the scene, so a material looked one way in the preview and another in the 3D view.
- Fixed Moby vertex and index buffers being read short, scene-renderer invalidations being recorded but never applied, new-engine material features not being gated on the material's own +0x20 flags, and depth synchronisation not being declared on every render pass.
- Added a robots.txt and the AI / PC-ports disclaimer notices.

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