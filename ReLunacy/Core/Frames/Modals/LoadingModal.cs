using System.Numerics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

public class LoadingModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly Lock _progressLock = new();
    private readonly List<LoadingProgress> _loadProgresses;
    public readonly DateTime LoadStart = DateTime.Now;
    public DateTime LoadEnd;

    public volatile bool loadingFinished;

    public LoadingModal(string loadingString, uint max)
    {
        FrameName = LM.Get("GUI_Frame_LoadingModal");
        _loadProgresses = [new LoadingProgress(loadingString, max, true)];
        BackgroundTasks.Begin(_loadProgresses[0]);
    }

    public LoadingModal(List<LoadingProgress> loadingTasks)
    {
        FrameName = LM.Get("GUI_Frame_LoadingModal");
        _loadProgresses = [.. loadingTasks];
        foreach (var task in _loadProgresses) BackgroundTasks.Begin(task);
    }

    public void AddProgress(LoadingProgress progress)
    {
        lock (_progressLock) _loadProgresses.Add(progress);
        BackgroundTasks.Begin(progress);
    }

    public void RemoveProgress(LoadingProgress progress)
    {
        lock (_progressLock) _loadProgresses.Remove(progress);
        BackgroundTasks.End(progress);
    }

    /// <summary>
    /// Stops the status bar listing this modal's work once the modal is gone.
    ///
    /// Done on close rather than when the progress reaches its total, because a task that has
    /// counted to its maximum is not necessarily finished - UpdateProgress replaces entries, and a
    /// load reports several stages through the same bar.
    /// </summary>
    public override void OnClosed()
    {
        List<LoadingProgress> snapshot;
        lock (_progressLock) snapshot = [.. _loadProgresses];
        BackgroundTasks.End(snapshot);
    }

    protected override void Render(double deltaTime)
    {
        List<LoadingProgress> snapshot;
        lock (_progressLock) snapshot = [.. _loadProgresses];

        foreach (var load in snapshot)
        {
            ImGui.BeginGroup();
            ImGui.Text(load.status);
            ImGui.ProgressBar(load.Progress, new Vector2(400, 20), load.isPercentage ? $"{load.GetPercents():N1}%" : $"{load.current:N0}/{load.max:N0}");
            ImGui.EndGroup();
            ImGui.Spacing();
        }

        if (!loadingFinished)
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

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }

    public void UpdateProgress(int index, LoadingProgress newProgress)
    {
        LoadingProgress? replaced = null;
        lock (_progressLock)
        {
            if (index >= _loadProgresses.Count) return;
            replaced = _loadProgresses[index];
            _loadProgresses[index] = newProgress;
        }
        // The old one is a different object, so the registry would keep listing it for ever.
        if (replaced is not null) BackgroundTasks.End(replaced);
        BackgroundTasks.Begin(newProgress);
    }
}
