using System.Numerics;
using System.Text.Json.Nodes;
using ReLunacy.Core.Project;
using ReLunacy.Core.Undo;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Projects;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// View > Script Graph: a Fall of Man level's ps3script.dat (<see cref="FallOfManScripts"/>) as node graphs, one per
/// script, viewed and edited. The palette, titles and field editors come from RFallOfMan.fields (every ScriptElem class
/// with its size, label and fields), so a class documented there needs nothing here.
///
/// Edits go on the editor's undo and save into the open project, with where the nodes were placed (editor data
/// "scriptGraph").
/// </summary>
public sealed partial class ScriptGraphFrame : DockedFrame, IDisposable
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private const string FileName = "ps3script.dat";
    private const uint ConditionClass = 0xFA, NuggetClass = 0x224, ScriptBaseClass = 0xE9;
    private static readonly GameId Game = GameId.RFallOfMan;

    /// <summary>Everything an undo step puts back: the file's bytes and every node's place, by setup and script index.</summary>
    private sealed record State(FileManager Files, byte[] Bytes, List<List<Vector2[]>> Layout, int Setup, int Script);

    private FileManager? _files;
    private LunaProject? _project;
    private bool _loaded;
    private string? _archive, _entry;
    private string _level = "";
    private FallOfManScripts? _scripts;
    private string? _error, _status;
    private byte[] _saved = [];
    private State? _state;
    private bool _bytesDirty, _layoutTouched;
    private Dictionary<ScriptElement, Vector2> _positions = [];
    /// <summary>Classes some element of the file branches from at +0x08 without being a condition.</summary>
    private readonly HashSet<uint> _elseClasses = [];

    private int _setupIndex;
    private ScriptRecord? _script;
    private ScriptElement? _selected;
    public bool Focused { get; private set; }

    public ScriptGraphFrame()
    {
        FrameName = LM.Get("GUI_Frame_ScriptGraph");
    }

    private ScriptSetup? Setup => _scripts != null && _setupIndex < _scripts.Setups.Count ? _scripts.Setups[_setupIndex] : null;

    public bool Dirty => _scripts != null && (_bytesDirty || _layoutTouched);

    public void Revert() => Load();

    public void Dispose()
    {
        if (!Save()) isOpen = true;
    }

    // ---------------------------------------------------------------- loading and saving

    private void Load()
    {
        var window = LunaWindow.Instance;
        _loaded = true;
        _files = window.fileManager;
        _project = ProjectSession.Current;
        _scripts = null;
        _error = _status = null;
        _state = null;
        _positions = [];
        _script = null;
        _selected = null;
        _bytesDirty = _layoutTouched = false;
        _elseClasses.Clear();
        if (_files == null || window.Level?.GameDefinition?.Id != Game) return;
        _level = ConfigDocument.LevelName;
        var located = _files.Locate(FileName);
        _archive = located?.ArchivePath;
        _entry = located?.EntryPath;
        try
        {
            byte[] bytes = _files.ReadOriginal(FileName) ?? throw new IOException(LM.Get("GUI_ScriptGraph_NoFile"));
            _scripts = FallOfManScripts.Parse(bytes);
            // Compared in the writer's own layout, so a file read and not changed is not dirty.
            _saved = _scripts.ToBytes();
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            _scripts = null;
            _error = e.Message;
            return;
        }
        foreach (var element in _scripts.Setups.SelectMany(s => s.Elements))
            if (element.Else != null && !IsCondition(element.ClassId)) _elseClasses.Add(element.ClassId);
        ReadLayout();
        // The setup the loaded region runs, when there are several (multiplayer modes).
        var regions = Engine.Scene.EntityManager.Singleton.Regions;
        _setupIndex = Math.Max(0, _scripts.Setups.FindIndex(s => regions.Any(r => r.RegionName == s.Name)));
        SelectScript(Setup?.Scripts.FirstOrDefault());
        _state = Capture(_saved);
    }

    public bool Save()
    {
        if (!Dirty) return true;
        if (_project is not { } project || !ReferenceEquals(project, ProjectSession.Current))
        {
            _status = LM.Get("GUI_ScriptGraph_NoProject");
            return false;
        }
        try
        {
            if (_bytesDirty)
            {
                if (_archive == null || _entry == null)
                {
                    _status = LM.Get("GUI_ScriptGraph_NoArchive");
                    return false;
                }
                byte[] bytes = _scripts!.ToCheckedBytes();
                var result = ArchiveEntryWriter.Replace(_archive, new Dictionary<string, byte[]> { [_entry] = bytes }, project.Sink);
                _status = result.Summary;
                if (!result.Succeeded) return false;
                _saved = bytes;
                _bytesDirty = false;
            }
            if (_layoutTouched)
            {
                project.SetScriptGraphLayout(_level, LayoutJson());
                _layoutTouched = false;
            }
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _status = e.Message;
            return false;
        }
    }

    // ---------------------------------------------------------------- undo

    private State Capture(byte[] bytes) => new(_files!, bytes,
        _scripts!.Setups.Select(s => s.Scripts.Select(r => r.Elements
            .Select(e => _positions.TryGetValue(e, out var p) ? p : new Vector2(float.NaN)).ToArray()).ToList()).ToList(),
        _setupIndex, Setup?.Scripts.IndexOf(_script!) ?? -1);

    /// <summary>Ends an edit: the model as it is now becomes one undo step.</summary>
    private void Commit(string labelKey)
    {
        if (_scripts == null || _state == null) return;
        byte[] bytes;
        try { bytes = _scripts.ToBytes(); }
        catch (InvalidDataException e)
        {
            _status = e.Message;
            return;
        }
        var before = _state;
        var after = Capture(bytes);
        if (before.Bytes.AsSpan().SequenceEqual(bytes) && before.Setup == after.Setup && before.Script == after.Script
            && before.Layout.SelectMany(s => s).SelectMany(p => p).SequenceEqual(after.Layout.SelectMany(s => s).SelectMany(p => p)))
            return;
        _state = after;
        _bytesDirty = !bytes.AsSpan().SequenceEqual(_saved);
        UndoManager.Singleton.Push(LM.Get(labelKey), () => Restore(before), () => Restore(after));
    }

    private void Restore(State state)
    {
        if (_scripts == null || !ReferenceEquals(state.Files, _files)) return;
        try { _scripts = FallOfManScripts.Parse(state.Bytes); }
        catch (InvalidDataException e)
        {
            _status = e.Message;
            return;
        }
        _positions = [];
        for (int s = 0; s < _scripts.Setups.Count && s < state.Layout.Count; s++)
            for (int r = 0; r < _scripts.Setups[s].Scripts.Count && r < state.Layout[s].Count; r++)
            {
                var elements = _scripts.Setups[s].Scripts[r].Elements;
                var places = state.Layout[s][r];
                for (int i = 0; i < elements.Count && i < places.Length; i++)
                    if (!float.IsNaN(places[i].X)) _positions[elements[i]] = places[i];
            }
        _state = state;
        _bytesDirty = !state.Bytes.AsSpan().SequenceEqual(_saved);
        _layoutTouched = true;
        _setupIndex = Math.Clamp(state.Setup, 0, Math.Max(0, _scripts.Setups.Count - 1));
        var scripts = Setup?.Scripts;
        _script = scripts is { Count: > 0 } ? scripts[Math.Clamp(state.Script, 0, scripts.Count - 1)] : null;
        _selected = null;
        if (_script != null) EnsureLayout(_script);
    }

    // ---------------------------------------------------------------- layout

    private static string LayoutKey(ScriptSetup setup, ScriptRecord script) => $"{setup.Name}/{script.Name}";

    private void ReadLayout()
    {
        if (_project?.ScriptGraphLayout(_level) is not { } layout) return;
        foreach (var setup in _scripts!.Setups)
            foreach (var script in setup.Scripts)
            {
                if (layout[LayoutKey(setup, script)] is not JsonArray places || places.Count != 2 * script.Elements.Count) continue;
                for (int i = 0; i < script.Elements.Count; i++)
                    _positions[script.Elements[i]] = new Vector2((float?)places[2 * i] ?? 0f, (float?)places[2 * i + 1] ?? 0f);
            }
    }

    private JsonObject LayoutJson()
    {
        var layout = new JsonObject();
        foreach (var setup in _scripts!.Setups)
            foreach (var script in setup.Scripts)
            {
                if (!script.Elements.All(_positions.ContainsKey)) continue;
                var places = new JsonArray();
                foreach (var element in script.Elements)
                {
                    var p = _positions[element];
                    places.Add(MathF.Round(p.X));
                    places.Add(MathF.Round(p.Y));
                }
                layout[LayoutKey(setup, script)] = places;
            }
        return layout;
    }

    /// <summary>Places a script's nodes that have no place yet: all of them in columns, or a new one beside the rest.</summary>
    private void EnsureLayout(ScriptRecord script)
    {
        if (script.Elements.All(_positions.ContainsKey)) return;
        if (!script.Elements.Any(_positions.ContainsKey))
        {
            AutoLayout(script);
            return;
        }
        float right = script.Elements.Where(_positions.ContainsKey).Max(e => _positions[e].X) + ColumnGap;
        float y = 0f;
        foreach (var element in script.Elements.Where(e => !_positions.ContainsKey(e)))
        {
            _positions[element] = new Vector2(right, y);
            y += RowGap;
        }
    }

    private const float ColumnGap = 260f, RowGap = 120f;

    /// <summary>Columns by distance from Start; nodes Start never reaches below, End last.</summary>
    private void AutoLayout(ScriptRecord script)
    {
        var depth = new Dictionary<ScriptElement, int>();
        var rows = new Dictionary<int, int>();
        float top = 0f;
        void Place(ScriptElement root, float baseY)
        {
            var queue = new Queue<ScriptElement>();
            depth[root] = 0;
            queue.Enqueue(root);
            float lowest = baseY;
            rows.Clear();
            while (queue.Count > 0)
            {
                var element = queue.Dequeue();
                int column = depth[element];
                int row = rows.GetValueOrDefault(column);
                rows[column] = row + 1;
                var place = new Vector2(column * ColumnGap, baseY + row * RowGap);
                _positions[element] = place;
                lowest = MathF.Max(lowest, place.Y);
                foreach (var (_, target) in element.Links())
                    if (target != script.End && depth.TryAdd(target, column + 1)) queue.Enqueue(target);
            }
            top = lowest + RowGap * 1.5f;
        }
        Place(script.Start, 0f);
        int last = depth.Count > 0 ? depth.Values.Max() : 0;
        foreach (var element in script.Elements)
            if (element != script.End && !depth.ContainsKey(element)) Place(element, top);
        _positions[script.End] = new Vector2((last + 1) * ColumnGap, 0f);
        _fit = true;
    }

    // ---------------------------------------------------------------- classes

    private static bool IsCondition(uint cls) => ClassFields.IsA(Game, cls, ConditionClass);

    private bool HasElse(ScriptElement element) => IsCondition(element.ClassId) || _elseClasses.Contains(element.ClassId) || element.Else != null;

    private static string Title(uint cls) =>
        ClassFields.Label(Game, cls) ?? ClassFields.ClassName(Game, cls)?.Replace("ScriptElem_", "") ?? $"0x{cls:X}";

    private static List<(uint Id, string Title, int Group)>? _palette;

    /// <summary>Every class a node can be: the script element classes nothing derives from (Start and End aside),
    /// grouped as condition, Truth Nugget or command.</summary>
    private static List<(uint Id, string Title, int Group)> Palette()
    {
        if (_palette != null) return _palette;
        var classes = ClassFields.Classes(Game);
        var parents = new HashSet<uint>(classes.SelectMany(c => ClassFields.Chain(Game, c).Skip(1)));
        return _palette = classes
            .Where(c => c is not (ScriptRecord.StartClass or ScriptRecord.EndClass) && !parents.Contains(c)
                        && ClassFields.IsA(Game, c, ScriptBaseClass) && ClassFields.SizeOf(Game, c) != null)
            .Select(c => (c, Title(c), ClassFields.IsA(Game, c, NuggetClass) ? 1 : IsCondition(c) ? 0 : 2))
            .OrderBy(p => p.Item3).ThenBy(p => p.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------------------------------------------------------------- edits

    private void SelectScript(ScriptRecord? script)
    {
        _script = script;
        _selected = null;
        _rename = script?.Name ?? "";
        if (script != null) EnsureLayout(script);
        _fit = true;
    }

    private ScriptElement? AddNode(uint cls, Vector2 place, (ScriptElement From, int Offset)? link)
    {
        if (_script == null || ClassFields.SizeOf(Game, cls) is not { } size) return null;
        var element = ScriptElement.Create(cls, size);
        // Instance indexes start as none.
        foreach (var field in ClassFields.Of(Game, cls))
            if (field is { Kind: ClassFieldKind.Enum, TypeName: "ScriptTarget" or "ScriptArea" } && field.Offset + 8 <= size)
                element.SetWord(field.Offset + 4, uint.MaxValue);
        _script.Add(element);
        _positions[element] = place;
        if (link is { } l) l.From.SetPointer(l.Offset, new ScriptElementPointer(element));
        _selected = element;
        _layoutTouched = true;
        Commit("GUI_ScriptGraph_UndoAdd");
        return element;
    }

    private void RemoveNode(ScriptElement element)
    {
        if (_script == null || element == _script.Start || element == _script.End) return;
        _script.Remove(element);
        _positions.Remove(element);
        if (_selected == element) _selected = null;
        Commit("GUI_ScriptGraph_UndoRemove");
    }

    /// <summary>Points a branch at a node; End or nothing finishes the script there, as the shipped scripts do.</summary>
    private void Link(ScriptElement from, int offset, ScriptElement? to)
    {
        if (_script == null) return;
        from.SetPointer(offset, to == null || to == _script.End || to == _script.Start ? null : new ScriptElementPointer(to));
        Commit("GUI_ScriptGraph_UndoLink");
    }

    private void AddScript()
    {
        if (Setup is not { } setup) return;
        string name = LM.Get("GUI_ScriptGraph_NewScriptName");
        for (int n = 2; setup.Scripts.Any(s => s.Name == name); n++) name = $"{LM.Get("GUI_ScriptGraph_NewScriptName")} {n}";
        var script = ScriptRecord.Create(name);
        setup.Scripts.Add(script);
        _positions[script.Start] = Vector2.Zero;
        _positions[script.End] = new Vector2(ColumnGap, 0f);
        SelectScript(script);
        _layoutTouched = true;
        Commit("GUI_ScriptGraph_UndoScript");
    }

    private void DeleteScript(ScriptRecord script)
    {
        if (Setup is not { } setup) return;
        int index = setup.Scripts.IndexOf(script);
        setup.Scripts.Remove(script);
        foreach (var element in script.Elements) _positions.Remove(element);
        SelectScript(setup.Scripts.Count > 0 ? setup.Scripts[Math.Clamp(index, 0, setup.Scripts.Count - 1)] : null);
        Commit("GUI_ScriptGraph_UndoScript");
    }

    /// <summary>Renames a script and the WaitOnScriptEnd nodes that name it (the game compares the name pointer).</summary>
    private bool RenameScript(ScriptRecord script, string name)
    {
        if (Setup is not { } setup || name.Length == 0 || name == script.Name) return false;
        if (setup.Scripts.Any(s => s != script && s.Name == name))
        {
            _status = LM.Get("GUI_ScriptGraph_NameTaken");
            return false;
        }
        foreach (var element in setup.Elements)
            foreach (var (offset, pointer) in element.Pointers.ToList())
                if (pointer is ScriptStringPointer text && text.Text == script.Name
                    && ClassFields.At(Game, element.ClassId, offset)?.TypeName == "Script")
                    element.SetPointer(offset, new ScriptStringPointer(name));
        script.Name = name;
        _layoutTouched = true;
        Commit("GUI_ScriptGraph_UndoScript");
        return true;
    }

    /// <summary>A new variable: one more f32 slot in the block the setup's nodes use most (a new block when none).</summary>
    private ScriptVariablePointer AddVariable()
    {
        var used = Setup?.Elements.SelectMany(e => e.Pointers.Values).OfType<ScriptVariablePointer>()
            .GroupBy(v => v.Block).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var block = used ?? _scripts!.Variables.FirstOrDefault();
        if (block == null) _scripts!.Variables.Add(block = new ScriptVariableBlock([]));
        block.Data = [.. block.Data, 0, 0, 0, 0];
        return new ScriptVariablePointer(block, block.Data.Length - 4);
    }

    private string VariableName(ScriptVariableBlock block, int offset)
    {
        int index = _scripts!.Variables.IndexOf(block);
        return _scripts.Variables.Count > 1 ? $"v{index}.{offset / 4}" : $"v{offset / 4}";
    }

    /// <summary>Nodes Start leads to, End always counted.</summary>
    private static HashSet<ScriptElement> Reachable(ScriptRecord script)
    {
        var seen = new HashSet<ScriptElement> { script.Start, script.End };
        var stack = new Stack<ScriptElement>([script.Start]);
        while (stack.Count > 0)
            foreach (var (_, target) in stack.Pop().Links())
                if (seen.Add(target)) stack.Push(target);
        return seen;
    }

    // ---------------------------------------------------------------- frame

    protected override void Render(double deltaTime)
    {
        Focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        var window = LunaWindow.Instance;
        if (!_loaded || !ReferenceEquals(_files, window.fileManager) || !ReferenceEquals(_project, ProjectSession.Current)) Load();
        if (_files == null || window.Level?.GameDefinition?.Id != Game)
        {
            ImGui.TextDisabled(LM.Get("GUI_ScriptGraph_NotFallOfMan"));
            return;
        }
        if (_scripts == null)
        {
            ImGui.TextWrapped(_error ?? LM.Get("GUI_ScriptGraph_NoFile"));
            if (ImGui.Button(LM.Get("GUI_ScriptGraph_Reload"))) Load();
            return;
        }

        RenderToolbar();
        float side = 240f * ImGui.GetFontSize() / 13f, inspector = 320f * ImGui.GetFontSize() / 13f;
        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##scripts", new Vector2(side, avail.Y), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
            RenderScriptList();
        ImGui.EndChild();
        ImGui.SameLine();
        float canvas = MathF.Max(80f, ImGui.GetContentRegionAvail().X - inspector - ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.BeginChild("##canvas", new Vector2(canvas, avail.Y), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            RenderCanvas();
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##inspector", new Vector2(0f, avail.Y), ImGuiChildFlags.Borders))
            RenderInspector();
        ImGui.EndChild();
    }

    private void RenderToolbar()
    {
        bool dirty = Dirty;
        ImGui.BeginDisabled(!dirty);
        if (ImGuiPlus.IconButton(Icons.Save, "save_scripts")) Save();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_ConfigTables_Save") + " (Ctrl+S)");
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_ConfigTables_Revert"))) Load();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("##setup", Setup?.Name ?? ""))
        {
            for (int i = 0; i < _scripts!.Setups.Count; i++)
                if (ImGui.Selectable($"{_scripts.Setups[i].Name}##{i}", i == _setupIndex))
                {
                    _setupIndex = i;
                    SelectScript(Setup?.Scripts.FirstOrDefault());
                }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ScriptGraph_Setup"));
        ImGui.SameLine();
        ImGui.BeginDisabled(_script == null);
        if (ImGuiPlus.IconButton(Icons.VectorSquare, "fit_graph")) _fit = true;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_ScriptGraph_Fit"));
        ImGui.SameLine();
        if (ImGuiPlus.IconButton(Icons.Grid, "arrange_graph") && _script != null)
        {
            AutoLayout(_script);
            _layoutTouched = true;
            Commit("GUI_ScriptGraph_UndoMove");
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_ScriptGraph_AutoLayout"));
        ImGui.EndDisabled();
        if (_status != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(_status);
        }
    }
}
