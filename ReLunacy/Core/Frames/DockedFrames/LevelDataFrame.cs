using System.Numerics;
using ReLunacy.Engine.Config;
using ReLunacy.Engine.Games;
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

        if (ImGui.CollapsingHeader(ImGuiPlus.Label(Icons.Info, LM.Get("GUI_Frame_LevelData_Config")), ImGuiTreeNodeFlags.DefaultOpen))
            RenderConfig();

        if (ImGui.CollapsingHeader(ImGuiPlus.Label(Icons.Lightbulb, LM.Get("GUI_Frame_LevelData_Lighting")), ImGuiTreeNodeFlags.DefaultOpen))
            RenderLighting(level);

        if (ImGui.CollapsingHeader(ImGuiPlus.Label(Icons.Folder, LM.Get("GUI_Frame_LevelData_Regions"))))
            RenderRegions(level);
    }

    /// <summary>What each loaded region owns and what each of its zones holds. On NewEngine+ the
    /// cubemap set, lighting, fog and sky belong to the region, and a zone can appear under more than
    /// one region - so "which region is this, and what does it declare" is a question that otherwise
    /// takes a log read to answer.</summary>
    private static void RenderRegions(LevelData level)
    {
        var em = EntityManager.Singleton;
        var am = LunaWindow.Instance.AssetManager;
        if (em.Regions.Count == 0) { ImGui.TextDisabled(LM.Get("GUI_Frame_LevelData_NoRegions")); return; }

        foreach (var region in em.Regions)
        {
            string name = region.RegionName;
            bool lightingHere = name == am?.ActiveEnvironmentRegion;
            string label = LM.Get("GUI_Frame_LevelData_RegionHeader", name, region.ZonesCount,
                region.allowRender ? LM.Get("GUI_Frame_LevelData_Shown") : LM.Get("GUI_Frame_LevelData_Hidden"));
            if (lightingHere) label += LM.Get("GUI_Frame_LevelData_SuppliesLighting");

            if (!ImGui.TreeNodeEx($"{label}###region_{name}")) continue;

            // The resources this region declares, not the union the renderer uploads: an instance
            // names its own resource, so this is what the region is authored to reflect.
            if (level.CubemapsByRegion.TryGetValue(name, out var cubes))
            {
                var resources = cubes.Select(c => c.ResourceTuid).Distinct().ToList();
                ImGui.Text(LM.Get("GUI_Frame_LevelData_RegionCubemapsLabel", resources.Count));
                foreach (var res in resources) { ImGui.SameLine(); TuidLink.Draw(res); }
            }
            else ImGui.TextDisabled(LM.Get("GUI_Frame_LevelData_RegionNoCubemaps"));

            ImGui.Text(LM.Get("GUI_Frame_LevelData_RegionLighting",
                level.LightingByRegion.ContainsKey(name) ? LM.Get("GUI_Frame_LevelData_Own") : LM.Get("GUI_Frame_LevelData_Inherited")));
            ImGui.Text(LM.Get("GUI_Frame_LevelData_RegionSkies",
                level.SkiesByRegion.TryGetValue(name, out var skies) ? skies.Count : 0));
            ImGui.Text(LM.Get("GUI_Frame_LevelData_RegionCounts",
                region.MobyInstances.Size, region.Volumes.Size, region.TiesCount, region.UFragsCount));

            if (ImGui.BeginTable($"zones_{name}", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
            {
                ImGui.TableSetupColumn(LM.Get("GUI_Frame_LevelData_ZoneName"));
                ImGui.TableSetupColumn(LM.Get("GUI_Frame_LevelData_ZoneTuid"));
                ImGui.TableSetupColumn(LM.Get("GUI_Frame_LevelData_ZoneTies"));
                ImGui.TableSetupColumn(LM.Get("GUI_Frame_LevelData_ZoneUFrags"));
                ImGui.TableHeadersRow();
                foreach (var zone in region.Zones)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (zone.allowRender) ImGui.Text(zone.ZoneName);
                    else ImGui.TextDisabled(zone.ZoneName);
                    ImGui.TableSetColumnIndex(1); TuidLink.Draw(zone.ZoneTUID);
                    ImGui.TableSetColumnIndex(2); ImGui.Text(zone.TieCount.ToString());
                    ImGui.TableSetColumnIndex(3); ImGui.Text(zone.UFragsCount.ToString());
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }
    }

    private string? _configFor;
    private ConfigTableRef? _configTable;
    private ConfigCsv? _config;
    private (int Start, int End)? _configSection;
    private string? _configError;

    /// <summary>
    /// The level's block of the game's level.csv (packed/game/global_cached.psarc /data/configs/level.csv, from
    /// A Crack in Time on): the settings the level folder itself does not hold - its name text, rewards, Zoni
    /// count, segments and their pods, bolt and XP pools, and in space sectors BoundsRadius, the play area's
    /// radius around the origin. Read once per level; edited in Config Tables.
    /// </summary>
    private void RenderConfig()
    {
        string source = Program.ProvidedPath;
        if (_configFor != source)
        {
            _configFor = source;
            _config = null; _configTable = null; _configSection = null; _configError = null;
            try
            {
                if (GameConfigs.GameFolderFor(source) is { } game && GameConfigs.LevelTable(game) is { } table)
                {
                    _configTable = table;
                    _config = GameConfigs.Read(table);
                    _configSection = GameConfigs.LevelSection(_config, GameLibraryScanner.GetLevelNameFromPath(source))?.Rows;
                }
            }
            catch (Exception e) { _configError = e.Message; }
        }

        if (_configError != null) { ImGui.TextWrapped(_configError); return; }
        if (_config == null || _configTable == null) { ImGui.TextDisabled(LM.Get("GUI_Frame_LevelData_ConfigNone")); return; }
        if (_configSection is not { } section)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_LevelData_ConfigNoSection",
                GameConfigs.LevelSectionName(GameLibraryScanner.GetLevelNameFromPath(source))));
            return;
        }

        var csv = _config;
        ImGui.TextDisabled($"{csv.Get(section.Start, 0)}  ({_configTable.EntryPath})");
        ImGui.SameLine();
        if (ImGui.SmallButton(LM.Get("GUI_Frame_LevelData_ConfigEdit")))
        {
            var frame = LunaWindow.Instance.IsAnyFrameOpened<ConfigTablesFrame>()
                ? LunaWindow.Instance.GetFirstFrame<ConfigTablesFrame>()
                : null;
            if (frame == null) { frame = new ConfigTablesFrame(); LunaWindow.Instance.AddFrame(frame); }
            frame.Reveal(_configTable, section.Start);
        }

        // Columns: the field name, the level-wide value, then one per segment - named by the block's Pod row.
        int width = 0;
        for (int r = section.Start + 1; r < section.End; r++) width = Math.Max(width, csv.Rows[r].Count);
        int values = Math.Max(1, width - 2);
        int pods = -1;
        for (int r = section.Start + 1; r < section.End; r++) if (csv.Get(r, 1) == "Pod") pods = r;

        if (!ImGui.BeginTable("leveldata_config", values + 1,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollX))
            return;
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_LevelData_ConfigField"));
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_LevelData_ConfigLevel"));
        for (int v = 1; v < values; v++)
        {
            string pod = pods >= 0 ? csv.Get(pods, v + 2) : "";
            ImGui.TableSetupColumn(pod.Length > 0 ? pod : LM.Get("GUI_Frame_LevelData_ConfigSegment", v));
        }
        ImGui.TableHeadersRow();
        for (int r = section.Start + 1; r < section.End; r++)
        {
            string field = csv.Get(r, 1);
            if (field.Length == 0 || field == "Pod") continue;
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(field);
            for (int v = 0; v < values; v++)
            {
                ImGui.TableSetColumnIndex(v + 1);
                ImGui.TextUnformatted(csv.Get(r, v + 2));
            }
        }
        ImGui.EndTable();
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
