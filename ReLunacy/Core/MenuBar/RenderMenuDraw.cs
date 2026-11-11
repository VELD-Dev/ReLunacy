using System.Numerics;
using ReLunacy.Core;
using ReLunacy.Engine.Assets.Collision;
using ReLunacy.Engine.Assets.Sky;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

internal static class RenderMenuDraw
{
    /// <summary>Writes the Render menu's own toggles through to EditorSettings and saves
    /// immediately - these are single-click menu checkboxes, not a settings panel with a batched
    /// "Apply" step, so there's nothing to defer: the same instant-persist convention the Layout
    /// menu's save/load actions and Game Browser's path already use. See EditorSettings.RenderMobys
    /// for why EntityManager can't just own this itself.</summary>
    internal static void PersistRenderSettings()
    {
        var settings = LunaWindow.Instance.EditorSettings;
        var em = EntityManager.Singleton;
        settings.RenderMobys = em.renderMobys;
        settings.RenderTies = em.renderTies;
        settings.RenderUFrags = em.renderUFrags;
        settings.RenderFoliage = em.renderFoliage;
        settings.RenderVolumes = em.renderVolumes;
        settings.RenderNavmesh = em.renderNavmesh;
        settings.RenderCollision = em.renderCollision;
        settings.RenderBoundingSpheres = em.renderBoundingSpheres;
        settings.MobyDistanceCullingEnabled = em.MobyDistanceCullingEnabled;
        settings.SaveSettingsToFile();
    }

