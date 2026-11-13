# A Crack in Time — Lua API

`api.lua` documents what has been read from the decrypted retail EBOOT (NPUA80966) and the
shipped scripts. Every entry names its evidence: the address of the native's Lua binding, or
the prelude (Lua text the EBOOT runs before any script, at 0xD2E7A9).

## Findings that change how to write scripts

- **Text output to the log is compiled out, and `rpcs3_lua_tty.yml` restores it.** `tty_out` and
  `screen_out` call functions that are a bare `blr` in the retail EBOOT (the prelude makes `prt` and
  `print` aliases of `screen_out`). The RPCS3 patch in this folder routes both to the game's own SPU
  log printer, so `tty_out("text\n")` appears in RPCS3's TTY.log as `SPU0: text`. Install it by
  copying it to `<RPCS3 config>/patches/imported_patch.yml` and enabling "Lua TTY output (ReLunacy)" in
  Manage > Game Patches. `debug_message(text, seconds)` displays on the HUD with or without it.
- **Handles compare by value**: `==` compares the handle numbers (`.eq`, 0x51CE88); `tostring` gives
  only the class name. `api.lua` has a helper that finds a handle's instance name.
- **`get_pos` takes a volume, not a Moby.** It returns the volume's centre. No native returning
  a Moby's position has been found, so a script cannot read the player's coordinates directly.
- `wait = coroutine.yield`: `while true do ... wait() end` runs once per frame.
- Volumes are tested at half-size / radius 1 along their matrix's axes (`is_inside`, 0x61A124).

## Inspecting values from a script

What the bindings are (from the EBOOT): tolua, with 13 registered classes - `controller_handle`,
`clue_handle`, `music_handle`, `path_handle`, `vo_handle`, `lua_vector`, `pfx_node_handle`,
`pod_handle`, `snd_handle`, `scent_handle`, `moby_handle`, `volume_handle`, `area_handle` (0xCA0424).
A handle is a userdata pointing at a 4-byte value: the instance's index in the low 16 bits, a check
value above (0x4FD50C). Three things to know before inspecting one:

- **Numbers are 32-bit floats** in this build (its chunks say `sizeof(lua_Number) == 4`): integers
  are exact only up to 2^24 = 16,777,216, so a 32-bit value has to be handled in 16-bit halves.
- **`type` is not Lua's**: the prelude sets `type = _extype`, which names a tolua object's class
  (`type(h) == "moby_handle"`); Lua's own is kept as `_type` (`"userdata"`).
- **The natives do not check a handle's class** (`tolua_tousertype` at 0x54008C only checks that the
  value is a userdata), so a class's getter can be called on another class's object.

The last point is what reads a handle's raw value: `lua_vector`'s `x` getter returns the first four
bytes of whatever object it is given, as a float, and the float's bits are the handle value.

```lua
-- The 32-bit value inside a handle, as "0xCCCCIIII" (C: check value, I: instance index).
local getx = rawget(rawget(getmetatable(get_pos(level_grid1)), ".get"), "x") -- any volume will do
function raw_word(h)
    local f = getx(h)
    if f ~= f then return "NaN: the value's bits 23-30 are all set" end
    local sign = (f < 0 or 1 / f < 0) and 1 or 0
    f = math.abs(f)
    local exp, mant = 0, 0
    if f ~= 0 then
        local m, e = math.frexp(f)                -- f = m * 2^e, 0.5 <= m < 1
        if e > -126 then exp, mant = e + 126, m * 2^24 - 2^23
        else mant = math.ldexp(m, e + 149) end    -- a subnormal: exponent bits 0
    end
    local hi = sign * 32768 + exp * 128 + math.floor(mant / 65536)
    local lo = mant - math.floor(mant / 65536) * 65536
    return string.format("0x%04X%04X", hi, lo)
end

tty_out(type(aphelion) .. " " .. raw_word(aphelion) .. "\n")
```

Reading `y` or `z` the same way would read past the handle's four bytes; do not. A class's own
members are in its metatable (methods, and the `.get` / `.set` tables of its fields), and a tolua
class's metatable has its base class's as its own metatable, so the chain can be walked:

