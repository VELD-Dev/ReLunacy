using System.Numerics;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core;

public class Overlay
{
    public static bool showOverlay;
    public static bool ShowFramerate => Program.Settings.OverlayFramerate;
    public static bool ShowLevelStats => Program.Settings.OverlayLevelStats;
    public static bool ShowProfiler => Program.Settings.OverlayProfiler;
    public static bool ShowCamInfo => Program.Settings.OverlayCamInfo;
    public static Vector2 Padding => Program.Settings.OverlayPadding;
    public static int Location => Program.Settings.OverlayPos;
    public static float OverlayAlpha => Program.Settings.OverlayOpacity;

    private static string levelName
    {
        get
        {
            if (string.IsNullOrEmpty(Program.ProvidedPath)) return "None";
            return GameLibraryScanner.GetLevelNameFromPath(Program.ProvidedPath);
        }
    }

    public static void DrawOverlay(bool p_open)
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoInputs;
        bool useView = LunaWindow.Instance.IsAnyFrameOpened<View3D>();
        View3D? view = LunaWindow.Instance.GetFirstFrame<View3D>();

        if (Location is >= 0 and <= 3)
        {
            Vector2 workPos = useView && view != null ? view.ViewportScreenPos : viewport.WorkPos;
            Vector2 workSize = useView && view != null ? view.ViewportSize : viewport.WorkSize;
            Vector2 windowPos, windowPosPivot;
            windowPos.X = Location is 1 or 3 ? workPos.X + workSize.X - Padding.X : workPos.X + Padding.X;
            windowPos.Y = Location >= 2 ? workPos.Y + workSize.Y - Padding.Y : workPos.Y + Padding.Y;
            windowPosPivot.X = Location is 1 or 3 ? 1.0f : 0.0f;
            windowPosPivot.Y = Location >= 2 ? 1.0f : 0.0f;
            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always, windowPosPivot);
            flags |= ImGuiWindowFlags.NoMove;
        }
        else if (Location == 4)
        {
            // Centre of the rendered image in SCREEN space. This used to read the centre of a
            // zero-origin rectangle, which is half the panel size measured from the top-left of the
            // monitor, so the centred overlay landed nowhere near the view.
            ImGui.SetNextWindowPos(useView && view != null ? view.ViewportScreenPos + view.ViewportSize * 0.5f : ImGui.GetWorkCenter(viewport), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
            flags |= ImGuiWindowFlags.NoMove;
        }

        ImGui.SetNextWindowBgAlpha(OverlayAlpha);
        if (ImGui.Begin("Stats Overlay", ref p_open, flags))
        {
            if (ShowFramerate || ShowProfiler)
            {
                ImGui.SeparatorText(LM.Get("GUI_Overlay_Performances"));
                ImGui.BeginGroup();
                if (ShowFramerate)
                {
                    Vector4 colGreen = new(40f / 255f, 1, 40f / 255f, 1);
                    Vector4 colYellow = new(142f / 255f, 1, 40f / 255f, 1);
                    Vector4 colRed = new(1, 40f / 255f, 40f / 255f, 1);
                    float fps = PerformanceProfiler.Singleton.Framerate;
                    var textCol = fps switch
                    {
                        < 15f => colRed,
                        < 50f => colYellow,
                        _ => colGreen,
                    };
                    ImGui.Text($"{LM.Get("GUI_Overlay_Framerate")}: ");
                    ImGui.SameLine();
                    ImGui.TextColored(textCol, $"{fps:N0}FPS");
                }
                if (ShowProfiler)
                {
                    ImGui.Text($"{LM.Get("GUI_Overlay_FpsAvg")}: {PerformanceProfiler.Singleton.FramerateAvg:N0}FPS");
                    ImGui.Text($"{LM.Get("GUI_Overlay_FpsMin")}: {PerformanceProfiler.Singleton.FramerateMin:N0}FPS");
                    ImGui.Text($"{LM.Get("GUI_Overlay_FpsMax")}: {PerformanceProfiler.Singleton.FramerateMax:N0}FPS");
                    ImGui.Text($"{LM.Get("GUI_Overlay_RenderDelay")}: {PerformanceProfiler.Singleton.RenderTime:N3}ms");
                    ImGui.Text($"{LM.Get("GUI_Overlay_RamUsage")}: {PerformanceProfiler.Singleton.RAMUsage / 1_000_000f:N2}MB");
                    ImGui.Text($"{LM.Get("GUI_Overlay_GCSize")}: {PerformanceProfiler.Singleton.GCRAMUsage / 1_000_000f:N2}MB");
                    ImGui.Text($"{LM.Get("GUI_Overlay_VramUsage")}: {PerformanceProfiler.Singleton.VRAMUsage / 1_000_000f:N2}MB");
                    ImGui.Text($"{LM.Get("GUI_Overlay_Threads")}: {PerformanceProfiler.Singleton.Threads:N0}");
                    ImGui.Text($"{LM.Get("GUI_Overlay_RenderedThisFrame")}: {Entity.EntitiesRenderedThisFrame}");
                }
                ImGui.EndGroup();
            }
            if (ShowLevelStats)
            {
                ImGui.Spacing();
                ImGui.SeparatorText(LM.Get("GUI_Overlay_RenderStats"));
                ImGui.BeginGroup();
                ImGui.Text($"Loaded level: {levelName}");
                ImGui.Text($"{LM.Get("GUI_Overlay_LevelRegions")}: {EntityManager.Singleton.Regions.Count:N0}");
                ImGui.Text($"{LM.Get("GUI_Overlay_LevelZones")}: {EntityManager.Singleton.ZonesCount:N0}");
                ImGui.Text($"{LM.Get("GUI_Overlay_LevelMobys")}: {EntityManager.Singleton.MobysCount:N0}");
                ImGui.Text($"{LM.Get("GUI_Overlay_LevelVolumes")}: {EntityManager.Singleton.VolumesCount:N0}");
                ImGui.Text($"{LM.Get("GUI_Overlay_LevelTies")}: {EntityManager.Singleton.TiesCount:N0}");
                ImGui.Text($"{LM.Get("GUI_Overlay_LevelUFrags")}: {EntityManager.Singleton.UFragsCount:N0}");
                ImGui.Text($"{LM.Get("GUI_Overlay_TotalEntities")}: {EntityCluster.TotalEntities:N0}");
                ImGui.Text($"{LM.Get("GUI_Overlay_EntitiesRenderedThisFrame")}: {Entity.EntitiesRenderedThisFrame:N0}");
                ImGui.Text($"{LM.Get("GUI_Overlay_Textures")}: {LunaWindow.Instance.AssetManager?.Mobys.Count ?? 0:N0}");
                ImGui.EndGroup();
            }
            if (ShowCamInfo && useView && view != null)
            {
                float x = view.Camera.GetPitch();
                float y = view.Camera.GetYaw();

                ImGui.Spacing();
                ImGui.SeparatorText(LM.Get("GUI_Overlay_CameraStats"));
                ImGui.BeginGroup();
                ImGui.Text($"{LM.Get("GUI_Overlay_CameraPosition")}: {view.Camera.Position:N3}");
                ImGui.Text($"{LM.Get("GUI_Overlay_CameraRotation")}: ({x:N3} deg, {y:N3} deg)");
                ImGui.Text($"{LM.Get("GUI_Overlay_Resolution")}: ({(int)view.ViewportSize.X}x{(int)view.ViewportSize.Y})");
                ImGui.EndGroup();
            }
        }
        ImGui.End();
    }
}
