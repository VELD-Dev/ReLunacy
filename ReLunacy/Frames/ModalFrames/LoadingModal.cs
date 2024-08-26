using Vector2 = System.Numerics.Vector2;

namespace ReLunacy.Frames.ModalFrames;

public class LoadingModal : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    public ValueTuple<string, Vector2>[] LoadProgresses { get; set; }
    public readonly DateTime LoadStart = DateTime.Now;
    public DateTime LoadEnd;

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
            ImGui.ProgressBar(load.Item2.X / load.Item2.Y, new(400, 20), $"{load.Item2.X:N0}/{load.Item2.Y:N0}");
            ImGui.EndGroup();
            ImGui.Spacing();
        }
        if(!loadingFinished)
        {
            var elapsed = DateTime.Now - LoadStart;
            ImGuiPlus.CenteredText($"{elapsed.TotalSeconds:N0}s elapsed");
        }
        else
        {
            var elapsed = LoadEnd - LoadStart;
            ImGuiPlus.CenteredText($"Completed in {elapsed.TotalSeconds:N0}s.");
        }
    }

    public override void RenderAsWindow(float deltaTime)
    {
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetWorkCenter(), ImGuiCond.Appearing, new(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
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
}
