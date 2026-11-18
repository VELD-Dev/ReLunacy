using System.Numerics;
using ReLunacy.Core;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Core.Placement;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

/// <summary>
/// The Create menu: a volume, a path or a controller, made at the surface in the middle of the 3D view and selected
/// (InstancePlacer). Needs a new-engine level and the 3D view open. Assets are placed from the Asset Explorer.
/// </summary>
internal static class CreateMenuDraw
{
    internal static void Menu()
    {
        if (!ImGui.BeginMenu(LM.Get("GUI_Menu_Create"))) return;
        var view = LunaWindow.Instance.IsAnyFrameOpened<View3D>() ? LunaWindow.Instance.GetFirstFrame<View3D>() : null;
        bool enabled = view != null && InstancePlacer.Available;

        if (ImGui.BeginMenu($"{Icons.VectorSquare}  {LM.Get("GUI_Create_Volume")}", enabled))
        {
            foreach (var shape in new[] { VolumeShape.Box, VolumeShape.Sphere, VolumeShape.Cylinder, VolumeShape.Capsule })
                if (ImGui.MenuItem(LM.Get($"GUI_Create_Shape{shape}")))
                {
                    var point = view!.CentrePlacement().Point;
                    InstancePlacer.CreateVolume(shape, point, view.MoonRotation(point));
                }
            ImGui.EndMenu();
        }

        if (ImGui.MenuItem($"{Icons.BezierCurve}  {LM.Get("GUI_Create_Path")}", "", false, enabled))
        {
            var place = view!.CentrePlacement();
            InstancePlacer.CreatePath(place.Point, place.Right);
        }

        if (ImGui.BeginMenu($"{Icons.Gear}  {LM.Get("GUI_Create_Controller")}", enabled && InstancePlacer.TargetRegion() != null))
        {
            var classes = InstancePlacer.ControllerClasses(InstancePlacer.TargetRegion()!);
            if (classes.Count == 0) ImGui.TextDisabled(LM.Get("GUI_Create_NoClasses"));
            bool separated = false;
            foreach (var (template, label, isCamera) in classes)
            {
                if (!isCamera && !separated && classes.Any(c => c.IsCamera))
                {
                    ImGui.Separator();
                    separated = true;
                }
                if (!ImGui.MenuItem(label)) continue;
                var place = view!.CentrePlacement();
                // A camera looks the way the view does: its local +Z is where it looks.
                Quaternion? rotation = isCamera
                    ? Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, -place.Forward, Vector3.UnitY))
                    : view.MoonRotation(place.Point);
                InstancePlacer.CreateController(template, place.Point, rotation);
            }
            ImGui.EndMenu();
        }
        if (ImGui.MenuItem($"{Icons.Gear}  {LM.Get("GUI_Create_Pod")}", "", false, enabled && InstancePlacer.TargetRegion()?.GroupLayers("Pod").Any() == true))
            CreateGroupModal.Open("Pod");
        if (ImGui.MenuItem($"{Icons.Gear}  {LM.Get("GUI_Create_Area")}", "", false, enabled && InstancePlacer.TargetRegion()?.GroupLayers("Area").Any() == true))
            CreateGroupModal.Open("Area");
        if (ImGui.MenuItem($"{Icons.Gear}  {LM.Get("GUI_Create_Scent")}", "", false, enabled && InstancePlacer.TargetRegion()?.GroupLayers("Scent").Any() == true))
            CreateGroupModal.Open("Scent");
        if (LunaWindow.IsSpaceSector && ImGui.MenuItem($"{Icons.Globe}  {LM.Get("GUI_Create_Moon")}", "", false, enabled && CreateMoonModal.Available))
            CreateMoonModal.Open();
        ImGui.EndMenu();
    }
}
