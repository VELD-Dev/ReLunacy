using ReLunacy.Engine.EntityManagement;
using System.Runtime.InteropServices;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace ReLunacy.Utility;

public class Overlay
{
    public static bool showOverlay = false;
    public static bool ShowFramerate { get => Program.Settings.OverlayFramerate; }
    public static bool ShowLevelStats { get => Program.Settings.OverlayLevelStats; }
    public static bool ShowProfiler { get => Program.Settings.OverlayProfiler; }
    public static bool ShowCamInfo { get => Program.Settings.OverlayCamInfo; }
    public static Vector2 Padding { get => Program.Settings.OverlayPadding; }
    public static int Location { get => Program.Settings.OverlayPos; }
    public static float OverlayAlpha { get => Program.Settings.OverlayOpacity; }

    private static string levelName
    {
        get
        {
            if (Program.ProvidedPath == "") return "None";

            List<string> chunks = [.. Program.ProvidedPath.Split(Path.DirectorySeparatorChar)];
            chunks.RemoveAll(s => s == "");
            return chunks[^1];
        }
    }

    public static void DrawOverlay(bool p_open)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoInputs;
        bool useView = Window.Singleton.IsAnyFrameOpened<View3DFrame>();
        View3DFrame? view = Window.Singleton.GetFirstFrame<View3DFrame>();

        if (Location >= 0 && Location <= 3)
        {
            Vector2 workPos = useView ? view.FramePos + view.FrameContentRegion.GetOriginF() : viewport.WorkPos;
            Vector2 workSize = useView ? view.FrameContentRegion.GetSizeF() : viewport.WorkSize;
            Vector2 windowPos, windowPosPivot;
            windowPos.X = (Location == 1 || Location == 3) ? (workPos.X + workSize.X - Padding.X) : (workPos.X + Padding.X);
            windowPos.Y = (Location >= 2) ? (workPos.Y + workSize.Y - Padding.Y) : (workPos.Y + Padding.Y);
            windowPosPivot.X = (Location == 1 || Location == 3) ? 1.0f : 0.0f;
            windowPosPivot.Y = (Location >= 2) ? 1.0f : 0.0f;
            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always, windowPosPivot);
            flags |= ImGuiWindowFlags.NoMove;
        }
        else if(Location == 4)
        {
            ImGui.SetNextWindowPos(useView ? view.FrameContentRegion.GetCenterF() : viewport.GetWorkCenter(), ImGuiCond.Always, new(0.5f, 0.5f));
            flags |= ImGuiWindowFlags.NoMove;
        }
        
        ImGui.SetNextWindowBgAlpha(OverlayAlpha);
        if(ImGui.Begin("Stats Overlay", ref p_open, flags)) 
        {
            if(ShowFramerate || ShowProfiler)
            {
                ImGui.SeparatorText("Performances");
                ImGui.BeginGroup();
                if (ShowFramerate)
                {
                    Vector4 colGreen = new(40f/255f, 1, 40f/255f, 1);
                    Vector4 colYellow = new(142f/255f, 1, 40f/255f, 1);
                    Vector4 colRed = new(1, 40f/255f, 40f/255f, 1);
                    float fps = PerformanceProfiler.Singleton.Framerate;
                    var textCol = true switch
                    {
                        true when fps < 15f => colRed,
                        true when fps >= 15f && fps < 50f => colYellow,
                        _ => colGreen,
                    };
                    ImGui.Text($"Framerate: ");
                    ImGui.SameLine();
                    ImGui.TextColored(textCol, $"{fps:N0}FPS");
                }
                if(ShowProfiler)
                {
                    ImGui.Text($"Framerate Avg.: {PerformanceProfiler.Singleton.FramerateAvg:N0}FPS");
                    ImGui.Text($"Framerate Min.: {PerformanceProfiler.Singleton.FramerateMin:N0}FPS");
                    ImGui.Text($"Framerate Max.: {PerformanceProfiler.Singleton.FramerateMax:N0}FPS");
                    ImGui.Text($"Render delay: {PerformanceProfiler.Singleton.RenderTime:N3}ms");
                    ImGui.Text($"RAM Usage: {PerformanceProfiler.Singleton.RAMUsage / Math.Pow(10, 6):N2}MB");
                    ImGui.Text($"GC Size: {PerformanceProfiler.Singleton.GCRAMUsage / Math.Pow(10, 6):N2}MB");
                    ImGui.Text($"VRAM Usage: {PerformanceProfiler.Singleton.VRAMUsage / Math.Pow(10, 6):N2}MB");
                    ImGui.Text($"Shaders: {MaterialManager.Materials.Count:N0}");
                    ImGui.Text($"Threads: {PerformanceProfiler.Singleton.Threads:N0}"); 
                }
                ImGui.EndGroup();
            }
            if(ShowLevelStats)
            {
                ImGui.Spacing();
                ImGui.SeparatorText("Render Stats");
                ImGui.BeginGroup();
                ImGui.Text($"Loaded level: {levelName}");
                // ... YES I AM CHEATING, WHAT NOW ?
                ImGui.Text($"Regions: {EntityManager.Singleton.Regions.Count:N0}");
                ImGui.Text($"Zones: {EntityManager.Singleton.ZonesCount:N0}");
                ImGui.Text($"Mobys: {EntityManager.Singleton.MobysCount:N0}");
                ImGui.Text($"Volumes: {EntityManager.Singleton.VolumesCount:N0}");
                ImGui.Text($"Ties: {EntityManager.Singleton.TiesCount:N0}");
                ImGui.Text($"UFrags: {EntityManager.Singleton.UFragsCount:N0}");
                ImGui.Text($"Total entities: {EntityCluster.TotalEntities:N0}");
                ImGui.Text($"Textures: {AssetManager.Singleton.Textures.Count:N0}");
                ImGui.Text($"Materials: {((Window.Singleton.FileManager?.isOld ?? false) ? Window.Singleton.AssetLoader?.shaderDB.Count : Window.Singleton.AssetLoader?.shaders.Count):N0}");
                ImGui.EndGroup();
            }
            if(ShowCamInfo)
            {
                float x, y;
                x = Camera.Main.transform.eulerRotation.X * (180f / MathHelper.Pi);
                y = Camera.Main.transform.eulerRotation.Y * (180f / MathHelper.Pi);
                ImGui.Spacing();
                ImGui.SeparatorText("Camera Info");
                ImGui.BeginGroup();
                ImGui.Text($"Position: {Camera.Main.transform.position:N3}");
                ImGui.Text($"Rotation: ({x:N3}°, {y:N3}°)");
                if(useView)
                {
                    float resx, resy;
                    resx = view.FrameContentRegion.Width;
                    resy = view.FrameContentRegion.Height;
                    ImGui.Text($"Resolution: ({resx}x{resy})");
                }
                ImGui.EndGroup();
            }
        }
        ImGui.End();
    }
}
