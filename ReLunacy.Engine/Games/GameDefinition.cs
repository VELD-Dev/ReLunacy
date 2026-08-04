namespace ReLunacy.Engine.Games;

public enum GameId
{
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
    public static readonly IReadOnlyList<GameDefinition> All =
    [
        new(GameId.ToolsOfDestruction, "Ratchet & Clank: Tools of Destruction", IsOldEngine: true, KnownLevels: ["apogee space station", "cobalia", "cragmite ruins", "fastoon", "fastoon_return", "imperial fight fest", "iris", "kerchu city", "level_transitions", "meridian city", "metropolis", "pirate base", "rykan v", "sargasso", "slags_fleet", "space combat i", "space combat ii", "space combat iii", "stratus city", "zordoom prison"]),
        new(GameId.QuestForBooty, "Ratchet & Clank: Quest for Booty", IsOldEngine: true, KnownLevels: ["level_transitions", "npc_island", "prologue", "treasure_island", "viper_caverns"]),
        new(GameId.ACrackInTime, "Ratchet & Clank: A Crack in Time", IsOldEngine: false, KnownLevels: []),
        new(GameId.FullFrontalAssault, "Ratchet & Clank: Full Frontal Assault", IsOldEngine: false, KnownLevels: []),
        new(GameId.All4One, "Ratchet & Clank: All 4 One", IsOldEngine: false, KnownLevels: []),
        new(GameId.IntoTheNexus, "Ratchet & Clank: Into the Nexus", IsOldEngine: false, KnownLevels: []),
    ];
}
