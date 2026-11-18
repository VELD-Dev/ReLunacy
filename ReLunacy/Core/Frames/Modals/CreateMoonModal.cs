using System.Numerics;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Placement;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>
/// Create > Moon: asks the moon's id, surface, radius and place, and makes the moon (MoonCreator through
/// InstancePlacer.CreateMoon): its layers, zone, surface Tie, SpaceMoon Clue, area and landing pad beam. The surfaces are
/// the moons of the loaded level, so only classes the level has can be used.
/// </summary>
public sealed class CreateMoonModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private const float Row = 56f, Thumb = 48f;

    // The moons a new one can copy, found again only when the scene's structure changes.
    private static int _cachedVersion = -1;
    private static List<MoonSurface> _cached = [];

    private static List<MoonSurface> Surfaces()
    {
        if (_cachedVersion != EntityManager.StructureVersion)
        {
            var regions = EntityManager.Singleton.Regions.ToList();
            _cached = LunaWindow.Instance.Level is { IsOldEngine: false } ? MoonCreator.Surfaces(regions) : [];
            _cachedVersion = EntityManager.StructureVersion;
        }
        return _cached;
    }

    /// <summary>The level is an A Crack in Time space sector with a moon to copy.</summary>
    public static bool Available => LunaWindow.IsSpaceSector && Surfaces().Count > 0 && EntityManager.Singleton.Regions.Count == 1;

    public static void Open()
    {
        if (!Available || LunaWindow.Instance.IsAnyFrameOpened<CreateMoonModal>()) return;
        var surfaces = Surfaces();
        var centre = LunaWindow.Instance.IsAnyFrameOpened<View3D>()
            ? LunaWindow.Instance.GetFirstFrame<View3D>().CentrePlacement().Point
            : surfaces[0].Tie.Transform.Translation + new Vector3(600f, 0f, 0f);
        LunaWindow.Instance.AddFrame(new CreateMoonModal(surfaces, new Vector2(centre.X, centre.Z)));
    }

    private readonly List<MoonSurface> _surfaces;
    private MoonSurface _surface;
    private string _id;
    private float _radius;
    private Vector2 _centre;
    private int _segment = -1;
    private string? _error;

    // Layers (-1: a new one, named by the text, empty = the default), zone, segment (0 none, 1 existing, 2 new) and the extras.
    private bool _register = true;
    private int _artIndex = -1, _gameplayIndex = -1, _zoneIndex = -1, _segmentMode;
    private string _artName = "", _gameplayName = "", _zoneName = "", _segmentName = "";
    private readonly List<(string Suffix, bool With)> _pods = [], _areas = [];

    private CreateMoonModal(List<MoonSurface> surfaces, Vector2 centre)
    {
        FrameName = LM.Get("GUI_CreateMoon_Title");
        _surfaces = surfaces;
        _surface = surfaces[0];
        _radius = _surface.Radius;
        _centre = centre;
        if (ConfigDocument.LevelBoundsRadius() is { } bounds)
        {
            float reach = MathF.Max(bounds - 400f - _radius, 0f);
            if (_centre.Length() > reach) _centre = _centre.Length() > 1e-3f ? Vector2.Normalize(_centre) * reach : Vector2.Zero;
        }
        _id = MoonCreator.NextFreeId(_surface.Region, _surface);
    }

    protected override void Render(double deltaTime)
    {
        AssetThumbnails.Singleton.Pump();
        var region = _surface.Region;

        ImGui.SetNextItemWidth(200f);
        ImGui.InputText(LM.Get("GUI_CreateMoon_Id"), ref _id, 31);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_CreateMoon_IdTip"));

        ImGui.TextUnformatted(LM.Get("GUI_CreateMoon_Surface"));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_CreateMoon_SurfaceTip"));
        ImGui.BeginChild("##surfaces", new Vector2(360f, Row * MathF.Min(_surfaces.Count, 4) + 8f), ImGuiChildFlags.Borders);
        foreach (var moon in _surfaces) DrawSurface(moon);
        ImGui.EndChild();

        ImGui.SetNextItemWidth(200f);
        ImGui.InputFloat(LM.Get("GUI_CreateMoon_Radius"), ref _radius, 1f, 10f, "%.1f");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_CreateMoon_RadiusTip"));
        _radius = Math.Clamp(float.IsFinite(_radius) ? _radius : _surface.Radius, 1f, 5000f);

        ImGui.SetNextItemWidth(200f);
        ImGui.InputFloat2(LM.Get("GUI_CreateMoon_Position"), ref _centre);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_CreateMoon_PositionTip"));
        ImGui.TextDisabled(LM.Get("GUI_CreateMoon_Height", MoonCreator.CentreY(_surface, _radius).ToString("F1")));

        if (ConfigDocument.LevelBoundsRadius() is { } limit && _centre.Length() + _radius > limit - 400f)
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_CreateMoon_OutOfBounds"));

        var request = BuildRequest();
        string? problem = MoonCreator.Problem(request);
        DrawLayers(region);
        DrawSegment(region);
        DrawGroups(_pods, "Pod", "GUI_CreateMoon_Pods", "GUI_CreateMoon_PodsTip");
        DrawGroups(_areas, "Area", "GUI_CreateMoon_Areas", "GUI_CreateMoon_AreasTip");
        if (problem != null) ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), problem);

        if (_error != null) ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), _error);
        ImGui.Spacing();
        ImGui.BeginDisabled(problem != null);
        if (ImGui.Button(LM.Get("GUI_CreateMoon_Create"))) Create();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
    }

    private void DrawSurface(MoonSurface moon)
    {
        var origin = ImGui.GetCursorScreenPos();
        bool selected = ReferenceEquals(moon, _surface);
        if (ImGui.Selectable($"##moon{moon.Name}", selected, ImGuiSelectableFlags.None, new Vector2(0f, Row - 4f)) && !selected)
        {
            _surface = moon;
            _radius = moon.Radius;
        }
        var dl = ImGui.GetWindowDrawList();
        var min = origin + new Vector2(2f);
        var max = min + new Vector2(Thumb);
        if (AssetThumbnails.Singleton.Get(true, moon.Tie.BaseTie.Id) is { } texture) dl.AddImage(texture, min, max);
        else dl.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        dl.AddText(origin + new Vector2(Thumb + 12f, 8f), ImGui.GetColorU32(ImGuiCol.Text), moon.Name);
        dl.AddText(origin + new Vector2(Thumb + 12f, 8f + ImGui.GetTextLineHeightWithSpacing()), ImGui.GetColorU32(ImGuiCol.TextDisabled),
            LM.Get("GUI_CreateMoon_SurfaceInfo", moon.Radius.ToString("0.#")));
    }

    private void Create()
    {
        var made = InstancePlacer.CreateMoon(BuildRequest(), out var problem);
        if (made == null) { _error = problem; return; }
        isOpen = false;
    }

    private MoonRequest BuildRequest()
    {
        var region = _surface.Region;
        string? Layer(int index, string name) => index >= 0 && index < region.Layers.Count ? region.Layers[index] : name.Length > 0 ? name : null;
        var segmentRequest = _segmentMode == 2 ? (_segmentName.Length > 0 ? _segmentName : MoonCreator.SegmentName(region, _id)) : null;
        return new MoonRequest(_id, _surface, _radius, _centre, _segmentMode == 1 ? _segment : -1)
        {
            NewSegment = segmentRequest,
            RegisterSegment = _register,
            ArtLayer = Layer(_artIndex, _artName),
            GameplayLayer = Layer(_gameplayIndex, _gameplayName),
            Zone = _zoneIndex >= 0 && _zoneIndex < region.Zones.Count ? region.Zones[_zoneIndex] : null,
            ZoneName = _zoneName.Length > 0 ? _zoneName : null,
            Pods = [.. _pods.Select(p => new MoonGroup($"{_id}_{p.Suffix}", p.With))],
            Areas = [.. _areas.Select(a => new MoonGroup($"{_id}_{a.Suffix}", a.With))],
        };
    }

    private void DrawLayers(EntityRegion region)
    {
        if (!ImGui.CollapsingHeader(LM.Get("GUI_CreateMoon_Layers"))) return;
        LayerChoice(region, "GUI_CreateMoon_ArtLayer", ref _artIndex, ref _artName, $"art_{_id}");
        LayerChoice(region, "GUI_CreateMoon_GameplayLayer", ref _gameplayIndex, ref _gameplayName, $"gameplay_{_id}");
        ImGui.SetNextItemWidth(200f);
        string preview = _zoneIndex >= 0 && _zoneIndex < region.Zones.Count ? region.Zones[_zoneIndex].ZoneName : LM.Get("GUI_CreateMoon_New");
        if (ImGui.BeginCombo(LM.Get("GUI_CreateMoon_Zone"), preview))
        {
            if (ImGui.Selectable(LM.Get("GUI_CreateMoon_New"), _zoneIndex < 0)) _zoneIndex = -1;
            for (int i = 0; i < region.Zones.Count; i++)
                if (ImGui.Selectable($"{region.Zones[i].ZoneName}##zone{i}", _zoneIndex == i)) _zoneIndex = i;
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_CreateMoon_ZoneTip"));
        if (_zoneIndex < 0)
        {
            ImGui.SetNextItemWidth(200f);
            ImGui.InputTextWithHint(LM.Get("GUI_CreateMoon_ZoneName"), $"art_{_id}", ref _zoneName, 63);
        }
    }

    private void LayerChoice(EntityRegion region, string label, ref int index, ref string name, string hint)
    {
        ImGui.SetNextItemWidth(200f);
        string preview = index >= 0 && index < region.Layers.Count ? region.Layers[index] : LM.Get("GUI_CreateMoon_New");
        if (ImGui.BeginCombo(LM.Get(label), preview))
        {
            if (ImGui.Selectable($"{LM.Get("GUI_CreateMoon_New")}##{label}", index < 0)) index = -1;
            for (int i = 0; i < region.Layers.Count; i++)
                if (ImGui.Selectable($"{region.Layers[i]}##{label}{i}", index == i)) index = i;
            ImGui.EndCombo();
        }
        if (index < 0)
        {
            ImGui.SetNextItemWidth(200f);
            ImGui.InputTextWithHint($"##{label}Name", hint, ref name, 63);
        }
    }

    private void DrawSegment(EntityRegion region)
    {
        ImGui.SetNextItemWidth(200f);
        string preview = _segmentMode switch
        {
            1 when _segment >= 0 && _segment < region.Pods.Count => region.Pods[_segment].Name,
            2 => LM.Get("GUI_CreateMoon_New"),
            _ => LM.Get("GUI_CreateMoon_NoSegment"),
        };
        if (ImGui.BeginCombo(LM.Get("GUI_CreateMoon_Segment"), preview))
        {
            if (ImGui.Selectable(LM.Get("GUI_CreateMoon_NoSegment"), _segmentMode == 0)) _segmentMode = 0;
            if (ImGui.Selectable(LM.Get("GUI_CreateMoon_New"), _segmentMode == 2)) _segmentMode = 2;
            for (int i = 0; i < region.Pods.Count; i++)
                if (region.Pods[i].Name.StartsWith("SEGMENT", StringComparison.OrdinalIgnoreCase)
                    && ImGui.Selectable($"{region.Pods[i].Name}##seg{i}", _segmentMode == 1 && _segment == i)) { _segmentMode = 1; _segment = i; }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_CreateMoon_SegmentTip"));
        if (_segmentMode == 2)
        {
            ImGui.SetNextItemWidth(200f);
            ImGui.InputTextWithHint(LM.Get("GUI_CreateMoon_SegmentName"), MoonCreator.SegmentName(region, _id), ref _segmentName, 100);
            ImGui.Checkbox(LM.Get("GUI_CreateMoon_Register"), ref _register);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_CreateMoon_RegisterTip"));
        }
    }

    private void DrawGroups(List<(string Suffix, bool With)> list, string id, string header, string tip)
    {
        if (!ImGui.CollapsingHeader($"{LM.Get(header)} ({list.Count})##{id}")) return;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get(tip));
        for (int i = 0; i < list.Count; i++)
        {
            var (suffix, with) = list[i];
            ImGui.TextUnformatted($"{_id}_");
            ImGui.SameLine(0f, 0f);
            ImGui.SetNextItemWidth(150f);
            ImGui.InputText($"##{id}s{i}", ref suffix, 80);
            ImGui.SameLine();
            ImGui.Checkbox($"{LM.Get("GUI_CreateMoon_WithObjects")}##{id}w{i}", ref with);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get(id == "Pod" ? "GUI_CreateMoon_PodObjectsTip" : "GUI_CreateMoon_AreaObjectsTip"));
            ImGui.SameLine();
            if (ImGui.Button($"{LM.Get("GUI_CreateMoon_Remove")}##{id}r{i}")) { list.RemoveAt(i); break; }
            list[i] = (suffix, with);
        }
        if (ImGui.Button($"{LM.Get("GUI_CreateMoon_Add")}##{id}add")) list.Add((id == "Pod" ? $"pod{list.Count + 1}" : $"area{list.Count + 1}", false));
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Pick.Centre();
        base.RenderAsWindow(deltaTime);
    }
}
