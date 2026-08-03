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

// Level names are intentionally empty for now — fill in KnownLevels per game to enable
// GameLibraryScanner's game-identification match. Detection/browsing itself works without
// them (any folder or archive that looks like a level is still found), it just can't yet
// tell you WHICH of the 6 games a USRDIR belongs to.
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
