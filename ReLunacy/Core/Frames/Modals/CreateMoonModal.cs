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
    public static bool Available => Surfaces().Count > 0 && EntityManager.Singleton.Regions.Count == 1;

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
        string? idProblem = MoonCreator.IdProblem(region, _id);
        if (idProblem != null) ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), idProblem);

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

        var pods = region.Pods.Select((pod, i) => (pod, i)).Where(p => p.pod.Name.StartsWith("SEGMENT", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pods.Count > 0)
        {
            ImGui.SetNextItemWidth(200f);
            string preview = _segment >= 0 && _segment < region.Pods.Count ? region.Pods[_segment].Name : LM.Get("GUI_CreateMoon_NoSegment");
            if (ImGui.BeginCombo(LM.Get("GUI_CreateMoon_Segment"), preview))
            {
                if (ImGui.Selectable(LM.Get("GUI_CreateMoon_NoSegment"), _segment < 0)) _segment = -1;
                foreach (var (pod, i) in pods)
                    if (ImGui.Selectable($"{pod.Name}##seg{i}", _segment == i)) _segment = i;
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_CreateMoon_SegmentTip"));
        }

        if (_error != null) ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), _error);
        ImGui.Spacing();
        ImGui.BeginDisabled(idProblem != null);
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
        var made = InstancePlacer.CreateMoon(new MoonRequest(_id, _surface, _radius, _centre, _segment), out var problem);
        if (made == null) { _error = problem; return; }
        isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Pick.Centre();
        base.RenderAsWindow(deltaTime);
    }
}
