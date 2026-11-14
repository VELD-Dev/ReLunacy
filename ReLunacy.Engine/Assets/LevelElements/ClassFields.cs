using System.Globalization;
using System.Reflection;
using System.Text;
using ReLunacy.Engine.Games;

namespace ReLunacy.Engine.Assets.LevelElements;

/// <summary>How a settings field is stored.</summary>
public enum ClassFieldKind
{
    U8, U16, U32, S32, F32,
    /// <summary>A tick box: one byte ("bool") or four ("bool32"), zero or not.</summary>
    Bool,
    /// <summary>A reference to another instance (four bytes); <see cref="ClassField.TypeName"/> says of which kind.</summary>
    Reference,
    /// <summary>An entry of the game's localised text table (u32).</summary>
    TextId,
    /// <summary>One of a named list of values, or a set of bit flags: <see cref="ClassField.TypeName"/> names the list.</summary>
    Enum,
    /// <summary>An embedded object: the field is its first byte, <see cref="ClassField.Length"/> its size.</summary>
    Component,
}

/// <summary>One documented field of a class's settings.</summary>
/// <param name="Owner">The class that declares it; every class below inherits it.</param>
/// <param name="Offset">Offset in the settings, which start with the class number.</param>
/// <param name="Length">Bytes the field covers.</param>
/// <param name="RunTime">The game overwrites the field when the level starts, so the editor shows it read-only.</param>
/// <param name="TypeName">For a reference the kind it must name (Moby, Path, Volume, Clue, Controller, Scent, Area,
/// Pod; null for any); for an enum the name of its <see cref="ClassEnum"/>.</param>
public sealed record ClassField(uint Owner, int Offset, int Length, ClassFieldKind Kind, bool RunTime,
    string Name, string Description, string? TypeName = null)
{
    /// <summary>The type as the .fields file spells it.</summary>
    public string TypeText => Kind switch
    {
        ClassFieldKind.U8 => "u8",
        ClassFieldKind.U16 => "u16",
        ClassFieldKind.U32 => "u32",
        ClassFieldKind.S32 => "s32",
        ClassFieldKind.F32 => "float",
        ClassFieldKind.Bool => Length == 1 ? "bool" : "bool32",
        ClassFieldKind.TextId => "text",
        ClassFieldKind.Reference => TypeName ?? "ref",
        ClassFieldKind.Enum => TypeName ?? "u32",
        _ => $"component(0x{Length:X})",
    };
}

/// <summary>A named list of values a field can hold (a destination level, a spawn rule), or of bit flags.</summary>
public sealed class ClassEnum
{
    public required string Name { get; set; }
    /// <summary>The entries are bits that combine, not exclusive values.</summary>
    public bool Flags { get; set; }
    /// <summary>Bytes a field of this type takes: 4 unless the file says u8 or u16.</summary>
    public int Size { get; set; } = 4;
    public List<(string Name, long Value)> Entries { get; set; } = [];

    public ClassEnum Clone() => new() { Name = Name, Flags = Flags, Size = Size, Entries = [.. Entries] };
}

/// <summary>
/// Where the hand-edited class data lives (Resources/Classes: &lt;Game&gt;.fields, .classes, .textids) and how it is
/// read and written. These files are the project's shared research, so the editor changes the file itself: when it
/// runs from a checkout of the sources that is the file under ReLunacy.Engine/Resources/Classes, ready to commit;
/// otherwise the copy shipped beside the executable. The RELUNACY_CLASSES_DIR environment variable overrides both.
/// The copy embedded in the assembly is only read when no file is found.
/// </summary>
public static class ClassDataFiles
{
    private static string? _folder;

    /// <summary>The folder the files are read from and written to.</summary>
    public static string Folder
    {
        get
        {
            if (_folder != null) return _folder;
            if (Environment.GetEnvironmentVariable("RELUNACY_CLASSES_DIR") is { Length: > 0 } forced) return _folder = forced;
            // A checkout of the sources, found from where the program runs (bin/Debug/net...).
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (int up = 0; up < 8 && directory != null; up++, directory = directory.Parent)
            {
                string sources = Path.Combine(directory.FullName, "ReLunacy.Engine", "Resources", "Classes");
                if (Directory.Exists(sources)) return _folder = sources;
            }
            return _folder = Path.Combine(AppContext.BaseDirectory, "Resources", "Classes");
        }
    }

    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>The file's text: from the folder, else the copy embedded in the assembly, else null.</summary>
    public static string? Read(string fileName)
    {
        try
        {
            string path = PathOf(fileName);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"ReLunacy.Engine.Resources.Classes.{fileName}");
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static void Write(string fileName, string text)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathOf(fileName), text);
    }
}

