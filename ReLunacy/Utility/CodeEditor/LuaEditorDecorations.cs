using ReLunacy.Engine.Scripting;

namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// Works out what to paint behind the text: the delimiter pair enclosing the caret, the snippet
/// stop being filled in, and the name under the caret together with its other mentions.
///
/// All three answer the same question - "what am I inside of right now" - which is why they are one
/// function rather than three hooks. It is pure, taking the document and the caret rather than
/// reading an editor, so the regions it produces can be checked against text without a frame; the
/// off-by-one that makes a bracket highlight land on the wrong character is invisible by eye and
/// obvious in a test.
/// </summary>
public static class LuaEditorDecorations
{
    /// <summary>
    /// Everything to paint, innermost meaning last: a later highlight is drawn over an earlier one,
    /// so the name under the caret shows through the bracket pair that contains it rather than the
    /// other way round.
    /// </summary>
    public static List<TextHighlight> Build(
        TextDocument document, TextPosition caret, LuaEditorTheme theme,
        TextPosition? snippetStop = null, LuaScopeAnalysis? scopes = null)
    {
        var highlights = new List<TextHighlight>();

        if (LuaDelimiterRules.Enclosing(document.GetText(), caret) is { } pair)
        {
            highlights.Add(new TextHighlight(pair.Open.Line, pair.Open.Column, 1,
                                             theme.MatchingDelimiterColour));
            highlights.Add(new TextHighlight(pair.Close.Line, pair.Close.Column, 1,
                                             theme.MatchingDelimiterColour));
        }

        // A stop is an insertion point, not a range, so it is drawn as an empty region - which the
        // editor shows a character wide, the same width as the caret's own block.
        if (snippetStop is { } stop)
            highlights.Add(new TextHighlight(stop.Line, stop.Column, 0, theme.SnippetStopColour));

        if (scopes?.FindAt(new LuaPosition(caret.Line, caret.Column)) is { } symbol)
            foreach (var reference in symbol.References)
            {
                // A name cannot span lines, so one reference is one region. Anything that does span
                // lines is not a name and is skipped rather than painted across the gap.
                if (reference.Start.Line != reference.End.Line) continue;

                int length = reference.End.Column - reference.Start.Column;
                if (length <= 0) continue;

                bool underCaret = reference.Contains(new LuaPosition(caret.Line, caret.Column));
                highlights.Add(new TextHighlight(
                    reference.Start.Line, reference.Start.Column, length,
                    underCaret ? theme.CaretWordColour : theme.OccurrenceColour));
            }

        return highlights;
    }
}
