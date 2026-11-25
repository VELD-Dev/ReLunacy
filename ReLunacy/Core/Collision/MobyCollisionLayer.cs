using System.Numerics;
using ReLunacy.Engine.Assets.Collision;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using Moby = ReLunacy.Engine.Assets.Mobys.Moby;

namespace ReLunacy.Core.Collision;

/// <summary>
/// The 3D view's Moby collision: every placed Moby whose class carries collision (see <see cref="MobyCollision"/>),
/// in its bind pose, as the renderer's Moby layer. It has its own filters, since a Moby's collision is not the
/// level's: primitives carry a mask byte rather than instance flags, and materials index the class's own palette.
/// Instances follow their Moby every frame (moved, hidden, region hidden). Shown and filtered from its own toolbar
/// button and panel, apart from the level's collision. Nothing here is editable.
/// </summary>
public sealed class MobyCollisionLayer
{
    public bool PanelOpen;

    private object? _level;
    private VulkanRenderer? _renderer;
    private int _mobyCount = -1;

    private EntityMoby[] _mobys = [];
    private EntityRegion[] _regions = [];
    private int[] _slots = [];
    private Matrix4x4[] _matrices = [];
    private bool[] _hidden = [];
    private uint[] _attributes = [];

    public MobyCollisionSummary? Summary { get; private set; }
    public bool HasCollision => Summary is { Placements: > 0 };

    // Per level, like the level layer's triangle-flag filter.
    private uint _triangleFlagMask = uint.MaxValue;

    /// <summary>Builds and uploads the layer when the level, the renderer or the set of Mobys changed.</summary>
    public void UploadIfStale(VulkanRenderer renderer, object? level)
    {
        var em = EntityManager.Singleton;
        int count = em.MobysCount;
        if (ReferenceEquals(level, _level) && ReferenceEquals(renderer, _renderer) && count == _mobyCount) return;
        if (!ReferenceEquals(level, _level)) _triangleFlagMask = uint.MaxValue;
        _level = level;
        _renderer = renderer;
        _mobyCount = count;

        var mobys = new List<EntityMoby>();
        var regions = new List<EntityRegion>();
        var placements = new List<(MobyCollision, Matrix4x4, uint)>();
        foreach (var region in em.Regions)
            foreach (var moby in region.MobyInstances.Entities.OfType<EntityMoby>())
            {
                if (moby.BaseMoby is not Moby { Collision: { IsEmpty: false } collision }) continue;
                mobys.Add(moby);
                regions.Add(region);
                placements.Add((collision, moby.Transform.GetMatrix(), uint.MaxValue));
            }

        var (scene, summary) = MobyCollisionGeometry.BuildPlaced(placements);
        renderer.SetMobyCollision(scene.Vertices, scene.Instances, scene.Batches);
        _mobys = [.. mobys];
        _regions = [.. regions];
        _slots = summary.Order;
        _matrices = [.. placements.Select(p => p.Item2)];
        _hidden = new bool[_mobys.Length];
        _attributes = new uint[_mobys.Length];
        for (int i = 0; i < _mobys.Length; i++) _attributes[i] = scene.Instances[_slots[i]].Attributes;
        Summary = summary.Placements > 0 ? summary : null;
        if (Summary != null)
            Console.WriteLine($"[Collision] {summary.Placements} Moby placement(s) of {summary.Classes} class(es), {scene.Batches.Length} draw(s).");
    }

    /// <summary>Moves and hides the instances with their Mobys, then pushes the filters. Every frame.</summary>
    public void Update(VulkanRenderer renderer, EditorSettings settings)
    {
        if (!ReferenceEquals(renderer, _renderer)) return;
        var em = EntityManager.Singleton;
        renderer.MobyCollisionVisible = em.renderMobyCollision;
        Apply(renderer, settings, _triangleFlagMask);
        // Compared against what was last pushed, so a Moby moved while the overlay was off catches up when it is shown.
        if (!em.renderMobyCollision) return;
        for (int i = 0; i < _mobys.Length; i++)
        {
            var moby = _mobys[i];
            var matrix = moby.Transform.GetMatrix();
            if (matrix != _matrices[i])
            {
                _matrices[i] = matrix;
                renderer.UpdateMobyCollisionInstanceTransform(_slots[i], matrix);
            }
            var region = _regions[i];
            bool hidden = !em.renderMobys || !moby.allowRender || !region.allowRender || !region.MobyInstances.allowRender;
            if (hidden != _hidden[i])
            {
                _hidden[i] = hidden;
                uint attributes = hidden ? _attributes[i] | CollisionGeometry.HiddenInstanceBit : _attributes[i];
                renderer.UpdateMobyCollisionInstanceAttributes(_slots[i], attributes);
            }
        }
    }

    /// <summary>The Moby layer's filters and colours, from the settings both viewers share.</summary>
    public static void Apply(VulkanRenderer renderer, EditorSettings settings, uint triangleFlagMask = uint.MaxValue)
    {
        renderer.MobyCollisionParts = settings.MobyCollisionParts;
        renderer.MobyCollisionColorMode = settings.MobyCollisionColorMode;
        renderer.MobyCollisionPrimitiveKinds = settings.MobyCollisionPrimitiveKinds;
        renderer.MobyCollisionMaskBits = settings.MobyCollisionMaskBits;
        renderer.MobyCollisionTriangleFlagMask = triangleFlagMask;
    }

