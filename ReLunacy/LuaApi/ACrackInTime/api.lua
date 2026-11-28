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

-------------------------------------------------------------------------------------------------
-- Spec enums
-------------------------------------------------------------------------------------------------

---Dialogue line ids by name, filled by global_cached's /scripts/dialoguespecs.lua
---(`DialogueSpecs["GEN_ZURK_MISC001"] = 1`). Ids run 0..186 and from 10001. Pass one to `speak`
---or `play_explicit_dialog_spec`; the level's fxconduit_packed.dat picks the line it plays.
---@type table<string, integer>
DialogueSpecs = {}

---Sound ids by name, filled by /scripts/soundspecs.lua. Pass one to `play_explicit_sound_spec`.
---@type table<string, integer>
SoundSpecs = {}

-------------------------------------------------------------------------------------------------
-- Other functions
-------------------------------------------------------------------------------------------------

---Represents a string ID (u32) in the localization table.
---@class localized_string
localized_string = {}

---Adds a new mission for a specific level, with the ID withing text table for title, description. Can be set as optional, shortterm or primary.
---@param level_index integer Level index to create the mission in.
---@param title localized_string Also used as Mission's ID.
---@param description localized_string
---@param optional boolean mandatory
---@param shortterm? boolean false by default
---@param primary? boolean false by default
function add_mission(level_index, title, description, optional, shortterm, primary) end

---Marks a mission as completed, and shows a popup during a certain time for it.
---@param level_index integer Level index within to mark a mission as completed
---@param title localized_string used to identify the mission
---@param ui_delay number how many seconds should the complete popup should display
function complete_mission(level_index, title, ui_delay) end

---Unmarks a mission as completed
---@param level_index integer
---@param title localized_string
function uncomplete_mission(level_index, title) end

---Checks if a level has a specific mission
---@param level_index integer
---@param title localized_string
---@return boolean
function has_mission(level_index, title) end

---Checks if a mission in a level is completed or not
---@param level_index integer
---@param title localized_string
---@return boolean
function did_mission(level_index, title) end

---Enables or disables the mission HUD
---@param new_state boolean
function mission_hud_enable(new_state) end

--- Clears the mission hud ?
function mission_hud_flush_queue() end

---Inflicts damages to a damageable entity
---@param target moby_handle must be damageable
---@param amount number
function damage(target, amount) end

---Executes when target moby enters a specified volume
---@param moby moby_handle
---@param volume volume_handle
---@param callback function
function on_enter(moby, volume, callback) end

---Must be used inside an event callback to prevent it from replay before death of player
function unset_event() end

---Shows a popup with the location name, playing the famous wooosh sound and stuff
---@param texthandle localized_string
function show_location(texthandle) end

-------------------------------------------------------------------------------------------------
-- Natives
-------------------------------------------------------------------------------------------------
-- Signatures read from the game's Lua bindings. Parameter names follow their types.

---@class CharConfig
CharConfig = {}

---@class MonolithicConfig
MonolithicConfig = {}

---@class event_handle : handle
event_handle = {}

---@class music_handle : handle
music_handle = {}

---@class pfx_node_handle : handle
pfx_node_handle = {}

---@class scent_handle : handle
scent_handle = {}

---@class snd_handle : handle
snd_handle = {}

---@class vo_handle : handle
vo_handle = {}

---@return moby_handle
function handle:get_first() end

---@return moby_handle
function handle:get_next() end

---@return boolean
function moby_handle:is_valid() end

---@param moby moby_handle
function activate(moby) end

---@param moby moby_handle
---@param controller controller_handle
---@param flag? boolean
---@param value? number
function activate_cam(moby, controller, flag, value) end

---@param moby moby_handle
---@param controller controller_handle
function activate_skydive_cam(moby, controller) end

---@param moby moby_handle
---@param controller controller_handle
---@param flag boolean
---@param value? number
function activate_world_cam(moby, controller, flag, value) end

---@param target handle
---@return number
function active(target) end

---@param moby moby_handle
---@return boolean
function actor_is_moving(moby) end

---@param moby moby_handle
---@return boolean
function actor_is_speaking(moby) end

