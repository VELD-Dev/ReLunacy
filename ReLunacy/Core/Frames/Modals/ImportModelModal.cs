using System.Numerics;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Placement;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Importing;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>
/// File > Import > Model: a glTF becomes a new Tie class (ModelImportSession). Name, scale, flip V and region, the material to
/// shader table (pre-filled by the shader catalog's auto-match), the collision source and its surface types, the numbers of the
/// built class and what the importer, splitter, builder and cooker had to say. The class is queued for the next save; the
/// source files are copied into the project.
/// </summary>
public sealed class ImportModelModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private static readonly Vector4 Bad = new(1f, 0.45f, 0.4f, 1f), Warn = new(1f, 0.7f, 0.3f, 1f);

    /// <summary>The level can take imported Ties.</summary>
    public static bool Available => LunaWindow.Instance is { Level: { } level, Reader: not null }
        && ModelImportSession.Supported(level) && EntityManager.Singleton.Regions.Count > 0;

    /// <summary>Asks for the model file, then opens the modal on it.</summary>
    public static void Choose()
    {
        if (!Available) return;
        string? path = null;
        try
        {
            var result = NativeFileDialogSharp.Dialog.FileOpen("gltf,glb");
            if (result.IsCancelled) return;
            if (result.IsOk) path = result.Path;
        }
        catch (Exception e)
        {
            // No native dialog on this platform: the modal takes a typed path.
            LunaLog.LogWarn($"File dialog unavailable: {e.Message}");
        }
        Open(path ?? "");
    }

    public static void Open(string path)
    {
        if (!Available || LunaWindow.Instance.IsAnyFrameOpened<ImportModelModal>()) return;
        var regions = EntityManager.Singleton.Regions.ToList();
        var window = LunaWindow.Instance;
        var region = InstancePlacer.TargetRegion() ?? regions[0];
        LunaWindow.Instance.AddFrame(new ImportModelModal(new ModelImportSession(window.fileManager!, window.Level!, region), regions, path));
    }

    private readonly ModelImportSession _session;
    private readonly List<EntityRegion> _regions;
    private Task? _job;
    private bool _parseDirty, _buildDirty, _firstParse = true, _place;
    private string? _error;
    private string _filter = "";
    private string _path;
    private string _collisionPath = "";
    private string _name = "";
    private float _scale = 1f;

    private ImportModelModal(ModelImportSession session, List<EntityRegion> regions, string path)
    {
        FrameName = LM.Get("GUI_ImportModel_Title");
        _session = session;
        _regions = regions;
        _path = path;
        session.ModelPath = path;
        _parseDirty = path.Length > 0;
    }

    private bool Busy => _job is { IsCompleted: false };

    private void StartJobs()
    {
        if (_job is { IsCompleted: true })
        {
            if (_job.Exception is { } e) _error = e.GetBaseException().Message;
            _job = null;
        }
        if (Busy) return;
        if (_parseDirty)
        {
            _parseDirty = _buildDirty = false;
            _error = null;
            _job = Task.Run(() =>
            {
                _session.Parse();
                if (_firstParse && _session.Parsed != null)
                {
                    _firstParse = false;
                    if (_session.NodeCollisionTriangles == 0) _session.Source = ModelCollisionSource.None;
                }
                if (_session.Parsed != null) _session.Build();
            });
        }
        else if (_buildDirty)
        {
            _buildDirty = false;
            _job = Task.Run(_session.Build);
        }
    }

    protected override void Render(double deltaTime)
    {
        StartJobs();
        bool busy = Busy;
        var s = _session;

        ImGui.BeginDisabled(busy);
        DrawFiles();
        if (s.Parsed != null)
        {
            DrawOptions();
            DrawMaterials();
            DrawCollision();
        }
        else if (_path.Length == 0) ImGui.TextDisabled(LM.Get("GUI_ImportModel_NoFile"));
        ImGui.EndDisabled();

        if (busy) ImGui.TextDisabled($"{LM.Get("GUI_ImportModel_Working")} {Icons.Refresh}");
        DrawResult();
        ImGui.Spacing();

        bool ready = !busy && !_parseDirty && !_buildDirty && s.CanCommit;
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button(LM.Get("GUI_ImportModel_Import"))) Import();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!ready || s.Region != InstancePlacer.TargetRegion());
        ImGui.Checkbox(LM.Get("GUI_ImportModel_Place"), ref _place);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_ImportModel_PlaceTip"));
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
    }

    // --- Files, options ------------------------------------------------------------------------------

    private void DrawFiles()
    {
        ImGui.SetNextItemWidth(360f);
        ImGui.InputTextWithHint("##model", LM.Get("GUI_ImportModel_Model"), ref _path, 512);
        if (ImGui.IsItemDeactivatedAfterEdit()) SetModel(_path.Trim().Trim('"'));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_ModelTip"));
        ImGui.SameLine();
        if (ImGui.Button($"...##model") && Browse() is { } picked) SetModel(picked);
        if (_session.ParseError != null) ImGui.TextColored(Bad, _session.ParseError);
    }

    private void SetModel(string path)
    {
        _path = path;
        _session.ModelPath = path;
        if (_name.Length == 0 || _name == _session.Name) _session.Name = _name = Path.GetFileNameWithoutExtension(path);
        _firstParse = true;
        _parseDirty = true;
    }

    private static string? Browse()
    {
        try
        {
            var result = NativeFileDialogSharp.Dialog.FileOpen("gltf,glb");
            return result.IsOk ? result.Path : null;
        }
        catch (Exception e)
        {
            LunaLog.LogWarn($"File dialog unavailable: {e.Message}");
            return null;
        }
    }

    private void DrawOptions()
    {
        var s = _session;
        if (_name.Length == 0) _name = s.Name;
        ImGui.SetNextItemWidth(200f);
        ImGui.InputText(LM.Get("GUI_ImportModel_Name"), ref _name, 63);
        if (ImGui.IsItemDeactivatedAfterEdit()) { s.Name = _name; _buildDirty = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_NameTip", s.Catalog.Template?.Reason ?? "-"));

        ImGui.SetNextItemWidth(200f);
        ImGui.InputFloat(LM.Get("GUI_ImportModel_Scale"), ref _scale, 0.1f, 1f, "%.3f");
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _scale = Math.Clamp(float.IsFinite(_scale) ? _scale : 1f, 0.001f, 1000f);
            s.Scale = _scale;
            _parseDirty = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_ScaleTip"));

        bool flip = s.FlipV;
        if (ImGui.Checkbox(LM.Get("GUI_ImportModel_FlipV"), ref flip)) { s.FlipV = flip; _parseDirty = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_FlipVTip"));

        ImGui.SetNextItemWidth(200f);
        ImGui.BeginDisabled(_regions.Count < 2);
        if (ImGui.BeginCombo(LM.Get("GUI_ImportModel_Region"), s.Region.RegionName))
        {
            foreach (var region in _regions)
                if (ImGui.Selectable(region.RegionName, ReferenceEquals(region, s.Region)) && !ReferenceEquals(region, s.Region))
                {
                    s.Region = region;
                    _parseDirty = true;
                }
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_ImportModel_RegionTip"));
    }

    // --- Materials -----------------------------------------------------------------------------------

    private void DrawMaterials()
    {
        var s = _session;
        var materials = s.Materials;
        if (!ImGui.CollapsingHeader(LM.Get("GUI_ImportModel_Materials", materials.Count), ImGuiTreeNodeFlags.DefaultOpen)) return;
        if (!ImGui.BeginTable("##materials", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg)) return;
        ImGui.TableSetupColumn(LM.Get("GUI_ImportModel_Material"), ImGuiTableColumnFlags.WidthFixed, 200f);
        ImGui.TableSetupColumn(LM.Get("GUI_ImportModel_Shader"), ImGuiTableColumnFlags.WidthFixed, 300f);
        ImGui.TableHeadersRow();
        for (int i = 0; i < materials.Count; i++)
        {
            var row = materials[i];
            ImGui.PushID(i);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            bool missing = row.ShaderTuid == 0;
            if (missing) ImGui.PushStyleColor(ImGuiCol.Text, Bad);
            ImGui.TextUnformatted(row.Info.Name.Length > 0 ? row.Info.Name : "-");
            if (missing) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(LM.Get("GUI_ImportModel_MaterialInfo", row.Info.TriangleCount, row.Info.BaseColorTextureName ?? "-"));
            ImGui.TableNextColumn();
            DrawShaderCombo(row);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void DrawShaderCombo(ImportMaterialRow row)
    {
        var catalog = _session.Catalog;
        var current = catalog.Find(row.ShaderTuid);
        string preview = current?.DisplayName ?? LM.Get("GUI_ImportModel_NoShader");
        ImGui.SetNextItemWidth(290f);
        bool open = ImGui.BeginCombo("##shader", preview);
        if (ImGui.IsItemHovered() && current != null)
            ImGui.SetTooltip(LM.Get("GUI_ImportModel_ShaderInfo", current.FullName, current.AlbedoName ?? "-"));
        if (!open) return;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) { ImGui.SetKeyboardFocusHere(); _filter = ""; }
        ImGui.InputTextWithHint("##filter", $"{Icons.Search}  {LM.Get("GUI_ImportModel_Search")}", ref _filter, 64);
        if (ImGui.BeginChild("##shaders", new Vector2(360f, 260f)))
        {
            bool proven = true;
            foreach (var candidate in catalog.Candidates)
            {
                if (_filter.Length > 0 && !candidate.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                    && !candidate.FullName.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                    && !(candidate.AlbedoName?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                if (proven && !candidate.TieProven) { proven = false; ImGui.Separator(); }
                string label = candidate.AlbedoName is { Length: > 0 } albedo ? $"{candidate.DisplayName}  [{albedo}]" : candidate.DisplayName;
                if (ImGui.Selectable($"{label}##{candidate.Tuid:X}", candidate.Tuid == row.ShaderTuid))
                {
                    row.ShaderTuid = candidate.Tuid;
                    _buildDirty = true;
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_ShaderInfo", candidate.FullName, candidate.AlbedoName ?? "-"));
            }
        }
        ImGui.EndChild();
        ImGui.EndCombo();
    }

    // --- Collision -----------------------------------------------------------------------------------

    private void DrawCollision()
    {
        var s = _session;
        if (!ImGui.CollapsingHeader(LM.Get("GUI_ImportModel_Collision"), ImGuiTreeNodeFlags.DefaultOpen)) return;
        SourceButton(ModelCollisionSource.Nodes, LM.Get("GUI_ImportModel_CollisionNodes", s.NodeCollisionTriangles), s.NodeCollisionTriangles > 0, false);
        SourceButton(ModelCollisionSource.File, LM.Get("GUI_ImportModel_CollisionFile"), true, true);
        SourceButton(ModelCollisionSource.None, LM.Get("GUI_ImportModel_CollisionNone"), true, false);
        SourceButton(ModelCollisionSource.RenderMesh, LM.Get("GUI_ImportModel_CollisionRender"), true, false);
        if (s.Source == ModelCollisionSource.RenderMesh) ImGui.TextColored(Warn, LM.Get("GUI_ImportModel_CollisionRenderWarn"));
        if (s.Source == ModelCollisionSource.File)
        {
            ImGui.SetNextItemWidth(330f);
            ImGui.InputTextWithHint("##collisionfile", LM.Get("GUI_ImportModel_CollisionFile"), ref _collisionPath, 512);
            if (ImGui.IsItemDeactivatedAfterEdit()) { s.CollisionPath = _collisionPath.Trim().Trim('"'); _parseDirty = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_CollisionFileTip"));
            ImGui.SameLine();
            if (ImGui.Button("...##collision") && Browse() is { } picked) { _collisionPath = s.CollisionPath = picked; _parseDirty = true; }
        }

        if (s.Source == ModelCollisionSource.RenderMesh)
            foreach (var material in s.Materials) CollisionWord(material.Info.Name, material.Info.Name, material.CollisionWord, w => material.CollisionWord = w);
        else if (s.Source != ModelCollisionSource.None)
            foreach (var row in s.CollisionRows) CollisionWord(row.Name, row.Name, row.Word, w => row.Word = w);
    }

    private void SourceButton(ModelCollisionSource source, string label, bool enabled, bool reparse)
    {
        ImGui.BeginDisabled(!enabled);
        if (ImGui.RadioButton(label, _session.Source == source) && _session.Source != source)
        {
            _session.Source = source;
            if (reparse) _parseDirty = true; else _buildDirty = true;
        }
        ImGui.EndDisabled();
    }

    private void CollisionWord(string id, string label, uint word, Action<uint> set)
    {
        ImGui.PushID(id);
        ImGui.SetNextItemWidth(250f);
        if (ImGui.BeginCombo(label.Length > 0 ? label : LM.Get("GUI_ImportModel_CollisionMaterial"), CollisionMaterials.Get(word).Name))
        {
            foreach (var choice in CollisionMaterials.Common)
            {
                if (ImGui.Selectable($"{choice.Name}##{choice.Word:X}", choice.Word == word)) { set(choice.Word); _buildDirty = true; }
                if (choice.Example.Length > 0 && ImGui.IsItemHovered()) ImGui.SetTooltip(choice.Example);
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_CollisionMaterialTip"));
        ImGui.PopID();
    }

    // --- Result --------------------------------------------------------------------------------------

    private void DrawResult()
    {
        var s = _session;
        if (_error != null) ImGui.TextColored(Bad, _error);
        if (s.Parsed == null) return;
        ImGui.Separator();
        ImGui.TextUnformatted(LM.Get("GUI_ImportModel_Stats", s.Parts, s.InputTriangles, s.Primitives, s.CollisionChunks));
        var problems = s.Problems;
        foreach (string problem in problems) ImGui.TextColored(Bad, problem);
        var warnings = s.Warnings;
        if (warnings.Count > 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_ImportModel_Warnings"));
            foreach (string warning in warnings.Take(8)) ImGui.TextColored(Warn, warning);
            if (warnings.Count > 8) ImGui.TextDisabled($"+{warnings.Count - 8}");
        }
    }

    // --- Import --------------------------------------------------------------------------------------

    private void Import()
    {
        var window = LunaWindow.Instance;
        var s = _session;
        var tie = s.Commit(window.Reader!, window.AssetManager);
        if (tie == null) { _error = s.Problems.FirstOrDefault() ?? "The class could not be queued."; return; }
        ImportRecords.Record(ProjectSession.Current, s);
        EntityManager.StructureVersion++;
        AssetExplorerFrame.Reveal(tie.Id, s.Built!.Name);
        if (_place && LunaWindow.Instance.IsAnyFrameOpened<View3D>())
        {
            var focus = window.GetFirstFrame<View3D>().CentrePlacement().Point;
            InstancePlacer.PlaceNew(new DraggedAsset(PlaceableKind.Tie, tie.Id), focus);
        }
        isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Pick.Centre();
        base.RenderAsWindow(deltaTime);
    }
}
