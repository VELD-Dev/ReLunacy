using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.CodeEditor;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>
/// Draws a signature with the editor's colours, so <c>wait(duration?: number): nil</c> reads the
/// same way the call site reads: the name as a function, the parameter as a parameter, the type as
/// a type, the punctuation as punctuation.
///
/// The colours are the ones already in the theme rather than new slots of their own. A signature is
/// describing the same things the editor colours in the text, so a reader who has learnt what
/// yellow means in the file should not have to learn it again in the tooltip.
/// </summary>
public static class SignatureView
{
    public static void Draw(IReadOnlyList<LuaSignatureRun> runs, string kind, LuaEditorTheme theme)
    {
        bool first = true;
        foreach (var run in runs)
        {
            if (run.Text.Length == 0) continue;
            // Placed edge to edge: a signature's spacing is part of its text, so ImGui's own item
            // spacing would open gaps that are not in it.
            if (!first) ImGui.SameLine(0f, 0f);
            first = false;

            if (run.Text.Trim().Length == 0) { ImGui.TextUnformatted(run.Text); continue; }
            ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(ColourOf(run.Part, kind, theme)), run.Text);
        }
        if (first) ImGui.Spacing();
    }

    /// <summary>
    /// The colour for a piece. A name takes the colour of whatever it IS - the point of the kind
    /// being on the line at all - so a field's name reads as a field and a local's as a local.
    /// </summary>
    private static uint ColourOf(LuaSignaturePart part, string kind, LuaEditorTheme theme) => part switch
    {
        LuaSignaturePart.Kind => theme.LineNumberColour,
        LuaSignaturePart.Parameter => theme.ParameterColour,
        LuaSignaturePart.Type => theme.ClassColour,
        LuaSignaturePart.Operator => theme.OperatorColour,
        LuaSignaturePart.Name => kind switch
        {
            "function" or "method" => theme.FunctionColour,
            "class" => theme.ClassColour,
            "field" => theme.FieldColour,
            "local" => theme.LocalColour,
            "parameter" => theme.ParameterColour,
            "loop variable" => theme.LoopVariableColour,
            "keyword" => theme.KeywordColour,
            // A global is documented or it is not, and the undocumented colour is the one that
            // says which - the same distinction the editor draws in the text.
            "global" => theme.FieldColour,
            _ => theme.TextColour,
        },
        _ => theme.TextColour,
    };
}
