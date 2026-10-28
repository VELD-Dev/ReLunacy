using Newtonsoft.Json;

namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// Colours for the code editor, as editable JSON.
///
/// Values are written as <c>#RRGGBB</c> or <c>#AARRGGBB</c> because that is what anyone picking
/// colours expects, and converted to ImGui's packed order on load. That conversion is the point of
/// doing this properly: the hardcoded constants this replaces were written directly in ImGui's
/// ABGR layout by hand, and two of the five had their red and blue channels swapped - the comment
/// green was #55996A instead of #6A9955 and the number green #A8CEB5 instead of #B5CEA8.
///
/// Roles are split into lexical, semantic and chrome. A lexer only ever sees a Name, so colouring
/// functions and classes separately needs the scope resolver and the API database, not more token
/// kinds.
///
/// Every colour is declared once, in <see cref="Slots"/>. Resolving, the theme editor's rows and
/// the round-trip check all read that table, so adding a colour means adding a property and one
/// slot rather than touching four parallel lists that can drift apart.
/// </summary>
public sealed class LuaEditorTheme
{
    public string Name { get; set; } = "Dark";

    // --- lexical: decided by the lexer alone ---------------------------------------------------
    public string Keyword { get; set; } = "#569CD6";
    public string String { get; set; } = "#CE9178";
    public string Number { get; set; } = "#B5CEA8";
    public string Comment { get; set; } = "#6A9955";
    public string Operator { get; set; } = "#D4D4D4";
    public string Error { get; set; } = "#F44747";

    // --- semantic: decided by scope resolution and the documented API --------------------------
    /// <summary>A documented global function.</summary>
    public string Function { get; set; } = "#DCDCAA";
    /// <summary>A method call through <c>:</c>.</summary>
    public string Method { get; set; } = "#DCDCAA";
    /// <summary>A documented class, or a variable carrying one.</summary>
    public string Class { get; set; } = "#4EC9B0";
    /// <summary>A documented field or table key.</summary>
    public string Field { get; set; } = "#9CDCFE";
    public string Local { get; set; } = "#9CDCFE";
    public string Parameter { get; set; } = "#9A9A9A";
    public string LoopVariable { get; set; } = "#9CDCFE";
    /// <summary>A global nothing documents - deliberately distinct, so an undocumented API is
    /// visible as such while it is being written up.</summary>
    public string GlobalUndocumented { get; set; } = "#C586C0";

    // --- chrome ------------------------------------------------------------------------------
    public string Text { get; set; } = "#D4D4D4";
    public string Caret { get; set; } = "#AEAFAD";
    public string Selection { get; set; } = "#264F78";
    public string CurrentLine { get; set; } = "#2A2D2E";
    public string LineNumber { get; set; } = "#858585";
    public string LineNumberCurrent { get; set; } = "#C6C6C6";

