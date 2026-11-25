using System.Numerics;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Placement;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Assets.Browser;
using ReLunacy.Engine.Importing;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

public sealed class MaterialAuthoringModal : Modal
{
    private static readonly (ImportTextureKind Kind, string Label, string Hint)[] Slots =
    [
        (ImportTextureKind.Albedo, "GUI_ImportModel_SlotAlbedo", "GUI_MaterialAuthoring_AlbedoHelp"),
        (ImportTextureKind.Normal, "GUI_ImportModel_SlotNormal", "GUI_MaterialAuthoring_NormalHelp"),
        (ImportTextureKind.Expensive, "GUI_ImportModel_SlotExpensive", "GUI_MaterialAuthoring_ExpensiveHelp"),
        (ImportTextureKind.Detail, "GUI_ImportModel_SlotDetail", "GUI_MaterialAuthoring_DetailHelp"),
    ];

    public static bool Available => LunaWindow.Instance is { Level: { } level, Reader: not null, AssetManager: not null }
        && TieShaderFactory.Problem(level) == null && EntityManager.Singleton.Regions.Count > 0;

    public static void Open(bool shader)
    {
        if (!Available || LunaWindow.Instance.IsAnyFrameOpened<MaterialAuthoringModal>()) return;
        LunaWindow.Instance.AddFrame(new MaterialAuthoringModal(shader));
    }

    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly bool _shader;
    private readonly FileManager _files = LunaWindow.Instance.fileManager!;
    private readonly LevelData _level = LunaWindow.Instance.Level!;
    private readonly LevelReader _reader = LunaWindow.Instance.Reader!;
    private readonly List<EntityRegion> _regions = [.. EntityManager.Singleton.Regions];
    private readonly Dictionary<ImportTextureKind, ShaderTextureBinding> _bindings = [];
    private readonly NewShaderRequest _request = new();
    private readonly List<ShaderTextureBinding> _textures = [];
    private EntityRegion _region;
    private Task<List<ShaderTemplate>>? _loadingTemplates;
    private Task<EncodedTexture>? _encoding;
    private List<ShaderTemplate> _templates = [];
    private string _path = "", _name = "";
    private string? _error;
    private int _kind;
    private bool _keepAlpha = true, _flipNormalGreen;
    private Vector4 _colour = Vector4.One;
    private float _detailTiling = 3f;

    private MaterialAuthoringModal(bool shader)
    {
        _shader = shader;
        _region = InstancePlacer.TargetRegion() ?? _regions[0];
        FrameName = LM.Get(shader ? "GUI_MenuItem_CreateShader" : "GUI_MenuItem_ImportTexture");
        if (!shader) return;
        foreach (var texture in _regions.SelectMany(r => r.PendingTextures))
            _textures.Add(ShaderTextureBinding.From(texture));
        foreach (var (id, record) in AuthoredMaterialRecords.ReadTextures(ProjectSession.Current, _files.SourcePath))
            if (_level.AllTextures.ContainsKey(id) && _reader.GetImportedTextureBinding(id, record.Name, record.Kind) is { } binding)
                _textures.Add(binding);
        _textures.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        LoadTemplates();
    }

    private void LoadTemplates()
    {
        string region = _region.RegionName;
        _templates = [];
        _request.TemplateTuid = 0;
        _error = null;
        _loadingTemplates = Task.Run(() => TieShaderFactory.Templates(_files, TieShaderCatalog.Build(_files, _level, region)));
    }

    protected override void Render(double deltaTime)
    {
        if (!ReferenceEquals(LunaWindow.Instance.Level, _level) || !Available) { isOpen = false; return; }
        if (_loadingTemplates is { IsCompleted: true } templates)
        {
            _loadingTemplates = null;
            if (templates.IsCompletedSuccessfully) _templates = templates.Result;
            else _error = templates.Exception?.GetBaseException().Message;
        }
        if (_encoding is { IsCompleted: true } encoding)
        {
            _encoding = null;
            if (encoding.IsCompletedSuccessfully) TryCommit(() => CommitTexture(encoding.Result));
            else _error = encoding.Exception?.GetBaseException().Message;
            if (!isOpen) return;
        }

        bool busy = _loadingTemplates != null || _encoding != null;
        ImGui.BeginDisabled(busy);
        DrawRegion();
        ImGui.SetNextItemWidth(300f);
        ImGui.InputText(LM.Get("GUI_MaterialAuthoring_Name"), ref _name, 64);
        if (_shader) DrawShader();
        else DrawTexture();
        ImGui.EndDisabled();

        if (busy) ImGui.TextDisabled(LM.Get("GUI_ImportModel_Working"));
        if (_error != null) ImGui.TextWrapped(_error);
        bool ready = !busy && TieShaderFactory.SafeName(_name).Length > 0 &&
            (_shader ? _templates.Count > 0 : !string.IsNullOrWhiteSpace(_path));
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button(LM.Get(_shader ? "GUI_MaterialAuthoring_Create" : "GUI_ImportModel_Import")))
        {
            _error = null;
            if (_shader) TryCommit(CommitShader);
            else StartTextureImport();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
    }

