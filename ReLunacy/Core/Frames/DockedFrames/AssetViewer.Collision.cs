using System.Numerics;
using ReLunacy.Engine.Assets.Collision;
using ReLunacy.Engine.Assets.Ties;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The Asset Viewer's Tie collision overlay: the selected Tie class's collision (see <see cref="TieClassCollision"/>)
/// drawn through the preview renderer's own collision pass, the one the 3D view uses.
/// </summary>
public partial class AssetViewer
{
    // Found once per class and kept (a null entry is a class with none); dropped with the level.
    private readonly Dictionary<ulong, TieClassCollision?> _tieCollisions = [];

    private TieClassCollision? CollisionOf(Tie tie)
    {
        if (_tieCollisions.TryGetValue(tie.Id, out var cached)) return cached;
        var byRegion = LunaWindow.Instance.Level?.CollisionByRegion;
        TieClassCollision? found = null;
        if (byRegion != null)
            found = TieClassCollision.Find(tie.Id, byRegion, () => EntityManager.Singleton.AllEntities().OfType<EntityTie>()
                .Where(e => e.BaseTie.Id == tie.Id).Take(32).Select(e => (e.PlacementMatrix, e.BoundingSphere)));
        return _tieCollisions[tie.Id] = found;
    }

    /// <summary>The collision to draw now, or null (off, no Tie selected, or the class has none).</summary>
    private TieClassCollision? ActivePreviewCollision =>
        Program.Settings.AssetViewerCollision && selectedTieAsset is { } asset ? CollisionOf(asset.Tie) : null;

    private bool HidePreviewMesh => Program.Settings.AssetViewerCollisionOnly && ActivePreviewCollision != null;

    /// <summary>Uploads the class's collision to the preview renderer just built (a new one per selection).</summary>
    private void UploadPreviewCollision()
    {
        if (_vkPreview == null || ActivePreviewCollision is not { } collision) return;
        var scene = collision.Scene;
        _vkPreview.SetCollision(scene.Vertices, scene.Instances, scene.Batches);
    }

    /// <summary>Per-frame settings of the collision pass (cheap property writes).</summary>
    private void ApplyPreviewCollisionSettings()
    {
        if (_vkPreview == null) return;
        _vkPreview.CollisionVisible = ActivePreviewCollision != null;
        _vkPreview.CollisionOpacity = HidePreviewMesh ? 0.85f : 0.5f;
        _vkPreview.CollisionColorMode = Program.Settings.AssetViewerCollisionByMaterial ? CollisionColorMode.Material : CollisionColorMode.Type;
    }

    private void DrawCollisionControls(ViewportOverlay overlay)
    {
        var settings = Program.Settings;
        bool isTie = selectedTieAsset != null;
        bool on = settings.AssetViewerCollision;
        if (overlay.ToggleButton("Col", ref on, LM.Get(isTie ? "GUI_Frame_AssetViewer_Collision_Tooltip" : "GUI_Frame_AssetViewer_Collision_TieOnly"),
                enabled: isTie, width: 40f))
        {
            settings.AssetViewerCollision = on;
            settings.SaveSettingsToFile();
            previewDirty = true;
        }
        if (!isTie) return;
        if (!on || !overlay.BeginPanel("collision", new Vector2(280f, 0f))) return;

        bool only = settings.AssetViewerCollisionOnly;
        if (ImGui.Checkbox(LM.Get("GUI_Frame_AssetViewer_Collision_Only"), ref only))
        {
            settings.AssetViewerCollisionOnly = only;
            settings.SaveSettingsToFile();
            previewDirty = true;
        }
        bool byMaterial = settings.AssetViewerCollisionByMaterial;
        if (ImGui.Checkbox(LM.Get("GUI_Frame_AssetViewer_Collision_ByMaterial"), ref byMaterial))
        {
            settings.AssetViewerCollisionByMaterial = byMaterial;
            settings.SaveSettingsToFile();
        }

        if (ActivePreviewCollision is { } c)
        {
            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_Collision_Chunks", c.Chunks.Count));
            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_Collision_Mesh", c.TriangleCount, c.VertexCount));
            if (c.PrimitiveCount > 0)
                ImGui.Text(LM.Get("GUI_Frame_AssetViewer_Collision_Primitives", c.PrimitiveCount));
            if (c.Materials.Count > 0)
            {
                _collisionMaterialText ??= (c, string.Join(", ", c.Materials));
                if (_collisionMaterialText.Value.Collision != c) _collisionMaterialText = (c, string.Join(", ", c.Materials));
                ImGui.TextWrapped(LM.Get("GUI_Frame_AssetViewer_Collision_Materials", _collisionMaterialText.Value.Text));
            }
        }
        else
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_Collision_None"));
        overlay.EndPanel();
    }

    // The material list as text, kept so the readout does not build a string every frame.
    private (TieClassCollision Collision, string Text)? _collisionMaterialText;
}