/// <summary>
/// A hand-edited "number=name" table (class names, text tags): lines starting with # are comments and stay where
/// they are when the editor changes a name.
/// </summary>
public sealed class NamedNumbers
{
    private readonly string _fileName;
    private readonly List<string> _lines = [];
    private readonly Dictionary<long, (string Name, int Line)> _names = [];

    public NamedNumbers(string fileName)
    {
        _fileName = fileName;
        if (ClassDataFiles.Read(fileName) is not { } text) return;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            _lines.Add(line);
            if (line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && long.TryParse(line[..eq].Trim(), out long number)) _names[number] = (line[(eq + 1)..].Trim(), _lines.Count - 1);
        }
        while (_lines.Count > 0 && _lines[^1].Length == 0) _lines.RemoveAt(_lines.Count - 1);
    }

    public int Count => _names.Count;
    public string? Name(long number) => _names.TryGetValue(number, out var entry) ? entry.Name : null;

    /// <summary>Names a number (an empty name removes it) and writes the file.</summary>
    public void Set(long number, string name)
    {
        name = name.Trim();
        lock (_lines)
        {
            if (_names.TryGetValue(number, out var entry))
            {
                if (name.Length == 0)
                {
                    _lines[entry.Line] = "";
                    _names.Remove(number);
                }
                else
                {
                    _lines[entry.Line] = $"{number}={name}";
                    _names[number] = (name, entry.Line);
                }
            }
            else if (name.Length > 0)
            {
                // Kept in numeric order: after the last smaller number.
                int at = _names.Any(n => n.Key < number) ? _names.Where(n => n.Key < number).Max(n => n.Value.Line) + 1
                    : _names.Count > 0 ? _names.Min(n => n.Value.Line) : _lines.Count;
                _lines.Insert(at, $"{number}={name}");
                foreach (long key in _names.Keys.ToList())
                    if (_names[key].Line >= at) _names[key] = (_names[key].Name, _names[key].Line + 1);
                _names[number] = (name, at);
            }
            else return;
            ClassDataFiles.Write(_fileName, string.Join("\n", _lines.Where((l, i) => l.Length > 0 || i == 0 || _lines[i - 1].Length > 0)) + "\n");
        }
    }
}

/// <summary>
/// What is known of the settings fields of a game's classes: names, types and descriptions, in
/// Resources/Classes/&lt;Game&gt;.fields. The file is the project's shared documentation: written by hand or from the
/// inspector ("Edit fields"), which changes the file itself (see <see cref="ClassDataFiles"/>). Plain text:
///
///   enum Level : u32            (or "flags Name : u8" for bits that combine)
///   {
///       ZolarForest = 2;
///   }
///
///   class 0x4C2 SpacePlanet : 0x11D
///   {
///       [0xC8, Level] Destination level = "The level the ship loads when it lands here.";
///       [0xD4, Volume] Location volume = "The ship can land while it is inside.";
///       [0xE8, float, runtime] Barrier spacing = "";
///   }
///
/// A field is [offset, type] or [offset, type, runtime], a name, and a description in quotes. Types: u8 u16 u32 s32
/// float bool bool32 text (a text id), ref (any instance) or Moby Path Volume Clue Controller Scent Area Pod,
/// component(size), or the name of an enum. A class inherits its parents' fields, so the file carries each class's
/// parent and needs no EBOOT. Lines starting with # are comments; a comment above a class, an enum or a field stays
/// with it when the editor rewrites the file.
/// </summary>
public static class ClassFields
{
    public static readonly string[] ReferenceKinds = ["Moby", "Path", "Volume", "Clue", "Controller", "Scent", "Area", "Pod"];

    private sealed class Table
    {
        public List<string> Header = [];
        public readonly Dictionary<uint, int> Parents = [];
        public readonly Dictionary<uint, string> Names = [];
        public readonly Dictionary<uint, SortedDictionary<int, ClassField>> Own = [];
        public readonly Dictionary<string, ClassEnum> Enums = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<string>> Comments = new(StringComparer.Ordinal);
        public readonly Dictionary<uint, IReadOnlyList<ClassField>> All = [];
    }

    private static readonly object Lock = new();
    private static readonly Dictionary<GameId, Table> Games = [];

    private static Table Load(GameId id)
    {
        lock (Lock)
        {
            if (Games.TryGetValue(id, out var table)) return table;
            table = new Table();
            if (ClassDataFiles.Read($"{id}.fields") is { } text) Parse(text, table);
            return Games[id] = table;
        }
    }