    internal static void ShowFoliage()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderFoliage"), "", EntityManager.Singleton.renderFoliage)) return;
        EntityManager.Singleton.renderFoliage = !EntityManager.Singleton.renderFoliage;
        PersistRenderSettings();
    }

    /// <summary>Which of the level's regions is loaded. A new-engine level is read one region at a time
    /// (All 4 One's are progression segments of one map that overlap in space and were never shown
    /// together), so picking another frees this one and reads that one from the level's files. Hidden
    /// for a level with a single region - every old-engine level, and A Crack in Time's.</summary>
    internal static void RegionSelection()
    {
        var window = LunaWindow.Instance;
        if (window.Level is not { RegionNames.Count: > 1 } level) return;
        if (!ImGui.BeginMenu(LM.Get("GUI_MenuItem_RenderRegion"))) return;

        bool loading = window.IsLoadingLevel;
        if (loading) ImGui.BeginDisabled();
        for (int r = 0; r < level.RegionNames.Count; r++)
        {
            string name = level.RegionNames[r];
            if (ImGui.MenuItem($"{name}###region_pick_{r}", "", level.Region?.Name == name))
                window.SwitchRegion(name);
        }
        if (loading) ImGui.EndDisabled();

        ImGui.EndMenu();
    }

    /// <summary>Per-zone render toggles - zones are the level's own streaming/culling unit (mostly
    /// relevant on new engine, which routinely has many; old engine levels have exactly one, id 0),
    /// so this is what lets the user turn a zone's Ties/UFrags off without hiding anything else.
    /// Mobys/Volumes are per-REGION, not per-zone (see EntityRegion vs EntityZone), so a zone tick
    /// leaves them alone; the per-region tick above each group is what hides those, along with every
    /// zone the region holds.
    ///
    /// Skips building the submenu entirely when there are no zones (no level loaded, or a level
    /// with none) rather than showing an empty one - same pattern ViewMenuDraw.LayoutPresets uses
    /// for its saved-layouts list.</summary>
    internal static void ZoneVisibility()
    {
        var em = EntityManager.Singleton;
        if (em.ZonesCount == 0) return;
        if (!ImGui.BeginMenu(LM.Get("GUI_MenuItem_RenderZones"))) return;

        // One region is the norm (every old-engine level, and most new-engine ones), so a lone region
        // lists its zones directly rather than behind a submenu nobody needs to open. Several regions
        // is the case this grouping exists for: A4O's levels overlap in space, and a region is how
        // NewEngine+ groups the zones meant to be seen together.
        var regions = em.Regions;
        bool nest = regions.Count > 1;

        for (int r = 0; r < regions.Count; r++)
        {
            var region = regions[r];
            // Region names are no more unique than zone names and EntityRegion carries no TUID,
            // so the list index is what keeps the ImGui IDs distinct.
            if (nest && !ImGui.BeginMenu($"{region.RegionName}###region_{r}")) continue;

            // The region's own tick, always present so it can be found: at the head of its submenu
            // when regions are nested, inline and labelled with the region name when there is only
            // one (nesting a single region would just add a click to reach the zones).
            if (ImGui.MenuItem(
                    nest ? LM.Get("GUI_MenuItem_RenderWholeRegion") : $"{region.RegionName}###region_{r}",
                    "", region.allowRender))
            {
                region.allowRender = !region.allowRender;
                LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer();
            }
            ImGui.Separator();

            // A region turned off hides its zones whatever their own flags say (see
            // EntityManager.AllRenderableEntities), so show them as overridden instead of letting a
            // ticked zone claim it is drawing when it is not.
            if (!region.allowRender) ImGui.BeginDisabled();
            foreach (var zone in region.Zones)
            {
                // ###-scoped by TUID, not just the name - zone names are not guaranteed unique
                // (e.g. several "UnnamedZone" fallbacks), and a duplicate ImGui ID here is exactly
                // the class of bug the Layout menu's saved-layout entries already had to fix.
                if (ImGui.MenuItem($"{zone.ZoneName}###zone_{zone.ZoneTUID:X}", "", zone.allowRender))
                {
                    zone.allowRender = !zone.allowRender;
                    LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer();
                }
            }
            if (!region.allowRender) ImGui.EndDisabled();

            if (nest) ImGui.EndMenu();
        }

        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_ShowAllZones")))
        {
            foreach (var region in em.Regions)
            {
                region.allowRender = true;
                foreach (var zone in region.Zones)
                    zone.allowRender = true;
            }
            LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer();
        }
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_HideAllZones")))
        {
            foreach (var region in em.Regions)
                foreach (var zone in region.Zones)
                    zone.allowRender = false;
            LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer();
        }

        ImGui.EndMenu();
    }

    /// <summary>Every sky of the loaded level, each with its own visibility tick. A new-engine region
    /// can hold several (All 4 One's seaside has three, switched in game by set_sky); the level opens
    /// on its first one alone, the one the game shows before a script calls set_sky(). Skipped when
    /// no level is open, like ZoneVisibility.</summary>
    internal static void SkyVisibility()
    {
        var level = LunaWindow.Instance.Level;
        if (level == null) return;
        if (!ImGui.BeginMenu(LM.Get("GUI_MenuItem_RenderSkies"))) return;

        if (level.Skies.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_MenuItem_SkyNone"));
            ImGui.EndMenu();
            return;
        }

        var settings = LunaWindow.Instance.EditorSettings;
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_ShowSky"), "", settings.RenderSky))
        {
            settings.RenderSky = !settings.RenderSky;
            SkyRenderOptions.Enabled = settings.RenderSky;
            settings.SaveSettingsToFile();
        }
        ImGui.Separator();

        ImGui.BeginDisabled(!settings.RenderSky);
        foreach (LevelSky sky in level.Skies)
        {
            bool visible = SkyRenderOptions.IsVisible(level, sky.Index);
            if (ImGui.Checkbox($"{sky.Label}###sky_{sky.Index}", ref visible))
                SkyRenderOptions.SetVisible(level, sky.Index, visible);
            if (level.Skies.Count > 1)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"{LM.Get("GUI_MenuItem_SkyOnly")}###sky_only_{sky.Index}"))
                    SkyRenderOptions.ShowOnly(level, sky.Index);
            }

            SkyData data = sky.Data;
            int shells = data.Family8160Records.Concat(data.Family8170Records).Count(record => record.IndexCount != 0);
            int clouds = data.DefinitionTextures.Count(slot => slot.Texture != null);
            string slots = sky.SetSkySlots.Count == 0 ? string.Empty : $"  -  set_sky slot {string.Join(", ", sky.SetSkySlots)}";
            ImGui.TextDisabled($"      {shells} shell(s), {data.RenderSources.Count} texture(s), clouds {clouds}/3{slots}");
        }
        ImGui.EndDisabled();

        if (level.Skies.Count > 1)
            ImGui.TextDisabled(LM.Get("GUI_MenuItem_SkySeveralHint"));
        ImGui.EndMenu();
    }

    /// <summary>The old engine's own fidelity switches: the level's fog (main.dat 0x7100) and the Tie
    /// cube reflections. On by default; they only hide. The game's RSX shaders are switched from the
    /// viewport toolbar, beside lit/unlit. A Crack in Time has the same linear fog, so it gets the fog
    /// switch alone.</summary>
    internal static void GameFidelity()
    {
        var settings = LunaWindow.Instance.EditorSettings;
        var level = LunaWindow.Instance.Level;
        bool old = level?.IsOldEngine == true;
        if (!old && level?.LightingEnvironment?.LinearFog == null) return;
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_OldEngineFog"), "", settings.OldEngineFog))
        {
            settings.OldEngineFog = !settings.OldEngineFog;
            OldEngineRenderOptions.FogEnabled = settings.OldEngineFog;
            settings.SaveSettingsToFile();
        }
        if (!old)
        {
            ImGui.Separator();
            return;
        }
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_OldEngineReflections"), "", settings.OldEngineReflections))
        {
            settings.OldEngineReflections = !settings.OldEngineReflections;
            OldEngineRenderOptions.ReflectionsEnabled = settings.OldEngineReflections;
            settings.SaveSettingsToFile();
        }
        ImGui.Separator();
    }

    internal static void ShowMobyDistanceCulling()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_MobyDistanceCulling"), "", EntityManager.Singleton.MobyDistanceCullingEnabled)) return;
        EntityManager.Singleton.MobyDistanceCullingEnabled = !EntityManager.Singleton.MobyDistanceCullingEnabled;
        PersistRenderSettings();
    }

    // Cubemap reflection controls (lit renderer only), reached through View3D.
    internal static void ReflectionControls()
    {
        var view = LunaWindow.Instance.GetFirstFrame<Core.Frames.DockedFrames.View3D>();
        if (view == null) return;

        // Rebuilds the scene: the instance lights this feeds are built when the scene is captured.
        bool trustScale = Engine.Rendering.Vulkan.VulkanRenderer.UseRetailBakedScale;
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_RetailBakedScale"), "", trustScale))
        {
            Engine.Rendering.Vulkan.VulkanRenderer.UseRetailBakedScale = !trustScale;
            LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_MenuItem_RetailBakedScaleHint"));

        if (trustScale)
        {
            float maxScale = Engine.Rendering.Vulkan.VulkanRenderer.MaxBakedScale;
            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderFloat(LM.Get("GUI_MenuItem_MaxBakedScale"), ref maxScale, 0f, 32f, maxScale > 0f ? "%.1f" : "off"))
            {
                Engine.Rendering.Vulkan.VulkanRenderer.MaxBakedScale = maxScale;
                LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_MenuItem_MaxBakedScaleHint"));
        }

        // Lighting first: it is the term a Tie and a Moby share, so it is where a fault common to
        // both has to be looked for before the reflection path is suspected.
        int lightMode = Engine.Rendering.Vulkan.VulkanRenderer.LightingDebugMode;
        string[] lightModes = [
            LM.Get("GUI_MenuItem_LightMode_Normal"),
            LM.Get("GUI_MenuItem_LightMode_Lighting"),
            LM.Get("GUI_MenuItem_LightMode_BakedColour"),
            LM.Get("GUI_MenuItem_LightMode_BakedDir"),
            LM.Get("GUI_MenuItem_LightMode_BakedSun"),
            LM.Get("GUI_MenuItem_LightMode_Prelight"),
            LM.Get("GUI_MenuItem_LightMode_BakedResult"),
            LM.Get("GUI_MenuItem_LightMode_Albedo"),
            LM.Get("GUI_MenuItem_LightMode_NonFinite")];
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo(LM.Get("GUI_MenuItem_LightMode"), ref lightMode, lightModes, lightModes.Length))
            Engine.Rendering.Vulkan.VulkanRenderer.LightingDebugMode = lightMode;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_MenuItem_LightModeHint"));

        // Replaces the old Reflection debug toggle, which set a uniform no shader ever read.
        var vk = typeof(Engine.Rendering.Vulkan.VulkanRenderer);
        int mode = Engine.Rendering.Vulkan.VulkanRenderer.ReflectionDebugMode;
        string[] modes = [
            LM.Get("GUI_MenuItem_ReflMode_Normal"),
            LM.Get("GUI_MenuItem_ReflMode_Only"),
            LM.Get("GUI_MenuItem_ReflMode_Cube"),
            LM.Get("GUI_MenuItem_ReflMode_Off")];
        ImGui.SetNextItemWidth(180);
        if (ImGui.Combo(LM.Get("GUI_MenuItem_ReflMode"), ref mode, modes, modes.Length))
            Engine.Rendering.Vulkan.VulkanRenderer.ReflectionDebugMode = mode;

        float specOverride = Engine.Rendering.Vulkan.VulkanRenderer.SpecularOverride;
        bool forceSpec = specOverride >= 0f;
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_ForceSpecular"), "", forceSpec))
            Engine.Rendering.Vulkan.VulkanRenderer.SpecularOverride = forceSpec ? -1f : 1f;
        if (forceSpec)
        {
            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderFloat(LM.Get("GUI_MenuItem_SpecularValue"), ref specOverride, 0f, 1f, "%.2f"))
                Engine.Rendering.Vulkan.VulkanRenderer.SpecularOverride = specOverride;
        }

        float intensity = view.ReflectionIntensity;
        ImGui.SetNextItemWidth(120);
        // Logarithmic and three decimals: the per-game starting values differ by more than an order of
        // magnitude (A4O 0.04 against everything else's 1.0), and on a linear two-decimal bar the
        // whole useful range for A4O was the leftmost two percent of the track.
        if (ImGui.SliderFloat(LM.Get("GUI_MenuItem_ReflectionIntensity"), ref intensity, 0f, 2f, "%.3f", ImGuiSliderFlags.Logarithmic))
            view.ReflectionIntensity = intensity;

        // Live cubemap knobs, so the retail values can be found by eye in the 3D view rather than by
        // rebuilding. Both feed the spare lanes of uPostParams (VulkanRenderer.CubeBiasOffset /
        // TonemapPreview) and apply to the generic and the retail shader path alike.
        float cubeBias = Engine.Rendering.Vulkan.VulkanRenderer.CubeBiasOffset;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderFloat(LM.Get("GUI_MenuItem_CubeBias"), ref cubeBias, -4f, 4f, "%.3f"))
            Engine.Rendering.Vulkan.VulkanRenderer.CubeBiasOffset = cubeBias;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_MenuItem_CubeBiasHint"));

        float exposure = Engine.Rendering.Vulkan.VulkanRenderer.TonemapExposure;
        bool tonemap = exposure > 0f;
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_TonemapPreview"), "", tonemap))
            Engine.Rendering.Vulkan.VulkanRenderer.TonemapExposure = tonemap ? 0f : 1f;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_MenuItem_TonemapPreviewHint"));
        if (tonemap)
        {
            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderFloat(LM.Get("GUI_MenuItem_Exposure"), ref exposure, 0.01f, 4f, "%.3f"))
                Engine.Rendering.Vulkan.VulkanRenderer.TonemapExposure = MathF.Max(exposure, 0.001f);
        }

        // Reflectivity floor (Fresnel F0): 0 reflects only where the specular map says to, 1 is a near-mirror everywhere.
        float reflBase = view.ReflectionBase;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderFloat(LM.Get("GUI_MenuItem_ReflectionBase"), ref reflBase, 0f, 1f, "%.2f"))
            view.ReflectionBase = reflBase;

        // Floor under a baked surface, as a fraction of the ambient fill.
        float bakedAmbient = view.BakedAmbient;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderFloat(LM.Get("GUI_MenuItem_BakedAmbient"), ref bakedAmbient, 0f, 1f, "%.2f"))
            view.BakedAmbient = bakedAmbient;
    }
}