    private void DrawRegion()
    {
        if (_regions.Count <= 1) return;
        ImGui.SetNextItemWidth(300f);
        if (!ImGui.BeginCombo(LM.Get("GUI_MaterialAuthoring_Region"), _region.RegionName)) return;
        foreach (var region in _regions)
            if (ImGui.Selectable(region.RegionName, region == _region) && region != _region)
            {
                _region = region;
                if (_shader) LoadTemplates();
            }
        ImGui.EndCombo();
    }

    private void DrawTexture()
    {
        ImGui.SetNextItemWidth(300f);
        ImGui.InputTextWithHint("##image_path", LM.Get("GUI_MaterialAuthoring_Image"), ref _path, 1024);
        if (ImGui.IsItemDeactivatedAfterEdit()) SetPath(_path);
        ImGui.SameLine();
        if (ImGui.Button("…##image"))
        {
            try
            {
                var result = NativeFileDialogSharp.Dialog.FileOpen("png,jpg,jpeg,tga,bmp");
                if (result.IsOk) SetPath(result.Path);
            }
            catch (Exception e) { _error = e.Message; }
        }

        ImGui.SetNextItemWidth(300f);
        if (ImGui.BeginCombo(LM.Get("GUI_MaterialAuthoring_Slot"), LM.Get(Slots[_kind].Label)))
        {
            for (int i = 0; i < Slots.Length; i++)
                if (ImGui.Selectable(LM.Get(Slots[i].Label), _kind == i)) _kind = i;
            ImGui.EndCombo();
        }
        ImGui.TextWrapped(LM.Get(Slots[_kind].Hint));
        if (Slots[_kind].Kind is ImportTextureKind.Albedo or ImportTextureKind.Expensive)
            ImGui.Checkbox(LM.Get("GUI_MaterialAuthoring_KeepAlpha"), ref _keepAlpha);
        if (Slots[_kind].Kind == ImportTextureKind.Normal)
            ImGui.Checkbox(LM.Get("GUI_ImportModel_FlipGreen"), ref _flipNormalGreen);
        ImGui.TextDisabled(LM.Get("GUI_MaterialAuthoring_TextureSize", TextureImporter.SafeSize));
    }

    private void SetPath(string path)
    {
        _path = path.Trim().Trim('"');
        if (_name.Length == 0) _name = Path.GetFileNameWithoutExtension(_path);
    }

