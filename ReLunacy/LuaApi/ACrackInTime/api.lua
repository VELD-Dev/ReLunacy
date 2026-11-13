---@meta
--- A Crack in Time API.
---
--- Written from the decrypted retail EBOOT (NPUA80966) and the shipped scripts, never from
--- guesswork. Each entry names its evidence: a native's address is where its Lua binding
--- starts, found through the EBOOT's registration tables (name, function descriptor). The
--- functions marked "prelude" are Lua the EBOOT runs before any script (text at 0xD2E7A9),
--- quoted rather than inferred.

-------------------------------------------------------------------------------------------------
-- Handles
-------------------------------------------------------------------------------------------------

---A handle to a placed instance, as the generated `instances.lua` declares them:
---`level_grid7 = volume_handle:new( 15, 8 )`.
---
---Every handle type is built the same way, from two integers. ReLunacy writes `instances.lua`
---for you, so use the handles it declares rather than building new ones. The first number is the
---instance's layer, in the order of the region's `metadata.dat` (space_sector_1: `gameplay_moon1a`
---is 9 and holds `SEGMENT_SS1_MOON1A = pod_handle:new( 9, 3 )`; `gameplay_space` is 15, as in
---`level_grid7 = volume_handle:new( 15, 8 )`). The second picks the instance within that layer
---(0x00613490 looks it up in a per-layer list); its exact order is not confirmed.
---@class handle
local handle = {}

---Whether the handle points at a live instance. The same test as the global `is_valid`.
---@return boolean
function handle:is_valid() end

