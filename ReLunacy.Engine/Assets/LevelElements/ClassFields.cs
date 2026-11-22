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
    /// <summary>A pointer to a list of references kept outside the struct (four bytes, an offset in the settings
    /// block, never a number to edit); <see cref="ClassField.TypeName"/> says of which kind.</summary>
    ReferenceList,
    /// <summary>A sound the level can play: a SoundSpecs id (u32).</summary>
    Sound,
    /// <summary>An effect the level can play: a Tags key its fxconduit maps to an effect (u32).</summary>
    Effect,
}

/// <summary>One documented field of a class's settings.</summary>
/// <param name="Owner">The class that declares it; every class below inherits it.</param>
/// <param name="Offset">Offset in the settings, which start with the class number.</param>
/// <param name="Length">Bytes the field covers.</param>
/// <param name="RunTime">The game overwrites the field when the level starts, so the editor shows it read-only.</param>
/// <param name="TypeName">For a reference or a reference list the kind it must name (Moby, Path, Volume, Clue, Controller, Scent, Area,
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
        ClassFieldKind.Sound => "sound",
        ClassFieldKind.Effect => "effect",
        ClassFieldKind.Reference => TypeName ?? "ref",
        ClassFieldKind.ReferenceList => (TypeName ?? "ref") + " list",
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
/// float bool bool32 text (a text id), sound (a SoundSpecs id), effect (a Tags key of the level's effects), ref (any instance) or Moby Path Volume Clue Controller Scent Area Pod,
/// any of those followed by "list" (a pointer to a list of them: "Volume list"), component(size), or the name of an enum. A class inherits its parents' fields, so the file carries each class's
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
        public readonly Dictionary<uint, SortedDictionary<(int Offset, bool Field), ClassField>> Own = [];
        public readonly Dictionary<string, ClassEnum> Enums = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<string>> Comments = new(StringComparer.Ordinal);
        public readonly Dictionary<uint, IReadOnlyList<ClassField>> All = [];
    }

    private static readonly object Lock = new();
    private static readonly Dictionary<GameId, Table> Games = [];

    // An embedded object starts at the same byte as its first field: both are kept, the object first.
    private static (int Offset, bool Field) KeyOf(ClassField field) => (field.Offset, field.Kind != ClassFieldKind.Component);

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
            var fields = new Dictionary<(int Offset, bool Field), ClassField>();
            // The nearest class wins an offset two of them name, and the bytes of a field it declares: an
            // ancestor's field overlapping them is left out.
            foreach (uint c in Chain(game, cls))
                if (table.Own.TryGetValue(c, out var own))
                {
                    var nearer = fields.Values.Where(f => f.Kind != ClassFieldKind.Component).ToList();
                    foreach (var (key, field) in own)
                        if (field.Kind == ClassFieldKind.Component || !nearer.Any(f => Overlaps(f, field.Offset, field.Length)))
                            fields.TryAdd(key, field);
                }
            return table.All[cls] = fields.OrderBy(f => f.Key).Select(f => f.Value).ToList();
        }
    }

    /// <summary>The field that starts at <paramref name="offset"/>, when documented (not an embedded object).</summary>
    public static ClassField? At(GameId? game, uint cls, int offset)
    {
        foreach (var field in Of(game, cls))
            if (field.Offset == offset && field.Kind != ClassFieldKind.Component) return field;
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

    private static bool Overlaps(ClassField field, int offset, int length) =>
        field.Offset < offset + length && offset < field.Offset + field.Length;

    /// <summary>The fields of <paramref name="owner"/> other than the one at <paramref name="offset"/> whose bytes a
    /// field of <paramref name="length"/> there would cover: what <see cref="SetField"/> replaces.</summary>
    public static IReadOnlyList<ClassField> Overlapping(GameId game, uint owner, int offset, int length)
    {
        var table = Load(game);
        lock (Lock)
            return table.Own.TryGetValue(owner, out var own)
                ? own.Values.Where(f => f.Kind != ClassFieldKind.Component && f.Offset != offset && Overlaps(f, offset, length)).ToList()
                : [];
    }

    /// <summary>Documents a field, or changes one; the class's other fields its bytes cover are removed.
    /// <paramref name="parent"/> declares the class when the table does not have it yet.</summary>
    public static void SetField(GameId game, ClassField field, int? parent = null)
    {
        var table = Load(game);
        lock (Lock)
        {
            Declare(table, field.Owner, parent);
            if (!table.Own.TryGetValue(field.Owner, out var own)) table.Own[field.Owner] = own = [];
            if (field.Kind != ClassFieldKind.Component)
                foreach (var (key, covered) in own.ToList())
                    if (key != KeyOf(field) && covered.Kind != ClassFieldKind.Component && Overlaps(covered, field.Offset, field.Length))
                    {
                        own.Remove(key);
                        table.Comments.Remove(FieldKey(field.Owner, covered.Offset));
                    }
            own[KeyOf(field)] = field;
            Save(game, table);
        }
    }

    /// <summary>
    /// How a settings block of <paramref name="length"/> bytes splits into rows, by offset: each field that fits and
    /// overlaps none before it, then the bytes no field covers, word by word. A word no field touches is one gap; what
    /// fields leave of a word is cut at the size of the smallest of them, aligned, so a bool at +0 leaves three
    /// one-byte gaps and a u16 at +0 one two-byte gap.
    /// </summary>
    public static List<(int Offset, int Size, ClassField? Field)> Layout(IEnumerable<ClassField> fields, int length, int start = 4)
    {
        var rows = new List<(int Offset, int Size, ClassField? Field)>();
        var covered = new bool[Math.Max(length, 0)];
        var smallest = new Dictionary<int, int>();
        foreach (var field in fields.Where(f => f.Kind != ClassFieldKind.Component).OrderBy(f => f.Offset))
        {
            if (field.Offset < start || field.Length is < 1 or > 4 || field.Offset + field.Length > length) continue;
            if (covered.AsSpan(field.Offset, field.Length).Contains(true)) continue;
            for (int b = field.Offset; b < field.Offset + field.Length; b++)
            {
                covered[b] = true;
                smallest[b & ~3] = Math.Min(smallest.GetValueOrDefault(b & ~3, 4), field.Length);
            }
            rows.Add((field.Offset, field.Length, field));
        }
        for (int word = (start + 3) & ~3; word + 4 <= length; word += 4)
        {
            int unit = smallest.GetValueOrDefault(word, 4);
            for (int b = word; b < word + 4;)
            {
                if (covered[b]) { b++; continue; }
                int size = unit;
                while (size > 1 && (b % size != 0 || b + size > word + 4 || covered.AsSpan(b, size).Contains(true))) size /= 2;
                rows.Add((b, size, null));
                b += size;
            }
        }
        rows.Sort((x, y) => x.Offset.CompareTo(y.Offset));
        return rows;
    }

    /// <summary>Removes a field's documentation: the bytes go back to being undocumented. An embedded object at the
    /// same offset stays unless <paramref name="component"/> asks for it.</summary>
    public static void RemoveField(GameId game, uint owner, int offset, bool component = false)
    {
        var table = Load(game);
        lock (Lock)
        {
            if (table.Own.TryGetValue(owner, out var own)) own.Remove((offset, !component));
            table.Comments.Remove(FieldKey(owner, offset, component));
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
                    foreach (var key in own.Keys.ToList())
                        if (own[key] is { Kind: ClassFieldKind.Enum } field && field.TypeName == renamedFrom)
                            own[key] = field with { TypeName = value.Name };
            }
            table.Enums[value.Name] = value;
            foreach (var own in table.Own.Values)
                foreach (var key in own.Keys.ToList())
                    if (own[key] is { Kind: ClassFieldKind.Enum } field && field.TypeName == value.Name && field.Length != value.Size)
                        own[key] = field with { Length = value.Size };
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

    private static string FieldKey(uint cls, int offset, bool component = false) => $"{(component ? "component" : "field")} {cls:X} {offset:X}";

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
                foreach (var field in own.Values)
                {
                    Comment(FieldKey(cls, field.Offset, field.Kind == ClassFieldKind.Component), "    ");
                    text.Append($"    [0x{field.Offset:X}, {field.TypeText}{(field.RunTime ? ", runtime" : "")}]");
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
                var parsed = new ClassField(cls, (int)offset, type.Length, type.Kind, runTime, name, description, type.TypeName);
                own[KeyOf(parsed)] = parsed;
                Attach(FieldKey(cls, (int)offset, type.Kind == ClassFieldKind.Component));
            }
        }
        // An enum's size is known once the whole file is read: fields typed before their enum take it now.
        foreach (var own in table.Own.Values)
            foreach (var key in own.Keys.ToList())
                if (own[key] is { Kind: ClassFieldKind.Enum, TypeName: { } name } field && table.Enums.TryGetValue(name, out var e) && e.Size != field.Length)
                    own[key] = field with { Length = e.Size };
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
            case "sound": return (ClassFieldKind.Sound, 4, null);
            case "effect": return (ClassFieldKind.Effect, 4, null);
            case "ref": return (ClassFieldKind.Reference, 4, null);
        }
        if (ReferenceKinds.Contains(text)) return (ClassFieldKind.Reference, 4, text);
        if (text.EndsWith(" list", StringComparison.Ordinal) && text[..^5].Trim() is var listed && (listed == "ref" || ReferenceKinds.Contains(listed)))
            return (ClassFieldKind.ReferenceList, 4, listed == "ref" ? null : listed);
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