    /// <summary>
    /// Every editable colour: which role it paints, how to show it, and how to read and write it.
    ///
    /// <c>Key</c> is the JSON property name, so a problem reported while resolving names the field
    /// to go and fix in the file.
    /// </summary>
    public static readonly IReadOnlyList<LuaEditorThemeSlot> Slots =
    [
        new(LuaEditorThemeRole.Keyword, LuaEditorThemeGroup.Lexical, nameof(Keyword), "Keyword",
            "if, then, local, function", t => t.Keyword, (t, v) => t.Keyword = v),
        new(LuaEditorThemeRole.String, LuaEditorThemeGroup.Lexical, nameof(String), "String",
            "Quoted and long-bracket strings", t => t.String, (t, v) => t.String = v),
        new(LuaEditorThemeRole.Number, LuaEditorThemeGroup.Lexical, nameof(Number), "Number",
            "Numeric literals", t => t.Number, (t, v) => t.Number = v),
        new(LuaEditorThemeRole.Comment, LuaEditorThemeGroup.Lexical, nameof(Comment), "Comment",
            "Line and long comments", t => t.Comment, (t, v) => t.Comment = v),
        new(LuaEditorThemeRole.Operator, LuaEditorThemeGroup.Lexical, nameof(Operator), "Operator",
            "Symbols and punctuation", t => t.Operator, (t, v) => t.Operator = v),
        new(LuaEditorThemeRole.Error, LuaEditorThemeGroup.Lexical, nameof(Error), "Error",
            "Text the lexer could not read", t => t.Error, (t, v) => t.Error = v),

        new(LuaEditorThemeRole.Function, LuaEditorThemeGroup.Semantic, nameof(Function), "Function",
            "A documented global function", t => t.Function, (t, v) => t.Function = v),
        new(LuaEditorThemeRole.Method, LuaEditorThemeGroup.Semantic, nameof(Method), "Method",
            "A call through ':'", t => t.Method, (t, v) => t.Method = v),
        new(LuaEditorThemeRole.Class, LuaEditorThemeGroup.Semantic, nameof(Class), "Class",
            "A documented class, or a variable carrying one", t => t.Class, (t, v) => t.Class = v),
        new(LuaEditorThemeRole.Field, LuaEditorThemeGroup.Semantic, nameof(Field), "Field",
            "A documented field or table key", t => t.Field, (t, v) => t.Field = v),
        new(LuaEditorThemeRole.Local, LuaEditorThemeGroup.Semantic, nameof(Local), "Local",
            "A local variable", t => t.Local, (t, v) => t.Local = v),
        new(LuaEditorThemeRole.Parameter, LuaEditorThemeGroup.Semantic, nameof(Parameter), "Parameter",
            "A function parameter", t => t.Parameter, (t, v) => t.Parameter = v),
        new(LuaEditorThemeRole.LoopVariable, LuaEditorThemeGroup.Semantic, nameof(LoopVariable),
            "Loop variable", "A for loop's control variable",
            t => t.LoopVariable, (t, v) => t.LoopVariable = v),
        new(LuaEditorThemeRole.GlobalUndocumented, LuaEditorThemeGroup.Semantic,
            nameof(GlobalUndocumented), "Undocumented global",
            "A global nothing documents yet - distinct on purpose",
            t => t.GlobalUndocumented, (t, v) => t.GlobalUndocumented = v),

        new(LuaEditorThemeRole.Text, LuaEditorThemeGroup.Chrome, nameof(Text), "Text",
            "Anything with no role of its own", t => t.Text, (t, v) => t.Text = v),
        new(LuaEditorThemeRole.Caret, LuaEditorThemeGroup.Chrome, nameof(Caret), "Caret",
            "The insertion point", t => t.Caret, (t, v) => t.Caret = v),
        new(LuaEditorThemeRole.Selection, LuaEditorThemeGroup.Chrome, nameof(Selection), "Selection",
            "Selected text's background", t => t.Selection, (t, v) => t.Selection = v),
        new(LuaEditorThemeRole.CurrentLine, LuaEditorThemeGroup.Chrome, nameof(CurrentLine),
            "Current line", "The caret's line background",
            t => t.CurrentLine, (t, v) => t.CurrentLine = v),
        new(LuaEditorThemeRole.LineNumber, LuaEditorThemeGroup.Chrome, nameof(LineNumber),
            "Line number", "The gutter", t => t.LineNumber, (t, v) => t.LineNumber = v),
        new(LuaEditorThemeRole.LineNumberCurrent, LuaEditorThemeGroup.Chrome,
            nameof(LineNumberCurrent), "Current line number", "The gutter, on the caret's line",
            t => t.LineNumberCurrent, (t, v) => t.LineNumberCurrent = v),
    ];

    /// <summary>Packed ImGui colours by role, resolved once per load rather than per glyph.</summary>
    private readonly uint[] _packed = new uint[Enum.GetValues<LuaEditorThemeRole>().Length];

    [JsonIgnore] public uint KeywordColour => ColourFor(LuaEditorThemeRole.Keyword);
    [JsonIgnore] public uint StringColour => ColourFor(LuaEditorThemeRole.String);
    [JsonIgnore] public uint NumberColour => ColourFor(LuaEditorThemeRole.Number);
    [JsonIgnore] public uint CommentColour => ColourFor(LuaEditorThemeRole.Comment);
    [JsonIgnore] public uint OperatorColour => ColourFor(LuaEditorThemeRole.Operator);
    [JsonIgnore] public uint ErrorColour => ColourFor(LuaEditorThemeRole.Error);
    [JsonIgnore] public uint FunctionColour => ColourFor(LuaEditorThemeRole.Function);
    [JsonIgnore] public uint MethodColour => ColourFor(LuaEditorThemeRole.Method);
    [JsonIgnore] public uint ClassColour => ColourFor(LuaEditorThemeRole.Class);
    [JsonIgnore] public uint FieldColour => ColourFor(LuaEditorThemeRole.Field);
    [JsonIgnore] public uint LocalColour => ColourFor(LuaEditorThemeRole.Local);
    [JsonIgnore] public uint ParameterColour => ColourFor(LuaEditorThemeRole.Parameter);
    [JsonIgnore] public uint LoopVariableColour => ColourFor(LuaEditorThemeRole.LoopVariable);
    [JsonIgnore] public uint GlobalUndocumentedColour => ColourFor(LuaEditorThemeRole.GlobalUndocumented);
    [JsonIgnore] public uint TextColour => ColourFor(LuaEditorThemeRole.Text);
    [JsonIgnore] public uint CaretColour => ColourFor(LuaEditorThemeRole.Caret);
    [JsonIgnore] public uint SelectionColour => ColourFor(LuaEditorThemeRole.Selection);
    [JsonIgnore] public uint CurrentLineColour => ColourFor(LuaEditorThemeRole.CurrentLine);
    [JsonIgnore] public uint LineNumberColour => ColourFor(LuaEditorThemeRole.LineNumber);
    [JsonIgnore] public uint LineNumberCurrentColour => ColourFor(LuaEditorThemeRole.LineNumberCurrent);

