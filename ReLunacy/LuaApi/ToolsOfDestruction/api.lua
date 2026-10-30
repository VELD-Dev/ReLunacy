---@meta
--- Tools of Destruction API.
---
--- Entries here are written from what the shipped scripts demonstrate, never from guesswork: a
--- placeholder with invented types produces confidently wrong completions, which is worse than
--- none. Each one records the evidence it rests on, so a later reader can tell a measured fact
--- from an assumption. The corpus referred to is the 524 .lua/.lc pairs in ToD's global_cached.

---Loads another of this level's scripts, by name.
---
---The name is the bare file name of a script in this level's own `scripts/` folder - no path and
---no `.lua`, so `load_level_script("music")` runs `/levels/<level>/scripts/music.lua`.
---
---Evidence: 310 call sites across the corpus. 307 name a script in the same level, one names a
---script that exists only in another level, and two name scripts no shipped archive contains - all
---three being references to content that was cut. Every call passes exactly one string literal,
---and no call site uses a return value.
---@param name string The script's file name, without folder or extension.
function load_level_script(name) end

---Loads one of the game-wide scripts, by name.
---
---The same shape as `load_level_script`, against `/scripts/` instead of the level's own folder:
---`load_global_script("globalhelp")` runs `/scripts/globalhelp.lua`. These are the scripts shared
---by every level rather than one level's own.
---
---Evidence: 20 call sites, all 20 naming a script under `/scripts/`, all passing one string
---literal, and none using a return value.
---@param name string The script's file name, without folder or extension.
function load_global_script(name) end