---@param moby moby_handle
---@param volume volume_handle
---@param value? number
---@param value2? number
---@param value3? number
function actor_move(moby, volume, value, value2, value3) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param flag? boolean
function actor_play_anim(moby, value, value2, value3, flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
function actor_set_color(moby, value, value2, value3) end

---@param moby moby_handle
---@param value number
---@param value2 number
function actor_set_gesture(moby, value, value2) end

---@param moby moby_handle
---@param flag boolean
function actor_set_idle_face_hero(moby, flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
function actor_set_stance(moby, value, value2) end

---@param moby moby_handle
---@param data table
function actor_speak(moby, data) end

---@param moby moby_handle
function actor_stop_moving(moby) end

---No arguments.
function add() end

---@param value number
function add_global_zoni_collected(value) end

---@param value number
---@param text string
function add_scaleform_dialog_spec(value, text) end

---@param value number
---@param text string
function add_scaleform_rumble_spec(value, text) end

---@param value number
---@param text string
function add_scaleform_sound_spec(value, text) end

---@param moby moby_handle
---@param value number
---@param data table
function add_security_module_pattern(moby, value, data) end

---@param volume volume_handle
---@param value number
---@param flag boolean
function add_timeslow_volume(volume, value, flag) end

---@param area area_handle
---@param volume volume_handle
function add_to_area(area, volume) end

---@param pod pod_handle
---@param moby moby_handle
function add_to_pod(pod, moby) end

---@param moby moby_handle
---@param value number
---@param value2 number
function agorian_set_rush_dists(moby, value, value2) end

---@param moby moby_handle
---@param value number
function agorian_set_shots_per_volley(moby, value) end

---@param moby moby_handle
---@param volume volume_handle
function ally_addautotargetvolume(moby, volume) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param flag? boolean
function ally_addtarget(moby, moby2, flag) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param flag boolean
function ally_bombingrun(moby, moby2, flag) end

---@param moby moby_handle
function ally_cancel_script_command(moby) end

---@param moby moby_handle
function ally_cheer(moby) end

---@param moby moby_handle
function ally_clear_face_moby(moby) end

---@param moby moby_handle
function ally_clear_home_area(moby) end

---@param moby moby_handle
function ally_clearautotargetvolume(moby) end

---@param moby moby_handle
function ally_clearfirepoints(moby) end

---@param moby moby_handle
function ally_cleartargets(moby) end

---@param moby moby_handle
function ally_debug(moby) end

---@param moby moby_handle
---@param value number
function ally_equipitem(moby, value) end

---@param moby moby_handle
---@param path path_handle
---@param flag boolean
---@param flag2 boolean
function ally_followpath(moby, path, flag, flag2) end

---@param moby moby_handle
---@param path path_handle
---@param value? number
---@param value2? number
---@param value3? number
---@param flag? boolean
function ally_followpathhover(moby, path, value, value2, value3, flag) end

---@param moby moby_handle
---@return number
function ally_getcurrentstate(moby) end

---@param moby moby_handle
---@param volume volume_handle
---@param flag boolean
---@param flag2 boolean
function ally_gotopoint(moby, volume, flag, flag2) end

---@param moby moby_handle
---@param volume volume_handle
---@param value? number
---@param value2? number
---@param value3? number
---@param flag? boolean
function ally_gotopointhover(moby, volume, value, value2, value3, flag) end

---@param moby moby_handle
---@param flag boolean
function ally_hackconsole(moby, flag) end

---@param moby moby_handle
function ally_hover_land(moby) end

---@param moby moby_handle
function ally_hover_takeoff(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param callback? function
---@return event_handle
function ally_interact(moby, moby2, callback) end

---@param moby moby_handle
---@param flag boolean
function ally_ranged_only(moby, flag) end

---@param moby moby_handle
---@param moby2 moby_handle
function ally_removetarget(moby, moby2) end

---@param moby moby_handle
---@param moby2 moby_handle
function ally_set_face_moby(moby, moby2) end

---@param moby moby_handle
---@param area area_handle
function ally_set_home_area(moby, area) end

---@param moby moby_handle
---@param value number
function ally_set_hover_speed_scale(moby, value) end

---@param moby moby_handle
---@param value number
function ally_set_speed(moby, value) end

---@param moby moby_handle
---@param value number
function ally_setaccuracy(moby, value) end

---@param moby moby_handle
---@param value number
function ally_setattitude(moby, value) end

---@param moby moby_handle
---@param area area_handle
function ally_setawareness(moby, area) end

---@param moby moby_handle
---@param moby2 moby_handle
function ally_setcollisionignore(moby, moby2) end

---@param moby moby_handle
---@param value number
function ally_setdamageamount(moby, value) end

---@param moby moby_handle
---@param area area_handle
function ally_setfirepoints(moby, area) end

---@param moby moby_handle
---@param moby2 moby_handle
function ally_settarget(moby, moby2) end

---@param moby moby_handle
---@param pod pod_handle
---@param value number
---@param value2 number
---@param value3? number
function ally_settargetpreference(moby, pod, value, value2, value3) end

---@param moby moby_handle
---@param volume volume_handle
---@param flag boolean
function ally_settargetvolume(moby, volume, flag) end

---@param moby moby_handle
---@param volume volume_handle
---@param volume2 volume_handle
---@param value number
function ally_skydive(moby, volume, volume2, value) end

---@param moby moby_handle
---@param flag boolean
function ally_specialidle(moby, flag) end

---@param moby moby_handle
function ally_trap_in_forcefield(moby) end

---@param moby moby_handle
---@param value number
function ally_unequipitem(moby, value) end

---No arguments.
function apply() end

---@param moby moby_handle
function arena_bring_aphelion(moby) end

---No arguments.
function arena_clear_cannons() end

---No arguments.
function arena_clear_edge() end

---@param moby moby_handle
---@return number
function arena_count(moby) end

---@param value? number
---@return pod_handle
function arena_create_spawnpod(value) end

---@param moby moby_handle
function arena_delete(moby) end

---@return number
function arena_get_agorian_sniper_ball_count() end

---@param moby moby_handle
function arena_grok_tag_in(moby) end

---@param moby moby_handle
function arena_grok_tag_out(moby) end

---No arguments.
function arena_hush_foo() end

---@param moby moby_handle
---@param volume volume_handle
function arena_hydra_tag_in(moby, volume) end

---@param moby moby_handle
---@param volume volume_handle
function arena_hydra_tag_out(moby, volume) end

---@param moby moby_handle
function arena_park_car(moby) end

---No arguments.
function arena_reset_agorian_balls() end

---No arguments.
function arena_reset_sniper_ball_count() end

---@param value number
function arena_set_advanced(value) end

---@param value number
---@param value2? number
function arena_set_agorian_sniper_ball_speed(value, value2) end

---@param path path_handle
function arena_set_edge(path) end

---No arguments.
function arena_snap_cannons() end

---@param value number
---@param value2? number
---@return moby_handle
function arena_spawn(value, value2) end

---No arguments.
function arena_unlock() end

---No arguments.
function arena_up_in_here() end

---@param volume volume_handle
---@param value number
---@param value2 number
---@param value3 number
function arena_warp_vol(volume, value, value2, value3) end

---No arguments.
function arenavo_cut_and_pause() end

---No arguments.
function arenavo_pause() end

---No arguments.
function arenavo_unpause() end

---@param moby moby_handle
---@param moby2 moby_handle
function attach_battery_bot(moby, moby2) end

---@param moby moby_handle
---@param value number
function awareness_add_targettype(moby, value) end

---@param moby moby_handle
function awareness_clear_targettypes(moby) end

---@param moby moby_handle
---@param value number
function awareness_remove_targettype(moby, value) end

---@param moby moby_handle
---@param moby2 moby_handle
function azimuth_backstab(moby, moby2) end

---@param moby moby_handle
function azimuth_end_grind(moby) end

---@param moby moby_handle
---@return number
function azimuth_get_deathblows_left(moby) end

---@param moby moby_handle
function azimuth_glass_shatter(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param value number
function azimuth_set_chase_distance(moby, moby2, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
function azimuth_setup_deathblows(moby, value, value2, value3) end

---@param moby moby_handle
---@param flag boolean
function azimuth_should_use_nav(moby, flag) end

---@param moby moby_handle
function azimuth_start_grind(moby) end

---@param moby moby_handle
---@param volume volume_handle
function azimuth_throw_bomb(moby, volume) end

---@param moby moby_handle
---@param volume volume_handle
function azimuth_throw_flare(moby, volume) end

---@param moby moby_handle
---@param flag boolean
function bangle_all_off(moby, flag) end

---@param moby moby_handle
---@param flag boolean
function bangle_all_on(moby, flag) end

---@param moby moby_handle
---@param value number
---@param flag boolean
function bangle_off(moby, value, flag) end

---@param moby moby_handle
---@param value number
---@param flag boolean
function bangle_on(moby, value, flag) end

---@param controller controller_handle
---@param moby moby_handle
---@param value number
---@param value2 number
function battlefieldcontroller_addtarget(controller, moby, value, value2) end

---@param controller controller_handle
function battlefieldcontroller_print(controller) end

---@param controller controller_handle
---@param moby moby_handle
function battlefieldcontroller_removetarget(controller, moby) end

---@param controller controller_handle
---@param moby moby_handle
---@param value number
---@param value2 number
function battlefieldcontroller_updatetarget(controller, moby, value, value2) end

---No arguments.
function beat_game_trophy() end

---@param moby moby_handle
---@param moby2 moby_handle
---@param moby3 moby_handle
function blob_scripted_combine(moby, moby2, moby3) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param value number
function boss_register_cam(moby, moby2, value) end

---@param moby moby_handle
---@param moby2 moby_handle
function boss_unregister_cam(moby, moby2) end

---@param moby moby_handle
function break_time_ribbon(moby) end

---@param moby moby_handle
---@param flag boolean
function bridge_extend(moby, flag) end

---@param moby moby_handle
---@return boolean
function bridge_is_extended(moby) end

---@param moby moby_handle
---@param flag boolean
function bridge_retract(moby, flag) end

---@param moby moby_handle
---@return boolean
function button_is_on(moby) end

---@param moby moby_handle
---@param flag boolean
---@return boolean
function button_off(moby, flag) end

---@param moby moby_handle
---@param flag boolean
---@return boolean
function button_on(moby, flag) end

---No arguments.
function call() end

---@param moby moby_handle
function camera_clear_recenter(moby) end

---@param moby moby_handle
---@param value? number
function camera_request_recenter(moby, value) end

---@param position vector
---@param value number
---@param value2 number
---@param value3 number
---@param value4? number
---@param value5? number
---@param flag? boolean
function camera_shake(position, value, value2, value3, value4, value5, flag) end

---@param value number
function cannon_set_rate(value) end

---@param moby moby_handle
---@param path path_handle
function cassy_exit_arena(moby, path) end

---@param moby moby_handle
---@param path path_handle
function cassy_follow_path(moby, path) end

---@param moby moby_handle
---@param volume volume_handle
function cassy_go_to_volume(moby, volume) end

---@param moby moby_handle
---@param flag boolean
function cassy_play_idle(moby, flag) end

---@param moby moby_handle
function cassy_return_arena(moby) end

---@param moby moby_handle
function cassy_start_fighting(moby) end

---@param target handle
---@param text string
---@return nil
function cast(target, text) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
function change_hud_colour(value, value2, value3, value4) end

---@param value number
---@return boolean
function check_hero_flag(value) end

---@param moby moby_handle
---@param pod pod_handle
---@param volume volume_handle
---@param flag boolean
---@param flag2 boolean
---@param flag3? boolean
function checkpoint(moby, pod, volume, flag, flag2, flag3) end

---@return boolean
function clank_collected_staff() end

---@param moby moby_handle
function clank_disable_puzzle_help(moby) end

---@param moby moby_handle
function clank_enable_puzzle_help(moby) end

---@param moby moby_handle
function clank_equip_reflect(moby) end

---@param moby moby_handle
function clank_equip_staff(moby) end

---@param moby moby_handle
function clank_equip_timebomb(moby) end

---@return boolean
function clank_is_fixing_time() end

---@param moby moby_handle
---@return boolean
function clank_is_puzzle_help_enabled(moby) end

---@return boolean
function clank_is_recording() end

---@param moby moby_handle
function clank_remove_reflect(moby) end

---@param moby moby_handle
function clank_remove_staff(moby) end

---@param moby moby_handle
function clank_remove_timebomb(moby) end

---@param moby moby_handle
function clank_root(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
function clank_station_warp(moby, moby2) end

---No arguments.
function clank_stop_recording() end

---@param moby moby_handle
function clank_unroot(moby) end

---No arguments.
function clear() end

---No arguments.
function clear_all_projectiles() end

---@param moby moby_handle
function clear_camera_turn_limits(moby) end

---@param value number
function clear_dialog_played(value) end

---@param value number
function clear_hero_flag(value) end

---No arguments.
function clear_location() end

---No arguments.
function clone() end

---@param moby moby_handle
function clone_pad_delete_all(moby) end

---@param moby moby_handle
function clone_pad_delete_one(moby) end

---@param moby moby_handle
function clone_pad_disable_sigmund(moby) end

---@param moby moby_handle
function clone_pad_enable_sigmund(moby) end

---@param moby moby_handle
---@return number
function clone_pad_get_count(moby) end

---@param moby moby_handle
---@return boolean
function clone_pad_has_clone(moby) end

---@param pod pod_handle
---@param pod2 pod_handle
function clone_pad_set_reset_pod(pod, pod2) end

---@param moby moby_handle
function clone_pad_set_sigmund_hidden(moby) end

---@param moby moby_handle
function clone_pad_set_sigmund_idle(moby) end

---@param moby moby_handle
---@param path path_handle
function clone_pad_set_sigmund_path(moby, path) end

---@param moby moby_handle
function clone_pad_set_sigmund_preview(moby) end

---No arguments.
function close_help() end

---@param moby moby_handle
function close_saw(moby) end

---@param moby moby_handle
function close_smasher(moby) end

---@param moby moby_handle
function coll_off(moby) end

---@param moby moby_handle
function coll_on(moby) end

---@return MonolithicConfig
function config() end

---@param moby moby_handle
function confirm_activate(moby) end

---@param moby moby_handle
function confirm_clear(moby) end

---@param moby moby_handle
function confirm_deactivate(moby) end

---@param moby moby_handle
---@param text string
function confirm_set_debug_text(moby, text) end

---@param moby moby_handle
---@param value number
function confirm_set_distance(moby, value) end

---@param moby moby_handle
---@param value number
function confirm_set_tag(moby, value) end

---No arguments.
function contains() end

---@param moby moby_handle
---@return number
function crank_get_progress(moby) end

---@param moby moby_handle
---@return boolean
function crank_is_complete(moby) end

---@param moby moby_handle
---@return boolean
function crank_is_in_use(moby) end

---@param moby moby_handle
function crank_reset(moby) end

---@param moby moby_handle
function crank_set_complete(moby) end

---@param value number
---@param value2? number
---@return area_handle
function create_new_area(value, value2) end

---@param value number
---@param flag boolean
---@return path_handle
function create_path(value, flag) end

---@param value number
---@param flag boolean
---@return path_handle
function create_railpath(value, flag) end

---@param position vector
---@param value number
---@param value2 number
function damage_sphere(position, value, value2) end

---@param moby moby_handle
function deactivate(moby) end

---@param moby moby_handle
---@param controller controller_handle
---@param flag? boolean
---@param value? number
function deactivate_cam(moby, controller, flag, value) end

---No arguments.
function deactivate_seeker_mines() end

---@param moby moby_handle
function deactivate_world_cam(moby) end

---No arguments.
function decode() end

---No arguments.
function default() end

---@param moby moby_handle
function default_follow_cam_look_at_height(moby) end

---@param moby moby_handle
function default_follow_cam_pos(moby) end

---@param area area_handle
function delete(area) end

---No arguments.
function delete_all_chunks() end

---@param moby moby_handle
function delete_enemies_except(moby) end

---@param volume volume_handle
function delete_vol(volume) end

---@param moby moby_handle
function detach_battery_bot(moby) end

---@param moby moby_handle
function disable(moby) end

---@param controller controller_handle
function disable_cam(controller) end

---No arguments.
function disable_great_clock() end

---No arguments.
function disable_weather() end

---@param text string
function disable_zone_rendering(text) end

---No arguments.
function distance() end

---@param moby moby_handle
function door_clear_force(moby) end

---@param moby moby_handle
---@param flag? boolean
function door_close(moby, flag) end

---@param moby moby_handle
---@param value? number
function door_force_closed(moby, value) end

---@param moby moby_handle
---@param value? number
function door_force_open(moby, value) end

---@param moby moby_handle
---@param flag? boolean
---@return boolean
function door_is_closed(moby, flag) end

---@param moby moby_handle
---@param flag? boolean
---@return boolean
function door_is_open(moby, flag) end

---@param moby moby_handle
---@param flag? boolean
function door_open(moby, flag) end

---@param moby moby_handle
function draw_off(moby) end

---@param moby moby_handle
function elevator_cranked_uncrank(moby) end

---@param moby moby_handle
---@return boolean
function elevator_is_at_first_node(moby) end

---@param moby moby_handle
---@return boolean
function elevator_is_at_second_node(moby) end

---@param moby moby_handle
---@return boolean
function elevator_is_moving(moby) end

---@param moby moby_handle
function elevator_move(moby) end

---@param moby moby_handle
function elevator_move_to_first_node(moby) end

---@param moby moby_handle
function elevator_move_to_second_node(moby) end

---@param moby moby_handle
function elevator_warp_to_first_node(moby) end

---@param moby moby_handle
function elevator_warp_to_second_node(moby) end

---@param moby moby_handle
function enable(moby) end

---No arguments.
function enable_all_cheats() end

---No arguments.
function enable_weather() end

---@param text string
function enable_zone_rendering(text) end

---@param pod pod_handle
---@return boolean
function enemy_any_awake(pod) end

---@param moby moby_handle
function enemy_clear_firepoint_controller(moby) end

---@param moby moby_handle
function enemy_clear_firepoints(moby) end

---@param moby moby_handle
function enemy_clear_volume_target(moby) end

---@param moby moby_handle
---@return number
function enemy_get_health(moby) end

---@param moby moby_handle
---@return number
function enemy_get_health_abs(moby) end

---@param moby moby_handle
function enemy_go_idle(moby) end

---@param moby moby_handle
---@return boolean
function enemy_is_awake(moby) end

---@param moby moby_handle
---@param value number
function enemy_kill_below_object(moby, value) end

---@param moby moby_handle
---@param ... any
function enemy_set_ambient_attack_scent(moby, ...) end

---@param moby moby_handle
---@param controller controller_handle
function enemy_set_firepoint_controller(moby, controller) end

---@param moby moby_handle
---@param area area_handle
function enemy_set_firepoints(moby, area) end

---@param moby moby_handle
---@param value number
---@param value2 number
function enemy_set_health(moby, value, value2) end

---@param moby moby_handle
---@param value number
function enemy_set_inner_awareness(moby, value) end

---@param moby moby_handle
---@param ... any
function enemy_set_intro_path(moby, ...) end

---@param moby moby_handle
---@param value number
function enemy_set_outer_awareness(moby, value) end

---@param moby moby_handle
---@param ... any
function enemy_set_patrol_path(moby, ...) end

---@param moby moby_handle
---@param volume volume_handle
function enemy_set_volume_target(moby, volume) end

---@param volume volume_handle
---@param value number
---@param value2 number
---@param pod pod_handle
---@param controller controller_handle
---@param value3? number
---@param value4? number
---@return moby_handle
function enemy_spawn(volume, value, value2, pod, controller, value3, value4) end

---@param moby moby_handle
function enemy_wakeup(moby) end

---No arguments.
function equals() end

---No arguments.
function escape() end

---@param value? number
function fade_from_black(value) end

---@param value? number
function fade_from_white(value) end

---@param value? number
function fade_to_black(value) end

---@param value? number
function fade_to_white(value) end

---@param path path_handle
function finish_grindrail(path) end

---@param path path_handle
function finish_path(path) end

---@param moby moby_handle
function fixable_break(moby) end

---@param moby moby_handle
function fixable_fix(moby) end

---@param moby moby_handle
---@return boolean
function fixable_is_broken(moby) end

---@param moby moby_handle
---@return boolean
function fixable_is_fixed(moby) end

---@param moby moby_handle
function flame_jet_off(moby) end

---@param moby moby_handle
function flame_jet_on(moby) end

---@param moby moby_handle
function flame_jet_restart(moby) end

---No arguments.
function force_abandon_pickups() end

---@param value? number
function force_item_bar(value) end

---No arguments.
function force_pickup_bolts() end

---@param controller controller_handle
---@param flag boolean
function force_track_fx(controller, flag) end

---@param value number
---@param value2? number
function ftg_goto_anomaly(value, value2) end

---@param value number
---@param value2? number
---@param value3? number
function ftg_goto_position(value, value2, value3) end

---@param value number
---@param value2? number
function ftg_goto_seeker(value, value2) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
function ftg_move_anomaly(value, value2, value3, value4) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
function ftg_move_powerup(value, value2, value3, value4) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
function ftg_move_seeker(value, value2, value3, value4) end

---@param moby moby_handle
function ftg_set_incomplete(moby) end

---@param value number
---@param value2? number
---@param value3? number
function ftg_set_seeker_target(value, value2, value3) end

---@param value number
---@param value2? number
---@param value3? number
function ftg_show_message(value, value2, value3) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param flag? boolean
---@param flag2? boolean
function ftg_spawn_anomaly(value, value2, value3, value4, flag, flag2) end

---@param value number
---@param value2? number
---@param value3? number
function ftg_spawn_bomb(value, value2, value3) end

---@param value number
---@param value2? number
---@param value3? number
function ftg_spawn_energy(value, value2, value3) end

---@param value number
---@param value2? number
---@param value3? number
---@param flag? boolean
---@param flag2? boolean
function ftg_spawn_seeker(value, value2, value3, flag, flag2) end

---@param value number
---@param value2? number
---@param value3? number
function ftg_spawn_timebomb(value, value2, value3) end

---@param value number
function ftg_start_firing(value) end

---@param value number
function ftg_stop_firing(value) end

---@param value number
function ftg_tutorial_done(value) end

---@param value number
---@param value2? number
function ftg_upgrade_anomaly(value, value2) end

---@param moby moby_handle
---@param value number
---@param value2 number
function gear_hazard_set_speed(moby, value, value2) end

---@param area area_handle
---@return number
function get_area_size(area) end

---@param moby moby_handle
---@return moby_handle
function get_attached_battery_bot(moby) end

---@return boolean
function get_cam_invert_x() end

---@return boolean
function get_cam_invert_y() end

---@param value number
---@return number
function get_challenge_successes(value) end

---@return volume_handle
function get_checkpoint_volume() end

---@param moby moby_handle
---@return moby_handle
function get_clank(moby) end

---@param pod pod_handle
---@param position vector
---@return moby_handle
function get_closest(pod, position) end

---@param scent scent_handle
---@param value number
---@return clue_handle
function get_clue(scent, value) end

---@return number
function get_core_rand() end

---@return number
function get_current_time() end

---@return number
function get_difficulty() end

---@param target handle
---@return moby_handle
function get_first(target) end

---@param moby moby_handle
---@return number
function get_follow_cam_dist(moby) end

---@param moby moby_handle
---@return number
function get_follow_cam_fov(moby) end

---@param moby moby_handle
---@return number
function get_follow_cam_height(moby) end

---@param moby moby_handle
---@return number
function get_follow_cam_pitch(moby) end

---@param moby moby_handle
---@return number
function get_grapple_progress(moby) end

---@param moby moby_handle
---@return moby_handle
function get_grapple_target(moby) end

---@return number
function get_greatclocka_skill_time() end

---@param path path_handle
---@return number
function get_length(path) end

---@return number
function get_level() end

---@return boolean
function get_lockstrafe_invert_x() end

---@return boolean
function get_lockstrafe_invert_y() end

---@param pod pod_handle
---@param value number
---@return moby_handle
function get_moby(pod, value) end

---@return number
function get_molonoth_timetrial_time() end

---@return boolean
function get_music_track_request() end

---@param moby moby_handle
---@param value number
---@param value2 number
---@return number
function get_nearby_target_count(moby, value, value2) end

---@param target handle
---@return moby_handle
function get_next(target) end

---@param pod pod_handle
---@return number
function get_num_alive(pod) end

---@param pod pod_handle
---@return number
function get_num_dancing_in_pod(pod) end

---@param pod pod_handle
---@return number
function get_num_in_pod(pod) end

---@param pod pod_handle
---@param volume volume_handle
---@return number
function get_num_inside(pod, volume) end

---@return number
function get_player_deaths_on_level() end

---@param pod pod_handle
---@return number
function get_pod_size(pod) end

---@param path path_handle
---@param value number
---@return vector
function get_pos_along_path(path, value) end

---@param scent scent_handle
---@return number
function get_scent_size(scent) end

---@return number
function get_skillpoint_bool() end

---@param moby moby_handle
---@return number
function get_switch_value(moby) end

---@return number
function get_time_step() end

---@return number
function get_total_clone_count() end

---@return number
function get_tumbler_skillpoint_time() end

---@return number
function get_valkyrie_skillpoint_time() end

---@param area area_handle
---@param value number
---@return volume_handle
function get_volume(area, value) end

---@return nil
function getpeer() end

---@return number
function global_num_zoni_collected() end

---@param text string
---@return boolean
function has_cinematic_played(text) end

---@param value number
---@return boolean
function has_dialog_played(value) end

---@param text string
---@return boolean
function has_movie_played(text) end

---@param text string
---@return boolean
function has_scene_played(text) end

---@param moby moby_handle
---@return boolean
function has_teleported(moby) end

---@return boolean
function has_valid_checkpoint() end

---@return boolean
function have_burrowers_been_killed() end

---@param moby moby_handle
---@param moby2 moby_handle
function head_lookat(moby, moby2) end

---@param moby moby_handle
function head_lookat_stop(moby) end

---@param moby moby_handle
---@param flag boolean
function health_set_invulnerability(moby, flag) end

---@param value number
---@param text string
---@param flag boolean
---@param flag2 boolean
---@param flag3 boolean
---@param flag4 boolean
---@param flag5 boolean
---@param flag6 boolean
---@param flag7 boolean
---@return boolean
function help(value, text, flag, flag2, flag3, flag4, flag5, flag6, flag7) end

---@param value number
---@param text string
---@param flag? boolean
---@param flag2? boolean
---@param flag3? boolean
---@param flag4? boolean
---@param flag5? boolean
---@param flag6? boolean
---@return boolean
function helptext(value, text, flag, flag2, flag3, flag4, flag5, flag6) end

---@param moby moby_handle
---@param value number
---@param flag? boolean
function hero_add_bolts(moby, value, flag) end

---@param moby moby_handle
---@param value number
---@param value2? number
function hero_add_object(moby, value, value2) end

---@param moby moby_handle
---@param value number
---@param value2? number
function hero_add_quick_select(moby, value, value2) end

---@param moby moby_handle
---@param value number
---@param controller controller_handle
---@param volume volume_handle
---@param value2? number
---@param value3? number
function hero_award_weapon(moby, value, controller, volume, value2, value3) end

---@param moby moby_handle
---@param value number
function hero_custom_prompt(moby, value) end

---@param moby moby_handle
---@param value number
function hero_equip(moby, value) end

---@param moby moby_handle
function hero_exit_turret(moby) end

---@param moby moby_handle
---@param flag boolean
function hero_force_idle_on_rail(moby, flag) end

---@param moby moby_handle
---@param flag boolean
function hero_force_index_cam(moby, flag) end

---@param moby moby_handle
---@param flag boolean
function hero_force_underwater_fx(moby, flag) end

---@param moby moby_handle
---@param value number
---@return number
function hero_get_ammo(moby, value) end

---@param moby moby_handle
---@return number
function hero_get_armor(moby) end

---@param moby moby_handle
---@return number
function hero_get_bolts(moby) end

---@param moby moby_handle
---@return number
function hero_get_equipped(moby) end

---@param moby moby_handle
---@return number
function hero_get_health(moby) end

---@param moby moby_handle
---@return number
function hero_get_num_items(moby) end

---@param moby moby_handle
---@param value number
---@return number
function hero_get_num_objects(moby, value) end

---@param moby moby_handle
---@return number
function hero_get_soaker_liquid(moby) end

---@param moby moby_handle
function hero_give_all_ammo(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
function hero_give_gold_bolt(moby, moby2) end

---@param moby moby_handle
---@param value number
function hero_give_part(moby, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
function hero_give_powerup(moby, value, value2) end

---@param moby moby_handle
---@param value number
---@param value2? number
---@param value3? number
function hero_give_weapon(moby, value, value2, value3) end

---@param moby moby_handle
---@param value number
---@return boolean
function hero_has_object(moby, value) end

---@param moby moby_handle
---@param value number
---@return boolean
function hero_has_part(moby, value) end

---@param moby moby_handle
---@param value number
---@return boolean
function hero_has_powerup(moby, value) end

---@param moby moby_handle
---@param value number
---@return boolean
function hero_has_weapon(moby, value) end

---@param moby moby_handle
---@param value number
---@return boolean
function hero_has_wrench_pickup(moby, value) end

---@param moby moby_handle
function hero_heal(moby) end

---@param moby moby_handle
---@return boolean
function hero_is_targeted(moby) end

---@param moby moby_handle
---@param flag boolean
---@param flag2? boolean
---@param flag3? boolean
function hero_lockout_control(moby, flag, flag2, flag3) end

---@param moby moby_handle
---@param moby2 moby_handle
function hero_look_at_moby(moby, moby2) end

---@param moby moby_handle
function hero_look_at_targets(moby) end

---@param moby moby_handle
function hero_look_at_weapon_target(moby) end

---@param moby moby_handle
---@param value number
function hero_remove_quick_select(moby, value) end

---@param moby moby_handle
---@param value number
function hero_restrict_equip(moby, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
function hero_set_ammo(moby, value, value2) end

---@param moby moby_handle
---@param value number
function hero_set_armor(moby, value) end

---@param moby moby_handle
---@param value number
function hero_set_bolts(moby, value) end

---@param moby moby_handle
---@param value number
function hero_set_grind_speed(moby, value) end

---@param moby moby_handle
---@param flag boolean
function hero_set_infinite_ammo(moby, flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
function hero_set_num_objects(moby, value, value2) end

---@param moby moby_handle
---@param value number
function hero_set_soaker_liquid(moby, value) end

---@param moby moby_handle
---@param flag boolean
function hero_set_swingshot(moby, flag) end

---@param moby moby_handle
---@param value number
function hero_set_xp(moby, value) end

---@param moby moby_handle
---@param text string
function hero_ship_add_enter_cine(moby, text) end

---@param moby moby_handle
---@param text string
function hero_ship_add_enter_movie(moby, text) end

---@param moby moby_handle
---@param text string
function hero_ship_add_exit_cine(moby, text) end

---@param moby moby_handle
---@param text string
function hero_ship_add_exit_movie(moby, text) end

---@param moby moby_handle
---@param flag boolean
---@param flag2? boolean
function hero_ship_fly_in(moby, flag, flag2) end

---@param moby moby_handle
---@param flag boolean
---@param flag2? boolean
function hero_ship_fly_out(moby, flag, flag2) end

---@param moby moby_handle
function hero_swingshot_fall(moby) end

---@param moby moby_handle
---@param value number
function hero_take_weapon(moby, value) end

---@param moby moby_handle
function hero_unrestrict_equip(moby) end

---@param ... any
function hide(...) end

---@param moby moby_handle
---@return boolean
function hookup_is_in_use(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
---@return number
function hoverboot_band(moby, moby2) end

---@param value number
function hud_set_count(value) end

---@param value number
function hud_set_max(value) end

---@param value number
function hud_set_tag(value) end

---@param value number
function hud_set_time(value) end

---@param value number
---@param value2? number
function hud_start_count_timer(value, value2) end

---@param value number
function hud_start_counter(value) end

---No arguments.
function hud_start_nef_legend() end

---@param value number
function hud_start_overlay(value) end

---@param value number
function hud_start_timer(value) end

---No arguments.
function hud_stop_nef_legend() end

---No arguments.
function hud_stop_overlay() end

---No arguments.
function hud_stop_timer_count() end

---@param moby moby_handle
---@return number
function hydra_tank_heads_alive(moby) end

---@param moby moby_handle
function hydra_tank_kill(moby) end

---@param moby moby_handle
---@return boolean
function hydra_tank_shield_took_damage(moby) end

---@param moby moby_handle
---@param volume volume_handle
function hydra_tank_shoot_at_volume(moby, volume) end

---@param moby moby_handle
function hydroplant_restore(moby) end

---No arguments.
function identity() end

---No arguments.
function imecommand() end

---No arguments.
function inflate() end

---No arguments.
function inherit() end

---@param volume volume_handle
---@param controller controller_handle
function init_arena(volume, controller) end

---No arguments.
function initialize() end

---No arguments.
function interpolate() end

---No arguments.
function intersection() end

---No arguments.
function intersects() end

---@return moby_handle
function invalid() end

---No arguments.
function invert() end

---@param moby moby_handle
---@return boolean
function is_activated(moby) end

---@param moby moby_handle
---@param controller controller_handle
---@return boolean
function is_active_cam(moby, controller) end

---@param moby moby_handle
---@return boolean
function is_alive(moby) end

---@return vo_handle
function is_avo_done() end

---@param moby moby_handle
---@return boolean
function is_cam_done(moby) end

---@param moby moby_handle
---@return boolean
function is_charging(moby) end

---@param volume volume_handle
---@param flag? boolean
---@return boolean
function is_clone_in_volume(volume, flag) end

---@param moby moby_handle
---@return boolean
function is_collected(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param flag boolean
---@return boolean
function is_cranking(moby, moby2, flag) end

---@param moby moby_handle
---@return boolean
function is_drawn(moby) end

---@param moby moby_handle
---@return boolean
function is_fix_time_game_complete(moby) end

---@param moby moby_handle
---@return boolean
function is_grabbing(moby) end

---@param moby moby_handle
---@return boolean
function is_grappling(moby) end

---@param moby moby_handle
---@return boolean
function is_grinding(moby) end

---@param moby moby_handle
---@return boolean
function is_hero_active(moby) end

---@param moby moby_handle
---@return boolean
function is_hidden(moby) end

---@param moby moby_handle
---@return boolean
function is_hoverboot_thrusting(moby) end

---@param moby moby_handle
---@return boolean
function is_hoverbooting(moby) end

---@return boolean
function is_hud_timer_done() end

---@return boolean
function is_in_gameplay() end

---@param moby moby_handle
---@param clue clue_handle
---@return boolean
function is_in_jump_slot(moby, clue) end

---@return boolean
function is_in_replay() end

---@param volume volume_handle
---@return boolean
function is_in_view(volume) end

---@param moby moby_handle
---@return boolean
function is_ledge_grabbing(moby) end

---@return boolean
function is_lock_strafe_active() end

---@param moby moby_handle
---@return boolean
function is_moby_slowed(moby) end

---@return boolean
function is_new_load() end

---@param moby moby_handle
---@return boolean
function is_on_gel_cube(moby) end

---@param moby moby_handle
---@return boolean
function is_on_grav_surface(moby) end

---@param moby moby_handle
---@return boolean
function is_pf_moving(moby) end

---@param moby moby_handle
---@return boolean
function is_pollyx_present(moby) end

---@param value number
---@return boolean
function is_post_effect_enabled(value) end

---@param text string
---@return boolean
function is_script_running(text) end

---@param pod pod_handle
---@return boolean
function is_segment_complete(pod) end

---@return boolean
function is_six_axis_active() end

---@param value number
---@return boolean
function is_skillpoint_done(value) end

---@return boolean
function is_skillpoint_time_fast_enough() end

---@param sound snd_handle
---@return boolean
function is_sound_done(sound) end

---@param moby moby_handle
---@return boolean
function is_triggered(moby) end

---@param moby moby_handle
---@return boolean
function is_update_on(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
---@return boolean
function is_using_jump_pad(moby, moby2) end

---@param volume volume_handle
---@return boolean
function is_view_thru_volume(volume) end

---@param vo vo_handle
---@return boolean
function is_vo_done(vo) end

---@param value number
---@return boolean
function is_weapon_type_used_unique(value) end

---@param moby moby_handle
---@return boolean
function is_webtacle_thrashing(moby) end

---@param moby moby_handle
---@return boolean
function is_zipping(moby) end

---No arguments.
function join() end

---@param target handle
---@return number
function last_reward(target) end

---@param moby moby_handle
---@param path path_handle
function lawrence_follow_path(moby, path) end

---@param moby moby_handle
---@param volume volume_handle
function lawrence_go_to_volume(moby, volume) end

---@param moby moby_handle
---@param flag boolean
function lawrence_set_attacking(moby, flag) end

---@param moby moby_handle
---@param value number
function lawrence_set_speed(moby, value) end

---@param moby moby_handle
function lawrence_stop_moving(moby) end

---@param value? number
function letter_box_off(value) end

---@param value? number
---@param flag boolean
function letter_box_on(value, flag) end

---@return number
function level_num_zoni_collected() end

---@param controller controller_handle
---@param volume volume_handle
function lightning(controller, volume) end

---No arguments.
function localhost() end

---@param target handle
---@return number
function m_unlocked(target) end

---@param moby moby_handle
function maingeom_off(moby) end

---@param moby moby_handle
function maingeom_on(moby) end

---@param moby moby_handle
function make_actor(moby) end

---@param moby moby_handle
---@param volume volume_handle
---@param value number
---@param flag boolean
function moby_simple_move(moby, volume, value, flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
function moby_simple_rotate(moby, value, value2, value3) end

---@param controller controller_handle
function mobyspawner_clear(controller) end

---@param controller controller_handle
---@param value number
function mobyspawner_spawn(controller, value) end

---No arguments.
function molonoth_junk_clear() end

---@param volume volume_handle
---@param value number
---@param value2 number
---@param value3 number
function move_volume(volume, value, value2, value3) end

---@param moby moby_handle
---@param value number
function multipad_set(moby, value) end

---@param pod pod_handle
---@param pod2 pod_handle
---@param pod3 pod_handle
---@param value number
---@param pod4? pod_handle
function nef_swarmer_fill_template(pod, pod2, pod3, value, pod4) end

---@param moby moby_handle
---@param pod pod_handle
---@param value number
function nef_trooper_spawn_swarmers(moby, pod, value) end

---@param moby moby_handle
---@param moby2 moby_handle
function nefarious_follow_lawrence(moby, moby2) end

---@param moby moby_handle
---@param path path_handle
function nefarious_follow_path(moby, path) end

---@param moby moby_handle
---@param volume volume_handle
function nefarious_go_to_volume(moby, volume) end

---@param moby moby_handle
---@param flag boolean
function nefarious_laser_bulb_charge(moby, flag) end

---@param moby moby_handle
function nefarious_laser_create(moby) end

---@param moby moby_handle
---@param flag boolean
---@param flag2 boolean
---@param value number
function nefarious_laser_set_eye_target(moby, flag, flag2, value) end

---@param moby moby_handle
---@param volume volume_handle
function nefarious_laser_start_chase(moby, volume) end

---@param moby moby_handle
function nefarious_laser_stop(moby) end

---@param moby moby_handle
function nefarious_laser_stop_chase(moby) end

---@param moby moby_handle
---@param volume volume_handle
function nefarious_laser_zot(moby, volume) end

---@param moby moby_handle
function nefarious_monologue(moby) end

---@param moby moby_handle
function nefarious_resume_behavior(moby) end

---@param moby moby_handle
---@return boolean
function nefarious_script_done(moby) end

---@param moby moby_handle
---@param value number
function nefarious_set_speed(moby, value) end

---@param moby moby_handle
---@param value number
---@param area area_handle
---@param pod pod_handle
---@param pod2 pod_handle
function nefarious_spawn_swarmers(moby, value, area, pod, pod2) end

---@param moby moby_handle
function nefarious_start_taunting(moby) end

---@param moby moby_handle
function nefarious_trigger_scanned(moby) end

---No arguments.
function normalize() end

---@param moby moby_handle
function npc_cheer(moby) end

---@param moby moby_handle
function npc_clear_face_moby(moby) end

---@param moby moby_handle
function npc_clear_targets(moby) end

---@param moby moby_handle
function npc_disable_damage(moby) end

---@param moby moby_handle
function npc_enable_damage(moby) end

---@param moby moby_handle
---@param path path_handle
---@param area area_handle
function npc_get_attention(moby, path, area) end

---@param moby moby_handle
function npc_go_idle(moby) end

---@param moby moby_handle
---@param volume volume_handle
function npc_goto(moby, volume) end

---@param moby moby_handle
function npc_holo_down(moby) end

---@param moby moby_handle
function npc_holo_up(moby) end

---@param moby moby_handle
function npc_icon_off(moby) end

---@param moby moby_handle
---@param value number
function npc_icon_on(moby, value) end

---@param moby moby_handle
---@param flag boolean
function npc_idle_dialogue_enable(moby, flag) end

---@param flag boolean
function npc_idle_dialogue_global_enable(flag) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param callback? function
---@return event_handle
function npc_interact(moby, moby2, callback) end

---@param moby moby_handle
---@param value number
---@param path path_handle
---@param area? area_handle
function npc_patrol(moby, value, path, area) end

---@param moby moby_handle
---@param value number
function npc_scale_damage(moby, value) end

---@param moby moby_handle
---@param flag boolean
function npc_set_face_hero(moby, flag) end

---@param moby moby_handle
---@param moby2 moby_handle
function npc_set_face_moby(moby, moby2) end

---@param moby moby_handle
---@param value number
function npc_set_speed(moby, value) end

---@param moby moby_handle
---@param moby2 moby_handle
function npc_set_target(moby, moby2) end

---@param moby moby_handle
function npc_start_attack(moby) end

---@param moby moby_handle
function npc_stop(moby) end

---@param moby moby_handle
function npc_stop_attack(moby) end

---@param moby moby_handle
---@param flag boolean
function npc_vorselon_set_visible(moby, flag) end

---@return number
function num_collected_ryno() end

---@return number
function num_gold_bolts_collected() end

---@return number
function num_prized_junk_collected() end

---@param target handle
---@return number
function num_trophies(target) end

---@return number
function number_times_map_activated() end

---No arguments.
function offset() end

---@param moby moby_handle
---@param callback function
---@param ... any
---@return event_handle
function on_activate(moby, callback, ...) end

---@param vo vo_handle
---@param callback function
---@return event_handle
function on_audio_end(vo, callback) end

---@param moby moby_handle
---@param callback function
---@return event_handle
function on_deactivate(moby, callback) end

---@param moby moby_handle
---@param callback function
---@return event_handle
function on_death(moby, callback) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param callback function
---@return event_handle
function on_disengage(moby, moby2, callback) end

---@param value number
---@param callback function
---@param ... any
---@return event_handle
function on_elapsed(value, callback, ...) end

---@param moby moby_handle
---@param volume volume_handle
---@param callback function
---@param ... any
---@return event_handle
function on_exit(moby, volume, callback, ...) end

---@param value number
---@param callback function
---@param ... any
---@return event_handle
function on_generic_event(value, callback, ...) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param callback function
---@return event_handle
function on_interact(moby, moby2, callback) end

---@param callback function
---@return event_handle
function on_next_cine_end(callback) end

---@param pod pod_handle
---@param value number
---@param callback function
---@param ... any
---@return event_handle
function on_num_alive(pod, value, callback, ...) end

---@param moby moby_handle
---@param callback function
---@return event_handle
function on_prompt_trigger(moby, callback) end

---@param value number
---@param value2 any
---@return event_handle
function on_time(value, value2) end

---@param moby moby_handle
function open_saw(moby) end

---@param moby moby_handle
function open_smasher(moby) end

---No arguments.
function patch_close_dialog() end

---@param text string
---@return number
function patch_get_f32(text) end

---@param text string
---@return number
function patch_get_f64(text) end

---@param text string
---@return number
function patch_get_index(text) end

---@param text string
---@return number
function patch_get_key_hash(text) end

---@param text string
---@param target handle
function patch_get_lua_vector(text, target) end

---@param text string
---@return number
function patch_get_u16(text) end

---@param text string
---@return number
function patch_get_u32(text) end

---@param text string
---@return number
function patch_get_u64(text) end

---@param text string
---@return number
function patch_get_u8(text) end

---@param text string
function patch_set_base_name(text) end

---@param text string
---@param value number
function patch_set_f32(text, value) end

---@param text string
---@param value any
function patch_set_f32_array(text, value) end

---@param text string
---@param value? number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@param value6? number
---@param value7? number
---@param value8? number
---@param value9? number
---@param ... any
function patch_set_f32v12(text, value, value2, value3, value4, value5, value6, value7, value8, value9, ...) end

---@param text string
---@param value? number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@param value6? number
---@param value7? number
---@param value8? number
---@param value9? number
---@param ... any
function patch_set_f32v16(text, value, value2, value3, value4, value5, value6, value7, value8, value9, ...) end

---@param text string
---@param value? number
---@param value2? number
---@param value3? number
---@param value4? number
function patch_set_f32v4(text, value, value2, value3, value4) end

---@param text string
---@param value number
function patch_set_f64(text, value) end

---@param text string
---@param position vector
function patch_set_lua_vector(text, position) end

---@param text string
function patch_set_screen_name(text) end

---@param text string
---@param value number
function patch_set_u16(text, value) end

---@param text string
---@param value number
function patch_set_u32(text, value) end

---@param text string
---@param value number
function patch_set_u64(text, value) end

---@param text string
---@param value number
function patch_set_u8(text, value) end

---@param moby moby_handle
function pause_lock_strafe(moby) end

---@param node pfx_node_handle
function pfx_begin_decay(node) end

---@param value? number
---@return pfx_node_handle
function pfx_create_node(value) end

---@param node pfx_node_handle
function pfx_delete_node(node) end

---@param node pfx_node_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
function pfx_set_node_bloom(node, value, value2, value3, value4, value5) end

---@param node pfx_node_handle
---@param value number
function pfx_set_node_blur(node, value) end

---@param node pfx_node_handle
---@param value number
function pfx_set_node_brightness(node, value) end

---@param node pfx_node_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
---@param value6 number
function pfx_set_node_fog(node, value, value2, value3, value4, value5, value6) end

---@param node pfx_node_handle
---@param value number
function pfx_set_node_intensity(node, value) end

---@param node pfx_node_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4? number
function pfx_set_node_lifetime(node, value, value2, value3, value4) end

---@param node pfx_node_handle
---@param value number
function pfx_set_node_saturation(node, value) end

---@param node pfx_node_handle
---@param value number
---@param value2 number
---@param value3 number
function pfx_set_node_tint(node, value, value2, value3) end

---@param moby moby_handle
function physics_off(moby) end

---@param moby moby_handle
function physics_on(moby) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function physics_push_moby(moby, value, value2, value3, value4) end

---@param pod pod_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function physics_push_pod(pod, value, value2, value3, value4) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
function physics_push_sphere(value, value2, value3, value4, value5) end

---@param area area_handle
---@return volume_handle
function pick_rand(area) end

---@param moby moby_handle
function planter_set_planted(moby) end

---@param moby moby_handle
---@param clue clue_handle
function platform_set_water(moby, clue) end

---No arguments.
function play() end

---@param moby moby_handle
---@param value number
---@param value2 number
function play_anim_by_index(moby, value, value2) end

---@param moby moby_handle
---@param value number
---@param value2 number
function play_anim_query(moby, value, value2) end

---@param value number
---@param moby moby_handle
---@param value2? number
---@return vo_handle
function play_explicit_dialog_spec(value, moby, value2) end

---@param value number
---@param moby moby_handle
---@return snd_handle
function play_explicit_sound_spec(value, moby) end

---@param value number
---@param moby moby_handle
function play_explicit_visual_spec(value, moby) end

---@param value number
---@param value2? number
---@param flag? boolean
---@param value3? number
---@param value4? number
---@param value5? number
---@return music_handle
function play_music(value, value2, flag, value3, value4, value5) end

---@param text string
---@param value number
function play_texture_movie(text, value) end

---No arguments.
function polar() end

---No arguments.
function pop() end

---@param value number
---@param value2? number
---@param flag? boolean
function popup_message(value, value2, flag) end

---@param value number
---@param flag boolean
function post_effect_enable(value, flag) end

---No arguments.
function procgen_init() end

---@param moby moby_handle
function prompt_activate(moby) end

---@param moby moby_handle
---@return boolean
function prompt_check(moby) end

---@param moby moby_handle
function prompt_clear(moby) end

---@param moby moby_handle
function prompt_deactivate(moby) end

---@param moby moby_handle
---@param text string
function prompt_set_debug_text(moby, text) end

---@param moby moby_handle
---@param value number
function prompt_set_distance(moby, value) end

---@param moby moby_handle
---@param value number
function prompt_set_tag(moby, value) end

---@param moby moby_handle
---@param volume volume_handle
function prompt_set_volume(moby, volume) end

---No arguments.
function push() end

---@param moby moby_handle
---@param volume volume_handle
function queue_ally_addautotargetvolume(moby, volume) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param flag boolean
function queue_ally_addtarget(moby, moby2, flag) end

---@param moby moby_handle
function queue_ally_clearautotargetvolume(moby) end

---@param moby moby_handle
function queue_ally_cleartargets(moby) end

---@param moby moby_handle
---@param path path_handle
function queue_ally_followpath(moby, path) end

---@param moby moby_handle
---@param path path_handle
function queue_ally_followpathhover(moby, path) end

---@param moby moby_handle
---@param volume volume_handle
function queue_ally_gotopoint(moby, volume) end

---@param moby moby_handle
---@param volume volume_handle
function queue_ally_gotopointhover(moby, volume) end

---@param moby moby_handle
function queue_ally_hover_land(moby) end

---@param moby moby_handle
function queue_ally_hover_takeoff(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
function queue_ally_removetarget(moby, moby2) end

---@param moby moby_handle
---@param value number
function queue_ally_set_attitude(moby, value) end

---@param moby moby_handle
---@param flag boolean
---@param flag2 boolean
function queue_ally_set_cancel_queue_conditions(moby, flag, flag2) end

---@param moby moby_handle
---@param moby2 moby_handle
function queue_ally_settarget(moby, moby2) end

---@param moby moby_handle
---@param pod pod_handle
---@param value number
---@param value2 number
---@param value3? number
function queue_ally_settargetpreference(moby, pod, value, value2, value3) end

---@param moby moby_handle
---@param value number
function queue_ally_timed_idle(moby, value) end

---@param moby moby_handle
---@param volume volume_handle
function queue_azimuth_throw_bomb(moby, volume) end

---@param volume volume_handle
---@param value number
---@param area area_handle
---@param area2 area_handle
---@param pod pod_handle
---@param value2 number
---@param value3? number
---@param value4? number
---@param ... any
---@return boolean
function queue_cannon_spawn(volume, value, area, area2, pod, value2, value3, value4, ...) end

---@param text string
---@param data table
---@return boolean
function queue_cinematic(text, data) end

---@param text string
---@param value number
---@param flag boolean
---@param value2 any
---@return boolean
function queue_cinematic_capture(text, value, flag, value2) end

---@param text string
---@param data table
---@return boolean
function queue_movie(text, data) end

---@param text string
---@param data? table
---@return boolean
function queue_scene(text, data) end

---@param callback function
---@param volume volume_handle
---@param area area_handle
---@param area2 area_handle
---@param area3 area_handle
---@return boolean
function queue_spinner_sprinter(callback, volume, area, area2, area3) end

---@param moby moby_handle
function qwark_activate_nurse_disguise(moby) end

---@param moby moby_handle
function qwark_cancel_smashers(moby) end

---@param moby moby_handle
function qwark_deactivate_nurse_disguise(moby) end

---@param moby moby_handle
---@param flag boolean
function qwark_door_enable_break(moby, flag) end

---@param moby moby_handle
function qwark_door_manual_break(moby) end

---@param moby moby_handle
function qwark_door_set_broken(moby) end

---@param moby moby_handle
---@return number
function qwark_get_deathblows_left(moby) end

---@param moby moby_handle
function qwark_push_smashers(moby) end

---@param moby moby_handle
---@param value number
function qwark_set_roam_radius(moby, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
function qwark_setup_deathblows(moby, value, value2, value3) end

---@param moby moby_handle
---@param moby2 moby_handle
function qwark_start_smashers(moby, moby2) end

---@param moby moby_handle
---@param volume volume_handle
function qwark_startled_goto(moby, volume) end

---@param moby moby_handle
---@param value? number
---@param value2? number
---@param value3? number
function radar_add_element(moby, value, value2, value3) end

---No arguments.
function radar_disable() end

---No arguments.
function radar_enable() end

---@param moby moby_handle
---@return boolean
function radar_is_element(moby) end

---@param moby moby_handle
---@param value? number
---@param value2? number
function radar_ping_element(moby, value, value2) end

---@param moby moby_handle
function radar_remove_element(moby) end

---@param flag boolean
function radar_set_draw_off_radar(flag) end

---@param value number
function radar_set_range(value) end

---@param moby moby_handle
function radar_stop_ping(moby) end

---@param value number
---@param value2? number
---@return number
function rand(value, value2) end

---@param value number
---@param value2? number
---@return number
function randint(value, value2) end

---@param controller controller_handle
---@param value number
function random_dialogue_add(controller, value) end

---@param controller controller_handle
function random_dialogue_clear(controller) end

---@param controller controller_handle
---@param value number
function random_dialogue_remove(controller, value) end

---@param moby moby_handle
---@param flag? boolean
function ratchet_activate_nefarious_disguise(moby, flag) end

---@param moby moby_handle
function ratchet_add_clankpack(moby) end

---@param moby moby_handle
function ratchet_arm_nefarious_disguise(moby) end

---@param moby moby_handle
---@param flag boolean
function ratchet_can_use_aphelion(moby, flag) end

---@param moby moby_handle
---@param flag? boolean
function ratchet_deactivate_nefarious_disguise(moby, flag) end

---@param moby moby_handle
---@param path path_handle
---@param volume volume_handle
---@param value? number
---@param value2? number
---@param value3 number
---@param value4? number
---@param value5 number
function ratchet_fling(moby, path, volume, value, value2, value3, value4, value5) end

---@param moby moby_handle
---@param flag boolean
function ratchet_force_stand(moby, flag) end

---@return boolean
function ratchet_nefarious_disguise_has_punished() end

---@param moby moby_handle
function ratchet_remove_clankpack(moby) end

---@param moby moby_handle
---@param pod pod_handle
function ratchet_set_lawrence_hologram_pod(moby, pod) end

---@param moby moby_handle
---@param flag boolean
function ratchet_wear_oxygen_mask(moby, flag) end

---@param controller controller_handle
---@param controller2 controller_handle
function register_fx_slave(controller, controller2) end

---@param moby moby_handle
function reinit(moby) end

---@param area area_handle
---@param volume volume_handle
function remove_from_area(area, volume) end

---@param pod pod_handle
---@param moby moby_handle
function remove_from_pod(pod, moby) end

---@param volume volume_handle
function remove_timeslow_volume(volume) end

---No arguments.
function reopen() end

---No arguments.
function replay_screen() end

---No arguments.
function request_auto_save() end

---No arguments.
function request_load() end

---@param text string
---@return boolean
function request_music_track(text) end

---No arguments.
function request_save() end

---@param text string
function reset_cinematic_played(text) end

---@param pod pod_handle
function reset_flares(pod) end

---@param moby moby_handle
---@return boolean
function reset_follow_cam(moby) end

---@param moby moby_handle
function reset_glide_pitch(moby) end

---@param text string
function reset_movie_played(text) end

---@param text string
function reset_scene_played(text) end

---No arguments.
function reset_weapon_types_used() end

---@param moby moby_handle
function restart_saw(moby) end

---@param moby moby_handle
function restart_smasher(moby) end

---@param moby moby_handle
function restore_destroyed_state(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
function restore_orvus_chamber(moby, moby2) end

---@param moby moby_handle
function resume_lock_strafe(moby) end

---No arguments.
function resume_weather() end

---@param pod pod_handle
---@param flag? boolean
function reveal(pod, flag) end

---@param moby moby_handle
function revert_actor(moby) end

---@param target handle
---@return number
function reward_cam(target) end

---@param target handle
---@return number
function reward_vol(target) end

---No arguments.
function roll_credits() end

---No arguments.
function rotate() end

---No arguments.
function round() end

---No arguments.
function scale() end

---No arguments.
function scene_clear_title() end

---@param text string
function scene_debug_subtitle(text) end

---@return number
function scene_get_menu_choice_num() end

---No arguments.
function scene_hide_subtitle() end

---@return boolean
function scene_is_menu_waiting() end

---@param value number
---@param volume volume_handle
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5 number
---@param value6 number
function scene_set_light(value, volume, value2, value3, value4, value5, value6) end

---@param value number
function scene_set_title(value) end

---@param flag? boolean
function scene_show_menu_internal(flag) end

---@param value number
---@param vo vo_handle
function scene_show_subtitle(value, vo) end

---@param value number
---@param value2? number
---@param value3? number
function scripted_qs(value, value2, value3) end

---@param moby moby_handle
---@param flag boolean
function security_module_bypass(moby, flag) end

---No arguments.
function send() end

---@param moby moby_handle
---@param value number
---@param value2 number
function set_anim_modifier(moby, value, value2) end

---@param moby moby_handle
---@param text string
function set_arcade_game(moby, text) end

---@param flag boolean
function set_cam_invert_x(flag) end

---@param flag boolean
function set_cam_invert_y(flag) end

---@param moby moby_handle
---@param volume volume_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function set_camera_turn_limits(moby, volume, value, value2, value3, value4) end

---No arguments.
function set_challenge_failure() end

---@param value? number
---@return boolean
function set_challenge_success(value) end

---@param value number
function set_cheat_points(value) end

---@param value number
---@param value2? number
function set_cheat_state(value, value2) end

---@param flag boolean
function set_cinematic_update_mode(flag) end

---@param value number
---@param value2? number
function set_custom_pistol_color(value, value2) end

---@param moby moby_handle
---@param flag boolean
function set_damageable(moby, flag) end

---@param moby moby_handle
---@param value number
function set_dialogue_threshold(moby, value) end

---@param controller controller_handle
---@param value number
function set_effect_time_scale(controller, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3? number
---@param value4? number
---@return boolean
function set_follow_cam(moby, value, value2, value3, value4) end

---@param moby moby_handle
---@param path path_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function set_follow_cam_indexed(moby, path, value, value2, value3, value4) end

---@param moby moby_handle
---@param value number
function set_follow_cam_look_at_height(moby, value) end

---@param moby moby_handle
---@param flag boolean
function set_force_thrust(moby, flag) end

---@param moby moby_handle
---@param value number
function set_glide_pitch(moby, value) end

---@param value number
function set_global_zoni_collected(value) end

---@param flag boolean
function set_halfpipe(flag) end

---@param value number
function set_hero_flag(value) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
function set_hoverboot_cam_offset(value, value2, value3, value4, value5) end

---No arguments.
function set_hoverboot_cam_offset_defaults() end

---@param value number
function set_hoverboot_max(value) end

---@param value number
function set_hoverboot_max_thrust(value) end

---@param moby moby_handle
---@param flag boolean
function set_hoverboots(moby, flag) end

---@param value number
---@param value2? number
function set_item_banner_amount(value, value2) end

---@param value number
---@param flag? boolean
---@param value2? number
function set_item_bar_amount(value, flag, value2) end

---@param value number
function set_item_bar_total(value) end

---@param moby moby_handle
---@param volume volume_handle
---@param value number
---@param value2 number
function set_jump_duncan_hack(moby, volume, value, value2) end

---@param value number
---@param flag? boolean
---@param flag2? boolean
function set_level(value, flag, flag2) end

---@param flag boolean
function set_lockstrafe_invert_x(flag) end

---@param flag boolean
function set_lockstrafe_invert_y(flag) end

---@param moby moby_handle
---@param value number
function set_monitor_shader(moby, value) end

---@param moby moby_handle
---@param flag boolean
function set_moving_surface_cam(moby, flag) end

---@param value number
function set_npc_talked_to(value) end

---@param moby moby_handle
---@param flag boolean
function set_override_gravity_input(moby, flag) end

---@param path path_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function set_path_point(path, value, value2, value3, value4) end

---@param moby moby_handle
---@param volume volume_handle
function set_pf_position(moby, volume) end

---@param moby moby_handle
---@param flag boolean
function set_pf_reverse(moby, flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
function set_pf_speed(moby, value, value2) end

---@param moby moby_handle
---@param volume volume_handle
function set_pf_stop(moby, volume) end

---@param path path_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function set_railpoint(path, value, value2, value3, value4) end

---@param value number
function set_save_level(value) end

---@param value number
function set_skillpoint_done(value) end

---@param value number
function set_sky(value) end

---@param value number
function set_space_level_miss_complete(value) end

---@param value number
function set_story_tag(value) end

---@param moby moby_handle
---@param value number
function set_switch_value(moby, value) end

---@param value number
function set_time_scale(value) end

---@param value number
function set_update_delay(value) end

---No arguments.
function setpeer() end

---@param moby moby_handle
---@param volume volume_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
function setup_security_module(moby, volume, value, value2, value3, value4, value5) end

---No arguments.
function shift() end

---No arguments.
function show() end

---No arguments.
function show_arena_menu() end

---@param moby moby_handle
function show_armor_vendor(moby) end

---No arguments.
function show_control_config() end

---@param moby moby_handle
function show_device_vendor(moby) end

---No arguments.
function show_difficulty_select() end

---No arguments.
function show_frontend() end

---@param value number
---@param value2? number
function show_info_screen(value, value2) end

---No arguments.
function show_menu() end

---@param moby moby_handle
function show_upgrade_screen(moby) end

---@param moby moby_handle
function show_weapon_vendor(moby) end

---@param moby moby_handle
---@param volume volume_handle
function sigmund_harrass(moby, volume) end

---@param moby moby_handle
function sigmund_holo_intro(moby) end

---@param moby moby_handle
function sigmund_holo_outro(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
function sigmund_set_clone_pad(moby, moby2) end

---@param moby moby_handle
---@param flag boolean
function sigmund_set_grounded(moby, flag) end

---@param moby moby_handle
function sigmund_stop_keypad(moby) end

---@param moby moby_handle
function sigmund_use_keypad(moby) end

---@param moby moby_handle
---@param position vector
function simplemotion_set_dest(moby, position) end

---@param moby moby_handle
---@param position vector
function simplemotion_set_offset(moby, position) end

---@param moby moby_handle
---@param value number
function simplemotion_set_speed(moby, value) end

---@param volume volume_handle
---@param volume2 volume_handle
---@param value number
---@param flag? boolean
---@param flag2? boolean
function skydive_init(volume, volume2, value, flag, flag2) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
---@param value6 number
---@param value7 number
---@param flag boolean
---@return moby_handle
function skydive_spawn_missile(moby, value, value2, value3, value4, value5, value6, value7, flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
---@param value6 number
---@param value7 number
---@param value8 number
---@param value9 number
---@return moby_handle
function skydive_spawn_radial_mine(moby, value, value2, value3, value4, value5, value6, value7, value8, value9) end

---No arguments.
function slice() end

---@param moby moby_handle
function smoky_reveal(moby) end

---@param value number
---@param value2? number
---@param value3? number
function sound_group_fade_internal(value, value2, value3) end

---No arguments.
function space2_graveyard_clear() end

---@param moby moby_handle
function space_cinematic_death(moby) end

---@param value number
---@return boolean
function space_did_action(value) end

---No arguments.
function space_disable_pip() end

---No arguments.
function space_drop_tether() end

---@param clue clue_handle
---@param flag boolean
function space_enable_barrier(clue, flag) end

---@param flag boolean
function space_enable_channel_change(flag) end

---@param clue clue_handle
---@param flag boolean
---@param flag2 boolean
function space_enable_moon(clue, flag, flag2) end

---@param clue clue_handle
---@param flag boolean
---@param flag2? boolean
function space_enable_planet(clue, flag, flag2) end

---@param flag boolean
function space_enable_takeoff(flag) end

---@return number
function space_get_radio_channel() end

---@param value number
---@return number
function space_get_zoni_for_upgrade(value) end

---@param value number
function space_give_weapon(value) end

---@param moby moby_handle
---@param moby2 moby_handle
function space_grab_junk(moby, moby2) end

---@return boolean
function space_has_comet_core() end

---@param value number
---@return boolean
function space_has_weapon(value) end

---@param moby moby_handle
---@return boolean
function space_is_attacking(moby) end

---@param moby moby_handle
---@return boolean
function space_is_aware(moby) end

---@return boolean
function space_is_boosting() end

---@return boolean
function space_is_in_ship() end

---@param moby moby_handle
---@return boolean
function space_is_towing(moby) end

---@param moby moby_handle
---@param clue clue_handle
function space_land_ship(moby, clue) end

---@param flag boolean
function space_lock_ship_counter(flag) end

---@param moby moby_handle
---@param flag boolean
function space_make_attacker(moby, flag) end

---@param moby moby_handle
---@param moby2 moby_handle
function space_match_aphelions(moby, moby2) end

---@param moby moby_handle
---@param text string
function space_npc_set_scene(moby, text) end

---@return number
function space_num_ships_destroyed() end

---No arguments.
function space_reset_destroyed_ships() end

---@param value number
function space_set_afterburner_level(value) end

---@param value number
---@param value2? number
---@param value3? number
function space_set_cam(value, value2, value3) end

---@param moby moby_handle
---@param flag boolean
function space_set_indestructible(moby, flag) end

---@param clue clue_handle
---@param value number
function space_set_moon_tag(clue, value) end

---@param clue clue_handle
function space_set_objective(clue) end

---@param clue clue_handle
---@param value number
function space_set_planet_tag(clue, value) end

---@param value number
function space_set_radio_channel(value) end

---@param value number
function space_set_shield_level(value) end

---@param value number
function space_set_shield_type(value) end

---No arguments.
function space_show_radio() end

---@param value number
function space_unlock_level(value) end

---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
---@return moby_handle
function spawn_agorian_warrior(value, value2, value3, value4, value5) end

---@param value number
---@param volume volume_handle
function spawn_ammo(value, volume) end

---@param value number
---@param value2 number
---@param value3 number
---@return moby_handle
function spawn_ammo_crate(value, value2, value3) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@param value6? number
---@param value7? number
---@param value8? number
---@param value9? number
---@param value10? number
---@return moby_handle
function spawn_blink(value, value2, value3, value4, value5, value6, value7, value8, value9, value10) end

---@param value number
---@param value2 any
---@param value3 number
function spawn_bolts(value, value2, value3) end

---@param value number
---@param value2 number
---@param value3 number
---@return moby_handle
function spawn_breakable_crate(value, value2, value3) end

---@param controller controller_handle
function spawn_car(controller) end

---@param volume volume_handle
---@return moby_handle
function spawn_firechucker(volume) end

---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
---@return moby_handle
function spawn_firespin(value, value2, value3, value4, value5) end

---@param value number
---@param value2? number
---@param value3? number
---@param flag boolean
---@param value4? number
---@param value5? number
---@return moby_handle
function spawn_flame(value, value2, value3, flag, value4, value5) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@param value6? number
---@param value7? number
---@param value8? number
---@param value9? number
---@param value10? number
---@return moby_handle
function spawn_floor(value, value2, value3, value4, value5, value6, value7, value8, value9, value10) end

---@param path path_handle
---@return moby_handle
function spawn_grindrail(path) end

---@param flag boolean
---@param volume volume_handle
function spawn_health(flag, volume) end

---@param value number
---@param value2 number
---@param value3 number
---@return moby_handle
function spawn_health_crate(value, value2, value3) end

---@param value number
---@param value2? number
---@param value3? number
---@return moby_handle
function spawn_hook_shot(value, value2, value3) end

---@param volume volume_handle
---@param pod pod_handle
---@param flag boolean
---@param area area_handle
---@param area2 area_handle
---@param area3 area_handle
---@param flag2? boolean
---@return moby_handle
function spawn_hydra_tank(volume, pod, flag, area, area2, area3, flag2) end

---@param volume volume_handle
function spawn_lattice_hazard(volume) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@return moby_handle
function spawn_moby(value, value2, value3, value4, value5) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@param value6? number
---@param value7? number
---@param value8? number
---@param value9? number
---@param value10? number
---@return moby_handle
function spawn_moving(value, value2, value3, value4, value5, value6, value7, value8, value9, value10) end

---@param volume volume_handle
---@return moby_handle
function spawn_pickup_bomb(volume) end

---@param value number
---@param volume volume_handle
function spawn_powerup(value, volume) end

---@param volume volume_handle
---@return moby_handle
function spawn_sawchucker(volume) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@param value6? number
---@param value7? number
---@return moby_handle
function spawn_simple_flame(value, value2, value3, value4, value5, value6, value7) end

---@param value number
---@param value2? number
---@param value3? number
---@return moby_handle
function spawn_swing_shot(value, value2, value3) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@return pod_handle
function spawn_tilefloor(value, value2, value3, value4, value5) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@param value6? number
---@return pod_handle
function spawn_tilewall(value, value2, value3, value4, value5, value6) end

---@param volume volume_handle
---@param pod pod_handle
---@param area area_handle
---@param area2 area_handle
---@param area3 area_handle
---@param flag? boolean
---@param value? number
---@return moby_handle
function spawn_toreshi_war_grok(volume, pod, area, area2, area3, flag, value) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@param value6? number
---@param value7? number
---@param value8? number
---@param value9? number
---@param value10? number
---@return moby_handle
function spawn_wall(value, value2, value3, value4, value5, value6, value7, value8, value9, value10) end

---@param value number
---@param volume volume_handle
function spawn_weapon(value, volume) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
---@param value5? number
---@param value6? number
---@param value7? number
---@param value8? number
---@param value9? number
---@param value10? number
---@return moby_handle
function spawn_wlat(value, value2, value3, value4, value5, value6, value7, value8, value9, value10) end

---@param path path_handle
---@return moby_handle
function spawn_zipline(path) end

---@param moby moby_handle
function spawner_clear(moby) end

---@param moby moby_handle
---@param value number
---@return moby_handle
function spawner_spawn(moby, value) end

---No arguments.
function splice() end

---No arguments.
function split() end

---No arguments.
function start() end

---No arguments.
function start_burrower_kill_watch() end

---@param value number
function start_challenge(value) end

---@param value number
---@param value2? number
---@param value3? number
function start_item_bar(value, value2, value3) end

---No arguments.
function stop() end

---No arguments.
function stop_all_vo() end

---No arguments.
function stop_item_bar() end

---@param music music_handle
function stop_music(music) end

---@param sound snd_handle
---@param value? number
function stop_sound(sound, value) end

---@param value number
function stop_texture_movie(value) end

---@param value number
---@param moby moby_handle
function stop_tracked_effect_event(value, moby) end

---@param vo vo_handle
function stop_vo(vo) end

---@param text string
---@param value? number
---@param value2? number
---@return number
function store_table(text, value, value2) end

---No arguments.
function substr() end

---No arguments.
function substring() end

---No arguments.
function subtract() end

---@param target handle
---@return boolean
function success(target) end

---@param value number
function suspend_weather(value) end

---No arguments.
function swarm_cam_reset() end

---@param moby moby_handle
---@param value number
function swarm_force_retreat(moby, value) end

---@param moby moby_handle
---@return boolean
function swarm_is_hiding(moby) end

---@param moby moby_handle
---@return boolean
function swarm_is_retreating(moby) end

---@param moby moby_handle
---@return boolean
function swarm_is_targeting_ambrosia(moby) end

---@param moby moby_handle
---@return boolean
function swarm_is_targeting_ratchet(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param value number
---@param value2 number
function swarm_set_override_target(moby, moby2, value, value2) end

---@param value number
---@param value2? number
---@param value3? number
---@param value4? number
function swarm_setup_chase_cam(value, value2, value3, value4) end

---@param moby moby_handle
---@return boolean
function swarm_targeted_ambrosia(moby) end

---@param moby moby_handle
---@return boolean
function swarm_took_non_ambrosia_damage(moby) end

---@param moby moby_handle
function switch_from_clank(moby) end

---@param moby moby_handle
---@param flag? boolean
function switch_to_clank(moby, flag) end

---No arguments.
function syn_tick_timer_thread() end

---No arguments.
function synth_mp3_decode_thread() end

---@param moby moby_handle
---@param flag boolean
function target_set_targetable(moby, flag) end

---@param moby moby_handle
---@param volume volume_handle
function taxi_warp(moby, volume) end

---@param value number
function tease_weapon(value) end

---@param moby moby_handle
function teleport(moby) end

---@param value? number
---@param value2? number
function testupg(value, value2) end

---@param moby moby_handle
function time_portal_clear_target_volume(moby) end

---@param moby moby_handle
---@param value number
function time_portal_set_id(moby, value) end

---@param moby moby_handle
---@param volume volume_handle
function time_portal_set_target_volume(moby, volume) end

---@return number
function time_since_map_activated() end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4? number
function tint(moby, value, value2, value3, value4) end

---No arguments.
function trace() end

---No arguments.
function translate() end

---@param moby moby_handle
function trigger(moby) end

---@param moby moby_handle
function trigger_auto_spring(moby) end

---@param value number
---@param moby moby_handle
---@param value2? number
---@param value3? number
function trigger_effect_event(value, moby, value2, value3) end

---@param moby moby_handle
function trigger_if_alive(moby) end

---@param value number
---@param moby moby_handle
---@param value2? number
---@param value3? number
function trigger_tracked_effect_event(value, moby, value2, value3) end

---@param moby moby_handle
---@return boolean
function tripad_is_active(moby) end

---@param moby moby_handle
function tripad_set_complete(moby) end

---@param moby moby_handle
function trooper_teleport(moby) end

---@param value number
function trophy_status(value) end

---@param moby moby_handle
---@return moby_handle
function turret_get_user(moby) end

---@param moby moby_handle
function turret_lock(moby) end

---@param moby moby_handle
function turret_unlock(moby) end

---No arguments.
function unescape() end

---No arguments.
function union() end

---No arguments.
function unlock_all_skillpoints() end

---@param value number
function unlock_level(value) end

---@param value number
function unlock_trophy(value) end

---@param value number
function unlock_weapon(value) end

---@param target handle
---@return boolean
function unlocked(target) end

---No arguments.
function unshift() end

---@param moby moby_handle
function untrigger(moby) end

---No arguments.
function unwatch() end

---@param moby moby_handle
function update_off(moby) end

---@param moby moby_handle
function update_on(moby) end

---@param moby moby_handle
---@param value number
function vap_set_gas_speed(moby, value) end

---@param value number
function vorselon_hazard_hack(value) end

---@param moby moby_handle
---@param value number
---@param flag boolean
function vx99_arm_ambient_attack(moby, value, flag) end

---@param moby moby_handle
---@param path path_handle
---@param path2 path_handle
---@param value? number
---@param value2? number
function vx99_attack_path(moby, path, path2, value, value2) end

---@param moby moby_handle
---@param path path_handle
---@param value? number
function vx99_follow_path(moby, path, value) end

---@param moby moby_handle
---@param value number
---@return number
function vx99_get_bolt_status(moby, value) end

---@param moby moby_handle
---@param volume volume_handle
---@param value? number
function vx99_go_to_volume(moby, volume, value) end

---@param moby moby_handle
function vx99_lower_turret(moby) end

---@param moby moby_handle
function vx99_open_flaps(moby) end

---@param moby moby_handle
function vx99_pull_dome(moby) end

---@param moby moby_handle
---@param flag boolean
function vx99_set_bob_active(moby, flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
function vx99_set_bolt_status(moby, value, value2) end

---@param moby moby_handle
---@param path path_handle
---@param volume volume_handle
function vx99_set_fling_data(moby, path, volume) end

---@param moby moby_handle
function vx99_start_top_attack(moby) end

---@param moby moby_handle
function vx99_start_turret_attack(moby) end

---@param moby moby_handle
function vx99_stop_moving(moby) end

---@param moby moby_handle
---@param volume volume_handle
---@param flag? boolean
---@param flag2? boolean
---@param flag3? boolean
function warp(moby, volume, flag, flag2, flag3) end

---@param moby moby_handle
---@param value number
---@param ... any
---@return boolean
function was_damaged(moby, value, ...) end

---@return boolean
function was_time_bomb_canceled() end

---No arguments.
function watch() end

---No arguments.
function zolar_breakables_clear() end

---@param moby moby_handle
function zoni_clear_stop(moby) end

---@param moby moby_handle
---@param path path_handle
---@param value? number
---@param flag? boolean
function zoni_path_follow(moby, path, value, flag) end

---@param moby moby_handle
function zoni_resurrect(moby) end

---@param moby moby_handle
---@param flag boolean
function zoni_set_forced_idle(moby, flag) end

---@param moby moby_handle
---@param volume volume_handle
function zoni_set_stop(moby, volume) end

---@param moby moby_handle
function zoni_vessel_restore(moby) end
