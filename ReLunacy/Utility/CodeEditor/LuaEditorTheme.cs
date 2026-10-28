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
/// Roles are split into lexical and semantic. A lexer only ever sees a Name, so colouring functions
/// and classes separately needs the scope resolver and the API database, not more token kinds.
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

    /// <summary>Packed ImGui colours, resolved once per load rather than per glyph.</summary>
    [JsonIgnore] public uint KeywordColour { get; private set; }
    [JsonIgnore] public uint StringColour { get; private set; }
    [JsonIgnore] public uint NumberColour { get; private set; }
    [JsonIgnore] public uint CommentColour { get; private set; }
    [JsonIgnore] public uint OperatorColour { get; private set; }
    [JsonIgnore] public uint ErrorColour { get; private set; }
    [JsonIgnore] public uint FunctionColour { get; private set; }
    [JsonIgnore] public uint MethodColour { get; private set; }
    [JsonIgnore] public uint ClassColour { get; private set; }
    [JsonIgnore] public uint FieldColour { get; private set; }
    [JsonIgnore] public uint LocalColour { get; private set; }
    [JsonIgnore] public uint ParameterColour { get; private set; }
    [JsonIgnore] public uint LoopVariableColour { get; private set; }
    [JsonIgnore] public uint GlobalUndocumentedColour { get; private set; }
    [JsonIgnore] public uint TextColour { get; private set; }
    [JsonIgnore] public uint CaretColour { get; private set; }
    [JsonIgnore] public uint SelectionColour { get; private set; }
    [JsonIgnore] public uint CurrentLineColour { get; private set; }
    [JsonIgnore] public uint LineNumberColour { get; private set; }
    [JsonIgnore] public uint LineNumberCurrentColour { get; private set; }

    /// <summary>Problems found while loading, so a mistyped colour is reported rather than silently
    /// becoming magenta.</summary>
    [JsonIgnore] public List<string> Problems { get; } = [];

    /// <summary>Converts every string to its packed form. Must be called after deserialising.</summary>
    public LuaEditorTheme Resolve()
    {
        Problems.Clear();
        KeywordColour = Pack(Keyword, nameof(Keyword));
        StringColour = Pack(String, nameof(String));
        NumberColour = Pack(Number, nameof(Number));
        CommentColour = Pack(Comment, nameof(Comment));
        OperatorColour = Pack(Operator, nameof(Operator));
        ErrorColour = Pack(Error, nameof(Error));
        FunctionColour = Pack(Function, nameof(Function));
        MethodColour = Pack(Method, nameof(Method));
        ClassColour = Pack(Class, nameof(Class));
        FieldColour = Pack(Field, nameof(Field));
        LocalColour = Pack(Local, nameof(Local));
        ParameterColour = Pack(Parameter, nameof(Parameter));
        LoopVariableColour = Pack(LoopVariable, nameof(LoopVariable));
        GlobalUndocumentedColour = Pack(GlobalUndocumented, nameof(GlobalUndocumented));
        TextColour = Pack(Text, nameof(Text));
        CaretColour = Pack(Caret, nameof(Caret));
        SelectionColour = Pack(Selection, nameof(Selection));
        CurrentLineColour = Pack(CurrentLine, nameof(CurrentLine));
        LineNumberColour = Pack(LineNumber, nameof(LineNumber));
        LineNumberCurrentColour = Pack(LineNumberCurrent, nameof(LineNumberCurrent));
        return this;
    }

    /// <summary>The colour for a semantic role, so callers switch on the role rather than repeating
    /// this mapping.</summary>
    public uint ColourFor(LuaEditorThemeRole role) => role switch
    {
        LuaEditorThemeRole.Keyword => KeywordColour,
        LuaEditorThemeRole.String => StringColour,
        LuaEditorThemeRole.Number => NumberColour,
        LuaEditorThemeRole.Comment => CommentColour,
        LuaEditorThemeRole.Operator => OperatorColour,
        LuaEditorThemeRole.Error => ErrorColour,
        LuaEditorThemeRole.Function => FunctionColour,
        LuaEditorThemeRole.Method => MethodColour,
        LuaEditorThemeRole.Class => ClassColour,
        LuaEditorThemeRole.Field => FieldColour,
        LuaEditorThemeRole.Local => LocalColour,
        LuaEditorThemeRole.Parameter => ParameterColour,
        LuaEditorThemeRole.LoopVariable => LoopVariableColour,
        LuaEditorThemeRole.GlobalUndocumented => GlobalUndocumentedColour,
        _ => TextColour,
    };

    /// <summary>
    /// <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c> to ImGui's packed layout, which is
    /// ABGR - alpha high, then blue, green, red. Writing these by hand is exactly how the previous
    /// constants ended up with red and blue transposed.
    /// </summary>
    private uint Pack(string value, string role)
    {
        string hex = value.Trim().TrimStart('#');
        byte a = 0xFF, r, g, b;
        try
        {
            switch (hex.Length)
            {
                case 3:
                    r = (byte)(Nibble(hex[0]) * 17); g = (byte)(Nibble(hex[1]) * 17); b = (byte)(Nibble(hex[2]) * 17);
                    break;
                case 6:
                    r = Byte(hex, 0); g = Byte(hex, 2); b = Byte(hex, 4);
                    break;
                case 8:
                    a = Byte(hex, 0); r = Byte(hex, 2); g = Byte(hex, 4); b = Byte(hex, 6);
                    break;
                default:
                    Problems.Add($"{role}: '{value}' is not #RGB, #RRGGBB or #AARRGGBB");
                    return 0xFFFF00FF;
            }
        }
        catch (FormatException)
        {
            Problems.Add($"{role}: '{value}' is not a hex colour");
            return 0xFFFF00FF;
        }
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    }

    private static byte Byte(string hex, int at) =>
        byte.Parse(hex.AsSpan(at, 2), System.Globalization.NumberStyles.HexNumber);

    private static int Nibble(char c) =>
        int.Parse(c.ToString(), System.Globalization.NumberStyles.HexNumber);
}

/// <summary>What a run of text is, for colouring. Lexical roles come from the lexer; the rest need
/// scope resolution and the documented API.</summary>
public enum LuaEditorThemeRole
{
    Text,
    Keyword, String, Number, Comment, Operator, Error,
    Function, Method, Class, Field, Local, Parameter, LoopVariable, GlobalUndocumented,
}
