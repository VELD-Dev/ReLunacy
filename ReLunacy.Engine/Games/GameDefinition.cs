namespace ReLunacy.Engine.Games;

public enum GameId
{
    RFallOfMan,
    ToolsOfDestruction,
    QuestForBooty,
    ACrackInTime,
    FullFrontalAssault,
    All4One,
    IntoTheNexus,
}

public sealed record GameDefinition(GameId Id, string DisplayName, bool IsOldEngine, IReadOnlyList<string> KnownLevels);

// TODO: Add all Ratchet & Clank level names
// TODO: Add Resistance games and their levels
public static class GameDefinitions
{
    /// <summary>The game whose <see cref="GameDefinition.KnownLevels"/> lists the level at
    /// <paramref name="levelPath"/> (a level folder, or an archive inside one), or null when no
    /// game knows it. Some rendering paths differ per game (A Crack in Time's data layout, which
    /// EBOOT's shaders a level is drawn with).</summary>
    public static GameDefinition? FromLevelPath(string? levelPath)
    {
        if (string.IsNullOrWhiteSpace(levelPath)) return null;
        string name = GameLibraryScanner.GetLevelNameFromPath(levelPath);
        return All.FirstOrDefault(game => game.KnownLevels.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    public static readonly IReadOnlyList<GameDefinition> All =
    [
        new(GameId.ToolsOfDestruction, "Ratchet & Clank: Tools of Destruction", IsOldEngine: true, KnownLevels: ["apogee space station", "cobalia", "cragmite ruins", "fastoon", "fastoon_return", "imperial fight fest", "iris", "kerchu city", "level_transitions", "meridian city", "metropolis", "pirate base", "rykan v", "sargasso", "slags_fleet", "space combat i", "space combat ii", "space combat iii", "stratus city", "zordoom prison"]),
        new(GameId.QuestForBooty, "Ratchet & Clank: Quest for Booty", IsOldEngine: true, KnownLevels: ["level_transitions", "npc_island", "prologue", "treasure_island", "viper_caverns"]),
        new(GameId.ACrackInTime, "Ratchet & Clank: A Crack in Time", IsOldEngine: false, KnownLevels: ["agorian_arena", "axiom_city", "front_end", "galacton_ship", "gimlick_valley", "great_clock_a", "great_clock_b", "great_clock_c", "great_clock_d", "great_clock_e", "insomniac_museum", "krell_canyon", "molonoth", "nefarious_station", "space_sector_1", "space_sector_2", "space_sector_3", "space_sector_4", "space_sector_5", "tombli", "valkyrie_fleet", "zolar_forest"]),
        new(GameId.FullFrontalAssault, "Ratchet & Clank: Full Frontal Assault", IsOldEngine: false, KnownLevels: ["acid_refinery_pre", "acid_refinery_pvp", "jungle_ruins_boss", "jungle_ruins_pre", "jungle_ruins_post", "jungle_ruins_pvp", "lobby", "phoenix_ph1", "phoenix_ph2", "phoenix_ph3", "plasma_harvester_pre", "plasma_harvester_pvp_pre", "pvp_dlc_0", "pvp_dlc_1", "pvp_dlc_3"]),
        new(GameId.All4One, "Ratchet & Clank: All 4 One", IsOldEngine: false, KnownLevels: ["creature_collector", "crumbler", "lobby", "luminopolis", "nest", "phonica_moon", "polar_sea", "rootworm", "seaside", "terawatt"]),
        new(GameId.IntoTheNexus, "Ratchet & Clank: Into the Nexus", IsOldEngine: false, KnownLevels: ["battle_arena", "clank_puzzles", "haunted_city", "lobby", "meridian_city", "prison_transport", "swamp_wasteland", "vendra_homeworld"]),
        new(GameId.RFallOfMan, "Resistance Fall Of Man", IsOldEngine: true, KnownLevels: ["level20", "level21", "level22", "level30", "level31", "level32", "level40", "level41", "level42", "level50", "level51", "level52"])
    ];
}
