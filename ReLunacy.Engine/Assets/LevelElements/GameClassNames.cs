using System.Collections.Concurrent;
using System.Reflection;
using ReLunacy.Engine.Games;

namespace ReLunacy.Engine.Assets.LevelElements;

/// <summary>
/// The new engine's class number -> class name, per game, where the game's EBOOT gave it: All 4 One and
/// QForce, from the native enum registrations Fianshoo's ReLunacy Resurgence recovered (its
/// Resources/All4One/All4One.h and FullFrontalAssault/FullFrontalAssault.h, extracted to
/// Resources/Classes/*.classes). The numbers are each game's own: 0x85B is ClueNoLedgeGrab on A Crack in
/// Time and ProjectileBatholithWeb on All 4 One, so a table only ever names its own game's classes.
/// </summary>
public static class GameClassNames
{
    private static readonly ConcurrentDictionary<GameId, NamedNumbers> Cache = new();

    private static NamedNumbers Table(GameId game) => Cache.GetOrAdd(game, static id => new NamedNumbers($"{id}.classes"));

    /// <summary>The class's name without "_CLASS" (ClueFallToDeath, CamArgBlendable), or null when the game has no
    /// table or the table does not have it.</summary>
    public static string? Name(GameId? game, uint classId) => game is { } id ? Table(id).Name(classId) : null;

    /// <summary>Names a class in the game's table and writes the file (see <see cref="ClassDataFiles"/>).</summary>
    public static void SetName(GameId game, uint classId, string name) => Table(game).Set(classId, name);
}
