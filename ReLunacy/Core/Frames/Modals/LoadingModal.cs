using Hexa.NET.ImGui;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Core.Frames.Modals;

public class LoadingModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly Lock _progressLock = new();
    private readonly List<LoadingProgress> _loadProgresses;
    public readonly DateTime LoadStart = DateTime.Now;
    public DateTime LoadEnd;

    public volatile bool loadingFinished = false;

    public LoadingModal(string loadingString, uint max) : base()
    {
        FrameName = LM.Get("GUI_Frame_LoadingModal");
        _loadProgresses = [new(loadingString, max, true)];
    }

    public LoadingModal(List<LoadingProgress> loadingTasks) : base()
    {
        FrameName = LM.Get("GUI_Frame_LoadingModal");
        _loadProgresses = [.. loadingTasks];
    }

    public void AddProgress(LoadingProgress progress)
    {
        lock (_progressLock)
            _loadProgresses.Add(progress);
    }

    public void RemoveProgress(LoadingProgress progress)
    {
        lock (_progressLock)
            _loadProgresses.Remove(progress);
    }

    protected override void Render(double deltaTime)
    {
        List<LoadingProgress> snapshot;
        lock (_progressLock)
            snapshot = _loadProgresses.ToList();

        foreach (var load in snapshot)
        {
            if (load is null)
                break;
            ImGui.BeginGroup();
            ImGui.Text(load.status);
            ImGui.ProgressBar(load.Progress, new(400, 20), load.isPercentage ? $"{load.GetPercents():N1}%" : $"{load.current:N0}/{load.max:N0}");
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
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().WorkPos + ImGui.GetMainViewport().WorkSize * 0.5f, ImGuiCond.Appearing, new(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }

    public void UpdateProgress(int index, Vector2 newProgress, string? newText = null)
    {
        lock (_progressLock)
        {
            var originalProg = _loadProgresses[index];
            if (newText is not null)
            {
                originalProg.status = newText;
            }
            originalProg.current = (uint)newProgress.X;
            originalProg.max = (uint)newProgress.Y;
            _loadProgresses[index] = originalProg;
        }
    }
}
