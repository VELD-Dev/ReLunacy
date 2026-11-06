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
        var owners = All.Where(game => game.KnownLevels.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (owners.Count <= 1) return owners.FirstOrDefault();
        // Two names are shared: "lobby" by Full Frontal Assault, All 4 One and Into the Nexus, and
        // "level_transitions" by Tools of Destruction and Quest for Booty. Taking the first listed
        // owner identified every A4O and ITN lobby as FFA - silently, and not only here: this is what
        // picks a level's EBOOT shader table and the ACiT data layout. The folder the level sits in
        // settles it, scored the same way a whole library is.
        var siblings = GameLibraryScanner.SiblingLevelNames(levelPath);
        return GameLibraryScanner.MatchGameAmong(owners, siblings) ?? owners[0];
    }

    /// <summary>
    /// GUESS, not a decoded constant. The gain the new-engine cubemap reflection is scaled by for a
    /// given game, which Render &gt; Reflection intensity starts at when one of its levels loads.
    ///
    /// All 4 One reads 0.04 BY EYE (VELD, 2026-11-06): at the 1.0 the editor used before, its
    /// reflections are far hotter than the retail game's, and this order of magnitude is what matches
    /// it. Nothing is claimed about the exact figure - it was found by comparing screenshots, not read
    /// out of the game, so treat the slider's value as the current best candidate rather than a result.
    ///
    /// Every other game keeps 1.0, which is not a measurement either - it is just the editor's old
    /// behaviour, left alone because no one has compared them.
    ///
    /// The real constant is still to be found. The reflection is already multiplied by the material's
    /// +0x34 strength and the 0x8B00 reflection occlusion (uReflOcclusion*, region.dat +0x90/+0x94),
    /// both decoded, so a missing per-game factor of ~25 is most likely a view- or region-level
    /// constant the atmosphere/lighting block carries, or a fragment-constant group the A4O EBOOT
    /// patches that QForce's does not. Candidates worth reading before trusting this number:
    /// region.dat 0x34000's unread fields, and A4O's equivalent of the QForce reflection upload at
    /// 0x0079DE48. If one turns up, delete this and read it per region.
    /// </summary>
    public static float DefaultReflectionIntensityGuess(GameId? game) => game switch
    {
        GameId.All4One => 0.04f,
        _ => 1f,
    };

    public static readonly IReadOnlyList<GameDefinition> All =
    [
        new(GameId.ToolsOfDestruction, "Ratchet & Clank: Tools of Destruction", IsOldEngine: true, KnownLevels: ["apogee space station", "cobalia", "cragmite ruins", "fastoon", "fastoon_return", "imperial fight fest", "iris", "kerchu city", "level_transitions", "meridian city", "metropolis", "pirate base", "rykan v", "sargasso", "slags_fleet", "space combat i", "space combat ii", "space combat iii", "stratus city", "zordoom prison"]),
        new(GameId.QuestForBooty, "Ratchet & Clank: Quest for Booty", IsOldEngine: true, KnownLevels: ["level_transitions", "npc_island", "prologue", "treasure_island", "viper_caverns"]),
        new(GameId.ACrackInTime, "Ratchet & Clank: A Crack in Time", IsOldEngine: false, KnownLevels: ["agorian_arena", "axiom_city", "front_end", "galacton_ship", "gimlick_valley", "great_clock_a", "great_clock_b", "great_clock_c", "great_clock_d", "great_clock_e", "insomniac_museum", "krell_canyon", "molonoth", "nefarious_station", "space_sector_1", "space_sector_2", "space_sector_3", "space_sector_4", "space_sector_5", "tombli", "valkyrie_fleet", "zolar_forest"]),
        new(GameId.FullFrontalAssault, "Ratchet & Clank: Full Frontal Assault", IsOldEngine: false, KnownLevels: ["acid_refinery_pre", "acid_refinery_pvp", "jungle_ruins_boss", "jungle_ruins_pre", "jungle_ruins_post", "jungle_ruins_pvp", "lobby", "phoenix_ph1", "phoenix_ph2", "phoenix_ph3", "plasma_harvester_pre", "plasma_harvester_pvp_pre", "pvp_dlc_0", "pvp_dlc_1", "pvp_dlc_2", "pvp_dlc_3"]),
        new(GameId.All4One, "Ratchet & Clank: All 4 One", IsOldEngine: false, KnownLevels: ["creature_collector", "crumbler", "lobby", "luminopolis", "nest", "phonica_moon", "polar_sea", "rootworm", "seaside", "terawatt"]),
        new(GameId.IntoTheNexus, "Ratchet & Clank: Into the Nexus", IsOldEngine: false, KnownLevels: ["battle_arena", "clank_puzzles", "haunted_city", "lobby", "meridian_city", "prison_transport", "swamp_wasteland", "vendra_homeworld"]),
        new(GameId.RFallOfMan, "Resistance Fall Of Man", IsOldEngine: true, KnownLevels: ["level20", "level21", "level22", "level30", "level31", "level32", "level40", "level41", "level42", "level50", "level51", "level52"])
    ];
}
