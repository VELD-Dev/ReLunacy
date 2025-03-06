
using ReLunacy.Frames.ModalFrames;

namespace ReLunacy.Frames
{
    internal class FileSelectionDialog : Frame
    {
        protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoDocking;

        public FileSelectionDialog() : base()
        {
            FrameName = "Select a Level";
        }

        public string levelPath = "";

        protected override void Render(float deltaTime)
        {
            ImGui.BeginGroup();
            ImGui.Text("Level Path");
            ImGui.SameLine();
            ImGui.InputTextWithHint("", "C:\\NPEA00088\\packed\\levels\\metropolis\\main.dat", ref levelPath, 256);
            ImGui.SameLine();
            ImGui.Button("...");
            ImGui.SameLine();
            if(ImGui.Button("Paste"))
            {
                try
                {
                    if (ImGui.GetClipboardText() != null)
                        levelPath = ImGui.GetClipboardText();
                }
                catch(Exception e)
                {
                    LunaLog.LogError($"Unable to paste from clipboard: {e}");
                }
            }


            if(ImGui.Button("Cancel")) isOpen = false;
            ImGui.SameLine();
            if(ImGui.Button("Load"))
            {
                if(levelPath == "")
                {
                    Console.WriteLine("Level Path is empty!");
                }
                else
                {
                    Program.ProvidedPath = levelPath;
                    var lm = new LoadingModal("Loading level...", 1);
                    Task.Run(() => Window.Singleton.LoadLevelDataAsync(levelPath, lm));
                    isOpen = false;
                }
            }
            ImGui.EndGroup();
        }

        public override void RenderAsWindow(float deltaTime)
        {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(450, 100));
            base.RenderAsWindow(deltaTime);
        }
    }
}