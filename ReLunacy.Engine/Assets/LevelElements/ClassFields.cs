using System.Collections.Concurrent;
using System.Reflection;
using ReLunacy.Engine.Games;

namespace ReLunacy.Engine.Assets.LevelElements;

/// <summary>How a settings field is stored and whether the level decides it.</summary>
public enum ClassFieldKind
{
    U8, U16, U32, S32, F32,
    /// <summary>A reference to another instance (four bytes).</summary>
    Reference,
    /// <summary>An entry of the game's localised text table (u32).</summary>
    TextId,
    /// <summary>An embedded object: the field is its first byte, <see cref="ClassField.Length"/> its size.</summary>
    Component,
}

/// <summary>One field of a class's settings struct.</summary>
/// <param name="Offset">Offset in the struct, which starts with the class number.</param>
/// <param name="Length">Bytes the field covers (a component's or a run-time range's whole size).</param>
/// <param name="RunTime">The game overwrites the field when the level starts: what the file holds is not used, so the
/// editor shows it read-only.</param>
/// <param name="CodeOnly">Known only from the class's code touching it, zero in every retail file: run-time state, or
/// a setting no level uses. Not locked, because which of the two is not known.</param>
/// <param name="Name">A short name, or empty when only the description says what it is.</param>
/// <param name="Evidence">Letters of docs/reverse/acit_space_sector_classes.md: E EBOOT, D data, T name tables, Q
/// QForce, S scripts, U tested in game.</param>
public sealed record ClassField(uint Owner, int Offset, int Length, ClassFieldKind Kind, bool RunTime, bool CodeOnly,
    string Name, string Description, string Evidence);

/// <summary>
/// The settings fields of a game's classes, where they are known: A Crack in Time's, for the classes placed in its
/// space sectors and their bases (Resources/Classes/ACrackInTime.fields, generated from the reverse-engineering notes
/// behind docs/reverse/acit_space_sector_classes.md). A field belongs to the class that declares it and is inherited
/// by every class below, so the table carries each class's parent and works without the game's EBOOT.
/// </summary>
public static class ClassFields
{
    private sealed class Table
    {
        public readonly Dictionary<uint, int> Parents = [];
        public readonly Dictionary<uint, string> Names = [];
        public readonly Dictionary<uint, List<ClassField>> Own = [];
        public readonly ConcurrentDictionary<uint, IReadOnlyList<ClassField>> All = new();
    }

    private static readonly ConcurrentDictionary<GameId, Table> Cache = new();

    /// <summary>Every known field of <paramref name="cls"/>, its ancestors' included, by offset; empty for a class or
    /// a game without a table.</summary>
    public static IReadOnlyList<ClassField> Of(GameId? game, uint cls)
    {
        if (game is not { } id) return [];
        var table = Load(id);
        return table.All.GetOrAdd(cls, c =>
        {
            var fields = new List<ClassField>();
            var seen = new HashSet<uint>();
            for (uint current = c; seen.Add(current);)
            {
                if (table.Own.TryGetValue(current, out var own)) fields.AddRange(own);
                if (!table.Parents.TryGetValue(current, out int parent) || parent < 0) break;
                current = (uint)parent;
            }
            // The nearest class wins an offset two of them name (a derived class's first field under its parent's
            // rounded-up size).
            return fields.GroupBy(f => f.Offset).Select(g => g.First()).OrderBy(f => f.Offset).ToList();
        });
    }

    /// <summary>The field that starts at <paramref name="offset"/>, when known.</summary>
    public static ClassField? At(GameId? game, uint cls, int offset)
    {
        foreach (var field in Of(game, cls))
            if (field.Offset == offset) return field;
        return null;
    }

    /// <summary>The class's name in the table (All 4 One's for the same number, or a description from its
    /// instances), or null.</summary>
    public static string? ClassName(GameId? game, uint cls) =>
        game is { } id && Load(id).Names.TryGetValue(cls, out var name) && name.Length > 0 ? name : null;

    /// <summary>Whether <paramref name="cls"/> is <paramref name="ancestor"/> or derives from it, by the table.</summary>
    public static bool IsA(GameId? game, uint cls, uint ancestor)
    {
        if (game is not { } id) return false;
        var table = Load(id);
        var seen = new HashSet<uint>();
        for (uint current = cls; seen.Add(current);)
        {
            if (current == ancestor) return true;
            if (!table.Parents.TryGetValue(current, out int parent) || parent < 0) return false;
            current = (uint)parent;
        }
        return false;
    }

    private static Table Load(GameId game) => Cache.GetOrAdd(game, static id =>
    {
        var table = new Table();
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"ReLunacy.Engine.Resources.Classes.{id}.fields");
        if (stream == null) return table;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] p = line.Split('\t');
            if (p[0] == "C" && p.Length >= 5 && uint.TryParse(p[1], out uint cls) && int.TryParse(p[2], out int parent))
            {
                table.Parents[cls] = parent;
                table.Names[cls] = p[3];
            }
            else if (p[0] == "F" && p.Length >= 8 && uint.TryParse(p[1], out uint owner) && int.TryParse(p[2], out int offset)
                     && int.TryParse(p[4], out int length) && Parse(p[3]) is { } kind)
            {
                if (!table.Own.TryGetValue(owner, out var list)) table.Own[owner] = list = [];
                list.Add(new ClassField(owner, offset, length, kind.Kind, kind.RunTime, kind.CodeOnly, p[6], p[7], p[5]));
            }
        }
        return table;
    });

    private static (ClassFieldKind Kind, bool RunTime, bool CodeOnly)? Parse(string type) => type switch
    {
        "u8" => (ClassFieldKind.U8, false, false),
        "u16" => (ClassFieldKind.U16, false, false),
        "u32" => (ClassFieldKind.U32, false, false),
        "s32" => (ClassFieldKind.S32, false, false),
        "f32" => (ClassFieldKind.F32, false, false),
        "ref" => (ClassFieldKind.Reference, false, false),
        "text" => (ClassFieldKind.TextId, false, false),
        "comp" => (ClassFieldKind.Component, false, false),
        "rt" => (ClassFieldKind.U32, true, false),
        "rt8" => (ClassFieldKind.U8, true, false),
        "rt16" => (ClassFieldKind.U16, true, false),
        "rtf" => (ClassFieldKind.F32, true, false),
        "code:u8" => (ClassFieldKind.U8, false, true),
        "code:u16" => (ClassFieldKind.U16, false, true),
        "code:u32" => (ClassFieldKind.U32, false, true),
        "code:f32" => (ClassFieldKind.F32, false, true),
        _ => null,
    };
}
