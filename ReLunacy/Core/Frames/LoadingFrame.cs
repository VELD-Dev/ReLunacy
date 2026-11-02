using System.Numerics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames;

/// <summary>
/// The window that shows what a load or an export is doing.
///
/// Deliberately not a modal. The work runs on its own thread and carries on whether or not anyone
/// is watching it, so blocking the whole editor behind it bought nothing - and a load that stalls
/// used to leave the user with no way back to the rest of the app. It can now be hidden, or closed
/// outright, and the work is unaffected: the status bar goes on reporting it either way.
///
/// Nothing about the work's lifetime hangs on this frame being on screen. Tasks leave the status
/// bar when the WORK reports itself finished, not when the window goes away.
/// </summary>
public class LoadingFrame : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } =
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings;

    private readonly Lock _progressLock = new();
    private readonly List<LoadingProgress> _loadProgresses;
    public readonly DateTime LoadStart = DateTime.Now;
    public DateTime LoadEnd;

    private volatile bool _loadingFinished;
    private bool _focusWanted;

    /// <summary>
    /// Whether the window is off screen. Hidden is NOT closed: the frame stays open and keeps
    /// taking progress, so the status bar carries on and the window can be brought back with
    /// everything it had. Only the drawing stops.
    /// </summary>
    public bool Hidden { get; private set; }

    /// <summary>
    /// Set by whoever is doing the work once it is done.
    ///
    /// Ending the status bar's tracking here, rather than when the window closes, is what lets the
    /// window be dismissed early: by then it may be hidden, or gone, or still up showing how long
    /// the job took - none of which says anything about whether the work is still running.
    /// </summary>
    public bool loadingFinished
    {
        get => _loadingFinished;
        set
        {
            _loadingFinished = value;
            if (value) EndTracking();
        }
    }

    public LoadingFrame(string loadingString, uint max)
    {
        FrameName = LM.Get("GUI_Frame_LoadingModal");
        _loadProgresses = [new LoadingProgress(loadingString, max, true)];
        Track(_loadProgresses[0]);
    }

    public LoadingFrame(List<LoadingProgress> loadingTasks)
    {
        FrameName = LM.Get("GUI_Frame_LoadingModal");
        _loadProgresses = [.. loadingTasks];
        foreach (var task in _loadProgresses) Track(task);
    }

    /// <summary>Registers a progress with the status bar, handing it the means to bring this window
    /// back - that is the only way back once the window has been hidden.</summary>
    private void Track(LoadingProgress progress) => BackgroundTasks.Begin(progress, Show);

    public void AddProgress(LoadingProgress progress)
    {
        lock (_progressLock) _loadProgresses.Add(progress);
        Track(progress);
    }

    public void RemoveProgress(LoadingProgress progress)
    {
        lock (_progressLock) _loadProgresses.Remove(progress);
        BackgroundTasks.End(progress);
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
        Track(newProgress);
    }

    public void Hide() => Hidden = true;

    /// <summary>Brings the window back, which is what the status bar's Show button does.</summary>
    public void Show()
    {
        Hidden = false;
        _focusWanted = true;
    }

    private void EndTracking()
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
            ImGuiPlus.CenteredText(LM.Get("GUI_LoadingFrame_Completed", $"{elapsed.TotalSeconds:N0}"));
        }

        ImGui.Separator();

        // Hide while it runs, close once it is done. Hiding stays available afterwards too, since
        // a finished window the user ignores is the same nuisance as a running one.
        if (ImGui.Button(LM.Get("GUI_LoadingFrame_Hide"))) Hide();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_LoadingFrame_HideTooltip"));

        if (loadingFinished)
        {
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_LoadingFrame_Close"))) isOpen = false;
        }
    }

    public override void RenderAsWindow(double deltaTime)
    {
        if (Hidden) return;

        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (_focusWanted)
        {
            // The window was not being submitted at all while hidden, so it has no position of its
            // own to come back to - centre it again, as if it had just opened.
            ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowFocus();
            _focusWanted = false;
        }

        base.RenderAsWindow(deltaTime);
    }
}
