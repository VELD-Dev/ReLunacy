# ReLunacy projects (`.lunaproj`)

Edits are saved into a project, never straight into the game. A project stores the bytes of every game file entry it
changed, and **Install to Game** rebuilds the affected archives from pristine copies plus those entries.

Code: `ReLunacy.Engine/Projects/` (engine), `ReLunacy/Core/Project/ProjectSession.cs` and
`ReLunacy/Core/Frames/Modals/ProjectModals.cs` (editor). Headless check: `dev/tools/projectprobe`.

## On disk

```
MyMod.lunaproj                 JSON manifest
MyMod.lunaproj.d/
  objects/<sha256>             one changed entry's bytes, named by their SHA-256 (content addressed, shared, collected
                               when no entry names them any more)
  pristine/<archive path>      the game's archive as the project recorded it, e.g.
                               pristine/packed/levels/space_sector_1/level_cached.psarc
```

A pristine copy is made **lazily**, the first time an entry of that archive is saved (level_cached.psarc of a new-engine
level is 100-200 MB; level_uncached.psarc is never copied unless something in it is edited). Archives the project only
reads are recorded by hash.

## Manifest (format 1)

```json
{
  "format": 1,
  "name": "NPUA80966",
  "game": "ACrackInTime",               // GameId, may be absent
  "gameRoot": "/.../NPUA80966/USRDIR",  // the folder holding packed/
  "created": "2026-11-16T18:00:00",
  "levels": [ { "name": "space_sector_1", "source": "packed/levels/space_sector_1/level_cached.psarc", "added": "..." } ],
  "archives": [
    {
      "path": "packed/levels/space_sector_1/level_cached.psarc",   // relative to gameRoot, '/'
      "kind": "archive",                // or "folder": a level opened from loose files (entries are file names)
      "baseSha256": "...", "baseSize": 134876175, "baseWriteTime": "...",
      "pristine": "pristine/packed/levels/space_sector_1/level_cached.psarc",  // absent until needed
      "installedSha256": "...",         // the archive the last install wrote; absent when never installed
      "installed": "...",
      "entries": [
        { "path": "/built/levels/space_sector_1/default/gp_prius.dat",
          "added": false,               // true: the base archive does not have it (a new script)
          "sha256": "...", "size": 123456,
          "baseSha256": "...",          // the base's own version when the project first changed it
          "saved": "..." }
      ]
    }
  ],
  "editor": { "notes": "", "bakeLights": [], "importedAssets": [] }   // editor-only data, installs nothing
}
```

Paths inside archives are the archives' own (`Locate`'s entry path), compared case-insensitively as PSARC hashes them.
The manifest is written through `<file>.tmp` and a move, so a failed write keeps the previous one.

## Overlay (reading)

`GameFileOverlay.Current` is the open project (null without one). Every reader of game files goes through it:

- `FileManager` opens an archive's **pristine copy** when the project has one (the game's file may be an install of the
  project, or changed since), and returns the project's bytes for any entry the project holds; added entries resolve
  too. `ReadOriginal` therefore returns "the file" as the project sees it, which is what every writer patches.
- Config tables (`GameConfigs.Read`), text tables (`GameText.ReadBytes`), the script browser (`OverlaidArchive`), and
  the Lua writer (`GameFileOverlay.ReadAll`).

## Sinks (writing)

`IGameFileSink` takes an archive path plus replaced and added entries (or a loose file). Writers compute bytes exactly
as before and hand them to the sink (`GameFileSink.Current` unless given one):

| Sink | Does |
|---|---|
| `ProjectSink` | `LunaProject.Store`: checks the entries exist in the base (or are project additions), copies the pristine archive on first use (refused if the game file no longer matches the hash recorded when the level was opened), writes objects, drops an entry whose bytes are back to the base's. |
| `GameArchiveSink` | The former save path: `ArchiveRebuild` (PsarcPatcher + full entry-by-entry verify + an archive's own `<stem>.toc` self-size fix), `Backups.Create`, replace (releasing the level's handles), refresh game.psarc's sizes. Used by Install and by tools/probes. |
| `NoProjectSink` | The editor's sink with no project open: refuses. File > Save opens New Project instead. |

Routed through the sink: `GameplayWriter.Save` (gp_prius, gameplay.dat, zones.dat, collision.dat, everything
`GameplayStructure`/`PodWriter`/`TieAppender`/`ZoneWriter`/`CollisionWriter` produce), `ArchiveEntryWriter.Replace`
(config CSVs, text tables), `LuaScriptPackWriter.Save` (scripts; a new script is the index replacement plus two
additions, its self-size settled at install). `GameplayWriter.Save(outputPath:)` still writes a patched copy for probes.
Not routed: Backups > Restore (a deliberate write of a game file), `GameArchiveSizes.Refresh` (install-time only).

## Install

`ProjectInstaller.Install(project, holder)`:

1. Archives to install: those with entries, or whose last install differs from the base (all entries reverted).
2. Each game file is hashed. It must be the base (`baseSha256`) or the project's last install (`installedSha256`);
   anything else is `Changed` and **nothing** is installed. File > Install offers **Use game file as base** per archive
   (`LunaProject.Rebase`): the game's file becomes the new pristine copy, entries stay, and entries whose game version
   also changed are listed (the project's version wins).
3. The pristine copy's hash is checked, the archive rebuilt from it plus the entries (`ArchiveRebuild`, verified), and
   compared with the game's file: identical means up to date, nothing written, no backup.
4. Otherwise: dated backup (`Backups.Create`), replace, record `installedSha256`. game.psarc goes last; game.toc sizes
   are refreshed once at the end (its own dated backup). The result lists backups; the dialog shows the disk cache
   notice.

## Session (editor)

- File > New Project (name defaults to the title id above USRDIR, location remembered, game folder from the open
  level or the Game Browser), Open Project, Recent Projects (`EditorSettings.RecentProjects`), Project Levels, Close
  Project, Save (Ctrl+S; without a project it opens New Project, then Save), Install to Game.
- Opening a project keeps the open level when it lies under the project's game folder (reloaded through the overlay
  if the project holds entries), else closes it. A level opened while a project is open is recorded in `levels` and
  its archives' hashes are taken (size + write time skip re-hashing).
- The window title is `<project>* - ReLunacy`; the star compares `UndoManager.Revision` with the last save's.
- A structural save still reloads the level, now through the overlay.
