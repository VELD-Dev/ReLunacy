---@meta
--- Tools of Destruction API.
---
--- Entries here are written from what the shipped scripts demonstrate, never from guesswork: a
--- placeholder with invented types produces confidently wrong completions, which is worse than
--- none. Each one records the evidence it rests on, so a later reader can tell a measured fact
--- from an assumption. The corpus referred to is the 524 .lua/.lc pairs in ToD's global_cached.

---Loads another of this level's scripts, by name.
---
---`load_level_script("music")` runs `/levels/<level>/scripts/music.lua`.
---@param name string The script's file name, without folder or extension.
function load_level_script(name) end

---Loads one of the game-wide scripts, by name.
---
---The same shape as `load_level_script`, against `/scripts/` instead of the level's own folder:
---`load_global_script("globalhelp")` runs `/scripts/globalhelp.lua`. These are the scripts shared
---by every level rather than one level's own.
---@param name string The script's file name, without folder or extension.
function load_global_script(name) end

---Yields the script (`coroutine.yield`) for `duration`, or until the next frame without one.
---@param duration? number The duration in seconds.
function wait(duration) end

-------------------------------------------------------------------------------------------------
-- Prelude
-------------------------------------------------------------------------------------------------
-- Lua the game runs before any script. Types follow the natives each function calls.

---Writes text to the screen: `prt = screen_out`.
---@param text string
function prt(text) end

---Clears the screen text: `cls = clear_screen`.
function cls() end

---Whether a voice line is still playing.
---@param vo vo_handle
---@return boolean
function is_vo_playing(vo) end

---Waits until a voice line ends. Returns at once for nil.
---@param vohandle? vo_handle
function wait_for_vo(vohandle) end

---Plays a dialogue line on a Moby, if it is alive.
---@param moby moby_handle
---@param spec number Dialogue spec.
---@param force2d? boolean
---@return vo_handle|nil
function speak(moby, spec, force2d) end

---Plays a dialogue line on a Moby and waits for it to end.
---@param moby moby_handle
---@param spec number Dialogue spec.
---@param force2d? boolean
function speak_and_wait(moby, spec, force2d) end

---Plays a dialogue line with no speaker.
---@param spec number Dialogue spec.
---@return vo_handle
function announce(spec) end

---Plays a dialogue line with no speaker and waits for it to end.
---@param spec number Dialogue spec.
function announce_and_wait(spec) end

---Runs one of this level's scripts, then waits a frame.
---@param name string
function run_level_script(name) end

---Runs one of the game-wide scripts, then waits a frame.
---@param name string
function run_global_script(name) end

---Blends the follow camera to new settings while the Moby is in an area; resets it on leaving. Call every frame.
---@param client moby_handle
---@param cam any Key of this adjustment; any value unique to it.
---@param area area_handle
---@param dist number
---@param pitch number
---@param fov? number 0 when omitted.
---@param height? number -1000 when omitted.
---@param time? number Blend time in seconds; 0 when omitted.
function update_camera_adjust_area(client, cam, area, dist, pitch, fov, height, time) end

---Blends the follow camera to new settings while the Moby is in a volume; resets it on leaving. Call every frame.
---@param client moby_handle
---@param cam any Key of this adjustment; any value unique to it.
---@param vol volume_handle
---@param dist number
---@param pitch number
---@param fov? number 0 when omitted.
---@param height? number -1000 when omitted.
---@param time? number Blend time in seconds; 0 when omitted.
function update_camera_adjust_vol(client, cam, vol, dist, pitch, fov, height, time) end

---One of its arguments, picked at random.
---@param ... any
---@return any
function rand_select(...) end

---The Mobys of a pod, as a list.
---@param pod pod_handle
---@return moby_handle[]
function get_moby_list(pod) end

---The volumes of an area, as a list.
---@param area area_handle
---@return volume_handle[]
function get_volume_list(area) end

---Queues a movie, then waits a frame.
---@param name string
---@return boolean
function play_movie(name) end

---Queues a cinematic, then waits a frame.
---@param name string
---@param obviate_pod? pod_handle Hidden during the cinematic.
---@param unobviate_pod? pod_handle Shown during the cinematic.
---@param warp_volume? volume_handle
---@return boolean
function play_cinematic(name, obviate_pod, unobviate_pod, warp_volume) end

---Makes an NPC speak and waits until it stops.
---@param npc moby_handle
---@param text string
---@param vo number
---@param spectime? number
---@param specanim? number
function npc_speak_and_wait(npc, text, vo, spectime, specanim) end

---Runs a conversation with an NPC: fades into the scene camera, shows the responses in `datatable`
---until a response's `func` returns false, then fades back.
---@param npc moby_handle
---@param scene_hero moby_handle
---@param heropos volume_handle
---@param scenecam controller_handle
---@param datatable table Responses; each may have a `func(response)` returning true to continue or a new table.
---@param initialdlg? number
function npc_conversation_loop(npc, scene_hero, heropos, scenecam, datatable, initialdlg) end

---Shows the arena challenge screen and returns the chosen challenge.
---@param hero moby_handle
---@param data table Challenges, with `arenaname`.
---@return number
function select_arena_challenge(hero, data) end

---`awareness_add_targettype` on every Moby of a pod.
---@param pod pod_handle
---@param target_type number
function awareness_add_targettype_pod(pod, target_type) end

---`awareness_remove_targettype` on every Moby of a pod.
---@param pod pod_handle
---@param target_type number
function awareness_remove_targettype_pod(pod, target_type) end

---`awareness_clear_targettypes` on every Moby of a pod.
---@param pod pod_handle
function awareness_clear_targettypes_pod(pod) end

---`health_set_invulnerability` on every Moby of a pod.
---@param pod pod_handle
---@param is_invulnerable boolean
function health_set_invulnerability_pod(pod, is_invulnerable) end




-------------------------------------------------------------------------------------------------
-- Natives
-------------------------------------------------------------------------------------------------
-- Signatures read from the game's Lua bindings. Parameter names follow their types.

---@class BotConfig
BotConfig = {}

---@class area_handle
area_handle = {}

---@class clue_handle
clue_handle = {}

---@class controller_handle
controller_handle = {}

---@class moby_handle
moby_handle = {}

---@class path_handle
path_handle = {}

---@class pod_handle
pod_handle = {}

---@class position
position = {}

---@class scent_handle
scent_handle = {}

---@class vo_handle
vo_handle = {}

---@class volume_handle
volume_handle = {}

---@return BotConfig
function BotConfig:new() end

---@return volume_handle
function area_handle:get_first() end

---@return volume_handle
function area_handle:get_next() end

---@return boolean
function area_handle:is_valid() end

---@param value number
---@return area_handle
function area_handle:new(value) end

---@return boolean
function clue_handle:is_valid() end

---@param value number
---@return clue_handle
function clue_handle:new(value) end

---@return boolean
function controller_handle:is_valid() end

---@param value number
---@return controller_handle
function controller_handle:new(value) end

---@return number
function moby_handle:get_handle() end

---@return boolean
function moby_handle:is_valid() end

---@param value number
---@return moby_handle
function moby_handle:new(value) end

---@return boolean
function path_handle:is_valid() end

---@param value number
---@return path_handle
function path_handle:new(value) end

---@return moby_handle
function pod_handle:get_first() end

---@return moby_handle
function pod_handle:get_next() end

---@return boolean
function pod_handle:is_valid() end

---@param value number
---@return pod_handle
function pod_handle:new(value) end

---@param value? number
---@param value2? number
---@param value3? number
---@return position
---@overload fun(): position
---@overload fun(value: number, value2: number, value3: number): position
function position:new(value, value2, value3) end

---@return boolean
function scent_handle:is_valid() end

---@param value number
---@return scent_handle
function scent_handle:new(value) end

---@param value number
---@return vo_handle
function vo_handle:new(value) end

