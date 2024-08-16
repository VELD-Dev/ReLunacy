using Vector2 = System.Numerics.Vector2;

namespace ReLunacy.Frames.ModalFrames;

public class LoadingModal : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize;

    public ValueTuple<string, Vector2>[] LoadProgresses { get; set; }

    public bool loadingFinished = false;

    public LoadingModal(string loadingString, Vector2 loadProgress) : base()
    {
        FrameName = "Loading...";
        LoadProgresses = [new(loadingString, loadProgress)];
    }

    public LoadingModal(List<ValueTuple<string, Vector2>> loadingTasks) : base()
    {
        FrameName = "Loading...";
        LoadProgresses = [.. loadingTasks];
    }

    protected override void Render(float deltaTime)
    {
        foreach (var load in LoadProgresses)
        {
            ImGui.BeginGroup();
            ImGui.Text(load.Item1);
            ImGui.ProgressBar(load.Item2.X / load.Item2.Y, new(200, 15), $"{load.Item2.X:N0}/{load.Item2.Y:N0}");
            ImGui.EndGroup();
            ImGui.Spacing();
        }

        if (GetCompletedLoadings() >= LoadProgresses.Length) loadingFinished = true;
    }

    private int GetCompletedLoadings()
    {
        return LoadProgresses.Where(vt => vt.Item2.X == vt.Item2.Y).Count();
    }

    public void UpdateProgress(int index, Vector2 newProgress, string? newText = null)
    {
        var originalProg = LoadProgresses[index];
        if(newText is not null)
        {
            originalProg.Item1 = newText;
        }
        originalProg.Item2 = newProgress;
        LoadProgresses[index] = originalProg;
    }

    public void CloseModal(bool force = false)
    {
        if (loadingFinished && !force) return;

        isOpen = false;
    }
}
