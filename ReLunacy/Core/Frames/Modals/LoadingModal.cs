using ImGuiNET;
using ReLunacy.Utility;
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

    public List<LoadingProgress> LoadProgresses { get; set; }
    public readonly DateTime LoadStart = DateTime.Now;
    public DateTime LoadEnd;

    public bool loadingFinished = false;

    public LoadingModal(string loadingString, uint max) : base()
    {
        FrameName = "Loading...";
        LoadProgresses = [new(loadingString, max)];
    }

    public LoadingModal(List<LoadingProgress> loadingTasks) : base()
    {
        FrameName = "Loading...";
        LoadProgresses = [.. loadingTasks];
    }

    protected override void Render(double deltaTime)
    {
        var snapshot = LoadProgresses.ToList();
        foreach (var load in snapshot)
        {
            if (load is null)
                break;
            ImGui.BeginGroup();
            ImGui.Text(load.status);
            ImGui.ProgressBar(load.Progress, new(400, 20), $"{load.current:N0}/{load.max:N0}");
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
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetWorkCenter(), ImGuiCond.Appearing, new(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }

    public void UpdateProgress(int index, Vector2 newProgress, string? newText = null)
    {
        var originalProg = LoadProgresses[index];
        if (newText is not null)
        {
            originalProg.status = newText;
        }
        originalProg.current = (uint)newProgress.X;
        originalProg.max = (uint)newProgress.Y;
        LoadProgresses[index] = originalProg;
    }
}