---@return boolean
function volume_handle:is_valid() end

---@param value number
---@return volume_handle
function volume_handle:new(value) end

---No arguments.
function abort_challenge() end

---@param target pod_handle|controller_handle|moby_handle
---@overload fun(pod: pod_handle)
---@overload fun(controller: controller_handle)
---@overload fun(moby: moby_handle)
---@overload fun(moby: moby_handle)
function activate(target) end

---@param moby moby_handle
---@param controller controller_handle
---@param flag? boolean
function activate_cam(moby, controller, flag) end

---@param moby moby_handle
---@param controller controller_handle
function activate_skydive_cam(moby, controller) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function add_dance_move(moby, value, value2, value3, value4) end

---@param value number
---@param value2 number
---@param value3 number
---@param flag boolean
function add_mission(value, value2, value3, flag) end

---@param pod pod_handle
---@param moby moby_handle
function add_to_pod(pod, moby) end

---@param moby moby_handle
---@param target moby_handle|pod_handle
---@overload fun(moby: moby_handle, moby2: moby_handle)
---@overload fun(moby: moby_handle, pod: pod_handle)
function ally_addtarget(moby, target) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param flag boolean
function ally_bombingrun(moby, moby2, flag) end

---@param moby moby_handle
function ally_clearfirepoints(moby) end

---@param moby moby_handle
function ally_cleartargets(moby) end

---@param moby moby_handle
function ally_debug(moby) end

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
function ally_followpathhover(moby, path, value, value2, value3) end

---@param moby moby_handle
---@return number
function ally_getcurrentstate(moby) end

---@param moby moby_handle
---@param volume volume_handle
---@param flag boolean
---@param flag2 boolean
function ally_gotopoint(moby, volume, flag, flag2) end

---@param moby moby_handle
---@param flag boolean
function ally_hackconsole(moby, flag) end

---@param moby moby_handle
---@param moby2 moby_handle
function ally_removetarget(moby, moby2) end

---@param moby moby_handle
---@param value number
function ally_setaccuracy(moby, value) end

---@param moby moby_handle
---@param value number
function ally_setattitude(moby, value) end

---@param moby moby_handle
---@param area area_handle
function ally_setawarenessarea(moby, area) end

---@param moby moby_handle
---@param volume volume_handle
function ally_setawarenessvolume(moby, volume) end

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
---@param value any
---@param value2 any
---@param value3 number
---@param area? area_handle
---@overload fun(moby: moby_handle, value: any, value2: any, value3: number)
---@overload fun(moby: moby_handle, value: any, value2: any, value3: number, area: area_handle)
function ally_skydive(moby, value, value2, value3, area) end

---@param moby moby_handle
---@param flag boolean
function ally_specialidle(moby, flag) end

---@param moby moby_handle
---@param volume volume_handle
function ally_warp(moby, volume) end

---@param moby moby_handle
---@return boolean
function apogee_escape_pod_is_player_ready(moby) end

---@param moby moby_handle
function apogee_escape_pod_signal_to_move(moby) end

---No arguments.
function arena_clear_edge() end

---@param moby moby_handle
---@return number
function arena_count(moby) end

---@param moby moby_handle
function arena_delete(moby) end

---@param value number
---@return vo_handle
function arena_dialog_track(value) end

---@param moby moby_handle
---@param volume volume_handle
---@param value number
---@param value2 number
function arena_hero_warp(moby, volume, value, value2) end

---@param value number
function arena_set_advanced(value) end

---@param path path_handle
function arena_set_edge(path) end

---@param value number
---@param value2 number
---@return moby_handle
function arena_spawn(value, value2) end

---@param moby moby_handle
---@param value number
function awareness_add_targettype(moby, value) end

---@param moby moby_handle
function awareness_clear_targettypes(moby) end

---@param moby moby_handle
---@param value number
function awareness_remove_targettype(moby, value) end

---@param moby moby_handle
---@param flag boolean
function bangle_alloff(moby, flag) end

---@param moby moby_handle
---@param flag boolean
function bangle_allon(moby, flag) end

---@param moby moby_handle
---@param value number
---@param flag boolean
function bangle_off(moby, value, flag) end

---@param moby moby_handle
---@param value number
---@param flag boolean
function bangle_on(moby, value, flag) end

---@param moby moby_handle
function baseturret_close(moby) end

---@param moby moby_handle
function baseturret_open(moby) end

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

---@param moby moby_handle
---@param moby2 moby_handle
function boss_arena_double_challenge(moby, moby2) end

---@param moby moby_handle
---@param value number
function boss_enter_arena(moby, value) end

---@param moby moby_handle
function boss_exit_arena(moby) end