```lua
local mt = getmetatable(aphelion)
for k, v in pairs(mt) do tty_out(tostring(k) .. " : " .. _type(v) .. "\n") end
local t = aphelion
while t do tty_out(type(t) .. "\n"); t = getmetatable(t) end
```

None of this has been run in game yet.

## Locating the player without a position getter

`is_inside(get_hero(), volume)` works for any volume. A grid of box volumes therefore locates
the player to a cell. In the space sectors, `level_grid17`–`80` is already such a grid: 8 × 8
cells of 1000, covering −4000 to +4000 on X and Z. Its boxes are thin (10 units each side of
Y = 0), so thicken them in the editor first:

```lua
while true do
    for i = 17, 80 do
        if is_inside(get_hero(), _G["level_grid" .. i]) then
            debug_message("cell " .. i, 0.2)
        end
    end
    wait()
end
```

Finer positions need smaller or more volumes.

## Script-facing natives

931 names are registered alongside the gameplay bindings (tables 0xC8CE30–0xC8F900 and
0xCA0460–0xCA07F0). Only those in `api.lua` have their arguments read; the rest are listed here
so they can be documented one by one. Engine-wide tables (Lua's standard library, Scaleform's
ActionScript classes, configuration fields) are left out.

`get_level` `set_level` `set_save_level` `unlock_level` `is_in_replay` `get_difficulty`
`checkpoint` `has_valid_checkpoint` `get_checkpoint_volume` `is_segment_complete`
`request_auto_save` `request_save` `request_load` `is_new_load` `add_mission` `complete_mission`
`uncomplete_mission` `has_mission` `did_mission` `mission_hud_enable` `mission_hud_flush_queue`
`set_story_tag` `roll_credits` `replay_screen` `disable_great_clock` `set_time_scale`
`unlock_trophy` `beat_game_trophy` `trophy_status` `set_space_level_miss_complete`
`set_cam_invert_y` `set_cam_invert_x` `set_lockstrafe_invert_y` `set_lockstrafe_invert_x`
`get_cam_invert_y` `get_cam_invert_x` `get_lockstrafe_invert_y` `get_lockstrafe_invert_x`
`on_enter` `on_exit` `on_death` `on_num_alive` `on_elapsed` `on_time` `on_audio_end`
`on_prompt_trigger` `on_activate` `on_deactivate` `on_interact` `on_disengage` `on_next_cine_end`
`on_generic_event` `unset_event` `check_hero_flag` `set_hero_flag` `clear_hero_flag` `queue_movie`
`queue_cinematic` `queue_cinematic_capture` `queue_scene` `has_movie_played` `has_cinematic_played`
`has_scene_played` `reset_movie_played` `reset_cinematic_played` `reset_scene_played`
`play_texture_movie` `stop_texture_movie` `set_monitor_shader` `show_armor_vendor`
`show_weapon_vendor` `show_device_vendor` `show_frontend` `show_control_config` `show_info_screen`
`show_difficulty_select` `vap_set_gas_speed` `init_arena` `show_arena_menu` `cannon_set_rate`
`queue_cannon_spawn` `queue_spinner_sprinter` `spawn_sawchucker` `spawn_firechucker`
`arena_park_car` `arena_bring_aphelion` `arena_create_spawnpod` `arena_warp_vol`
`arena_clear_cannons` `arena_snap_cannons` `is_avo_done` `arenavo_pause` `arenavo_unpause`
`arenavo_cut_and_pause` `arena_reset_sniper_ball_count` `arena_get_agorian_sniper_ball_count`
`arena_set_agorian_sniper_ball_speed` `arena_reset_agorian_balls` `arena_unlock` `arena_up_in_here`
`arena_hush_foo` `start_challenge` `set_challenge_success` `set_challenge_failure`
`get_challenge_successes` `arena_spawn` `arena_count` `arena_delete` `arena_set_advanced`
`arena_set_edge` `arena_clear_edge` `arena_hydra_tag_out` `arena_hydra_tag_in` `arena_grok_tag_out`
`arena_grok_tag_in` `molonoth_junk_clear` `zolar_breakables_clear` `space2_graveyard_clear`
`get_greatclocka_skill_time` `start_burrower_kill_watch` `have_burrowers_been_killed`
`is_skillpoint_time_fast_enough` `get_valkyrie_skillpoint_time` `get_molonoth_timetrial_time`
`get_tumbler_skillpoint_time` `get_skillpoint_bool` `set_npc_talked_to` `unlock_all_skillpoints`
`help` `helptext` `close_help` `debug_message` `popup_message` `show_location` `clear_location`
`is_triggered` `trigger` `untrigger` `trigger_if_alive` `is_alive` `get_num_alive` `disable`
`enable` `update_off` `update_on` `is_update_on` `physics_off` `physics_on` `coll_off` `coll_on`
`is_activated` `activate` `deactivate` `maingeom_on` `maingeom_off` `bangle_on` `bangle_off`
`bangle_all_on` `bangle_all_off` `reinit` `warp` `draw_off` `trigger_auto_spring` `damage`
`damage_sphere` `was_damaged` `set_damageable` `physics_push_moby` `physics_push_pod`
`physics_push_sphere` `moby_simple_move` `moby_simple_rotate` `simplemotion_set_dest`
`simplemotion_set_offset` `simplemotion_set_speed` `play_anim_query` `set_anim_modifier`
`play_anim_by_index` `camera_shake` `teleport` `force_track_fx` `delete_all_chunks`
`has_dialog_played` `clear_dialog_played` `enable_weather` `disable_weather` `suspend_weather`
`resume_weather` `lightning` `register_fx_slave` `post_effect_enable` `is_post_effect_enabled`
`set_custom_pistol_color` `spawn_car` `set_pf_position` `set_pf_speed` `set_pf_stop`
`set_pf_reverse` `is_pf_moving` `set_follow_cam` `reset_follow_cam` `default_follow_cam_pos`
`set_follow_cam_look_at_height` `default_follow_cam_look_at_height` `get_follow_cam_dist`
`get_follow_cam_pitch` `get_follow_cam_height` `get_follow_cam_fov` `set_follow_cam_indexed`
`activate_cam` `deactivate_cam` `is_active_cam` `disable_cam` `set_override_gravity_input`
`activate_world_cam` `deactivate_world_cam` `activate_skydive_cam` `is_cam_done` `get_cam_pos`
`camera_request_recenter` `camera_clear_recenter` `set_camera_turn_limits`
`clear_camera_turn_limits` `pause_lock_strafe` `resume_lock_strafe` `button_on` `button_off`
`button_is_on` `set_switch_value` `get_switch_value` `tripad_is_active` `tripad_set_complete`
`multipad_set` `door_open` `door_close` `door_is_open` `door_is_closed` `door_force_open`
`door_force_closed` `door_clear_force` `is_webtacle_thrashing` `bridge_extend` `bridge_retract`
`bridge_is_extended` `elevator_move` `elevator_move_to_first_node` `elevator_move_to_second_node`
`elevator_warp_to_first_node` `elevator_warp_to_second_node` `elevator_is_moving`
`elevator_is_at_first_node` `elevator_is_at_second_node` `elevator_cranked_uncrank`
`crank_is_in_use` `crank_is_complete` `crank_set_complete` `crank_get_progress` `crank_reset`
`hookup_is_in_use` `turret_get_user` `turret_lock` `turret_unlock` `play_music` `stop_music`
`request_music_track` `get_music_track_request` `is_skillpoint_done` `set_skillpoint_done`
`enable_all_cheats` `set_cheat_state` `set_cheat_points` `radar_enable` `radar_disable`
`radar_add_element` `radar_remove_element` `radar_ping_element` `radar_stop_ping`
`radar_is_element` `radar_set_draw_off_radar` `radar_set_range` `get_hero` `is_hero_active`
`unlock_weapon` `tease_weapon` `hero_give_weapon` `hero_award_weapon` `hero_take_weapon`
`hero_has_weapon` `hero_get_num_items` `hero_get_ammo` `hero_set_ammo` `hero_give_all_ammo`
`hero_give_gold_bolt` `hero_set_xp` `hero_add_bolts` `hero_set_bolts` `hero_get_bolts`
`hero_set_armor` `hero_get_armor` `hero_give_part` `hero_has_part` `hero_give_powerup`
`hero_has_powerup` `hero_get_health` `hero_heal` `hero_equip` `hero_restrict_equip`
`hero_unrestrict_equip` `hero_set_infinite_ammo` `hero_has_object` `hero_add_object`
`hero_get_num_objects` `hero_set_num_objects` `hero_add_quick_select` `hero_remove_quick_select`
`hero_lockout_control` `hero_set_grind_speed` `hero_get_equipped` `hero_exit_turret`
`hero_set_soaker_liquid` `hero_get_soaker_liquid` `hero_is_targeted` `hero_swingshot_fall`
`hero_set_swingshot` `ratchet_force_stand` `hero_force_index_cam` `ratchet_can_use_aphelion`
`set_moving_surface_cam` `hero_custom_prompt` `hero_look_at_targets` `hero_look_at_moby`
`hero_look_at_weapon_target` `num_gold_bolts_collected` `num_prized_junk_collected`
`level_num_zoni_collected` `global_num_zoni_collected` `set_global_zoni_collected`
`add_global_zoni_collected` `zoni_set_forced_idle` `zoni_resurrect` `zoni_vessel_restore`
`num_collected_ryno` `is_cranking` `is_on_grav_surface` `is_on_gel_cube` `is_using_jump_pad`
`is_grinding` `is_in_jump_slot` `is_charging` `is_zipping` `is_ledge_grabbing` `is_grappling`
`is_grabbing` `is_hoverbooting` `is_hoverboot_thrusting` `set_halfpipe` `set_jump_duncan_hack`
`set_force_thrust` `set_hoverboot_cam_offset` `set_hoverboot_cam_offset_defaults` `set_hoverboots`
`hoverboot_band` `set_glide_pitch` `reset_glide_pitch` `hero_force_idle_on_rail`
`hero_has_wrench_pickup` `is_weapon_type_used_unique` `reset_weapon_types_used`
`hero_force_underwater_fx` `get_grapple_target` `get_grapple_progress` `ratchet_add_clankpack`
`ratchet_remove_clankpack` `is_six_axis_active` `is_lock_strafe_active` `ratchet_fling`
`set_hoverboot_max` `set_hoverboot_max_thrust` `get_nearby_target_count`
`ratchet_activate_nefarious_disguise` `ratchet_deactivate_nefarious_disguise`
`ratchet_arm_nefarious_disguise` `ratchet_nefarious_disguise_has_punished`
`ratchet_set_lawrence_hologram_pod` `is_pollyx_present` `ratchet_wear_oxygen_mask`
`nefarious_trigger_scanned` `hero_ship_fly_in` `hero_ship_fly_out` `hero_ship_add_enter_cine`
`hero_ship_add_enter_movie` `hero_ship_add_exit_cine` `hero_ship_add_exit_movie` `head_lookat`
`head_lookat_stop` `spawn_ammo` `spawn_health` `spawn_bolts` `spawn_powerup` `spawn_weapon`
`force_pickup_bolts` `force_abandon_pickups` `spawner_spawn` `spawner_clear` `mobyspawner_spawn`
`mobyspawner_clear` `spawn_lattice_hazard` `spawn_hydra_tank` `spawn_toreshi_war_grok`
`boss_register_cam` `boss_unregister_cam` `vx99_follow_path` `vx99_go_to_volume` `vx99_stop_moving`
`vx99_open_flaps` `vx99_attack_path` `vx99_set_fling_data` `vx99_get_bolt_status`
`vx99_set_bolt_status` `vx99_lower_turret` `vx99_start_top_attack` `vx99_pull_dome`
`vx99_start_turret_attack` `vx99_arm_ambient_attack` `vx99_set_bob_active` `nefarious_laser_create`
`nefarious_laser_stop` `nefarious_laser_set_eye_target` `nefarious_laser_bulb_charge`
`nefarious_laser_start_chase` `nefarious_laser_stop_chase` `nefarious_laser_zot`
`nefarious_go_to_volume` `nefarious_follow_path` `nefarious_resume_behavior`
`nefarious_script_done` `nefarious_monologue` `nefarious_follow_lawrence` `nefarious_set_speed`
`nefarious_start_taunting` `nefarious_spawn_swarmers` `lawrence_go_to_volume`
`lawrence_follow_path` `lawrence_set_attacking` `lawrence_stop_moving` `lawrence_set_speed`
`skydive_init` `skydive_spawn_missile` `skydive_spawn_radial_mine` `npc_set_speed` `npc_goto`
`npc_patrol` `npc_stop` `npc_icon_on` `npc_icon_off` `npc_interact` `npc_set_target`
`npc_clear_targets` `npc_start_attack` `npc_stop_attack` `npc_enable_damage` `npc_disable_damage`
`npc_scale_damage` `sigmund_set_clone_pad` `sigmund_set_grounded` `sigmund_use_keypad`
`sigmund_stop_keypad` `sigmund_holo_intro` `sigmund_holo_outro` `sigmund_harrass`
`npc_vorselon_set_visible` `npc_set_face_hero` `npc_set_face_moby` `npc_clear_face_moby`
`npc_holo_down` `npc_holo_up` `npc_cheer` `npc_go_idle` `npc_idle_dialogue_global_enable`
`npc_idle_dialogue_enable` `npc_get_attention` `prompt_activate` `prompt_deactivate` `prompt_check`
`prompt_clear` `prompt_set_tag` `prompt_set_debug_text` `prompt_set_distance` `prompt_set_volume`
`confirm_activate` `confirm_deactivate` `confirm_clear` `confirm_set_tag` `confirm_set_debug_text`
`confirm_set_distance` `make_actor` `revert_actor` `actor_speak` `actor_is_speaking`
`actor_set_color` `actor_set_stance` `actor_set_gesture` `actor_move` `actor_stop_moving`
`actor_is_moving` `actor_play_anim` `actor_set_idle_face_hero` `scene_show_menu_internal`
`scene_is_menu_waiting` `scene_get_menu_choice_num` `scene_set_title` `scene_clear_title`
`scene_show_subtitle` `scene_debug_subtitle` `scene_hide_subtitle` `scene_set_light`
`awareness_add_targettype` `awareness_remove_targettype` `awareness_clear_targettypes`
`target_set_targetable` `health_set_invulnerability` `enemy_spawn` `trooper_teleport`
`enemy_wakeup` `enemy_is_awake` `enemy_any_awake` `enemy_get_health_abs` `enemy_get_health`
`enemy_set_health` `enemy_set_intro_path` `enemy_set_patrol_path` `enemy_set_inner_awareness`
`enemy_set_outer_awareness` `enemy_clear_firepoint_controller` `enemy_clear_firepoints`
`enemy_set_firepoint_controller` `enemy_set_firepoints` `enemy_set_ambient_attack_scent`
`enemy_kill_below_object` `enemy_set_volume_target` `enemy_clear_volume_target` `enemy_go_idle`
`cassy_exit_arena` `cassy_return_arena` `cassy_go_to_volume` `cassy_follow_path`
`cassy_start_fighting` `cassy_play_idle` `hydra_tank_shoot_at_volume` `hydra_tank_heads_alive`
`hydra_tank_shield_took_damage` `hydra_tank_kill` `swarm_setup_chase_cam` `swarm_cam_reset`
`swarm_set_override_target` `swarm_targeted_ambrosia` `swarm_took_non_ambrosia_damage`
`swarm_force_retreat` `swarm_is_targeting_ratchet` `swarm_is_targeting_ambrosia`
`swarm_is_retreating` `swarm_is_hiding` `nef_swarmer_fill_template` `nef_trooper_spawn_swarmers`
`zoni_path_follow` `zoni_set_stop` `zoni_clear_stop` `agorian_set_rush_dists`
`agorian_set_shots_per_volley` `battlefieldcontroller_addtarget`
`battlefieldcontroller_updatetarget` `battlefieldcontroller_removetarget`
`battlefieldcontroller_print` `reset_flares` `azimuth_should_use_nav` `ally_getcurrentstate`
`ally_setattitude` `ally_settarget` `ally_addtarget` `ally_addautotargetvolume`
`ally_settargetvolume` `ally_removetarget` `ally_clearautotargetvolume` `ally_cleartargets`
`ally_settargetpreference` `ally_setawareness` `ally_setfirepoints` `ally_clearfirepoints`
`ally_followpath` `ally_followpathhover` `ally_gotopoint` `ally_gotopointhover` `ally_skydive`
`ally_hover_takeoff` `ally_hover_land` `ally_hackconsole` `ally_bombingrun` `ally_specialidle`
`ally_trap_in_forcefield` `ally_setdamageamount` `ally_setaccuracy` `ally_setcollisionignore`
`ally_equipitem` `ally_unequipitem` `ally_debug` `ally_cancel_script_command`
`ally_set_hover_speed_scale` `ally_ranged_only` `ally_set_speed` `ally_interact`
`ally_set_home_area` `ally_clear_home_area` `azimuth_throw_bomb` `azimuth_throw_flare`
`azimuth_backstab` `azimuth_setup_deathblows` `azimuth_get_deathblows_left` `azimuth_start_grind`
`azimuth_end_grind` `azimuth_set_chase_distance` `queue_ally_set_cancel_queue_conditions`
`queue_ally_set_attitude` `queue_ally_settarget` `queue_ally_addtarget`
`queue_ally_addautotargetvolume` `queue_ally_removetarget` `queue_ally_clearautotargetvolume`
`queue_ally_cleartargets` `queue_ally_settargetpreference` `queue_ally_followpath`
`queue_ally_followpathhover` `queue_ally_gotopoint` `queue_ally_gotopointhover`
`queue_ally_hover_takeoff` `queue_ally_hover_land` `queue_ally_timed_idle`
`queue_azimuth_throw_bomb` `ally_set_face_moby` `ally_clear_face_moby` `ally_cheer`
`is_fix_time_game_complete` `ftg_set_incomplete` `ftg_spawn_anomaly` `ftg_spawn_seeker`
`ftg_spawn_energy` `ftg_spawn_bomb` `ftg_spawn_timebomb` `ftg_goto_anomaly` `ftg_goto_seeker`
`ftg_goto_position` `ftg_show_message` `ftg_upgrade_anomaly` `ftg_set_seeker_target`
`ftg_move_anomaly` `ftg_move_seeker` `ftg_move_powerup` `ftg_start_firing` `ftg_stop_firing`
`ftg_tutorial_done` `add_scaleform_sound_spec` `add_scaleform_rumble_spec`
`add_scaleform_dialog_spec` `show_upgrade_screen` `get_num_dancing_in_pod` `has_teleported`
`set_arcade_game` `platform_set_water` `smoky_reveal` `get_player_deaths_on_level`
`delete_enemies_except` `open_smasher` `close_smasher` `restart_smasher` `open_saw` `close_saw`
`restart_saw` `flame_jet_on` `flame_jet_off` `flame_jet_restart` `spawn_pickup_bomb`
`attach_battery_bot` `detach_battery_bot` `get_attached_battery_bot` `is_moby_slowed`
`was_time_bomb_canceled` `add_timeslow_volume` `remove_timeslow_volume` `restore_destroyed_state`
`fixable_fix` `fixable_break` `fixable_is_fixed` `fixable_is_broken` `deactivate_seeker_mines`
`random_dialogue_clear` `random_dialogue_add` `random_dialogue_remove` `set_dialogue_threshold`
`setup_security_module` `add_security_module_pattern` `security_module_bypass`
`restore_orvus_chamber` `hydroplant_restore` `azimuth_glass_shatter` `set_effect_time_scale`
`taxi_warp` `planter_set_planted` `clear_all_projectiles` `vorselon_hazard_hack`
`gear_hazard_set_speed` `get_clank` `switch_from_clank` `switch_to_clank` `clank_equip_staff`
`clank_remove_staff` `clank_equip_reflect` `clank_remove_reflect` `clank_equip_timebomb`
`clank_remove_timebomb` `clank_is_fixing_time` `clank_is_recording` `clank_stop_recording`
`clone_pad_has_clone` `clone_pad_get_count` `clone_pad_set_reset_pod` `get_total_clone_count`
`clone_pad_delete_one` `clone_pad_delete_all` `clone_pad_enable_sigmund`
`clone_pad_disable_sigmund` `clone_pad_set_sigmund_idle` `clone_pad_set_sigmund_preview`
`clone_pad_set_sigmund_hidden` `clone_pad_set_sigmund_path` `clank_collected_staff`
`is_clone_in_volume` `clank_station_warp` `clank_enable_puzzle_help` `clank_disable_puzzle_help`
`clank_is_puzzle_help_enabled` `clank_root` `clank_unroot` `break_time_ribbon`
`qwark_door_enable_break` `qwark_door_set_broken` `qwark_door_manual_break`
`qwark_activate_nurse_disguise` `qwark_deactivate_nurse_disguise` `qwark_start_smashers`
`qwark_push_smashers` `qwark_cancel_smashers` `qwark_startled_goto` `qwark_set_roam_radius`
`qwark_setup_deathblows` `qwark_get_deathblows_left` `hud_start_count_timer` `hud_start_timer`
`hud_start_counter` `hud_set_count` `hud_set_time` `hud_set_max` `hud_set_tag`
`hud_stop_timer_count` `start_item_bar` `stop_item_bar` `set_item_bar_amount` `force_item_bar`
`set_item_banner_amount` `is_hud_timer_done` `set_item_bar_total` `time_since_map_activated`
`number_times_map_activated` `change_hud_colour` `scripted_qs` `hud_start_overlay`
`hud_stop_overlay` `hud_start_nef_legend` `hud_stop_nef_legend` `set_sky` `fade_to_black`
`fade_from_black` `fade_to_white` `fade_from_white` `letter_box_on` `letter_box_off`
`space_npc_set_scene` `space_land_ship` `space_set_cam` `space_enable_moon` `space_enable_planet`
`space_set_moon_tag` `space_set_planet_tag` `space_enable_takeoff` `space_set_shield_level`
`space_set_afterburner_level` `space_give_weapon` `space_has_weapon` `space_has_comet_core`
`space_set_objective` `space_set_shield_type` `space_enable_barrier` `space_get_zoni_for_upgrade`
`space_unlock_level` `space_is_aware` `space_set_indestructible` `space_grab_junk`
`space_match_aphelions` `space_num_ships_destroyed` `space_reset_destroyed_ships`
`space_lock_ship_counter` `space_drop_tether` `space_is_towing` `space_is_boosting`
`space_make_attacker` `space_is_attacking` `space_did_action` `space_is_in_ship`
`space_set_radio_channel` `space_get_radio_channel` `space_show_radio` `space_cinematic_death`
`space_disable_pip` `space_enable_channel_change` `is_collected` `enable_zone_rendering`
`disable_zone_rendering` `time_portal_set_target_volume` `time_portal_clear_target_volume`
`time_portal_set_id` `blob_scripted_combine` `procgen_init` `show_menu` `addVol` `delete_vol`
`move_volume` `procPrint` `getProcSeed` `create_path` `set_path_point` `finish_path`
`create_railpath` `set_railpoint` `finish_grindrail` `spawn_moby` `spawn_floor` `spawn_blink`
`spawn_wall` `spawn_wlat` `spawn_flame` `spawn_simple_flame` `spawn_moving` `spawn_grindrail`
`spawn_zipline` `spawn_hook_shot` `spawn_swing_shot` `spawn_breakable_crate` `spawn_ammo_crate`
`spawn_health_crate` `spawn_tilefloor` `spawn_tilewall` `spawn_firespin` `spawn_agorian_warrior`
`getConfig` `new` `invalid` `get_first` `get_next` `x` `y` `z` `tty_out` `screen_out` `error`
`set_update_delay` `set_cinematic_update_mode` `is_in_gameplay` `run_script` `is_script_running`
`get_time_step` `get_current_time` `is_valid` `pfx_create_node` `pfx_delete_node`
`pfx_set_node_lifetime` `pfx_set_node_intensity` `pfx_set_node_saturation`
`pfx_set_node_brightness` `pfx_set_node_bloom` `pfx_set_node_blur` `pfx_set_node_tint`
`pfx_set_node_fog` `pfx_begin_decay` `is_inside` `pick_rand` `get_volume` `get_area_size`
`is_in_view` `is_view_thru_volume` `get_pos` `create_new_area` `delete` `add_to_area`
`remove_from_area` `get_pos_along_path` `get_length` `get_dist` `rand` `randint` `get_num_in_pod`
`get_pod_size` `get_closest` `get_num_inside` `add_to_pod` `remove_from_pod` `get_moby` `hide`
`reveal` `scale` `get_clue` `get_scent_size` `is_drawn` `is_hidden` `set_pos` `tint`
`trigger_effect_event` `trigger_tracked_effect_event` `stop_tracked_effect_event`
`play_explicit_visual_spec` `play_explicit_sound_spec` `play_explicit_dialog_spec` `is_sound_done`
`stop_sound`
