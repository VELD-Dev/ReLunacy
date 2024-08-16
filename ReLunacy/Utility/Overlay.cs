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
    public static int location = 0;
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
            | ImGuiWindowFlags.NoNav;
        if(location >= 0)
        {
            float PAD = 10.0f;
            Vector2 workPos = viewport.WorkPos;
            Vector2 workSize = viewport.WorkSize;
            Vector2 windowPos, windowPosPivot;
            windowPos.X = (location == 1) ? (workPos.X + workSize.X - PAD) : (workPos.X + PAD);
            windowPos.Y = (location == 2) ? (workPos.Y + workSize.Y - PAD) : (workPos.Y + PAD);
            windowPosPivot.X = (location == 1) ? 1.0f : 0.0f;
            windowPosPivot.Y = (location == 2) ? 1.0f : 0.0f;
            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always, windowPosPivot);
            flags |= ImGuiWindowFlags.NoMove;
        }
        else if(location == -2)
        {
            ImGui.SetNextWindowPos(viewport.GetWorkCenter(), ImGuiCond.Always, new(0.5f, 0.5f));
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
                ImGui.Text($"Mobys: {EntityManager.Singleton.Mobys.Count:N0}");
                ImGui.Text($"Moby Handles: {EntityManager.Singleton.MobyHandles.Count:N0}");
                ImGui.Text($"Ties: {AssetManager.Singleton.Ties.Count:N0}");
                ImGui.Text($"UFrags: {AssetManager.Singleton.UFrags.Count:N0}");
                ImGui.Text($"Textures: {AssetManager.Singleton.Textures.Count:N0}");
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
                ImGui.EndGroup();
            }
        }
        ImGui.End();
    }
}
