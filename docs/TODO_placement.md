# Placement editing - what is left

Requested 2026-11-15. Ticked = built (not tested in game unless said).

- [x] Gizmo World/Local toggle (X, Tools > Local Space); Local on path keys = frame along the path.
- [x] Double-click a selected path to add a key where clicked (one undo step).
- [ ] gp_prius structural writer: add Moby / volume / Clue / path / controller to a layer, path point count change,
      layer / Pod / Area / Scent membership, reference remap. Spec: docs/reverse/acit_gp_prius_coverage.md,
      dev/tools/gpcoverage. Report: docs/reverse/newengine_gp_prius_writer.md.
- [ ] Decode collision.dat 0x8200/0x8300 (planet levels) so Ties can be added there; TieAppender refuses them today.
- [x] Tie appender (zones.dat + collision + assetlookup + assetstats + toc) and the CollisionWriter BVH box fix
      (a Tie moved far loses collision). Spec: docs/reverse/acit_zones_coverage.md, acit_collision_format.md.
      Report: docs/reverse/newengine_tie_append.md.
- [x] Asset explorer frame: Mobys / Ties tabs (no UFrags), 3D thumbnails, favourites per game, drag source.
- [x] Drop into the 3D View: preview on the surface under the mouse, create the new instance, select it.
- [x] New-instance model: IsNew, layer, zone (Ties), fresh TUID, unique name, settings copied from a same-class
      template, EntityRegion Add/Remove, undo, Delete removes unsaved ones, shown in hierarchy + inspector.
- [x] Create menu: volume (shapes), path (two keys), controller (class from the region's).
- [x] Inspector: assign Clue class (volumes), controller class, layer, Pods, Areas, Scents.
- [x] Moon edit mode: corner button in the 3D View when a SpaceMoon Clue is selected; hide what is not part of
      the moon (layer / Pod / references); orbit camera around the moon centre (WASD/ZQSD move on the sphere,
      wheel = distance, mouse = look direction, pivot unchanged).
- [x] Moon mode placement: objects placed in moon mode get their up axis along the moon's radius (away from the
      centre).
- [x] Gizmo "Moon" space (besides World / Local): moves in spherical coordinates around the moon centre
      (latitude / longitude / altitude), keeping the object's up axis radial.
- [x] Wire the writers into File > Save (Save dialog lists additions; refusals as problems; backups). GameplayStructure -> GameplayWriter.Patch -> GameplayBudgets -> ZoneWriter + CollisionWriter -> TieAppender; reloads the level after a structural save. Probe: dev/tools/structprobe.
- [x] Delete instances the files hold (Mobys, volumes with their Clue, paths, controllers, cameras): Delete key / Edit > Delete,
      confirm modal listing who names them, undoable, clears or remaps every reference in gp_prius at Save (not tested in game).
      Spec: docs/reverse/newengine_gp_prius_writer.md section 7. Probes: dev/tools/delprobe, delsaveprobe.
- [x] Delete retail Ties (2026-11-17): TieAppender.Delete (zones.dat rows, clusters, names, collision instances and BVH leaves), run before
      the appends in the save chain, structural (level reloads). New unsaved Ties are just removed. Probe: dev/tools/tiedeleteprobe
      (space_sector_1, krell_canyon). Planet 0x8200 records are kept (stale TUID), lighting.dat is kept. Not tested in game.
- [ ] Test in game: add a Moby, a Tie with collision, a volume, a path, a controller on a copy.

## Moon creation tooling (requested 2026-11-16)

- [x] Gizmo toolbar in the 3D View (operation + space World / Local / Moon + snapping), bottom centre. Pivot option skipped.
- [x] Aphelion fly height: find it (EBOOT / configs); draw a plane like the sector boundary.
- [x] Project system (.lunaproj): edits saved to the project, not to the game; Install to game builds from the
      pristine files. Bake point lights and other editor-only data live in the project (docs/project_format.md;
      the manifest's `editor` section is reserved for them, nothing reads it yet).
- [x] New layers (art_<id>, gameplay_<id>) and a new art_ zone (pending in the editor, saved by File > Save; not tested in game): gp_prius / metadata / gameplay.dat and zones /
      region / lighting / collision / assetlookup writers.
- [x] Create moon (dev/tools/moonmakeprobe; not tested in game): moon id, surface (known moon surface Ties), radius (scales the surface Tie and SpaceMoon +0xE0),
      other settings; places the SpaceMoon Clue, area volume, landing beam and the layers + zone.
- [x] Create moon options (not tested in game; probe dev/tools/moonmakeprobe): segment none / existing SEGMENT Pod / new Pod
      named like retail (SEGMENT_SS<sector>_<ID>, holds the landing beam); art and gameplay layer new (default art_<id> /
      gameplay_<id>, custom name) or existing; zone new or existing; extra Pods and Areas in the gameplay layer, optionally
      holding the beam / the moon volume. One undo step. A new segment is only live in game once level.csv
      (global_cached /data/configs/level.csv, View > Config Tables) has a column for its Pod name (Pod, Type SEG_MOON, pools);
      the moon creator registers it (SegmentRegistry: a last column copied from the model moon's, Pod name set, counts empty, pool totals raised), written
      with File > Save into global_cached through the project; undo covers it. Probe: dev/tools/segcsvprobe.
- [x] New Pods and Areas (Create > Pod / Area, and in the moon modal): pending in the region, written by GameplayStructure
      (GpPriusDocument.AppendGroup) at the END of their table, so only layers from the last one holding one of the kind
      (every new layer qualifies); members through the usual assignment. Undoable, removable before save.
- [x] Evaluate a "curve to moon" tool (docs/reverse/newengine_tie_asset_writing.md: in place feasible now, bent copy ~1 week, copy collision class not written)
- [ ] Build "curve to moon" (bent copy under a new class TUID + collision.dat class entry)
- [ ] Fix Tie normals on stretched classes (reader divides normals by the quantisation scale)
- [ ] Decode the ACiT prelight word (needed for baking)
- [ ] Tie import: split meshes by material (one material per mesh), existing shaders/materials only.
- [ ] Bake prelight / baked lighting for new Ties: game's directional lights + placeable bake point lights.
