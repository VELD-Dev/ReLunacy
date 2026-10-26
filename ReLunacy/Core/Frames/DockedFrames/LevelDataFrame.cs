using System.Numerics;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>Level overview + live lighting controls. Read-only counts (instances, assets, textures,
/// lightmaps...) plus editors for the level's analytic lighting environment, which edit
/// LevelData.LightingEnvironment in place; View3D re-reads those every frame, so changes are live.
/// Falls back to the editor's own sun (EditorSettings) for levels that ship no environment.</summary>
public class LevelDataFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private const ImGuiColorEditFlags ColourFlags = ImGuiColorEditFlags.Float | ImGuiColorEditFlags.Hdr;

    public LevelDataFrame() : base()
    {
        FrameName = LM.Get("GUI_Frame_LevelData");
    }

    protected override void Render(double deltaTime)
    {
        var level = LunaWindow.Instance.Level;
        if (level == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_LevelData_NoLevel"));
            return;
        }

        if (ImGui.CollapsingHeader(ImGuiPlus.Label(Icons.Info, LM.Get("GUI_Frame_LevelData_Overview")), ImGuiTreeNodeFlags.DefaultOpen))
            RenderOverview(level);

        if (ImGui.CollapsingHeader(ImGuiPlus.Label(Icons.Lightbulb, LM.Get("GUI_Frame_LevelData_Lighting")), ImGuiTreeNodeFlags.DefaultOpen))
            RenderLighting(level);
    }

    private static void Row(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.Text(value);
    }

    private static void RenderOverview(LevelData level)
    {
        var em = EntityManager.Singleton;
        if (!ImGui.BeginTable("leveldata_overview", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
            return;

        Row(LM.Get("GUI_Frame_LevelData_Engine"), level.IsOldEngine ? "Old" : "New");
        Row(LM.Get("GUI_Frame_LevelData_MobyInstances"), em.MobysCount.ToString());
        Row(LM.Get("GUI_Frame_LevelData_MobyAssets"), level.Mobys.Count.ToString());
        Row(LM.Get("GUI_Frame_LevelData_TieInstances"), em.TiesCount.ToString());
        Row(LM.Get("GUI_Frame_LevelData_TieAssets"), level.Ties.Count.ToString());
        Row(LM.Get("GUI_Frame_LevelData_UFrags"), em.UFragsCount.ToString());
        Row(LM.Get("GUI_Frame_LevelData_Zones"), level.Zones.Count.ToString());
        Row(LM.Get("GUI_Frame_LevelData_Volumes"), em.VolumesCount.ToString());
        Row(LM.Get("GUI_Frame_LevelData_Foliage"), em.Foliage.Count.ToString());
        Row(LM.Get("GUI_Frame_LevelData_Cubemaps"), level.Cubemaps.Count.ToString());
        Row(LM.Get("GUI_Frame_LevelData_Textures"), level.AllTextures.Count.ToString());
        Row(LM.Get("GUI_Frame_LevelData_Shaders"), level.Shaders.Count.ToString());
        Row(LM.Get("GUI_Frame_LevelData_Lightmaps"), level.ZoneLightmaps.Count.ToString());
        Row(LM.Get("GUI_Frame_LevelData_Directionals"), level.ZoneDirectionals.Count.ToString());

        ImGui.EndTable();
    }

    private static void RenderLighting(LevelData level)
    {
        var env = level.LightingEnvironment;
        if (env == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_LevelData_NoLightEnv"));
            return;
        }

        var ambient = env.Ambient;
        if (ImGui.ColorEdit3(LM.Get("GUI_Frame_LevelData_Ambient") + "##amb", ref ambient, ColourFlags))
            env.Ambient = ambient;

        for (int i = 0; i < env.Lights.Count; i++)
        {
            var light = env.Lights[i];
            ImGui.SeparatorText(LM.Get("GUI_Frame_LevelData_Light", i));

            var c = light.Colour;
            if (ImGui.ColorEdit3(LM.Get("GUI_Frame_LevelData_LightColour") + "##c" + i, ref c, ColourFlags))
                light.Colour = c;

            var d = light.Direction;
            if (ImGui.DragFloat3(LM.Get("GUI_Frame_LevelData_LightDir") + "##d" + i, ref d, 0.01f, -1f, 1f))
                light.Direction = d.LengthSquared() > 1e-6f ? Vector3.Normalize(d) : Vector3.UnitY;
        }
    }
}
