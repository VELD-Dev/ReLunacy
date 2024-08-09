using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace ReLunacy.Utility;

public class Overlay
{
    public static bool showOverlay = false;
    public static bool ShowFramerate { get => Program.Settings.OverlayFramerate; }
    public static bool ShowLevelStats { get => Program.Settings.OverlayLevelStats; }
    public static bool ShowProfiler { get => Program.Settings.OverlayProfiler; }
    public static int location = 0;
    public static float OverlayAlpha { get => Program.Settings.OverlayOpacity; }

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
                    ImGui.TextColored(textCol, $"{Math.Round(fps)}FPS");
                }
                if(ShowProfiler)
                {
                    ImGui.Text($"Render delay: {Math.Round(PerformanceProfiler.Singleton.RenderTime, 3)}ms");
                    ImGui.Text($"RAM Usage: {Math.Round(PerformanceProfiler.Singleton.RAMUsage / Math.Pow(10, 6), 2)}MB");
                    ImGui.Text($"GC Size: {Math.Round(PerformanceProfiler.Singleton.GCRAMUsage / Math.Pow(10, 6), 2)}MB");
                    ImGui.Text($"VRAM Usage: {Math.Round(PerformanceProfiler.Singleton.VRAMUsage / Math.Pow(10, 6), 2)}MB");
                    ImGui.Text($"Shaders: {MaterialManager.Materials.Count}");
                }
                ImGui.EndGroup();
            }
            if(ShowLevelStats)
            {
                ImGui.Spacing();
                ImGui.SeparatorText("Rendering Stats");
                ImGui.BeginGroup();
                ImGui.Text($"Loaded level: {(Program.ProvidedPath != "" ? Program.ProvidedPath.Split(Path.PathSeparator)[^1] : "None")}");
                // ... YES I AM CHEATING, WHAT NOW ?
                ImGui.Text($"Mobys: {EntityManager.Singleton.Mobys.Count}");
                ImGui.Text($"Moby Handles: {EntityManager.Singleton.MobyHandles.Count}");
                ImGui.Text($"Ties: {AssetManager.Singleton.Ties.Count}");
                ImGui.Text($"UFrags: {AssetManager.Singleton.UFrags.Count}");
                ImGui.Text($"Textures: {AssetManager.Singleton.Textures.Count}");
                ImGui.EndGroup();
            }
        }
        ImGui.End();
    }
}
