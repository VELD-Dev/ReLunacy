namespace ReLunacy.Engine.Scripting;

/// <summary>What a function's string argument names.</summary>
public enum LuaScriptNameKind
{
    /// <summary>Nothing known - an ordinary string.</summary>
    None,
    /// <summary>A script in the current level's own <c>scripts/</c> folder.</summary>
    LevelScript,
    /// <summary>A script under <c>/scripts/</c>, shared by every level.</summary>
    GameScript,
}

/// <summary>
/// Which functions take a script's name as a string, and in which argument.
///
/// A table in code rather than a tag in the documentation, because what a string NAMES is a
/// different kind of fact from its type: the annotation format has no way to say "this string is a
/// file in that folder", and inventing one would mean a doc syntax nothing else understands. The
/// documentation says these take a string and what it means in prose; this says where to get the
/// candidates.
///
/// Kept small on purpose. Every entry has to be backed by call sites that agree on the shape, and
/// a wrong entry is worse than a missing one - it offers names that cannot be right. <c>load_config</c>
/// is deliberately absent: it has three call sites, takes two arguments, and the second looks like
/// a level name on the strength of one value appearing twice. That is not enough to act on.
/// </summary>
public static class LuaStringArguments
{
    private static readonly Dictionary<(string Function, int Argument), LuaScriptNameKind> Known = new()
    {
        [("load_level_script", 0)] = LuaScriptNameKind.LevelScript,
        [("load_global_script", 0)] = LuaScriptNameKind.GameScript,
    };

    /// <summary>What the given argument of the given function names, or None.</summary>
    public static LuaScriptNameKind KindFor(string function, int argument) =>
        Known.GetValueOrDefault((function, argument), LuaScriptNameKind.None);

    /// <summary>Every function that has an argument like this, for a probe to enumerate.</summary>
    public static IEnumerable<(string Function, int Argument, LuaScriptNameKind Kind)> All =>
        Known.Select(e => (e.Key.Function, e.Key.Argument, e.Value));
}
