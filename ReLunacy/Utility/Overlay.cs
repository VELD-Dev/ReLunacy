using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace ReLunacy.Utility;

public class Overlay
{
    public static bool showOverlay = false;
    public static bool showFramerate = true;
    public static bool showLevelStats = false;
    public static bool showProfiler = false;
    public static int location = 0;
    public static float overlayAlpha = 0.35f;

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

        ImGui.SetNextWindowBgAlpha(overlayAlpha);
        if(ImGui.Begin("Stats Overlay", ref p_open, flags)) 
        {
            if(showFramerate || showProfiler)
            {
                ImGui.Text("Performances");
                ImGui.Separator();
                if (showFramerate)
                {
                    Vector4 colGreen = new(40, 255, 40, 255);
                    Vector4 colYellow = new(142, 255, 40, 255);
                    Vector4 colRed = new(255, 40, 40, 255);
                    Vector4 textCol;
                    float fps = Window.Singleton.framerate;
                    switch (true)
                    {
                        case true when fps < 15f:
                            textCol = colRed;
                            break;
                        case true when fps >= 15f && fps < 50f:
                            textCol = colYellow;
                            break;
                        default:
                            textCol = colGreen;
                            break;
                    }

                    ImGui.Text($"Framerate: ");
                    ImGui.SameLine();
                    ImGui.TextColored(textCol, $"{fps}FPS");

                }
            }
        }
    }
}