    /// <summary>The file a game's fields are read from and written to.</summary>
    public static string FilePath(GameId game) => ClassDataFiles.PathOf($"{game}.fields");

    /// <summary>Forgets what was read, so the next question reads the file again (after a hand edit).</summary>
    public static void Reload()
    {
        lock (Lock) Games.Clear();
    }

    // ---------------------------------------------------------------- reading

    /// <summary>The class and its ancestors by the table, the class first; just the class without a table.</summary>
    public static List<uint> Chain(GameId? game, uint cls)
    {
        var chain = new List<uint> { cls };
        if (game is not { } id) return chain;
        var table = Load(id);
        lock (Lock)
            for (uint current = cls; table.Parents.TryGetValue(current, out int parent) && parent >= 0 && !chain.Contains((uint)parent);)
            {
                current = (uint)parent;
                chain.Add(current);
            }
        return chain;
    }

    /// <summary>Whether <paramref name="cls"/> is <paramref name="ancestor"/> or derives from it, by the table.</summary>
    public static bool IsA(GameId? game, uint cls, uint ancestor) => Chain(game, cls).Contains(ancestor);

    /// <summary>Every documented field of <paramref name="cls"/>, its ancestors' included, by offset.</summary>
    public static IReadOnlyList<ClassField> Of(GameId? game, uint cls)
    {
        if (game is not { } id) return [];
        var table = Load(id);
        lock (Lock)
        {
            if (table.All.TryGetValue(cls, out var cached)) return cached;
            var fields = new Dictionary<int, ClassField>();
            // The nearest class wins an offset two of them name.
            foreach (uint c in Chain(game, cls))
                if (table.Own.TryGetValue(c, out var own))
                    foreach (var (offset, field) in own) fields.TryAdd(offset, field);
            return table.All[cls] = fields.Values.OrderBy(f => f.Offset).ToList();
        }
    }

    /// <summary>The field that starts at <paramref name="offset"/>, when documented.</summary>
    public static ClassField? At(GameId? game, uint cls, int offset)
    {
        foreach (var field in Of(game, cls))
            if (field.Offset == offset) return field;
        return null;
    }

    /// <summary>The class's name in the table, or null.</summary>
    public static string? ClassName(GameId? game, uint cls)
    {
        if (game is not { } id) return null;
        var table = Load(id);
        lock (Lock) return table.Names.TryGetValue(cls, out var name) && name.Length > 0 ? name : null;
    }