---@param moby moby_handle
---@return boolean
function boss_finished_path(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
---@param value number
function boss_register_cam(moby, moby2, value) end

---@param moby moby_handle
---@param volume volume_handle
function boss_set_direction(moby, volume) end

---@param moby moby_handle
---@param value any
---@param value2 any
function boss_set_jumpin_jumpout(moby, value, value2) end

---@param moby moby_handle
---@param path path_handle
---@overload fun(moby: moby_handle, path: path_handle)
---@overload fun(moby: moby_handle, path: path_handle)
function boss_set_path(moby, path) end

---@param moby moby_handle
---@param value number
---@param value2 number
function boss_set_speed(moby, value, value2) end

---@param moby moby_handle
---@param moby2 moby_handle
function boss_unregister_cam(moby, moby2) end

---@param moby moby_handle
---@param flag? boolean
function bridge_extend(moby, flag) end

---@param moby moby_handle
---@return boolean
function bridge_is_extended(moby) end

---@param moby moby_handle
---@param flag? boolean
function bridge_retract(moby, flag) end

---@param moby moby_handle
---@return boolean
function button_is_on(moby) end

---@param moby moby_handle
---@param flag boolean
---@param flag2? boolean
---@return boolean
function button_switch(moby, flag, flag2) end

---@param value number
function call_pause_menu(value) end

---@param target position|moby_handle|volume_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4? number
---@param value5? number
---@overload fun(position: position, value: number, value2: number, value3: number, value4?: number, value5?: number)
---@overload fun(moby: moby_handle, value: number, value2: number, value3: number, value4?: number, value5?: number)
---@overload fun(volume: volume_handle, value: number, value2: number, value3: number, value4?: number, value5?: number)
function camera_shake(target, value, value2, value3, value4, value5) end

---@param moby moby_handle
---@return boolean
function captainslag_is_watching_battle(moby) end

---@return boolean
function captainslag_should_play_help() end

---@param moby moby_handle
function captainslag_wakeup(moby) end

---@param controller controller_handle
function car_spawner_halt_cars(controller) end

---@param controller controller_handle
---@return boolean
function car_spawner_is_midpoint_stopped(controller) end

---@param controller controller_handle
function car_spawner_obviate(controller) end

---@param controller controller_handle
function car_spawner_obviate_midpoint(controller) end

---@param controller controller_handle
function car_spawner_restart_cars(controller) end

---@param controller controller_handle
function car_spawner_unobviate(controller) end

---@param moby moby_handle
---@param value number
function cbot_set_mode(moby, value) end

---@param moby moby_handle
---@param path path_handle
function cbot_set_path(moby, path) end

---@param moby moby_handle
---@param value number
function cbot_set_patrol_mode(moby, value) end

---@param moby moby_handle
---@param area area_handle
function cbot_set_stops(moby, area) end

---@param value number
---@return boolean
---@overload fun(value: number): boolean
---@overload fun(value: number): boolean
function check_flag(value) end

---@param value? number
function checkpoint(value) end

---@param volume volume_handle
---@param flag? boolean
function checkpoint_volume(volume, flag) end

---@param moby moby_handle
function clear_dances(moby) end

---@param value number
---@overload fun(value: number)
---@overload fun(value: number)
function clear_dialog_played(value) end

---@param value number
---@overload fun(value: number)
---@overload fun(value: number)
function clear_flag(value) end

---No arguments.
function clear_screen() end

---@param clue clue_handle
---@param flag boolean
function clue_set_active(clue, flag) end

---@param pod pod_handle
function coll_off(pod) end

---@param pod pod_handle
function coll_on(pod) end

---@param clue clue_handle
function complete_geo_laser(clue) end

---@param value number
---@param value2 number
function complete_mission(value, value2) end

---@param text string
---@param flag? boolean
function complete_segment(text, flag) end

---@param controller controller_handle
---@param value number
function controller_time_restore(controller, value) end

---@param controller controller_handle
---@param value number
---@param value2 number
function controller_time_scale(controller, value, value2) end

---@param pod pod_handle
function cragmite_set_swarmer_pod(pod) end

---@param moby moby_handle
---@param value number
function cragmiteturret_setaggressive(moby, value) end

---@param moby moby_handle
---@param value number
function cragmiteturret_setidletime(moby, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
function cragmiteturret_setspeed(moby, value, value2) end

---@param moby moby_handle
---@param target moby_handle|volume_handle|area_handle
---@overload fun(moby: moby_handle, moby2: moby_handle)
---@overload fun(moby: moby_handle, volume: volume_handle)
---@overload fun(moby: moby_handle, area: area_handle)
function cragmiteturret_settarget(moby, target) end

---@param moby moby_handle
function cragmiteturret_switchlights(moby) end

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

---No arguments.
function credits_exit() end

---@param moby moby_handle
---@param moby2 moby_handle
function crushto_set_dais(moby, moby2) end

---@param target moby_handle|pod_handle
---@param value number
---@overload fun(moby: moby_handle, value: number)
---@overload fun(pod: pod_handle, value: number)
function damage(target, value) end

---@param moby moby_handle
---@return boolean
function dc_is_done(moby) end

---@param moby moby_handle
---@return boolean
function dc_is_just_completed(moby) end

---@param moby moby_handle
---@param value number
function dc_set_ball_size(moby, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
function dc_set_ball_speed(moby, value, value2) end

---@param moby moby_handle
---@param value number
---@param value2 number
function dc_set_ball_start(moby, value, value2) end

---@param moby moby_handle
---@param value number
---@param value2 number
function dc_set_grid_size(moby, value, value2) end

---@param moby moby_handle
---@param value number
function dc_set_spark_speed(moby, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function dc_set_tile(moby, value, value2, value3, value4) end

---@param target pod_handle|controller_handle|moby_handle
---@overload fun(pod: pod_handle)
---@overload fun(controller: controller_handle)
---@overload fun(moby: moby_handle)
function deactivate(target) end

---@param moby moby_handle
---@param controller controller_handle
---@param flag? boolean
function deactivate_cam(moby, controller, flag) end

---@param moby moby_handle
function default_follow_cam_look_at_height(moby) end

---@param moby moby_handle
function default_follow_cam_pos(moby) end

---@param moby moby_handle
function delete(moby) end

---No arguments.
function delete_all_chunks() end

---@param moby moby_handle
function delete_zoni(moby) end

---@param moby moby_handle
function detonator_start(moby) end

---@param moby moby_handle
function disable(moby) end

---@param controller controller_handle
function disable_cam(controller) end

---No arguments.
function disable_weather() end

---@param moby moby_handle
---@param volume volume_handle
---@param text string
---@param text2 string
---@param value? number
---@param value2? number
---@return boolean
function do_music_track_transition_check(moby, volume, text, text2, value, value2) end

---@param moby moby_handle
---@param flag? boolean
function door_close(moby, flag) end

---@param moby moby_handle
---@return boolean
function door_is_closed(moby) end

---@param moby moby_handle
---@return boolean
function door_is_open(moby) end

---@param target moby_handle|pod_handle
---@param flag? boolean
---@overload fun(moby: moby_handle, flag?: boolean)
---@overload fun(pod: pod_handle, flag?: boolean)
function door_open(target, flag) end

---@param moby moby_handle
---@param value number
function dropship_setintropathstartt(moby, value) end

---@param moby moby_handle
function elevator_cranked_uncrank(moby) end

---@param moby moby_handle
---@param value number
function elevator_warp_to_node(moby, value) end

---@param moby moby_handle
function enable(moby) end

---No arguments.
function enable_all_cheats() end

---No arguments.
function end_magcycle() end

---No arguments.
function end_timer() end

---@param moby moby_handle
function enemy_alert(moby) end

---@param moby moby_handle
---@param value number
function enemy_disable_transition(moby, value) end

---@param moby moby_handle
---@param value number
function enemy_enable_transition(moby, value) end

---@param moby moby_handle
---@return number
function enemy_get_health(moby) end

---@param moby moby_handle
---@return number
function enemy_get_health_abs(moby) end

---@param moby moby_handle
---@param value number
---@param pod pod_handle
function enemy_kill_below_object(moby, value, pod) end

---@param target moby_handle|pod_handle
---@overload fun(moby: moby_handle)
---@overload fun(pod: pod_handle)
function enemy_sendmsg_jump(target) end

---@param target moby_handle|pod_handle
---@param path? path_handle
---@overload fun(moby: moby_handle)
---@overload fun(moby: moby_handle, path: path_handle)
---@overload fun(pod: pod_handle, path: path_handle)
---@overload fun(pod: pod_handle)
function enemy_set_intro_path(target, path) end

---@param moby moby_handle
function enemy_wakeup(moby) end

---@param text string
function execute(text) end

---No arguments.
function exit_galactic_map() end

---No arguments.
function exit_pause_menu() end

---No arguments.
function exit_to_galacticmap() end

---@param value? number
function fade_from_black(value) end

---@param value? number
function fade_from_white(value) end

---@param value? number
function fade_to_black(value) end

---@param value? number
function fade_to_white(value) end

---@param text string
function filter_events_class(text) end

---@param value number
function filter_events_type(value) end

---@param flag boolean
function force_item_bar(flag) end

---No arguments.
function force_pickup_bolts() end

---@param controller controller_handle
---@param flag boolean
function force_track_fx(controller, flag) end

---@return number
function get_all_ammo_cost() end

---@param value number
---@param value2? number
---@return number
function get_ammo_cost(value, value2) end

---@param value number
---@return string
function get_ammo_name(value) end

---@param value number
---@return number
function get_ammo_needed(value) end

---@param moby moby_handle
---@return number
function get_anim(moby) end

---@return string
function get_arena_name() end

---@param value number
---@return number
function get_armor_cost(value) end

---@param value number
---@return string
function get_armor_desc(value) end

---@param value number
---@return string
function get_armor_name(value) end

---@return number
function get_boot_type() end

---@return number
function get_button_layout() end

---@return number
---@overload fun(): number
---@overload fun(): number
function get_camera_speed() end

---@param value number
---@return number
function get_challenge_bolt_reward(value) end

---@param value number
---@return string
function get_challenge_desc(value) end

---@param value number
---@return string
function get_challenge_image(value) end

---@param value number
---@return string
function get_challenge_name(value) end

---@param value number
---@return number
function get_challenge_successes(value) end

---@param value number
---@return number
function get_challenge_time(value) end

---@param value number
---@return number
function get_challenge_weapon(value) end

---@param value number
---@return number
function get_challenge_weapon_reward(value) end

---@param value number
---@return string
function get_cheat_desc(value) end

---@param value number
---@return string
function get_cheat_name(value) end

---@param value number
---@return number
function get_cheat_state(value) end

---@param value number
---@param value2 number
---@return string
function get_cheat_state_name(value, value2) end

---@param value number
---@return number
function get_cheat_value(value) end

---@param moby moby_handle
---@return moby_handle
function get_clank(moby) end

---@param pod pod_handle
---@param target position|moby_handle|volume_handle
---@return moby_handle
---@overload fun(pod: pod_handle, position: position): moby_handle
---@overload fun(pod: pod_handle, moby: moby_handle): moby_handle
---@overload fun(pod: pod_handle, volume: volume_handle): moby_handle
function get_closest(pod, target) end

---@param value number
---@return string
function get_control_scheme_name(value) end

---@return number
function get_cur_control_scheme() end

---@return number
function get_current_level() end

---@return number
function get_current_skillpoints() end

---@return number
function get_default_challenge() end

---@param position position
---@param position2 position
---@return number
function get_dist(position, position2) end

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

---@param value number
---@return string
function get_help_text(value) end

---@return number
function get_hero_bolts() end

---@return number
function get_hero_minerals() end

---@param value number
---@return number
function get_item_cost(value) end

---@param value number
---@return string
function get_item_desc(value) end

---@param value number
---@return string
function get_item_name(value) end

---@param target path_handle|position
---@return number
---@overload fun(path: path_handle): number
---@overload fun(position: position): number
function get_length(target) end

---@return number
function get_level() end

---@param value number
---@return string
---@overload fun(value: number): string
---@overload fun(value: number): string
function get_level_description(value) end

---@param value number
---@return string
function get_level_image(value) end

---@param value number
---@return number
function get_level_missions_completed(value) end

---@param value number
---@return number
function get_level_missions_total(value) end

---@param value number
---@return string
---@overload fun(value: number): string
---@overload fun(value: number): string
function get_level_name(value) end

---@param value number
---@return number
function get_level_sector(value) end

---@param value number
---@return string
---@overload fun(value: number): string
---@overload fun(value: number): string
function get_location_name(value) end

---@param value number
---@param value2 number
---@return string
---@overload fun(value: number, value2: number): string
---@overload fun(value: number, value2: number): string
function get_mission_desc(value, value2) end

---@param value number
---@param value2 number
---@return string
---@overload fun(value: number, value2: number): string
---@overload fun(value: number, value2: number): string
function get_mission_name(value, value2) end

---@param value number
---@param value2 number
---@return number
function get_mod_cost(value, value2) end

---@param value number
---@param value2 number
---@return string
function get_mod_desc(value, value2) end

---@param value number
---@param value2 number
---@return string
function get_mod_name(value, value2) end

---@param value number
---@param value2 number
---@return number
function get_mod_type(value, value2) end

---@param value number
---@param value2 number
---@return number
function get_mod_value(value, value2) end

---@param value number
---@return string
function get_monster_desc(value) end

---@param value number
---@return string
function get_monster_name(value) end

---@param value number
---@param value2 number
---@return number
function get_monster_stat_data(value, value2) end

---@param value number
---@param value2 number
---@return string
function get_monster_stat_name(value, value2) end

---@return boolean
function get_music_track_request() end

---@return number
---@overload fun(): number
---@overload fun(): number
function get_music_vol() end

---@return number
function get_next_level() end

---@param pod pod_handle
---@return number
function get_num_alive(pod) end

---@return number
function get_num_blueprints() end

---@return number
function get_num_challenges() end

---@param value number
---@return number
function get_num_cheat_states(value) end

---@return number
function get_num_cheats() end

---@return number
function get_num_control_schemes() end

---@param pod pod_handle
---@return number
function get_num_dancing_in_pod(pod) end

---@return number
function get_num_help() end

---@param pod pod_handle
---@param area area_handle
---@return number
function get_num_in_area(pod, area) end

---@param pod pod_handle
---@return number
function get_num_in_pod(pod) end

---@param pod pod_handle
---@param volume volume_handle
---@return number
function get_num_in_volume(pod, volume) end

---@param value number
---@return number
function get_num_items_purchasable(value) end

---@param value number
---@return number
---@overload fun(value: number): number
---@overload fun(value: number): number
function get_num_missions(value) end

---@param value number
---@return number
function get_num_mods(value) end

---@param value number
---@return number
function get_num_monster_stats(value) end

---@return number
function get_num_monsters() end

---@param value number
---@return number
---@overload fun(value: number): number
---@overload fun(value: number): number
function get_num_objects(value) end

---@param value number
---@return number
function get_num_skillpoints(value) end

---@return number
function get_pack_type() end

---@param target volume_handle|moby_handle|clue_handle
---@return position
---@overload fun(volume: volume_handle): position
---@overload fun(moby: moby_handle): position
---@overload fun(clue: clue_handle): position
function get_pos(target) end

---@param path path_handle
---@param value number
---@return position
function get_pos_along_path(path, value) end

---@param value number
---@return number
function get_qs_weapon_in_slot(value) end

---@return number
function get_return_screen() end

---@param value number
---@return string
function get_sector_name(value) end

---@param value number
---@return string
function get_simple_object_desc(value) end

---@param value number
---@return string
function get_simple_object_name(value) end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function get_six_axis_controls() end

---@param value number
---@param value2 number
---@return string
function get_skillpoint_desc(value, value2) end

---@param value number
---@param value2 number
---@return string
function get_skillpoint_name(value, value2) end

---@param value number
---@param value2 number
---@return number
function get_skillpoint_value(value, value2) end

---@param value number
---@return number
function get_skin_cost(value) end

---@param value number
---@return string
function get_skin_desc(value) end

---@param value number
---@return string
function get_skin_movie(value) end

---@param value number
---@return string
function get_skin_name(value) end

---@return number
---@overload fun(): number
---@overload fun(): number
function get_sound_vol() end

---@param value number
---@return number
function get_space_stat(value) end

---@param value? number
---@return number
---@overload fun(value?: number): number
---@overload fun(value: number): number
function get_special_bolts_collected(value) end

---@return number
function get_special_bolts_owned() end

---@param value? number
---@return number
---@overload fun(value?: number): number
---@overload fun(value: number): number
function get_special_bolts_total(value) end

---@return number
function get_starting_screen() end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function get_surround_on() end

---@param value number
---@return number
function get_times_challenge_completed(value) end

---@param value number
---@return string
function get_vendor_weapon_desc(value) end

---@param value number
---@return string
function get_vendor_weapon_name(value) end

---@return number
---@overload fun(): number
---@overload fun(): number
function get_voice_vol() end

---@param value number
---@return number
function get_weapon_ammo(value) end

---@param value number
---@return number
function get_weapon_cost(value) end

---@param value number
---@return string
function get_weapon_desc(value) end

---@param value number
---@return number
function get_weapon_level(value) end

---@param value number
---@return number
function get_weapon_max_ammo(value) end

---@param value number
---@return string
function get_weapon_name(value) end

---@param value number
---@return number
function get_weapon_power(value) end

---@param value number
---@return number
function get_weapon_progress(value) end

---No arguments.
function gmap() end

---@param value number
---@param value2 number
---@param value3 number
function group_fade_low_pass(value, value2, value3) end

---@param value number
---@param value2 number
---@param value3 number
function group_fade_pitch_mod(value, value2, value3) end

---@param value number
---@param value2 number
---@param value3 number
function group_fade_vol(value, value2, value3) end

---@param moby moby_handle
---@param volume volume_handle
---@param value? number
---@param value2? number
---@param value3? number
function gunship_attack_grenade(moby, volume, value, value2, value3) end

---@param moby moby_handle
---@param path path_handle
---@param value? number
function gunship_attack_laser(moby, path, value) end

---@param moby moby_handle
---@param path path_handle
---@param value number
function gunship_grindrail(moby, path, value) end

---@param value number
---@return boolean
---@overload fun(value: number): boolean
---@overload fun(value: number): boolean
function has_blueprint(value) end

---@param text string
---@return boolean
function has_cinematic_played(text) end

---@return boolean
function has_dance_game_started() end

---@param value number
---@return boolean
---@overload fun(value: number): boolean
---@overload fun(value: number): boolean
function has_dialog_played(value) end

---@param moby moby_handle
---@return boolean
function has_lost_dance(moby) end

---@param text string
---@return boolean
---@overload fun(text: string): boolean
---@overload fun(text: string): boolean
function has_movie_played(text) end

---@param value number
---@return boolean
function has_owned_item(value) end

---@param moby moby_handle
---@return boolean
function has_teleported(moby) end

---@return boolean
function has_valid_checkpoint() end

---@param moby moby_handle
---@param flag boolean
function health_set_invulnerability(moby, flag) end

---@return boolean
function help() end

---@return boolean
function helptext() end

---@param moby moby_handle
---@param value number
function hero_add_bolts(moby, value) end

---@param moby moby_handle
---@param value number
function hero_add_minerals(moby, value) end

---@param moby moby_handle
---@param value number
---@param value2? number
function hero_add_object(moby, value, value2) end

---@param moby moby_handle
---@param value number
---@param value2? number
function hero_add_quick_select(moby, value, value2) end

---@param moby moby_handle
function hero_end_scene(moby) end

---@param moby moby_handle
---@param value number
function hero_equip(moby, value) end

---@param moby moby_handle
---@param value number
---@return number
function hero_get_ammo(moby, value) end

---@param moby moby_handle
---@return number
function hero_get_armor(moby) end

---@param moby moby_handle
---@return number
function hero_get_blueprints(moby) end

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
function hero_get_minerals(moby) end

---@param moby moby_handle
---@return number
function hero_get_num_items(moby) end

---@param moby moby_handle
---@param value number
---@return number
function hero_get_num_objects(moby, value) end

---@param moby moby_handle
function hero_give_all_ammo(moby) end

---@param moby moby_handle
function hero_give_all_blueprints(moby) end

---@param moby moby_handle
---@param value number
function hero_give_blueprint(moby, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
function hero_give_mod(moby, value, value2) end

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
function hero_has_blueprint(moby, value) end

---@param moby moby_handle
---@param value number
---@return boolean
function hero_has_object(moby, value) end

---@param moby moby_handle
---@param value number
---@return boolean
function hero_has_powerup(moby, value) end

---@param moby moby_handle
---@param value number
---@return boolean
function hero_has_weapon(moby, value) end

---@param moby moby_handle
function hero_heal(moby) end

---@param moby moby_handle
---@param flag boolean
---@param flag2? boolean
---@param flag3? boolean
function hero_lockout_control(moby, flag, flag2, flag3) end

---@param moby moby_handle
---@param controller controller_handle
function hero_magcycle_acquire(moby, controller) end

---@param moby moby_handle
---@return boolean
function hero_play_space_teleporter_dialog(moby) end

---@param moby moby_handle
---@param value number
function hero_remove_quick_select(moby, value) end

---@param value number
function hero_request_equip(value) end

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
function hero_set_minerals(moby, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
function hero_set_num_objects(moby, value, value2) end

---@param moby moby_handle
---@param value number
function hero_set_xp(moby, value) end

---@param moby moby_handle
---@param flag? boolean
---@param flag2? boolean
function hero_ship_fly_in(moby, flag, flag2) end

---@param moby moby_handle
---@param flag? boolean
---@param flag2? boolean
function hero_ship_fly_out(moby, flag, flag2) end

---@param moby moby_handle
---@param value number
---@param text string
function hero_ship_set_cinematic(moby, value, text) end

---@param moby moby_handle
---@param value number
---@param text string
function hero_ship_set_fmv(moby, value, text) end

---@param moby moby_handle
---@param volume volume_handle
---@param moby2 moby_handle
function hero_start_scene(moby, volume, moby2) end

---@param moby moby_handle
function hero_stop_space_teleporter_dialog(moby) end

---@param moby moby_handle
function hero_unrestrict_equip(moby) end

---@param moby moby_handle
---@param value volume_handle|number|position|moby_handle
---@param value2? boolean|number
---@param value3? number
---@param flag? boolean
---@overload fun(moby: moby_handle, volume: volume_handle, flag?: boolean)
---@overload fun(moby: moby_handle, value: number, value2: number, value3: number, flag?: boolean)
---@overload fun(moby: moby_handle, position: position, flag?: boolean)
---@overload fun(moby: moby_handle, moby2: moby_handle, flag?: boolean)
function hero_warp(moby, value, value2, value3, flag) end

---@param moby moby_handle
---@param flag? boolean
function hide(moby, flag) end

---@param moby moby_handle
---@return boolean
function hookup_is_in_use(moby) end

---@param moby moby_handle
---@return boolean
function infobot_is_triggered(moby) end

---@param target moby_handle|controller_handle|clue_handle
---@return boolean
---@overload fun(moby: moby_handle): boolean
---@overload fun(controller: controller_handle): boolean
---@overload fun(clue: clue_handle): boolean
function is_activated(target) end

---@param moby moby_handle
---@param controller controller_handle
---@return boolean
function is_active_cam(moby, controller) end

---@param moby moby_handle
---@return boolean
function is_alive(moby) end

---@param value number
---@return boolean
function is_armor_available(value) end

---@param value number
---@return boolean
function is_armor_owned(value) end

---@param moby moby_handle
---@return boolean
function is_assembly_complete(moby) end

---@param value number
---@return boolean
function is_boss_challenge(value) end

---@param value number
---@return boolean
function is_challenge_active(value) end

---@param moby moby_handle
---@return boolean
function is_charging(moby) end

---@param value number
---@return boolean
function is_cheat_unlocked(value) end

---@param moby moby_handle
---@param moby2 moby_handle
---@return boolean
function is_cranking(moby, moby2) end

---@param value volume_handle|number
---@return boolean
---@overload fun(volume: volume_handle): boolean
---@overload fun(value: number): boolean
function is_current_checkpoint(value) end

---@param value number
---@return boolean
function is_device(value) end

---@param moby moby_handle
---@return boolean
function is_drawn(moby) end

---@param moby moby_handle
---@return boolean
function is_flying(moby) end

---@param value number
---@return boolean
function is_gadget(value) end

---@param clue clue_handle
---@return boolean
function is_geo_laser_complete(clue) end

---@param moby moby_handle
---@return boolean
function is_grinding(moby) end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function is_help_text_on() end

---@param moby moby_handle
---@return boolean
function is_hero_active(moby) end

---@param moby moby_handle
---@return boolean
function is_hero_ship_introing(moby) end

---@return boolean
function is_hud_timer_done() end

---@param moby moby_handle
---@param area area_handle
---@return boolean
function is_in_area(moby, area) end

---@param moby moby_handle
---@param area area_handle
---@return boolean
function is_in_area_ratchet(moby, area) end

---@param moby moby_handle
---@param area area_handle
---@return boolean
function is_in_area_switch(moby, area) end

---@return boolean
function is_in_challenge() end

---@param moby moby_handle
---@param clue clue_handle
---@return boolean
function is_in_jump_slot(moby, clue) end

---@param moby moby_handle
---@return boolean
function is_in_mag_cycle(moby) end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function is_in_replay() end

---@param volume volume_handle
---@return boolean
function is_in_view(volume) end

---@param moby moby_handle
---@param volume volume_handle
---@return boolean
function is_in_volume(moby, volume) end

---@param moby moby_handle
---@param volume volume_handle
---@return boolean
function is_in_volume_ratchet(moby, volume) end

---@param moby moby_handle
---@param volume volume_handle
---@return boolean
function is_in_volume_switch(moby, volume) end

---@param value number
---@return boolean
function is_item_available(value) end

---@param value number
---@return boolean
function is_level_seen(value) end

---@param value number
---@return boolean
function is_level_space_combat(value) end

---@param value number
---@return boolean
function is_level_unlocked(value) end

---@param value number
---@return boolean
function is_level_visible(value) end

---@param value number
---@return boolean
function is_level_visitable(value) end

---@param value number
---@return boolean
function is_level_visited(value) end

---@return boolean
function is_lock_strafe_active() end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function is_look_x_inverted() end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function is_look_y_inverted() end

---@param value number
---@param value2 number
---@return boolean
---@overload fun(value: number, value2: number): boolean
---@overload fun(value: number, value2: number): boolean
function is_mission_available(value, value2) end

---@param value number
---@param value2 number
---@return boolean
---@overload fun(value: number, value2: number): boolean
---@overload fun(value: number, value2: number): boolean
function is_mission_complete(value, value2) end

---@param value number
---@param value2 number
---@return boolean
---@overload fun(value: number, value2: number): boolean
---@overload fun(value: number, value2: number): boolean
function is_mission_optional(value, value2) end

---@param value number
---@param value2 number
---@return boolean
function is_mod_available(value, value2) end

---@param value number
---@param value2 number
---@return boolean
function is_mod_owned(value, value2) end

---@param moby moby_handle
---@return boolean
function is_obviated(moby) end

---@param moby moby_handle
---@return boolean
function is_on_gel_cube(moby) end

---@param moby moby_handle
---@return boolean
function is_on_grav_surface(moby) end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function is_quick_select_pause_on() end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function is_rumble_connected() end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function is_rumble_on() end

---@param text string
---@return boolean
function is_script_running(text) end

---@param value number
---@return boolean
function is_sector_visible(value) end

---@param value number
---@return boolean
function is_sector_visitable(value) end

---@param text string
---@return boolean
function is_segment_complete(text) end

---@param text string
---@return boolean
function is_segment_just_completed(text) end

---@return boolean
function is_six_axis_active() end

---@param value number
---@param value2 number
---@return boolean
function is_skillpoint_complete(value, value2) end

---@param value number
---@return boolean
function is_skillpoint_done(value) end

---@param value number
---@return boolean
function is_skin_available(value) end

---@param value number
---@return boolean
function is_skin_owned(value) end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function is_subtitles_on() end

---@return boolean
function is_timer_done() end

---@param moby moby_handle
---@return boolean
function is_triggered(moby) end

---@param moby moby_handle
---@return boolean
function is_using_geo_laser(moby) end

---@param moby moby_handle
---@param moby2 moby_handle
---@return boolean
function is_using_jump_pad(moby, moby2) end

---@param vo vo_handle
---@return boolean
function is_vo_done(vo) end

---@param value number
---@return boolean
function is_weapon(value) end

---@param value number
---@return boolean
function is_weapon_available(value) end

---@param value number
---@return boolean
function is_weapon_owned(value) end

---@param value number
---@return boolean
function is_weapon_type_used_unique(value) end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function is_x_inverted() end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function is_y_inverted() end

---@param moby moby_handle
---@return boolean
function is_zipping(moby) end

---@param moby moby_handle
function kboss_do_hover(moby) end

---@param moby moby_handle
function kboss_do_intro(moby) end

---@param moby moby_handle
---@param flag boolean
function kboss_set_flying_damage_state(moby, flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 any
---@param value5 any
function kboss_shoot_follow_line(moby, value, value2, value3, value4, value5) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param path path_handle
function kboss_shoot_follow_path(moby, value, value2, value3, path) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param volume volume_handle
function kboss_shoot_vol_target(moby, value, value2, value3, volume) end

---@param moby moby_handle
---@param moby2 moby_handle
function kboss_smash_grind_rail(moby, moby2) end

---@param moby moby_handle
function kboss_start_arena_fight(moby) end

---@param moby moby_handle
function kboss_start_flight(moby) end

---@param moby moby_handle
---@param value number
function kboss_start_sidewinders(moby, value) end

---@param moby moby_handle
function kboss_stop_sidewinders(moby) end

---No arguments.
function kill_help() end

---@param value? number
function letter_box_off(value) end

---@param value? number
function letter_box_on(value) end

---@param text string
---@param text2? string
function load_config(text, text2) end

---@param value number
---@overload fun(value: number)
---@overload fun(value: number)
function load_level(value) end

---No arguments.
function magcycle_clear_force_path() end

---@param moby moby_handle
---@return boolean
function magcycle_launcher_used(moby) end

---@param value number
function magcycle_set_damp_type(value) end

---@param path path_handle
---@param value number
function magcycle_set_force_path(path, value) end

---@param moby moby_handle
function maingeom_off(moby) end

---@param moby moby_handle
function maingeom_on(moby) end

---@param moby moby_handle
---@return pod_handle
function moby_get_pod(moby) end

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

---@param moby moby_handle
function move_2D_end(moby) end

---@param moby moby_handle
---@param volume volume_handle
---@param value? number
---@param value2? number
function move_2D_start(moby, volume, value, value2) end

---@param moby moby_handle
---@return boolean
function npc_is_speaking(moby) end

---@param moby moby_handle
---@return boolean
function npc_is_waiting(moby) end

---@param moby moby_handle
function npc_show_responses(moby) end

---@param moby moby_handle
---@param text string
---@param value number
---@param value2? number
---@param value3? number
function npc_speak(moby, text, value, value2, value3) end

---@param moby moby_handle
---@return number
function number_of_slow_time_presses(moby) end

---@param moby moby_handle
---@return number
function number_of_zoni_following(moby) end

---@return number
function number_times_map_activated() end

---@param target pod_handle|moby_handle
---@overload fun(pod: pod_handle)
---@overload fun(moby: moby_handle)
function obviate(target) end

---@param target pod_handle|moby_handle
---@overload fun(pod: pod_handle)
---@overload fun(moby: moby_handle)
function physics_off(target) end

---@param target pod_handle|moby_handle
---@overload fun(pod: pod_handle)
---@overload fun(moby: moby_handle)
function physics_on(target) end

---@param target area_handle|pod_handle
---@return volume_handle|moby_handle
---@overload fun(area: area_handle): volume_handle
---@overload fun(pod: pod_handle): moby_handle
function pick_rand(target) end

---@param moby moby_handle
function pirate_barge_reset(moby) end

---@param moby moby_handle
function pirate_barge_spawn_hunchback(moby) end

---@param moby moby_handle
function pirate_barge_spawn_skinny_melee(moby) end

---@param moby moby_handle
function pirate_barge_spawn_skinny_shooter(moby) end

---@param moby moby_handle
---@param flag boolean
function pirate_stealth(moby, flag) end

---@param value number
function play_dialog(value) end

---@param value number
---@param moby? moby_handle
---@param flag? boolean
---@return vo_handle
---@overload fun(value: number, moby: moby_handle, flag?: boolean): vo_handle
---@overload fun(value: number): vo_handle
function play_explicit_dialog_spec(value, moby, flag) end

---@param value number
---@param moby moby_handle
function play_explicit_sound_spec(value, moby) end

---@param value number
---@param moby moby_handle
function play_explicit_visual_spec(value, moby) end

---@param value number
function play_help(value) end

---@param value number
---@param value2 number
---@param flag? boolean
---@param value3? number
---@param value4? number
---@param value5? number
function play_music(value, value2, flag, value3, value4, value5) end

---@param pod pod_handle
---@param value number
---@return moby_handle
function pod_getmember(pod, value) end

---@param value number|string
---@param value2? number
---@overload fun(value: number, value2: number)
---@overload fun(text: string)
function popup_message(value, value2) end

---@return boolean
function purchase_all_ammo() end

---@param value number
---@param value2? number
---@return boolean
function purchase_ammo(value, value2) end

---@param value number
---@return boolean
function purchase_armor(value) end

---@param value number
---@return boolean
function purchase_item(value) end

---@param value number
---@param value2 number
---@return boolean
function purchase_mod(value, value2) end

---@param value number
---@return boolean
function purchase_skin(value) end

---@param value number
---@return boolean
function purchase_weapon(value) end

---@param moby moby_handle
---@param text string
function queue_arena_challenge_screen(moby, text) end

---@param text string
---@param value? any
---@param value2? any
---@param volume? volume_handle
---@return boolean
function queue_cinematic(text, value, value2, volume) end

---@param text string
---@param value? any
---@param value2? any
---@param volume? volume_handle
---@return boolean
function queue_cinematic_end_white(text, value, value2, volume) end

---@param text string
---@param value? any
---@param value2? any
---@param volume? volume_handle
---@return boolean
function queue_cinematic_no_fade_back(text, value, value2, volume) end

---@param text string
---@param value? any
---@param value2? any
---@param volume? volume_handle
---@return boolean
function queue_cinematic_start_slow_white(text, value, value2, volume) end

---@param text string
---@return boolean
function queue_movie(text) end

---@param value number
---@param value2 number
---@return number
function rand(value, value2) end

---@param moby moby_handle
function ratchet_add_clankpack(moby) end

---@param moby moby_handle
---@return boolean
function ratchet_checked(moby) end

---@param moby moby_handle
function ratchet_remove_clankpack(moby) end

---@param moby moby_handle
---@param flag boolean
function ratchet_set_stache(moby, flag) end

---@param pod pod_handle
---@param moby moby_handle
function remove_from_pod(pod, moby) end

---No arguments.
function request_auto_save() end

---No arguments.
function request_delete() end

---No arguments.
function request_delete_interface() end

---No arguments.
function request_load() end

---No arguments.
function request_load_interface() end

---@param text string
---@return boolean
function request_music_track(text) end

---No arguments.
function request_save() end

---No arguments.
function request_save_interface() end

---@param moby moby_handle
---@return boolean
function reset_follow_cam(moby) end

---@param value number
function reset_help(value) end

---@param value number
function reset_helptext(value) end

---No arguments.
function reset_weapon_types_used() end

---@param moby moby_handle
---@param flag boolean
function restrict_pirate_guise(moby, flag) end

---@param moby moby_handle
---@return boolean
function robowing_pad_used(moby) end

---@param text string
function run_script(text) end

---@param moby moby_handle
function sailboat_reset(moby) end

---@return boolean
function save_game_exists() end

---@return boolean
---@overload fun(): boolean
---@overload fun(): boolean
function save_load_is_interface_displayed() end

---@param text string
---@overload fun(text: string)
---@overload fun(text: string)
function screen_out(text) end

---@param value number
function select_challenge(value) end

---@param value number
---@return boolean
function select_skin(value) end

---@param moby moby_handle
---@param value number
function set_anim(moby, value) end

---@param value number
function set_boot_type(value) end

---@param value number
function set_button_layout(value) end

---@param value number
---@overload fun(value: number)
---@overload fun(value: number)
function set_camera_speed(value) end

---@param value number
function set_challenge_failure(value) end

---@param value number
function set_challenge_success(value) end

---@param value number
function set_cheat_points(value) end

---@param value number
---@param value2 number
---@overload fun(value: number, value2: number)
---@overload fun(value: number, value2: number)
function set_cheat_state(value, value2) end

---@param value number
function set_control_scheme(value) end

---@param moby moby_handle
function set_crushtwo(moby) end

---@param moby moby_handle
---@param flag boolean
function set_damageable(moby, flag) end

---@param value number
---@overload fun(value: number)
---@overload fun(value: number)
function set_flag(value) end

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

---No arguments.
function set_global_view() end

---@param flag boolean
---@overload fun(flag: boolean)
---@overload fun(flag: boolean)
function set_help_text_on(flag) end

---@param value number
function set_highlight_level(value) end

---No arguments.
function set_highlight_off() end

---@param value number
function set_highlight_sector(value) end

---@param value number
function set_item_bar_amount(value) end

---@param value number
function set_item_bar_total(value) end

---@param value number
---@param flag? boolean
function set_level(value, flag) end

---@param value number
function set_level_view(value) end

---@param flag boolean
---@overload fun(flag: boolean)
---@overload fun(flag: boolean)
function set_look_x_inverted(flag) end

---@param flag boolean
---@overload fun(flag: boolean)
---@overload fun(flag: boolean)
function set_look_y_inverted(flag) end

---@param value number
---@overload fun(value: number)
---@overload fun(value: number)
function set_music_vol(value) end

---@param value number
function set_next_level(value) end

---@param value number
function set_pack_type(value) end

---@param moby moby_handle
---@param volume volume_handle
function set_pf_position(moby, volume) end

---@param moby moby_handle
---@param value number
---@param value2? number
function set_pf_speed(moby, value, value2) end

---@param value number
---@param value2 number
function set_qs_weapon_in_slot(value, value2) end

---@param flag boolean
---@overload fun(flag: boolean)
---@overload fun(flag: boolean)
function set_quick_select_pause_on(flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@return boolean
function set_robowing_cam(moby, value, value2) end

---@param flag boolean
---@overload fun(flag: boolean)
---@overload fun(flag: boolean)
function set_rumble_on(flag) end

---@param value number
function set_save_level(value) end

---@param value number
function set_sector_view(value) end

---@param text string
function set_segment(text) end

---@param flag boolean
---@overload fun(flag: boolean)
---@overload fun(flag: boolean)
function set_six_axis_controls(flag) end

---@param value number
function set_skillpoint_done(value) end

---@param value number
---@overload fun(value: number)
---@overload fun(value: number)
function set_sound_vol(value) end

---@param flag boolean
---@overload fun(flag: boolean)
---@overload fun(flag: boolean)
function set_subtitles_on(flag) end

---@param flag boolean
---@overload fun(flag: boolean)
---@overload fun(flag: boolean)
function set_surround_on(flag) end

---@param moby moby_handle
---@param value number
function set_update_dist(moby, value) end

---@param value number
function set_update_frequency(value) end

---@param value number
---@overload fun(value: number)
---@overload fun(value: number)
function set_voice_vol(value) end

---@param flag boolean
---@overload fun(flag: boolean)
---@overload fun(flag: boolean)
function set_x_inverted(flag) end

---@param flag boolean
---@overload fun(flag: boolean)
---@overload fun(flag: boolean)
function set_y_inverted(flag) end

---@param volume volume_handle
---@param value number
function shingle_cannonball_spawn(volume, value) end

---@param moby moby_handle
function shingle_start_hanging(moby) end

---@param moby moby_handle
function ship_end_path(moby) end

---@param moby moby_handle
---@param value number
function ship_set_speed(moby, value) end

---@param moby moby_handle
---@param path path_handle
---@param value? number
function ship_start_path(moby, path, value) end

---@param moby moby_handle
---@param flag? boolean
function show(moby, flag) end

---@param moby moby_handle
function show_armor_vendor(moby) end

---No arguments.
function show_credits() end

---@param moby moby_handle
function show_device_vendor(moby) end

---@param value number
---@param value2? number
function show_location(value, value2) end

---@param moby moby_handle
function show_weapon_vendor(moby) end

---@param moby moby_handle
---@param position position
function simplemotion_set_dest(moby, position) end

---@param moby moby_handle
---@param position position
function simplemotion_set_offset(moby, position) end

---@param moby moby_handle
---@param value number
function simplemotion_set_speed(moby, value) end

---@param moby moby_handle
---@param pod pod_handle
---@param value number
function skinnypirate_tellcampfirestory(moby, pod, value) end

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

---@return moby_handle
function skydive_spawn_radial_mine() end

---@param moby moby_handle
---@return boolean
function slags_battleship_is_alive(moby) end

---@param moby moby_handle
function slags_battleship_start_spawning(moby) end

---@param moby moby_handle
function slags_battleship_stop_spawning(moby) end

---@param moby moby_handle
function slags_cargo_start_spawning(moby) end

---@param moby moby_handle
function slags_cargo_stop_spawning(moby) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5? number
function space_combat_add_command_go_to_offset(moby, value, value2, value3, value4, value5) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5? number
function space_combat_add_command_go_to_point(moby, value, value2, value3, value4, value5) end

---@param moby moby_handle
---@param value number
function space_combat_add_command_hold(moby, value) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
function space_combat_add_command_impulse_offset(moby, value, value2, value3, value4, value5) end

---@param moby moby_handle
---@param value number
function space_combat_add_command_wait(moby, value) end

---@param value number
---@param value2 number
function space_combat_apply_roll(value, value2) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@return moby_handle
function space_combat_battleship_deploy_turret(moby, value, value2) end

---@param moby moby_handle
function space_combat_bs_turret_fire(moby) end

---No arguments.
function space_combat_cancel_roll() end

---@param text string
function space_combat_dump_core_matrix(text) end

---@param moby moby_handle
---@param flag boolean
function space_combat_enemy_set_cloak(moby, flag) end

---@param moby moby_handle
---@param flag boolean
function space_combat_enemy_set_shield(moby, flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
function space_combat_enemy_set_snatcher_fire(moby, value, value2) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function space_combat_enemy_set_stealth_fire(moby, value, value2, value3, value4) end

---@return number
function space_combat_get_path_param() end

---@return number
function space_combat_get_time() end

---@param flag boolean
function space_combat_reset(flag) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function space_combat_satellite_fire(moby, value, value2, value3, value4) end

---@param value number
---@param value2 number
function space_combat_set_absolute_roll(value, value2) end

---@param moby moby_handle
---@param value? number
---@param value2? number
function space_combat_set_command_start_fire(moby, value, value2) end

---@param moby moby_handle
function space_combat_set_command_stop_fire(moby) end

---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
---@param value6 number
---@param value7 number
---@param value8 number
---@param value9 number
function space_combat_set_core_matrix(value, value2, value3, value4, value5, value6, value7, value8, value9) end

---No arguments.
function space_combat_set_default_path_speed() end

---@param path path_handle
---@param value? number
function space_combat_set_path(path, value) end

---@param value number
---@param value2 number
---@param value3 number
function space_combat_set_path_speed(value, value2, value3) end

---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
function space_combat_set_roll_data(value, value2, value3, value4, value5) end

---@param value number
function space_combat_set_shunnel_size(value) end

---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function space_combat_set_speed_data(value, value2, value3, value4) end

---@param flag boolean
function space_combat_set_turret_mode(flag) end

---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@return moby_handle
function space_combat_spawn_enemy(value, value2, value3, value4) end

---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
function space_combat_spawn_thrust_mine(value, value2, value3, value4) end

---@param value number
function space_combat_start_roll_speed(value) end

---No arguments.
function space_combat_stop_roll() end

---@param moby moby_handle
function space_combat_sunsucker_fire(moby) end

---@param path path_handle
function space_combat_switch_path(path) end

---@param moby moby_handle
---@param value number
---@param value2 number
---@param value3 number
---@param value4 number
---@param value5 number
---@param value6 number
function space_combat_turret_fire(moby, value, value2, value3, value4, value5, value6) end

---@param moby moby_handle
---@return boolean
function space_teleporter_hero_is_flying(moby) end

---@param value number
---@param volume volume_handle
function spawn_ammo(value, volume) end

---@param value number
---@param volume volume_handle
---@param value2? number
function spawn_bolts(value, volume, value2) end

---@param controller controller_handle
function spawn_car(controller) end

---@param volume volume_handle
---@param value? number
---@param value2? number
---@return moby_handle
function spawn_groovitron(volume, value, value2) end

---@param flag boolean
---@param volume volume_handle
function spawn_health(flag, volume) end

---@param value number
---@param volume volume_handle
function spawn_item(value, volume) end

---@param volume volume_handle
function spawn_lattice_hazard(volume) end

---@param value number
---@param volume volume_handle
function spawn_minerals(value, volume) end

---@param value number
---@param volume volume_handle
function spawn_powerup(value, volume) end

---@param volume volume_handle
function spawn_soul(volume) end

---@param value number
---@param volume volume_handle
function spawn_weapon(value, volume) end

---@param moby moby_handle
function spawner_clear(moby) end

---@param moby moby_handle
---@param value number
---@return moby_handle
function spawner_spawn(moby, value) end

---@param value number
function start_challenge(value) end

---@param moby moby_handle
function start_dance_game(moby) end

---No arguments.
function start_game() end

---@param value number
---@param value2 number
function start_item_bar(value, value2) end

---@param flag? boolean
function start_magcycle(flag) end

---@param value number
function start_timer(value) end

---No arguments.
function stop_dialog() end

---No arguments.
function stop_item_bar() end

---@param vo vo_handle
function stop_vo(vo) end

---@param text string
---@return number
function string_length(text) end

---@param moby moby_handle
function switch_from_clank(moby) end

---@param moby moby_handle
function switch_from_pirate(moby) end

---@param moby moby_handle
---@param flag? boolean
function switch_to_clank(moby, flag) end

---@param moby moby_handle
---@param path path_handle
---@param value number
---@param value2? number
function tachyon_destroyer_fire_path_beam(moby, path, value, value2) end

---@param moby moby_handle
---@param path path_handle
---@param value number
---@param value2? number
function tachyon_destroyer_fire_path_missile(moby, path, value, value2) end

---@param moby moby_handle
---@return boolean
function tachyon_is_speaking(moby) end

---@param moby moby_handle
---@param value number
---@param flag? boolean
function tachyon_speak(moby, value, flag) end

---@param moby moby_handle
---@return boolean
function talking_head_complete(moby) end

---@param moby moby_handle
---@return boolean
function taxi_is_moving(moby) end

---No arguments.
function test() end

---No arguments.
function time_clear_scale() end

---@param value number
function time_set_scale(value) end

---@return number
function time_since_map_activated() end

---@param moby moby_handle
---@return number
function time_since_slow_time_press(moby) end

---@param moby moby_handle
function trigger(moby) end

---@param value number
---@param moby moby_handle
---@param value2? number
---@param value3? number
function trigger_effect_event(value, moby, value2, value3) end

---@param moby moby_handle
---@return boolean
function tripad_is_active(moby) end

---@param moby moby_handle
function tripad_set_complete(moby) end

---@param text string
---@param value number
---@return string
function truncate_string(text, value) end

---@param text string
function tty_out(text) end

---@param moby moby_handle
---@return number
function turret_get_kills(moby) end

---@param text string
function unlock_from_level(text) end

---@param value number
function unlock_level(value) end

---@param value number
function unlock_weapon(value) end

---@param target pod_handle|moby_handle
---@overload fun(pod: pod_handle)
---@overload fun(moby: moby_handle)
function unobviate(target) end

---@param moby moby_handle
function unobviate_teleport(moby) end

---@param moby moby_handle
function untrigger(moby) end

---@param target pod_handle|moby_handle
---@overload fun(pod: pod_handle)
---@overload fun(moby: moby_handle)
function update_off(target) end

---@param pod pod_handle
function update_on(pod) end

---@param moby moby_handle
---@param value number
function walker_setambient(moby, value) end

---@param moby moby_handle
---@param target moby_handle|pod_handle|volume_handle
---@overload fun(moby: moby_handle, moby2: moby_handle)
---@overload fun(moby: moby_handle, pod: pod_handle)
---@overload fun(moby: moby_handle, volume: volume_handle)
function walker_setambienttarget(moby, target) end

---@param moby moby_handle
---@param value number
---@param volume volume_handle
function walker_specialintro(moby, value, volume) end

---@param moby moby_handle
---@param volume volume_handle
function walker_specialintro_settarget(moby, volume) end

---@param moby moby_handle
---@param value number
---@return boolean
function was_damaged(moby, value) end
