using System.Numerics;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The 3D viewport's path panel: what the paths' colour says (their role, their layer, or nothing), and which
/// roles are drawn. The role list is the legend. Like the volume filters, the hidden roles start over with every level.
/// </summary>
internal sealed class PathFilterPanel
{
    public bool Open;
    private object? _level;

    public void Draw(ViewportOverlay overlay, float anchorLeft)
    {
        if (!Open || !overlay.BeginPanel("pathfilters", new Vector2(320f, 0f), anchorLeft)) return;
        var em = EntityManager.Singleton;
        var paths = em.Regions.SelectMany(r => r.Paths.Entities).OfType<EntityPath>().Select(p => p.Path).ToList();

        var level = LunaWindow.Instance.Level;
        if (!ReferenceEquals(level, _level))
        {
            _level = level;
            em.HiddenPathRoles.Clear();
        }

        if (ImGui.Checkbox(LM.Get("GUI_Paths_Show"), ref em.renderPaths))
            MenuBar.RenderMenuDraw.PersistRenderSettings();
        ImGui.SameLine();
        ImGui.TextDisabled(LM.Get("GUI_Volumes_Summary", paths.Count(em.IsPathShown), paths.Count));

        bool changed = false;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo(LM.Get("GUI_Volumes_ColorBy"), LM.Get("GUI_Paths_Color_" + em.PathColorMode)))
        {
            foreach (var mode in Enum.GetValues<PathColorMode>())
                if (ImGui.Selectable(LM.Get("GUI_Paths_Color_" + mode), em.PathColorMode == mode))
                {
                    em.PathColorMode = mode;
                    changed = true;
                }
            ImGui.EndCombo();
        }

        // Roles: what a Clue or controller uses them for first, the name's guesses next, the unassigned last.
        ImGui.SeparatorText(LM.Get("GUI_Paths_Roles"));
        var roles = paths.GroupBy(p => p.Role)
            .OrderBy(g => g.Key == PathRoles.Unassigned)
            .ThenBy(g => g.All(p => p.RoleSource == null))
            .ThenByDescending(g => g.Count())
            .ToList();
        changed |= VolumeFilterPanel.List("pathrole", roles.Select(g => g.Key), em.HiddenPathRoles, role =>
        {
            var group = roles.First(g => g.Key == role);
            int linked = group.Count(p => p.RoleSource != null);
            string tooltip = role == PathRoles.Unassigned
                ? LM.Get("GUI_Paths_UnassignedHint")
                : linked == group.Count()
                    ? LM.Get("GUI_Paths_RoleLinked", group.First().RoleSource!)
                    : LM.Get("GUI_Paths_RoleNamed", linked, group.Count() - linked);
            return ($"{role} ({group.Count()})", PathRoles.Colour(role), tooltip);
        });
        if (changed) em.VisibilityVersion++;
        overlay.EndPanel();
    }
}
