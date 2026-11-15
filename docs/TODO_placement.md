# Placement editing - what is left

Requested 2026-11-15. Ticked = built (not tested in game unless said).

- [x] Gizmo World/Local toggle (X, Tools > Local Space); Local on path keys = frame along the path.
- [x] Double-click a selected path to add a key where clicked (one undo step).
- [ ] gp_prius structural writer: add Moby / volume / Clue / path / controller to a layer, path point count change,
      layer / Pod / Area / Scent membership, reference remap. Spec: docs/reverse/acit_gp_prius_coverage.md,
      dev/tools/gpcoverage. Report: docs/reverse/newengine_gp_prius_writer.md.
- [ ] Tie appender (zones.dat + collision + assetlookup + assetstats + toc) and the CollisionWriter BVH box fix
      (a Tie moved far loses collision). Spec: docs/reverse/acit_zones_coverage.md, acit_collision_format.md.
      Report: docs/reverse/newengine_tie_append.md.
- [x] Asset explorer frame: Mobys / Ties tabs (no UFrags), 3D thumbnails, favourites per game, drag source.
- [x] Drop into the 3D View: preview on the surface under the mouse, create the new instance, select it.
- [x] New-instance model: IsNew, layer, zone (Ties), fresh TUID, unique name, settings copied from a same-class
      template, EntityRegion Add/Remove, undo, Delete removes unsaved ones, shown in hierarchy + inspector.
- [x] Create menu: volume (shapes), path (two keys), controller (class from the region's).
- [ ] Inspector: assign Clue class (volumes), controller class, layer, Pods, Areas, Scents.
- [ ] Moon edit mode: corner button in the 3D View when a SpaceMoon Clue is selected; hide what is not part of
      the moon (layer / Pod / references); orbit camera around the moon centre (WASD/ZQSD move on the sphere,
      wheel = distance, mouse = look direction, pivot unchanged).
- [ ] Moon mode placement: objects placed in moon mode get their up axis along the moon's radius (away from the
      centre).
- [ ] Gizmo "Moon" space (besides World / Local): moves in spherical coordinates around the moon centre
      (latitude / longitude / altitude), keeping the object's up axis radial.
- [ ] Wire the writers into File > Save (Save dialog lists additions; refusals as problems; backups).
- [ ] Test in game: add a Moby, a Tie with collision, a volume, a path, a controller on a copy.
