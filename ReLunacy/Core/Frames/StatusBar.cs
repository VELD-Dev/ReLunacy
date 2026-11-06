using System.Numerics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames;

/// <summary>
/// The strip under the dockspace: what the editor is working on, at the right-hand end.
///
/// Drawn as a viewport side bar rather than as a window of its own, because that is what takes the
/// space out of the viewport's work area - the dockspace sizes itself from WorkPos/WorkSize, so it
/// shrinks to fit above this without either of them knowing about the other. It therefore has to be
/// drawn BEFORE the dockspace each frame.
///
/// Only the newest task is shown. Several at once is normal - a level load uploads textures while
/// it reads geometry - but a bar that lists them all would resize itself constantly and still not
/// fit; the rest are a click away.
/// </summary>
public static class StatusBar
{
    private const string PopupId = "##relunacy_tasks";

    /// <summary>The share of the bar the progress takes. A sixth leaves the left-hand end free for
    /// whatever else wants to say something, and is wide enough for a label plus its bar.</summary>
    private const float WidthFraction = 1f / 6f;

    /// <summary>Drawn at the bar's left end. Set by whatever has something to say.</summary>
    public static string? Message { get; set; }

    /// <summary>
    /// How tall the bar has to be for a full-height widget to fit inside it.
    ///
    /// Window padding applies at the top AND the bottom, so a height of one frame plus a single
    /// padding leaves the content region SHORTER than the frame it has to hold - the progress bar
    /// is then clipped away entirely, and NoScrollbar means nothing shows that it happened. The
    /// bar looked empty for exactly that reason.
    /// </summary>
    public static float Height => ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y * 2f;

    /// <summary>Draws the bar. Call before the dockspace, so the space is reserved first.</summary>
    public static void Render()
    {
        var viewport = ImGui.GetMainViewport();
        bool open = ImGuiP.BeginViewportSideBar("##relunacy_status_bar", viewport, ImGuiDir.Down, Height,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking);

        if (open)
        {
            float top = ImGui.GetCursorPosY();
            if (Message is { Length: > 0 } message) ImGui.TextUnformatted(message);
            DrawTasks(top);
        }

        // Ended whatever Begin returned, as the side bar is still a window either way.
        ImGui.End();
    }

    /// <param name="top">The row's own Y, so the right-hand end lines up with the left-hand end
    /// whether or not anything was drawn there. SameLine cannot do this: with no preceding item on
    /// the line - the usual case, since Message is normally unset - it has nothing to sit beside.</param>
    private static void DrawTasks(float top)
    {
        var tasks = BackgroundTasks.Snapshot();
        float width = MathF.Max(120f, ImGui.GetWindowWidth() * WidthFraction);
        float right = ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X;

        if (tasks.Count == 0)
        {
            // Nothing running still gets a line, so the bar does not change height as work starts
            // and stops - a status bar that jumps is worse than one that says little.
            string idle = LM.Get("GUI_StatusBar_Idle");
            ImGui.SetCursorPos(new Vector2(right - ImGui.CalcTextSize(idle).X, top));
            ImGui.TextDisabled(idle);
            return;
        }

        var latest = tasks[0];
        ImGui.SetCursorPos(new Vector2(right - width, top));

        // The label rides inside the bar, so the whole thing is one click target of a known width.
        string overlay = tasks.Count > 1
            ? string.Format(LM.Get("GUI_StatusBar_AndMore"), Short(latest.Name), tasks.Count - 1)
            : Short(latest.Name);
        ImGui.ProgressBar(Math.Clamp(latest.Progress.Progress, 0f, 1f),
                          new Vector2(width, ImGui.GetFrameHeight()), overlay);

        // ProgressBar is not a button, so the click is read off the item it did submit.
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(string.Format(LM.Get("GUI_StatusBar_Tooltip"), tasks.Count));
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) ImGui.OpenPopup(PopupId);
        }

        DrawAllTasksPopup(tasks, width);
    }

    /// <summary>Every task at once, which is the thing the bar itself deliberately does not do.</summary>
    private static void DrawAllTasksPopup(IReadOnlyList<BackgroundTask> tasks, float width)
    {
        if (!ImGui.BeginPopup(PopupId)) return;

        ImGui.TextDisabled(string.Format(LM.Get("GUI_StatusBar_Running"), tasks.Count));
        ImGui.Separator();

        foreach (var task in tasks)
        {
            var progress = task.Progress;
            ImGui.PushID(task.Sequence.GetHashCode());
            ImGui.TextUnformatted(task.Name);
            ImGui.ProgressBar(Math.Clamp(progress.Progress, 0f, 1f),
                              new Vector2(MathF.Max(width, 260f), ImGui.GetFrameHeight()),
                              progress.isPercentage ? $"{progress.GetPercents():N1}%"
                                                    : $"{progress.current:N0}/{progress.max:N0}");
            if (progress.detail is { Length: > 0 } detail) ImGui.TextDisabled(detail);
            ImGui.TextDisabled($"{task.Elapsed.TotalSeconds:N0}s");

            // The way back to a window that was hidden or closed while its work carried on.
            if (task.Reveal is not null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(LM.Get("GUI_StatusBar_Show")))
                {
                    task.Reveal();
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.PopID();
            ImGui.Spacing();
        }

        ImGui.EndPopup();
    }

    /// <summary>A name trimmed to fit a sixth of the bar, since the bar cannot grow to suit it.</summary>
    private static string Short(string name)
    {
        const int limit = 28;
        name = name.Trim();
        return name.Length <= limit ? name : string.Concat(name.AsSpan(0, limit - 1), "…");
    }
}
