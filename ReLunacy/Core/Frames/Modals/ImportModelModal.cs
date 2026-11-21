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
    private int _selected;
    private readonly HashSet<ImportMaterialRow> _decided = [];

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
        if (!Busy)
            foreach (var row in materials)
                if (_decided.Add(row) && s.NewShaderProblem == null && row.NewShader == null && row.ShaderTuid == 0 && row.Auto == null && row.Info.BaseColorImage != null)
                {
                    row.NewShader = NewShaderRequest.From(row.Info);
                    _buildDirty = true;
                }
        _selected = Math.Clamp(_selected, 0, Math.Max(0, materials.Count - 1));
        if (materials.Count == 0) return;

        float height = 330f;
        if (ImGui.BeginChild("##materials", new Vector2(190f, height), ImGuiChildFlags.Borders))
        {
            for (int i = 0; i < materials.Count; i++)
            {
                var row = materials[i];
                bool missing = row.ShaderTuid == 0 && row.NewShader == null;
                if (missing) ImGui.PushStyleColor(ImGuiCol.Text, Bad);
                string label = row.Info.Name.Length > 0 ? row.Info.Name : "-";
                if (ImGui.Selectable($"{label}##{i}", i == _selected)) _selected = i;
                if (missing) ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(LM.Get("GUI_ImportModel_MaterialInfo", row.Info.TriangleCount, row.Info.BaseColorTextureName ?? "-"));
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##material", new Vector2(480f, height), ImGuiChildFlags.Borders))
        {
            var row = materials[_selected];
            ImGui.PushID(_selected);
            DrawMaterial(row);
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    private void DrawMaterial(ImportMaterialRow row)
    {
        bool canNew = _session.NewShaderProblem == null;
        if (!canNew) row.NewShader = null;
        DrawShaderChoice("base", ref row.ShaderTuid, ref row.NewShader, row.BuiltTemplate, () => NewShaderRequest.From(row.Info), canNew);

        ImGui.Separator();
        bool lit = row.Lightmapped || row.Overlay != null;
        ImGui.BeginDisabled(row.Overlay != null);
        if (ImGui.Checkbox(LM.Get("GUI_ImportModel_Lightmapped"), ref lit)) { row.Lightmapped = lit; _buildDirty = true; }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_ImportModel_LightmappedTip"));
        ImGui.SameLine();
        bool overlay = row.Overlay != null;
        if (ImGui.Checkbox(LM.Get("GUI_ImportModel_Overlay"), ref overlay))
        {
            row.Overlay = overlay ? new ImportOverlay() : null;
            _buildDirty = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_OverlayTip"));
        if (row.Overlay is not { } o) return;

        ImGui.Indent();
        var mask = _session.OverlayMask;
        ImageSlot(LM.Get("GUI_ImportModel_OverlayMask"), "mask", ref mask, LM.Get("GUI_ImportModel_OverlayMaskTip"));
        if (!ReferenceEquals(mask, _session.OverlayMask)) { _session.OverlayMask = mask; _buildDirty = true; }
        ImGui.SetNextItemWidth(220f);
        if (ImGui.DragFloat2(LM.Get("GUI_ImportModel_OverlayUvScale"), ref o.UvScale, 0.01f, 0.01f, 64f)) _buildDirty = true;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.DragFloat2(LM.Get("GUI_ImportModel_OverlayUvOffset"), ref o.UvOffset, 0.01f, -64f, 64f)) _buildDirty = true;
        DrawShaderChoice("overlay", ref o.ShaderTuid, ref o.NewShader, o.BuiltTemplate,
            () => new NewShaderRequest { Name = $"{row.Info.Name}_overlay" }, canNew);
        ImGui.Unindent();
    }

    /// <summary>Existing / new radios, then the shader combo or the new shader's settings.</summary>
    private void DrawShaderChoice(string id, ref ulong tuid, ref NewShaderRequest? request, ShaderTemplate? built, Func<NewShaderRequest> make, bool canNew)
    {
        ImGui.PushID(id);
        if (canNew)
        {
            if (ImGui.RadioButton(LM.Get("GUI_ImportModel_ModeExisting"), request == null) && request != null)
            {
                request = null;
                _buildDirty = true;
            }
            ImGui.SameLine();
            if (ImGui.RadioButton(LM.Get("GUI_ImportModel_NewShader"), request != null) && request == null)
            {
                request = make();
                _buildDirty = true;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_NewShaderTip"));
        }
        if (request != null) DrawNewShader(request, built);
        else DrawShaderCombo(ref tuid);
        ImGui.PopID();
    }

    private void DrawNewShader(NewShaderRequest r, ShaderTemplate? built)
    {
        ImGui.SetNextItemWidth(220f);
        ImGui.InputText(LM.Get("GUI_ImportModel_ShaderName"), ref r.Name, 63);
        if (ImGui.IsItemDeactivatedAfterEdit()) _buildDirty = true;

        var shaderClass = ShaderClasses.Find(r.Class);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo(LM.Get("GUI_ImportModel_Class"), shaderClass?.Name ?? $"0x{r.Class:X2}"))
        {
            foreach (var c in ShaderClasses.Creatable)
                if (ImGui.Selectable(c.Name, c.Id == r.Class) && c.Id != r.Class) { r.Class = c.Id; r.TemplateTuid = 0; _buildDirty = true; }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_ClassTip"));

        if (ImGui.RadioButton(LM.Get("GUI_ImportModel_SourceGltf"), !r.Manual) && r.Manual) { r.Manual = false; _buildDirty = true; }
        ImGui.SameLine();
        if (ImGui.RadioButton(LM.Get("GUI_ImportModel_SourceManual"), r.Manual) && !r.Manual) { r.Manual = true; _buildDirty = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_SourceManualTip"));

        ImageSlot(LM.Get("GUI_ImportModel_SlotAlbedo"), "albedo", ref r.Albedo);
        ImageSlot(LM.Get("GUI_ImportModel_SlotNormal"), "normal", ref r.Normal);
        if (r.Manual)
        {
            if (PackedToggle("expensiveMode", LM.Get("GUI_ImportModel_SlotExpensive"), ref r.ExpensiveSplit))
                ImageSlot(LM.Get("GUI_ImportModel_SlotExpensive"), "expensive", ref r.Expensive, LM.Get("GUI_ImportModel_SlotExpensiveTip"));
            else
            {
                ImageSlot(LM.Get("GUI_ImportModel_SlotExpSpecular"), "expR", ref r.ExpensiveSpecular);
                ImageSlot(LM.Get("GUI_ImportModel_SlotExpHeight"), "expG", ref r.ExpensiveHeight);
                ImageSlot(LM.Get("GUI_ImportModel_SlotExpEmissive"), "expB", ref r.ExpensiveEmissive, LM.Get("GUI_ImportModel_SlotExpEmissiveTip"));
                ImageSlot(LM.Get("GUI_ImportModel_SlotExpMask"), "expA", ref r.ExpensiveMask, LM.Get("GUI_ImportModel_SlotExpMaskTip"));
            }
            if (PackedToggle("detailMode", LM.Get("GUI_ImportModel_SlotDetail"), ref r.DetailSplit))
                ImageSlot(LM.Get("GUI_ImportModel_SlotDetail"), "detail", ref r.Detail, LM.Get("GUI_ImportModel_SlotDetailTip"));
            else
            {
                ImageSlot(LM.Get("GUI_ImportModel_SlotDetailNormal"), "detN", ref r.DetailNormal, LM.Get("GUI_ImportModel_SlotDetailNormalTip"));
                ImageSlot(LM.Get("GUI_ImportModel_SlotDetailAlbedo"), "detC", ref r.DetailAlbedo, LM.Get("GUI_ImportModel_SlotDetailGrayTip"));
                ImageSlot(LM.Get("GUI_ImportModel_SlotDetailSpecular"), "detS", ref r.DetailSpecular, LM.Get("GUI_ImportModel_SlotDetailGrayTip"));
            }
        }
        else
        {
            ImageSlot(LM.Get("GUI_ImportModel_SlotExpensive"), "expensive", ref r.Expensive, LM.Get("GUI_ImportModel_SlotExpensiveTip"));
            ImageSlot(LM.Get("GUI_ImportModel_SlotDetail"), "detail", ref r.Detail, LM.Get("GUI_ImportModel_SlotDetailTip"));
            ImageSlot(LM.Get("GUI_ImportModel_SlotEmissive"), "emissive", ref r.Emissive, LM.Get("GUI_ImportModel_SlotEmissiveTip"));
        }

        ImGui.SetNextItemWidth(220f);
        ImGui.ColorEdit4(LM.Get("GUI_ImportModel_Factor"), ref r.AlbedoFactor);
        if (ImGui.IsItemDeactivatedAfterEdit()) _buildDirty = true;
        if (!r.Manual)
        {
            ImGui.SetNextItemWidth(220f);
            ImGui.ColorEdit3(LM.Get("GUI_ImportModel_EmissiveFactor"), ref r.EmissiveFactor, ImGuiColorEditFlags.Hdr | ImGuiColorEditFlags.Float);
            if (ImGui.IsItemDeactivatedAfterEdit()) _buildDirty = true;
        }
        bool specular = r.Specular != null;
        if (ImGui.Checkbox(LM.Get("GUI_ImportModel_Specular"), ref specular)) { r.Specular = specular ? 0.25f : null; _buildDirty = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_SpecularTip"));
        if (r.Specular is { } level)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120f);
            if (ImGui.SliderFloat("##specular", ref level, 0f, 1f)) r.Specular = level;
            if (ImGui.IsItemDeactivatedAfterEdit()) _buildDirty = true;
        }

        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo(LM.Get("GUI_ImportModel_Alpha"), AlphaName(r.Alpha)))
        {
            foreach (var mode in Enum.GetValues<ImportAlphaMode>())
                if (ImGui.Selectable(AlphaName(mode), mode == r.Alpha) && mode != r.Alpha) { r.Alpha = mode; _buildDirty = true; }
            ImGui.EndCombo();
        }
        if (ImGui.Checkbox(LM.Get("GUI_ImportModel_DoubleSided"), ref r.DoubleSided)) _buildDirty = true;
        ImGui.SameLine();
        if (ImGui.Checkbox(LM.Get("GUI_ImportModel_FlipGreen"), ref r.FlipNormalGreen)) _buildDirty = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_FlipGreenTip"));

        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo(LM.Get("GUI_ImportModel_MaxSize"), r.MaxSize.ToString()))
        {
            foreach (int size in MaxSizes)
                if (ImGui.Selectable(size.ToString(), size == r.MaxSize) && size != r.MaxSize) { r.MaxSize = size; _buildDirty = true; }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_MaxSizeTip"));
        int largest = r.LargestSource;
        if (largest > TextureImporter.SafeSize)
        {
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.3f, 1f), LM.Get("GUI_ImportModel_LargeTexture", largest));
            if (ImGui.RadioButton(LM.Get("GUI_ImportModel_Downscale"), !r.KeepFullSize)) { r.KeepFullSize = false; _buildDirty = true; }
            ImGui.SameLine();
            if (ImGui.RadioButton(LM.Get("GUI_ImportModel_KeepFullSize"), r.KeepFullSize)) { r.KeepFullSize = true; _buildDirty = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_KeepFullSizeTip"));
        }

        var templates = _session.ShaderTemplates.Where(t => t.Class == r.Class).ToList();
        var chosen = r.TemplateTuid == 0 ? null : templates.FirstOrDefault(t => t.Tuid == r.TemplateTuid);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo(LM.Get("GUI_ImportModel_Template"), chosen?.Candidate.DisplayName ?? LM.Get("GUI_ImportModel_TemplateAuto")))
        {
            if (ImGui.Selectable(LM.Get("GUI_ImportModel_TemplateAuto"), r.TemplateTuid == 0) && r.TemplateTuid != 0) { r.TemplateTuid = 0; _buildDirty = true; }
            foreach (var t in templates)
            {
                if (ImGui.Selectable($"{t.Candidate.DisplayName}##{t.Tuid:X}", t.Tuid == r.TemplateTuid) && t.Tuid != r.TemplateTuid) { r.TemplateTuid = t.Tuid; _buildDirty = true; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(t.SlotSummary);
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered() && chosen != null) ImGui.SetTooltip(chosen.SlotSummary);

        if (built != null && !_buildDirty && !Busy)
        {
            ImGui.TextUnformatted(LM.Get("GUI_ImportModel_BuiltFrom", built.Candidate.DisplayName));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(built.SlotSummary);
            string key = TieShaderFactory.SafeName(r.Name);
            var shader = _session.BuiltShaders.FirstOrDefault(p => p.Name == $"{key}/{key}");
            if (shader != null)
                foreach (var texture in shader.Textures)
                {
                    var t = texture.Texture;
                    string format = t.Format switch { 0x08 => "DXT5", 0x05 => "ARGB8", _ => "DXT1" };
                    ImGui.TextDisabled(LM.Get("GUI_ImportModel_BuiltTexture", t.Kind, t.Width, t.Height, format, t.MipCount));
                }
        }
    }

    private static readonly int[] MaxSizes = [128, 256, 512];

    private static string AlphaName(ImportAlphaMode mode) => mode switch
    {
        ImportAlphaMode.Cutout => LM.Get("GUI_ImportModel_AlphaCutout"),
        ImportAlphaMode.Blend => LM.Get("GUI_ImportModel_AlphaBlend"),
        _ => LM.Get("GUI_ImportModel_AlphaOpaque"),
    };

    /// <summary>A map's label with Packed / Separate radios; returns true for packed.</summary>
    private bool PackedToggle(string id, string label, ref bool split)
    {
        ImGui.PushID(id);
        ImGui.TextUnformatted(label);
        ImGui.SameLine(110f);
        if (ImGui.RadioButton(LM.Get("GUI_ImportModel_Packed"), !split) && split) { split = false; _buildDirty = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_PackedTip"));
        ImGui.SameLine();
        if (ImGui.RadioButton(LM.Get("GUI_ImportModel_Separate"), split) && !split) { split = true; _buildDirty = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_SeparateTip"));
        ImGui.PopID();
        return !split;
    }

    private void ImageSlot(string label, string id, ref ImportImage? image, string? tip = null)
    {
        ImGui.PushID(id);
        ImGui.TextUnformatted(label);
        if (tip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
        ImGui.SameLine(110f);
        string source = image == null ? LM.Get("GUI_ImportModel_SlotNone")
            : image.SourcePath != null ? Path.GetFileName(image.SourcePath) : image.Name.Length > 0 ? image.Name : "-";
        ImGui.TextDisabled(source);
        ImGui.SameLine(330f);
        if (ImGui.Button("...") && Pick.File("png,jpg,jpeg") is { } picked)
        {
            try
            {
                image = ImportImage.FromFile(picked);
                _buildDirty = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _error = e.Message; }
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ImportModel_Browse"));
        ImGui.SameLine();
        ImGui.BeginDisabled(image == null);
        if (ImGui.Button(Icons.Close)) { image = null; _buildDirty = true; }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_ImportModel_Clear"));
        ImGui.PopID();
    }

    private void DrawShaderCombo(ref ulong tuid)
    {
        var catalog = _session.Catalog;
        var current = catalog.Find(tuid);
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
                if (ImGui.Selectable($"{label}##{candidate.Tuid:X}", candidate.Tuid == tuid))
                {
                    tuid = candidate.Tuid;
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
        AssetsBrowserFrame.Reveal(new Engine.Assets.Browser.AssetKey(Engine.Assets.Browser.AssetKind.Tie, tie.Id));
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