    /// <summary>The panel hanging from the toolbar's Moby collision button.</summary>
    public void DrawPanel(ViewportOverlay overlay, float anchorLeft, EditorSettings settings)
    {
        if (!PanelOpen || Summary is not { } summary || !overlay.BeginPanel("mobycollision", new Vector2(320f, 0f), anchorLeft)) return;
        var em = EntityManager.Singleton;
        if (ImGui.Checkbox(LM.Get("GUI_Collision_Show"), ref em.renderMobyCollision))
            MenuBar.RenderMenuDraw.PersistRenderSettings();
        ImGui.SameLine();
        ImGui.TextDisabled(LM.Get("GUI_Label_MobyCollisionSummary", summary.Placements, summary.Classes));
        if (DrawFilters(settings, summary, ref _triangleFlagMask)) settings.SaveSettingsToFile();
        overlay.EndPanel();
    }

    /// <summary>Colour mode, parts, primitive kinds, mask bits and (when given a mask) triangle-flag values.</summary>
    public static bool DrawFilters(EditorSettings settings, MobyCollisionSummary summary, ref uint triangleFlagMask, bool classes = true)
    {
        bool changed = false;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo(LM.Get("GUI_Label_CollisionColorBy") + "##mobycolour", LM.Get("GUI_MenuItem_MobyCollisionColor_" + settings.MobyCollisionColorMode)))
        {
            foreach (var mode in Enum.GetValues<MobyCollisionColorMode>())
            {
                if (!classes && mode == MobyCollisionColorMode.Class) continue;
                if (ImGui.Selectable(LM.Get("GUI_MenuItem_MobyCollisionColor_" + mode), settings.MobyCollisionColorMode == mode))
                {
                    settings.MobyCollisionColorMode = mode;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }

        if (summary.MeshPlacements > 0)
        {
            Swatch(MeshColor, "mobymesh");
            ImGui.SameLine();
            changed |= Part(settings, MobyCollisionParts.Mesh, LM.Get("GUI_Label_MobyCollisionMesh", summary.MeshPlacements));
        }
        if (summary.PrimitivePlacements > 0)
        {
            Swatch(CollisionGeometry.PrimitiveColor(CollisionPrimitiveKind.Box), "mobyprims");
            ImGui.SameLine();
            changed |= Part(settings, MobyCollisionParts.Primitives, LM.Get("GUI_Label_MobyCollisionPrimitives", summary.PrimitivePlacements));
            ImGui.Indent();
            foreach (var (kind, count) in summary.PrimitivesByKind.OrderBy(kv => kv.Key))
            {
                var bit = (CollisionPrimitiveKinds)(1u << (int)kind);
                bool on = settings.MobyCollisionPrimitiveKinds.HasFlag(bit);
                Swatch(CollisionGeometry.PrimitiveColor(kind), $"mobykind{kind}");
                ImGui.SameLine();
                if (ImGui.Checkbox(LM.Get("GUI_Label_CollisionPrimitive_" + kind, count) + $"###mobykind{kind}", ref on))
                {
                    settings.MobyCollisionPrimitiveKinds = on ? settings.MobyCollisionPrimitiveKinds | bit : settings.MobyCollisionPrimitiveKinds & ~bit;
                    changed = true;
                }
            }

            ImGui.TextUnformatted(LM.Get("GUI_Label_MobyCollisionMaskBits"));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Label_MobyCollisionMaskTip"));
            for (int b = 0; b < summary.PrimitivesByMaskBit.Length; b++)
            {
                if (summary.PrimitivesByMaskBit[b] == 0) continue;
                bool on = (settings.MobyCollisionMaskBits & (1u << b)) != 0;
                if (ImGui.Checkbox(LM.Get("GUI_Label_MobyCollisionMaskBit", b, summary.PrimitivesByMaskBit[b]) + $"###mobymask{b}", ref on))
                {
                    settings.MobyCollisionMaskBits = on ? settings.MobyCollisionMaskBits | (1u << b) : settings.MobyCollisionMaskBits & ~(1u << b);
                    changed = true;
                }
            }
            ImGui.Unindent();
        }

        if (summary.TriangleFlags.Count > 1)
        {
            ImGui.TextUnformatted(LM.Get("GUI_Label_MobyCollisionTriangleFlags"));
            ImGui.Indent();
            foreach (var (flags, count) in summary.TriangleFlags.OrderBy(kv => kv.Key))
            {
                bool on = (triangleFlagMask & (1u << flags)) != 0;
                Swatch(CollisionGeometry.FlagColor(flags), $"mobyflags{flags}");
                ImGui.SameLine();
                if (ImGui.Checkbox(LM.Get("GUI_Label_MobyCollisionTriangleFlag", flags, count) + $"###mobyflags{flags}", ref on))
                    triangleFlagMask = on ? triangleFlagMask | (1u << flags) : triangleFlagMask & ~(1u << flags);
            }
            ImGui.Unindent();
        }
        return changed;
    }

    /// <summary>The shader's Kind colour for a Moby mesh.</summary>
    private static readonly Vector3 MeshColor = new(0.20f, 0.85f, 0.85f);

    private static bool Part(EditorSettings settings, MobyCollisionParts part, string label)
    {
        bool on = settings.MobyCollisionParts.HasFlag(part);
        if (!ImGui.Checkbox(label + $"###mobypart{part}", ref on)) return false;
        settings.MobyCollisionParts = on ? settings.MobyCollisionParts | part : settings.MobyCollisionParts & ~part;
        return true;
    }

    private static void Swatch(Vector3 colour, string id)
    {
        float size = ImGui.GetTextLineHeight();
        ImGui.ColorButton($"##swatch_{id}", new Vector4(colour, 1f),
            ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop, new Vector2(size, size));
    }
}
