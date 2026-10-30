using System.Numerics;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.CodeEditor;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>
/// The results of "find references": a list under the editor, each row a place the name is written.
///
/// Under the editor rather than in a tab of its own, because a result is only useful next to the
/// code it points at - clicking one has to move the caret somewhere still visible. A tab would hide
/// the thing the list is for.
/// </summary>
public sealed class LuaReferencesView
{
    private readonly List<LuaReference> _hits = [];
    private string _title = "";
    private int _selected = -1;

    /// <summary>Raised when a row is picked: the file to show and where in it.</summary>
    public Action<LuaReference>? Activated { get; set; }

    public bool HasResults => _hits.Count > 0;

    public void Show(string name, IReadOnlyList<LuaReference> hits)
    {
        _hits.Clear();
        _hits.AddRange(hits);
        _selected = -1;

        int files = _hits.Select(h => h.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _title = files > 1
            ? string.Format(LM.Get("GUI_LuaEnv_RefsCountFiles"), _hits.Count, name, files)
            : string.Format(LM.Get("GUI_LuaEnv_RefsCount"), _hits.Count, name);
    }

    /// <summary>Shows a message with no rows - "no references found" is a result too, and a panel
    /// that simply does not appear reads as the command having failed.</summary>
    public void ShowMessage(string message)
    {
        _hits.Clear();
        _selected = -1;
        _title = message;
    }

    public void Clear()
    {
        _hits.Clear();
        _title = "";
        _selected = -1;
    }

    public void Render(float height)
    {
        if (_title.Length == 0) return;

        ImGui.Separator();
        ImGui.TextUnformatted(_title);
        ImGui.SameLine();
        if (ImGui.SmallButton(LM.Get("GUI_LuaEnv_RefsClose"))) { Clear(); return; }

        if (!ImGui.BeginChild("lua_refs", new Vector2(0f, height), ImGuiChildFlags.Borders))
        {
            ImGui.EndChild();
            return;
        }

        for (int i = 0; i < _hits.Count; i++)
        {
            var hit = _hits[i];
            // The file name only when there is more than one file in play, so the common
            // single-file case is not padded out with the same path on every row.
            string where = $"{hit.Span.Start.Line + 1}:{hit.Span.Start.Column + 1}";
            string label = $"{where}  {hit.LineText.Trim()}";

            if (hit.IsDeclaration)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.86f, 0.86f, 0.67f, 1f));
            if (ImGui.Selectable($"{label}###ref{i}", _selected == i))
            {
                _selected = i;
                Activated?.Invoke(hit);
            }
            if (hit.IsDeclaration) ImGui.PopStyleColor();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{hit.Path}:{where}\n{hit.LineText.Trim()}");
        }

        ImGui.EndChild();
    }
}
