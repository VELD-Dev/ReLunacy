using ReLunacy.Core;
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
    private static void PersistRenderSettings()
    {
        var settings = LunaWindow.Instance.EditorSettings;
        var em = EntityManager.Singleton;
        settings.RenderMobys = em.renderMobys;
        settings.RenderTies = em.renderTies;
        settings.RenderUFrags = em.renderUFrags;
        settings.RenderFoliage = em.renderFoliage;
        settings.RenderVolumes = em.renderVolumes;
        settings.RenderBoundingSpheres = em.renderBoundingSpheres;
        settings.MobyDistanceCullingEnabled = em.MobyDistanceCullingEnabled;
        settings.SaveSettingsToFile();
    }

    internal static void ShowMobys()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderMobys"), "", EntityManager.Singleton.renderMobys)) return;
        EntityManager.Singleton.renderMobys = !EntityManager.Singleton.renderMobys;
        PersistRenderSettings();
    }

    internal static void ShowTies()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderTies"), "", EntityManager.Singleton.renderTies)) return;
        EntityManager.Singleton.renderTies = !EntityManager.Singleton.renderTies;
        PersistRenderSettings();
    }

    internal static void ShowUFrags()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderUFrags"), "", EntityManager.Singleton.renderUFrags)) return;
        EntityManager.Singleton.renderUFrags = !EntityManager.Singleton.renderUFrags;
        PersistRenderSettings();
    }

    internal static void ShowFoliage()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderFoliage"), "", EntityManager.Singleton.renderFoliage)) return;
        EntityManager.Singleton.renderFoliage = !EntityManager.Singleton.renderFoliage;
        PersistRenderSettings();
    }

    internal static void ShowVolumes()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderVolumes"), "", EntityManager.Singleton.renderVolumes)) return;
        EntityManager.Singleton.renderVolumes = !EntityManager.Singleton.renderVolumes;
        PersistRenderSettings();
    }

    internal static void ShowBoundingSpheres()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderBoundingSpheres"), "", EntityManager.Singleton.renderBoundingSpheres)) return;
        EntityManager.Singleton.renderBoundingSpheres = !EntityManager.Singleton.renderBoundingSpheres;
        PersistRenderSettings();
    }

    /// <summary>Per-zone render toggles - zones are the level's own streaming/culling unit (mostly
    /// relevant on new engine, which routinely has many; old engine levels have exactly one, id 0),
    /// so this is what lets the user turn a zone's ties/UFrags off without hiding anything else.
    /// Mobys/Volumes are per-REGION, not per-zone (see EntityRegion vs EntityZone), so they're
    /// untouched by this - there's normally only one region per level anyway.
    ///
    /// Skips building the submenu entirely when there are no zones (no level loaded, or a level
    /// with none) rather than showing an empty one - same pattern ViewMenuDraw.LayoutPresets uses
    /// for its saved-layouts list.</summary>
    internal static void ZoneVisibility()
    {
        var em = EntityManager.Singleton;
        if (em.ZonesCount == 0) return;
        if (!ImGui.BeginMenu(LM.Get("GUI_MenuItem_RenderZones"))) return;

        foreach (var region in em.Regions)
        {
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
        }

        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_ShowAllZones")))
        {
            foreach (var region in em.Regions)
                foreach (var zone in region.Zones)
                    zone.allowRender = true;
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

    /// <summary>The game's own fidelity switches: its fragment programs (read from a decrypted EBOOT
    /// in the level's install, and the programs a material ships itself), and on the old engine the
    /// level's fog (main.dat 0x7100) and the TIE cube reflections. All on by default; they only hide.</summary>
    internal static void GameFidelity()
    {
        var settings = LunaWindow.Instance.EditorSettings;
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_GameShaders"), "", settings.GameShaders))
        {
            settings.GameShaders = !settings.GameShaders;
            Engine.Rendering.Vulkan.VulkanRenderer.UseRetailEmbeddedPrograms = settings.GameShaders;
            settings.SaveSettingsToFile();
            LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer();
        }
        if (LunaWindow.Instance.Level?.IsOldEngine != true) return;
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_OldEngineFog"), "", settings.OldEngineFog))
        {
            settings.OldEngineFog = !settings.OldEngineFog;
            OldEngineRenderOptions.FogEnabled = settings.OldEngineFog;
            settings.SaveSettingsToFile();
        }
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_OldEngineReflections"), "", settings.OldEngineReflections))
        {
            settings.OldEngineReflections = !settings.OldEngineReflections;
            OldEngineRenderOptions.ReflectionsEnabled = settings.OldEngineReflections;
            settings.SaveSettingsToFile();
        }
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

        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_ReflectionDebug"), "", view.ReflectionDebugView))
            view.ReflectionDebugView = !view.ReflectionDebugView;

        float intensity = view.ReflectionIntensity;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderFloat(LM.Get("GUI_MenuItem_ReflectionIntensity"), ref intensity, 0f, 2f, "%.2f"))
            view.ReflectionIntensity = intensity;

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