    /// <summary>The game's enums, by name.</summary>
    public static IReadOnlyList<ClassEnum> Enums(GameId? game)
    {
        if (game is not { } id) return [];
        var table = Load(id);
        lock (Lock) return table.Enums.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ClassEnum? Enum(GameId? game, string? name) =>
        name == null ? null : Enums(game).FirstOrDefault(e => e.Name == name);

    // ---------------------------------------------------------------- editing: every change is written to the file

    /// <summary>Documents a field, or changes one. <paramref name="parent"/> declares the class when the table
    /// does not have it yet.</summary>
    public static void SetField(GameId game, ClassField field, int? parent = null)
    {
        var table = Load(game);
        lock (Lock)
        {
            Declare(table, field.Owner, parent);
            if (!table.Own.TryGetValue(field.Owner, out var own)) table.Own[field.Owner] = own = [];
            own[field.Offset] = field;
            Save(game, table);
        }
    }

    /// <summary>Removes a field's documentation: the bytes go back to being undocumented.</summary>
    public static void RemoveField(GameId game, uint owner, int offset)
    {
        var table = Load(game);
        lock (Lock)
        {
            if (table.Own.TryGetValue(owner, out var own)) own.Remove(offset);
            table.Comments.Remove(FieldKey(owner, offset));
            Save(game, table);
        }
    }

    /// <summary>Names a class (and declares it, with its parent, when the table does not have it).</summary>
    public static void SetClassName(GameId game, uint cls, string name, int? parent = null)
    {
        var table = Load(game);
        lock (Lock)
        {
            Declare(table, cls, parent);
            table.Names[cls] = name.Trim();
            Save(game, table);
        }
    }

    public static void SetEnum(GameId game, ClassEnum value, string? renamedFrom = null)
    {
        var table = Load(game);
        lock (Lock)
        {
            if (renamedFrom != null && renamedFrom != value.Name)
            {
                table.Enums.Remove(renamedFrom);
                if (table.Comments.Remove("enum " + renamedFrom, out var comment)) table.Comments["enum " + value.Name] = comment;
                // The fields typed with it follow the new name.
                foreach (var own in table.Own.Values)
                    foreach (int offset in own.Keys.ToList())
                        if (own[offset] is { Kind: ClassFieldKind.Enum } field && field.TypeName == renamedFrom)
                            own[offset] = field with { TypeName = value.Name };
            }
            table.Enums[value.Name] = value;
            foreach (var own in table.Own.Values)
                foreach (int offset in own.Keys.ToList())
                    if (own[offset] is { Kind: ClassFieldKind.Enum } field && field.TypeName == value.Name && field.Length != value.Size)
                        own[offset] = field with { Length = value.Size };
            Save(game, table);
        }
    }

    public static void RemoveEnum(GameId game, string name)
    {
        var table = Load(game);
        lock (Lock)
        {
            table.Enums.Remove(name);
            table.Comments.Remove("enum " + name);
            Save(game, table);
        }
    }

    private static void Declare(Table table, uint cls, int? parent)
    {
        if (!table.Parents.ContainsKey(cls)) table.Parents[cls] = parent ?? -1;
        table.Names.TryAdd(cls, "");
    }

    private static void Save(GameId game, Table table)
    {
        table.All.Clear();
        ClassDataFiles.Write($"{game}.fields", Write(table));
    }

    // ---------------------------------------------------------------- the file

    private static string FieldKey(uint cls, int offset) => $"field {cls:X} {offset:X}";

    private static string Write(Table table)
    {
        var text = new StringBuilder();
        foreach (string line in table.Header) text.Append(line).Append('\n');
        if (table.Header.Count > 0) text.Append('\n');
        void Comment(string key, string indent)
        {
            if (table.Comments.TryGetValue(key, out var lines))
                foreach (string line in lines) text.Append(indent).Append(line).Append('\n');
        }
        foreach (var e in table.Enums.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            Comment("enum " + e.Name, "");
            text.Append($"{(e.Flags ? "flags" : "enum")} {e.Name} : {(e.Size == 1 ? "u8" : e.Size == 2 ? "u16" : "u32")}\n{{\n");
            foreach (var (name, value) in e.Entries)
                text.Append($"    {name} = {(e.Flags ? "0x" + value.ToString("X") : value.ToString(CultureInfo.InvariantCulture))};\n");
            text.Append("}\n\n");
        }
        foreach (uint cls in table.Parents.Keys.Union(table.Own.Keys).Order())
        {
            string name = table.Names.GetValueOrDefault(cls, "");
            int parent = table.Parents.GetValueOrDefault(cls, -1);
            Comment($"class {cls:X}", "");
            text.Append($"class 0x{cls:X}{(name.Length > 0 ? " " + name : "")}{(parent >= 0 ? $" : 0x{parent:X}" : "")}\n{{\n");
            if (table.Own.TryGetValue(cls, out var own))
                foreach (var (offset, field) in own)
                {
                    Comment(FieldKey(cls, offset), "    ");
                    text.Append($"    [0x{offset:X}, {field.TypeText}{(field.RunTime ? ", runtime" : "")}]");
                    if (field.Name.Length > 0) text.Append(' ').Append(field.Name);
                    text.Append($" = \"{field.Description.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")}\";\n");
                }
            text.Append("}\n\n");
        }
        return text.ToString().TrimEnd('\n') + "\n";
    }

    private static void Parse(string text, Table table)
    {
        ClassEnum? currentEnum = null;
        uint? currentClass = null;
        bool header = true;
        var pending = new List<string>();
        void Attach(string key)
        {
            if (pending.Count > 0) table.Comments[key] = [.. pending];
            pending.Clear();
        }
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            bool comment = line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal);
            if (header)
            {
                // The comment block at the top of the file, up to its first blank line, is the file's own.
                if (comment) { table.Header.Add(line); continue; }
                header = false;
                if (line.Length == 0) continue;
            }
            if (comment) { pending.Add(line); continue; }
            if (line.Length == 0 || line == "{") continue;
            if (line == "}") { currentEnum = null; currentClass = null; pending.Clear(); continue; }
            if (line.EndsWith('{')) line = line[..^1].TrimEnd();

            if (line.StartsWith("enum ", StringComparison.Ordinal) || line.StartsWith("flags ", StringComparison.Ordinal))
            {
                bool flags = line[0] == 'f';
                string[] parts = line[(flags ? 6 : 5)..].Split(':', 2);
                string name = parts[0].Trim(), size = parts.Length > 1 ? parts[1].Trim() : "u32";
                currentClass = null;
                currentEnum = new ClassEnum { Name = name, Flags = flags, Size = size == "u8" ? 1 : size == "u16" ? 2 : 4 };
                table.Enums[name] = currentEnum;
                Attach("enum " + name);
                continue;
            }
            if (line.StartsWith("class ", StringComparison.Ordinal))
            {
                string rest = line[6..].Trim();
                int parent = -1;
                int colon = rest.LastIndexOf(" : ", StringComparison.Ordinal);
                if (colon >= 0 && TryNumber(rest[(colon + 3)..], out long p)) { parent = (int)p; rest = rest[..colon].Trim(); }
                int space = rest.IndexOf(' ');
                if (!TryNumber(space < 0 ? rest : rest[..space], out long number)) { currentClass = null; continue; }
                currentClass = (uint)number;
                currentEnum = null;
                table.Parents[currentClass.Value] = parent;
                table.Names[currentClass.Value] = space < 0 ? "" : rest[(space + 1)..].Trim();
                Attach($"class {number:X}");
                continue;
            }
            if (currentEnum != null)
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && TryNumber(line[(eq + 1)..].TrimEnd(';').Trim(), out long value))
                    currentEnum.Entries.Add((line[..eq].Trim(), value));
                continue;
            }
            if (currentClass is { } cls && line[0] == '[')
            {
                int close = line.IndexOf(']');
                if (close < 0) continue;
                string[] head = line[1..close].Split(',').Select(s => s.Trim()).ToArray();
                if (head.Length < 2 || !TryNumber(head[0], out long offset)) continue;
                if (!table.Own.TryGetValue(cls, out var own)) table.Own[cls] = own = [];
                bool runTime = head.Skip(2).Any(h => h.Equals("runtime", StringComparison.OrdinalIgnoreCase));
                string rest = line[(close + 1)..].Trim();
                string name, description = "";
                int eq = rest.IndexOf('=');
                if (eq >= 0)
                {
                    name = rest[..eq].Trim();
                    description = Unquote(rest[(eq + 1)..]);
                }
                else name = rest.TrimEnd(';').Trim();
                if (TypeOf(head[1], table.Enums) is not { } type) continue;
                own[(int)offset] = new ClassField(cls, (int)offset, type.Length, type.Kind, runTime, name, description, type.TypeName);
                Attach(FieldKey(cls, (int)offset));
            }
        }
        // An enum's size is known once the whole file is read: fields typed before their enum take it now.
        foreach (var own in table.Own.Values)
            foreach (int offset in own.Keys.ToList())
                if (own[offset] is { Kind: ClassFieldKind.Enum, TypeName: { } name } field && table.Enums.TryGetValue(name, out var e) && e.Size != field.Length)
                    own[offset] = field with { Length = e.Size };
    }

    /// <summary>A type as the file spells it. An unknown word is taken for an enum's name (four bytes until the
    /// enum says otherwise), so a field can be typed before its enum exists.</summary>
    public static (ClassFieldKind Kind, int Length, string? TypeName)? TypeOf(string text, IReadOnlyDictionary<string, ClassEnum>? enums = null)
    {
        switch (text)
        {
            case "u8": return (ClassFieldKind.U8, 1, null);
            case "u16": return (ClassFieldKind.U16, 2, null);
            case "u32": return (ClassFieldKind.U32, 4, null);
            case "s32": return (ClassFieldKind.S32, 4, null);
            case "float" or "f32": return (ClassFieldKind.F32, 4, null);
            case "bool": return (ClassFieldKind.Bool, 1, null);
            case "bool32": return (ClassFieldKind.Bool, 4, null);
            case "text": return (ClassFieldKind.TextId, 4, null);
            case "ref": return (ClassFieldKind.Reference, 4, null);
        }
        if (ReferenceKinds.Contains(text)) return (ClassFieldKind.Reference, 4, text);
        if (text.StartsWith("component(", StringComparison.Ordinal) && text.EndsWith(')') && TryNumber(text[10..^1], out long size))
            return (ClassFieldKind.Component, (int)size, null);
        if (text.Length == 0 || !(char.IsLetter(text[0]) || text[0] == '_')) return null;
        int length = enums != null && enums.TryGetValue(text, out var e) ? e.Size : 4;
        return (ClassFieldKind.Enum, length, text);
    }

    private static bool TryNumber(string text, out long value)
    {
        text = text.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string Unquote(string text)
    {
        text = text.Trim().TrimEnd(';').Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
        return text.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
