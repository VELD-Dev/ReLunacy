using System.Numerics;
using ReLunacy.Utility.CodeEditor;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The Lua source editor. Hosts <see cref="CodeEditorControl"/> and owns opening and saving.
///
/// Saving writes the document's own text back, which preserves the file's line endings, tabs and
/// trailing-newline state - the shipped scripts are CRLF with tabs and a fifth of them have no final
/// newline, and these sources get recompiled and written back into the archive, so a normalising
/// save would corrupt them.
///
/// Syntax highlighting, completion and rename attach to the control's hooks and land in the
/// following sub-steps; the widget carries them unwired so it is usable and reviewable on its own.
/// </summary>
public class LuaEditorFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private readonly CodeEditorControl _editor = new();
    private string _path = "";
    private string? _status;
    private bool _modified;

    public LuaEditorFrame()
    {
        FrameName = LM.Get("GUI_Frame_LuaEditor");
        // F2's handler lands in sub-step 3d, once scope resolution exists. Reporting the word for
        // now makes the plumbing visible rather than silently doing nothing.
        _editor.RenameRequested = (_, word) => _status = string.IsNullOrEmpty(word)
            ? LM.Get("GUI_Frame_LuaEditor_RenameNoWord")
            : string.Format(LM.Get("GUI_Frame_LuaEditor_RenamePending"), word);
    }

    /// <summary>Loads source already in memory - how the PSARC browser hands a script over without
    /// extracting it to disk first.</summary>
    public void Load(string text, string label)
    {
        _editor.SetText(text);
        _path = label;
        _modified = false;
        _status = null;
    }

    protected override void Render(double deltaTime)
    {
        ImGui.SetNextItemWidth(-220f);
        ImGui.InputText(LM.Get("GUI_Frame_LuaEditor_Path"), ref _path, 1024);
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_LuaEditor_Open")))
        {
            if (File.Exists(_path))
            {
                Load(File.ReadAllText(_path), _path);
                _status = string.Format(LM.Get("GUI_Frame_LuaEditor_Opened"), _editor.Document.LineCount);
            }
            else _status = string.Format(LM.Get("GUI_Frame_LuaEditor_NoFile"), _path);
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_LuaEditor_Save")))
        {
            try
            {
                // WriteAllText with the document's own text: no re-encoding, no added newline.
                File.WriteAllText(_path, _editor.Document.GetText());
                _modified = false;
                _status = string.Format(LM.Get("GUI_Frame_LuaEditor_Saved"), _path);
            }
            catch (Exception e) { _status = e.Message; }
        }

        var doc = _editor.Document;
        ImGui.Text($"{doc.LineCount} lines  {(doc.LineEnding == "\r\n" ? "CRLF" : "LF")}" +
                   $"{(doc.EndsWithNewline ? "" : "  no trailing newline")}" +
                   $"  |  {_editor.Caret}{(_modified ? "  *" : "")}");
        if (_status is { } s) { ImGui.SameLine(); ImGui.TextDisabled($"|  {s}"); }

        _editor.Draw("lua_editor", new Vector2(0f, -ImGui.GetFrameHeightWithSpacing()), deltaTime);
        if (_editor.TextChanged) _modified = true;

        ImGui.TextDisabled(LM.Get("GUI_Frame_LuaEditor_Hint"));
    }
}