    /// <summary>Problems found while loading, so a mistyped colour is reported rather than silently
    /// becoming magenta.</summary>
    [JsonIgnore] public List<string> Problems { get; } = [];

    /// <summary>
    /// Converts every string to its packed form. Must be called after deserialising, and again
    /// after any edit - which is how the theme editor applies a colour live: the views hold this
    /// same instance and read the packed array, so there is nothing to invalidate.
    /// </summary>
    public LuaEditorTheme Resolve()
    {
        Problems.Clear();
        foreach (var slot in Slots) _packed[(int)slot.Role] = Pack(slot.Get(this), slot.Key);
        return this;
    }

    /// <summary>The colour for a role, so callers switch on the role rather than repeating this
    /// mapping.</summary>
    public uint ColourFor(LuaEditorThemeRole role) => _packed[(int)role];

    /// <summary>A copy, for editing against a revert point.</summary>
    public LuaEditorTheme Clone()
    {
        var copy = new LuaEditorTheme { Name = Name };
        foreach (var slot in Slots) slot.Set(copy, slot.Get(this));
        return copy.Resolve();
    }

    /// <summary>
    /// <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c> to ImGui's packed layout, which is
    /// ABGR - alpha high, then blue, green, red. Writing these by hand is exactly how the previous
    /// constants ended up with red and blue transposed.
    /// </summary>
    private uint Pack(string value, string role)
    {
        if (!TryParseHex(value, out byte a, out byte r, out byte g, out byte b))
        {
            Problems.Add($"{role}: '{value}' is not #RGB, #RRGGBB or #AARRGGBB");
            return 0xFFFF00FF;
        }
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    }

    /// <summary>
    /// Reads <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c>. Shared with the theme editor so the
    /// colour pickers and the loader agree on what a theme file may say.
    /// </summary>
    public static bool TryParseHex(string value, out byte a, out byte r, out byte g, out byte b)
    {
        a = 0xFF; r = g = b = 0;
        string hex = value.Trim().TrimStart('#');
        try
        {
            switch (hex.Length)
            {
                case 3:
                    r = (byte)(Nibble(hex[0]) * 17); g = (byte)(Nibble(hex[1]) * 17); b = (byte)(Nibble(hex[2]) * 17);
                    return true;
                case 6:
                    r = Byte(hex, 0); g = Byte(hex, 2); b = Byte(hex, 4);
                    return true;
                case 8:
                    a = Byte(hex, 0); r = Byte(hex, 2); g = Byte(hex, 4); b = Byte(hex, 6);
                    return true;
                default:
                    return false;
            }
        }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// Back to a theme file's spelling. Alpha is only written when it is not opaque, so an ordinary
    /// colour stays the <c>#RRGGBB</c> that is pleasant to hand-edit.
    /// </summary>
    public static string ToHex(byte a, byte r, byte g, byte b) =>
        a == 0xFF ? $"#{r:X2}{g:X2}{b:X2}" : $"#{a:X2}{r:X2}{g:X2}{b:X2}";

    private static byte Byte(string hex, int at) =>
        byte.Parse(hex.AsSpan(at, 2), System.Globalization.NumberStyles.HexNumber);

    private static int Nibble(char c) =>
        int.Parse(c.ToString(), System.Globalization.NumberStyles.HexNumber);
}

/// <summary>One editable colour: the role it paints and how to read and write it on a theme.</summary>
/// <param name="Key">The JSON property name.</param>
/// <param name="Label">What the theme editor calls it.</param>
public sealed record LuaEditorThemeSlot(
    LuaEditorThemeRole Role,
    LuaEditorThemeGroup Group,
    string Key,
    string Label,
    string Description,
    Func<LuaEditorTheme, string> Get,
    Action<LuaEditorTheme, string> Set);

/// <summary>How a colour is decided, which is also how the theme editor groups its rows.</summary>
public enum LuaEditorThemeGroup { Lexical, Semantic, Chrome }

/// <summary>What a run of text is, for colouring. Lexical roles come from the lexer; the semantic
/// ones need scope resolution and the documented API; the rest paint the editor itself.</summary>
public enum LuaEditorThemeRole
{
    Text,
    Keyword, String, Number, Comment, Operator, Error,
    Function, Method, Class, Field, Local, Parameter, LoopVariable, GlobalUndocumented,
    Caret, Selection, CurrentLine, LineNumber, LineNumberCurrent,
}
