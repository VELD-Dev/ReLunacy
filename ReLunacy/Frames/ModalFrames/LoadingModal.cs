using Vector2 = System.Numerics.Vector2;

namespace ReLunacy.Frames.ModalFrames;

public class LoadingModal : ModalFrame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; }

    public ValueTuple<string, Vector2>[] LoadProgresses { get; set; }
    
    public LoadingModal(string loadingString, Vector2 loadProgress) : base()
    {
        LoadProgresses = [new(loadingString, loadProgress)];
    }

    public LoadingModal(List<ValueTuple<string, Vector2>> loadingTasks) : base()
    {
        LoadProgresses = [.. loadingTasks];
    }

    protected override void Render(float deltaTime)
    {
        foreach (var load in LoadProgresses)
        {
            ImGui.BeginGroup();
            ImGui.Text(load.Item1);
            ImGui.ProgressBar(load.Item2.X / load.Item2.Y, new(200, 50), $"{load.Item2.X:N0}/{load.Item2.Y:N0}");
            ImGui.EndGroup();
            ImGui.Spacing();
        }

        if (GetCompletedLoadings() >= LoadProgresses.Length) isOpen = false;
    }

    private int GetCompletedLoadings()
    {
        return LoadProgresses.Where(vt => vt.Item2.X == vt.Item2.Y).Count();
    }
}