    private void DrawShader()
    {
        ImGui.TextUnformatted(LM.Get("GUI_MaterialAuthoring_Class", ShaderClasses.Find(ShaderClasses.Standard)!.Name));
        if (_loadingTemplates == null && _templates.Count == 0)
            ImGui.TextWrapped(LM.Get("GUI_MaterialAuthoring_NoTemplate"));

        ImGui.SetNextItemWidth(300f);
        var selected = _templates.FirstOrDefault(t => t.Tuid == _request.TemplateTuid);
        if (ImGui.BeginCombo(LM.Get("GUI_MaterialAuthoring_Template"), selected?.Candidate.DisplayName ?? LM.Get("GUI_MaterialAuthoring_Automatic")))
        {
            if (ImGui.Selectable(LM.Get("GUI_MaterialAuthoring_Automatic"), selected == null)) _request.TemplateTuid = 0;
            foreach (var template in _templates)
                if (ImGui.Selectable($"{template.Candidate.DisplayName}##{template.Tuid:X}", selected == template))
                    _request.TemplateTuid = template.Tuid;
            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(300f);
        if (ImGui.BeginCombo(LM.Get("GUI_MaterialAuthoring_RenderMode"), AlphaLabel(_request.Alpha)))
        {
            foreach (var mode in Enum.GetValues<ImportAlphaMode>())
                if (ImGui.Selectable(AlphaLabel(mode), mode == _request.Alpha)) _request.Alpha = mode;
            ImGui.EndCombo();
        }
        ImGui.Checkbox(LM.Get("GUI_ImportModel_DoubleSided"), ref _request.DoubleSided);

        foreach (var (kind, label, hint) in Slots)
        {
            ImGui.PushID((int)kind);
            _bindings.TryGetValue(kind, out var current);
            string empty = LM.Get(kind == ImportTextureKind.Albedo ? "GUI_MaterialAuthoring_Solid" : "GUI_Frame_ShaderBrowser_None");
            ImGui.SetNextItemWidth(300f);
            if (ImGui.BeginCombo(LM.Get(label), current?.Name ?? empty))
            {
                if (ImGui.Selectable(empty, current == null)) _bindings.Remove(kind);
                foreach (var texture in _textures.Where(t => t.Kind == kind))
                    if (ImGui.Selectable($"{texture.Name}##{texture.Tuid:X}", current == texture)) _bindings[kind] = texture;
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get(hint));
            ImGui.PopID();
        }
        if (!_bindings.ContainsKey(ImportTextureKind.Albedo))
            ImGui.ColorEdit4(LM.Get("GUI_MaterialAuthoring_Colour"), ref _colour);
        if (_bindings.ContainsKey(ImportTextureKind.Detail))
            ImGui.DragFloat(LM.Get("GUI_MaterialAuthoring_DetailTiling"), ref _detailTiling, 0.05f, 0.01f, 128f);
        ImGui.TextDisabled(LM.Get("GUI_MaterialAuthoring_UseImported"));
    }

    private static string AlphaLabel(ImportAlphaMode mode) => LM.Get(mode switch
    {
        ImportAlphaMode.Cutout => "GUI_MaterialAuthoring_Cutout",
        ImportAlphaMode.Blend => "GUI_MaterialAuthoring_Blend",
        _ => "GUI_MaterialAuthoring_Opaque",
    });

    private void StartTextureImport()
    {
        string path = _path.Trim().Trim('"');
        var kind = Slots[_kind].Kind;
        var options = new TextureImportOptions { KeepAlpha = _keepAlpha, FlipNormalGreen = _flipNormalGreen };
        _encoding = Task.Run(() => TextureImporter.Encode(ImportImage.FromFile(path), kind, options));
    }

    private void TryCommit(Action commit)
    {
        try { commit(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _error = e.Message;
        }
    }

    private void CommitTexture(EncodedTexture encoded)
    {
        var pending = AcitMaterialAuthoring.NameTexture(encoded, _name, AcitMaterialAuthoring.ReservedTextureIds(_files, _regions));
        var saved = AuthoredMaterialRecords.ReadTextures(ProjectSession.Current, _files.SourcePath);
        if (_regions.SelectMany(r => r.PendingTextures).Any(t => t.Name == pending.Name) ||
            saved.Any(p => p.Value.Name == pending.Name && _level.AllTextures.ContainsKey(p.Key)))
            throw new InvalidOperationException(LM.Get("GUI_MaterialAuthoring_TextureExists", pending.Name));
        _reader.StageTexture(pending);
        _region.PendingTextures.Add(pending);
        Complete(new(AssetKind.Texture, pending.Tuid));
    }

    private void CommitShader()
    {
        _request.Name = _name;
        _request.DetailTiling = _bindings.ContainsKey(ImportTextureKind.Detail) ? _detailTiling : null;
        static float ToLinear(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        _request.AlbedoFactor = new Vector4(ToLinear(_colour.X), ToLinear(_colour.Y), ToLinear(_colour.Z), _colour.W);
        var result = AcitMaterialAuthoring.BuildShader(_files, _regions, _templates, _request, _bindings);
        if (result.Shader is not { } shader)
        {
            _error = string.Join("\n", result.Problems);
            return;
        }
        _reader.StageShader(shader);
        _region.AddPendingShader(shader);
        Complete(new(AssetKind.Shader, shader.Tuid));
    }

    private void Complete(AssetKey key)
    {
        EntityManager.StructureVersion++;
        AssetsBrowserFrame.Reveal(key);
        isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Pick.Centre();
        base.RenderAsWindow(deltaTime);
    }
}
