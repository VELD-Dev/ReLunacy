using System.Numerics;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// What the scripts say about an instance. Nothing outside ps3script.dat starts a script: every script of the
/// active setup steps each frame from the level's start (the ones whose first node is Run At Load in the load
/// pass), so a volume or a Moby takes part only by being named in a node: Player Is Inside, In Area, Is Dead...
/// The inspector lists those nodes; a click opens the graph on one.
/// </summary>
public sealed partial class ScriptGraphFrame
{
    public readonly record struct ScriptUse(int Setup, ScriptRecord Script, ScriptElement Element, string Field);

    // The scripts the inspector reads when no graph is open, by the level's files.
    private static FileManager? _usersFiles;
    private static FallOfManScripts? _usersScripts;
    // Bumped on every edit, so a list built before it is built again.
    private static int _revision;

    /// <summary>The scripts as the graph has them (edits included), else as the level's file has them.</summary>
    private static FallOfManScripts? CurrentScripts()
    {
        var window = LunaWindow.Instance;
        if (window.GetFirstFrame<ScriptGraphFrame>() is { _scripts: { } open } frame && ReferenceEquals(frame._files, window.fileManager))
            return open;
        if (window.fileManager is not { } files || window.Level?.GameDefinition?.Id != Game) return null;
        if (!ReferenceEquals(files, _usersFiles))
        {
            _usersFiles = files;
            try { _usersScripts = files.ReadOriginal(FileName) is { } bytes ? FallOfManScripts.Parse(bytes) : null; }
            catch (Exception e) when (e is IOException or InvalidDataException) { _usersScripts = null; }
        }
        return _usersScripts;
    }

    /// <summary>Every instance a node names: its targets and areas, and the fields typed as a reference.</summary>
    private static IEnumerable<(string Kind, int Index, ClassField Field)> References(ScriptElement element)
    {
        foreach (var field in ClassFields.Of(Game, element.ClassId))
        {
            if (field.Offset + 4 > element.Data.Length) continue;
            if (field is { Kind: ClassFieldKind.Enum, TypeName: "ScriptTarget" or "ScriptArea" } && field.Offset + 8 <= element.Data.Length)
            {
                bool target = field.TypeName == "ScriptTarget";
                int kind = (int)element.Word(field.Offset);
                yield return (KindName(target, kind), IndexOf(target, kind, element.Word(field.Offset + 4)),
                    ClassFields.At(Game, element.ClassId, field.Offset + 4) ?? field);
            }
            else if (field is { Kind: ClassFieldKind.Reference, TypeName: { } kind })
                yield return (kind, OldClassReferences.Decode(kind, element.Word(field.Offset)), field);
        }
    }

    /// <summary>The nodes that name an instance, in the setup a region of that name runs (the first when none is).</summary>
    public static List<ScriptUse> UsersOf(string region, string kind, int index)
    {
        var uses = new List<ScriptUse>();
        if (index < 0 || CurrentScripts() is not { } scripts) return uses;
        int setup = Math.Max(0, scripts.Setups.FindIndex(s => s.Name == region));
        if (setup >= scripts.Setups.Count) return uses;
        foreach (var script in scripts.Setups[setup].Scripts)
            foreach (var element in script.Elements)
                foreach (var (k, at, field) in References(element))
                    if (k == kind && at == index)
                        uses.Add(new ScriptUse(setup, script, element, field.Name));
        return uses;
    }

    private static object? _shownFor;
    private static int _shownRevision = -1;
    private static List<ScriptUse> _shown = [];

    /// <summary>The inspector's "Used by scripts" list for an instance: each node, a click opens the graph on it.</summary>
    public static void RenderUsers(object entity, string region, string kind, int index)
    {
        if (!ReferenceEquals(entity, _shownFor) || _shownRevision != _revision)
        {
            _shownFor = entity;
            _shownRevision = _revision;
            _shown = UsersOf(region, kind, index);
        }
        if (_shown.Count == 0) return;
        ImGui.TextUnformatted(LM.Get("GUI_ScriptGraph_UsedBy"));
        for (int i = 0; i < _shown.Count; i++)
        {
            var use = _shown[i];
            ImGui.PushID(i);
            ImGui.Bullet();
            if (ImGui.Selectable($"{use.Script.Name}: {Title(use.Element.ClassId)}"))
                Reveal(use);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ScriptGraph_UsedByOpen"));
            if (use.Field.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(use.Field);
            }
            ImGui.PopID();
        }
    }

    // The node the canvas centres on once it has a size.
    private ScriptElement? _reveal;

    /// <summary>Opens the graph (a new one when none is) on a node: its setup, its script, the node selected and centred.</summary>
    private static void Reveal(ScriptUse use)
    {
        var window = LunaWindow.Instance;
        var frame = window.GetFirstFrame<ScriptGraphFrame>();
        if (frame == null) window.AddFrame(frame = new ScriptGraphFrame());
        if (!frame._loaded || !ReferenceEquals(frame._files, window.fileManager)) frame.Load();
        if (frame._scripts is not { } scripts || use.Setup >= scripts.Setups.Count) return;
        // The list may come from the file while the graph read its own copy: the same place in it.
        var setup = scripts.Setups[use.Setup];
        int scriptAt = CurrentScripts() is { } source && use.Setup < source.Setups.Count ? source.Setups[use.Setup].Scripts.IndexOf(use.Script) : -1;
        var script = scriptAt >= 0 && scriptAt < setup.Scripts.Count ? setup.Scripts[scriptAt] : setup.Scripts.FirstOrDefault(s => s.Name == use.Script.Name);
        if (script == null) return;
        int elementAt = use.Script.Elements.IndexOf(use.Element);
        frame._setupIndex = use.Setup;
        frame.SelectScript(script);
        frame._selected = elementAt >= 0 && elementAt < script.Elements.Count ? script.Elements[elementAt] : null;
        frame._reveal = frame._selected;
        frame.Focus();
    }

    /// <summary>Centres the canvas on the node <see cref="Reveal"/> asked for, at full size.</summary>
    private void CentreOnReveal(Vector2 size, Vector2 nodeSize)
    {
        if (_reveal == null || !_positions.TryGetValue(_reveal, out var place)) return;
        _fit = false;
        _zoom = 1f;
        _pan = size * 0.5f - (place + nodeSize * 0.5f) * _zoom;
        _reveal = null;
    }
}