-- How handles behave as Lua values (the bindings are tolua's):
--
-- `a == b` calls the handle class's `.eq` (0x0051CE88), which compares the 32-bit handle value inside
-- each one: the instance's index in its low 16 bits and a check value above (0x004FD50C). Two handles
-- to the same instance are equal even when they are different Lua objects (`get_hero() == aphelion`).
--
-- `tostring(h)` gives the class name ("moby_handle"), never the value, and no native returns the
-- value as a number (README.md shows how to read it anyway). `type(h)` is the class name too: the
-- prelude replaces `type` with `_extype`, which returns the name the object's metatable is
-- registered under (0x004FA0F8), and keeps Lua's own as `_type` ("userdata"). To learn which placed
-- instance a handle is, compare it with the named ones `instances.lua` declares:
--
--     local function name_of(h)
--         for k, v in pairs(_G) do
--             if _type(v) == "userdata" and type(v) == type(h) and v == h then return k end
--         end
--     end
--
-- Passing a handle where text is expected (`tty_out(h)`) raises a Lua error: wrap it in `tostring` or
-- `name_of`.

---A placed Moby: `moby_handle:new( 0, 0 )` (1,112 in space_sector_1).
---@class moby_handle : handle
moby_handle = {}

---@param a integer
---@param b integer
---@return moby_handle
function moby_handle:new(a, b) end

---A placed volume: `volume_handle:new( 15, 8 )` (512 in space_sector_1).
---@class volume_handle : handle
volume_handle = {}

---@param a integer
---@param b integer
---@return volume_handle
function volume_handle:new(a, b) end

---A placed Clue.
---@class clue_handle : handle
clue_handle = {}

---@param a integer
---@param b integer
---@return clue_handle
function clue_handle:new(a, b) end

---A placed controller (cameras and similar).
---@class controller_handle : handle
controller_handle = {}

---@param a integer
---@param b integer
---@return controller_handle
function controller_handle:new(a, b) end

---A placed path.
---@class path_handle : handle
path_handle = {}

---@param a integer
---@param b integer
---@return path_handle
function path_handle:new(a, b) end

---A group of volumes. Walk it with `get_first` / `get_next`, `members`, or `get_volume_table`.
---@class area_handle : handle
area_handle = {}

---@param a integer
---@param b integer
---@return area_handle
function area_handle:new(a, b) end

---The first volume of the area; walking starts here (prelude: `get_volume_table`).
---@return volume_handle
function area_handle:get_first() end

---The next volume, or an invalid handle once the area is walked (prelude: `get_volume_table`).
---@return volume_handle
function area_handle:get_next() end

---A group of Mobys. Walk it with `get_first` / `get_next`, `members`, or `get_moby_table`.
---@class pod_handle : handle
pod_handle = {}

---@param a integer
---@param b integer
---@return pod_handle
function pod_handle:new(a, b) end

---The first Moby of the pod; walking starts here (prelude: `get_moby_table`).
---@return moby_handle
function pod_handle:get_first() end

---The next Moby, or an invalid handle once the pod is walked (prelude: `get_moby_table`).
---@return moby_handle
function pod_handle:get_next() end

---A position returned by the engine: three floats. Registered as `lua_vector` (0xCA0438), with
---`x`, `y`, `z` and the operators `+ - * /` (0xCA0560-0xCA059C).
---
---No script builds one from numbers, and no constructor for it has been found: it can only be
---obtained from functions such as `get_pos` and passed back to others such as `set_pos` or `get_dist`.
---
---`tostring(v)` gives only `"lua_vector"`; log its fields (32-bit floats; Y is up):
---
---```lua
---local p = get_cam_pos(get_hero())
---tty_out(string.format("%.1f %.1f %.1f\n", p.x, p.y, p.z))
---```
---@class vector
---@field x number
---@field y number
---@field z number

---The player as Ratchet on foot. In space sectors the level starts with `aphelion`, and
---`get_hero()` returns whichever of the two is being played (space_sector_1 `main.lua`).
---@type moby_handle
ratchet = nil

---The player's ship.
---@type moby_handle
aphelion = nil

-------------------------------------------------------------------------------------------------
-- Script control
-------------------------------------------------------------------------------------------------

---Gives the rest of the frame back to the engine: `wait = coroutine.yield` (prelude).
---
---With no argument the script resumes on the next frame, which makes the usual loop:
---
---```lua
---while true do
---    -- once per frame
---    wait()
---end
---```
---
---Scripts also pass a time (`wait(.25)` in `scene_sector_arrival.lua`): the value is yielded
---to the engine's script runner, which reads it as seconds.
---@param seconds? number
function wait(seconds) end

---Starts another of the level's scripts by name: `run_script("functions")` runs
---`scripts/gameplay/functions.lua` (space_sector_1 `main.lua`). Native at 0x521904.
---@param name string The script's file name, without folder or extension.
function run_script(name) end

---Whether a handle points at a live instance. Native at 0x520E44.
---@param h handle
---@return boolean
function is_valid(h) end

-------------------------------------------------------------------------------------------------
-- Output
-------------------------------------------------------------------------------------------------

---Writes text to the TTY, **with the RPCS3 patch `rpcs3_lua_tty.yml`** beside this file; without it
---it does nothing in the retail game. Its native (0x521A88) passes the string to 0x4F98C8, a bare
---`blr` (the debug output was compiled out); the patch sends it to the game's own SPU log printer
---(0x717508) instead, which writes `SPU0: <text>` with `sys_tty_write` - into RPCS3's TTY.log.
---
---The printer adds no line break and keeps 255 characters, so end each line yourself:
---
---```lua
---tty_out("hero at cell " .. i .. "\n")
---```
---
---Only strings: anything else raises a Lua error (use `tostring`). For a script error's text, run the
---code under `pcall` and print what it returns - the game's own error report does not reach the TTY:
---
---```lua
---local ok, err = pcall(my_function)
---if not ok then tty_out("error: " .. tostring(err) .. "\n") end
---```
---@param text string
function tty_out(text) end

---Like `tty_out`, through the same patch (its native, 0x521A4C, used the other empty function,
---0x4F98CC). Without the patch it does nothing. The prelude points `prt` and `print` at it.
---@param text string
function screen_out(text) end

---`prt = screen_out` (prelude): writes to the TTY with the RPCS3 patch, see `tty_out`.
---@param text string
function prt(text) end

---Shows text on screen for a while. Unlike `prt`, this works in the retail game: the native
---(0x28DB58) copies the text and hands it to the HUD's message display (0x21C7B4).
---
---```lua
---debug_message("If you find yourself lost on a moon, press R3 ...", 8.0)  -- main.lua
---```
---@param text string
---@param seconds? number How long it stays up; 5 when omitted (the native's default).
---@param flag? boolean Passed to the display; true when omitted. Its effect is unknown.
function debug_message(text, seconds, flag) end

-------------------------------------------------------------------------------------------------
-- Positions and volumes
-------------------------------------------------------------------------------------------------

---Whether a Moby's position is inside a volume. Native at 0x51FB5C; the test is 0x61A124.
---
---The Moby's origin is taken into the volume's frame and checked against the volume's unit
---shape, of half-size / radius 1 along each of its matrix's axes:
---
---- box: |x|, |y| and |z| each at most 1
---- cylinder: distance from the local Y axis at most 1, and |y| at most 1
---- sphere: distance from the centre at most 1
---
---```lua
---if is_inside(ratchet, table_moons[i].vol) and get_hero() == ratchet then  -- main.lua
---```
---@param moby moby_handle
---@param volume volume_handle
---@return boolean
function is_inside(moby, volume) end

---A volume's centre. Native at 0x51F4D8: it reads the volume's runtime entry (0x90 bytes, the
---same table `is_inside` reads volumes from), whose +0x30 holds its matrix's translation.
---
---It takes a volume, not a Moby: no native returning a Moby's position has been found. It does not
---check the handle's class: given a Moby handle it reads the volume at that index, and when the
---check value does not match (almost always) it reads from address 0x30 instead - expect a crash.
---For a position near a player, see `get_cam_pos`.
---@param volume volume_handle
---@return vector
function get_pos(volume) end

---A camera position, reached through a Moby. Native at 0x2877F0: it resolves the Moby, tests its
---class against 0xE5 with the game's class check (0x3A8CE0; probably the player class), follows
---the object at the Moby's +0x40 (0x49E950) and returns the three floats at +0x2900 of what that
---gives.
---
---So `get_cam_pos(get_hero())` should be the camera following the player - the closest a script
---gets to the player's own position, which no native returns. Not yet checked in game whether it is
---the camera or the ship itself, nor what it gives for a Moby that is not a player.
---
---It is one of the three natives returning a `vector`, with `get_pos` and `get_pos_along_path`.
---@param moby moby_handle
---@return vector
function get_cam_pos(moby) end

---Moves a Moby. Native at 0x51D6DC: the position goes to the Moby's runtime entry (0x180
---bytes, +0x30), the same place `is_inside` reads a Moby's position from (0x4FC5FC).
---@param moby moby_handle
---@param position vector
function set_pos(moby, position) end

---The straight-line distance between two positions. Native at 0x51EBA4; the sum of squares
---and square root at 0x4FB458.
---@param a vector
---@param b vector
---@return number
function get_dist(a, b) end

---The Moby being played: `ratchet` on foot, `aphelion` in the ship.
---
---```lua
---if get_hero() == aphelion then  -- main.lua
---```
---@return moby_handle
function get_hero() end

-------------------------------------------------------------------------------------------------
-- Groups (prelude)
-------------------------------------------------------------------------------------------------

---An iterator over an area's volumes, a pod's Mobys or a scent's Clues (prelude), skipping
---invalid ones:
---
---```lua
---for v in members(some_area) do ... end
---```
---@param group area_handle|pod_handle|handle
---@return function
function members(group) end

---A pod's Mobys as a list, from `get_first` / `get_next` (prelude).
---@param pod pod_handle
---@return table
function get_moby_table(pod) end

---An area's volumes as a list, from `get_first` / `get_next` (prelude).
---@param area area_handle
---@return table
function get_volume_table(area) end

---Prints a table's keys and values with `prt` (prelude), so **nothing appears in the retail
---game** (see `screen_out`).
---@param T table
---@param prefix? string
function prt_table(T, prefix) end
