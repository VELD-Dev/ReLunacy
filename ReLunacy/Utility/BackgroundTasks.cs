namespace ReLunacy.Utility;

/// <summary>One piece of work the editor is doing, as the status bar sees it.</summary>
public sealed class BackgroundTask(LoadingProgress progress, long sequence)
{
    public LoadingProgress Progress { get; } = progress;

    /// <summary>
    /// Brings this task's window back, when it has one. A loading frame can be hidden or closed
    /// while its work runs on, and the status bar is then the only thing still pointing at it -
    /// so it has to be able to point the user back.
    /// </summary>
    public Action? Reveal { get; internal set; }

    /// <summary>
    /// Registration order, and what "latest" means. A clock is not enough: two tasks begun in the
    /// same tick read the same time, and the bar would then pick between them arbitrarily from one
    /// frame to the next.
    /// </summary>
    public long Sequence { get; } = sequence;

    public DateTime Started { get; } = DateTime.Now;

    /// <summary>Read live from the progress, so a task that renames itself mid-run says so.</summary>
    public string Name => Progress.status;

    public TimeSpan Elapsed => DateTime.Now - Started;
}

/// <summary>
/// Everything the editor is working on right now, for the status bar to show.
///
/// A registry rather than a walk over the open modals, for two reasons. Work outlives the modal
/// that started it - texture uploads carry on after the loading modal has gone - and work that
/// never had a modal should still be visible. Producers register what they are doing and remove it
/// when they are done, and nothing else needs to know who is watching.
///
/// Registration happens on worker threads while the UI thread reads, so every access is locked and
/// readers get a snapshot rather than the live list.
/// </summary>
public static class BackgroundTasks
{
    private static readonly Lock Gate = new();
    private static readonly List<BackgroundTask> Running = [];
    private static long _sequence;

    /// <summary>Starts tracking a progress. Safe to call twice with the same one - the second is
    /// ignored rather than listed twice, since producers hand the same progress to more than one
    /// consumer.</summary>
    public static BackgroundTask Begin(LoadingProgress progress, Action? reveal = null)
    {
        lock (Gate)
        {
            var existing = Running.Find(t => ReferenceEquals(t.Progress, progress));
            if (existing is not null)
            {
                // A later caller may know how to show the window when the first one did not.
                existing.Reveal ??= reveal;
                return existing;
            }
            var task = new BackgroundTask(progress, ++_sequence) { Reveal = reveal };
            Running.Add(task);
            return task;
        }
    }

    public static void End(LoadingProgress progress)
    {
        lock (Gate) Running.RemoveAll(t => ReferenceEquals(t.Progress, progress));
    }

    /// <summary>Stops tracking several at once - what a modal does when it closes.</summary>
    public static void End(IEnumerable<LoadingProgress> progresses)
    {
        foreach (var progress in progresses) End(progress);
    }

    /// <summary>Everything running, newest first.</summary>
    public static IReadOnlyList<BackgroundTask> Snapshot()
    {
        lock (Gate) return [.. Running.OrderByDescending(t => t.Sequence)];
    }

    /// <summary>The most recently started task, which is the one the bar shows.</summary>
    public static BackgroundTask? Latest
    {
        get
        {
            lock (Gate)
            {
                BackgroundTask? latest = null;
                foreach (var task in Running)
                    if (latest is null || task.Sequence > latest.Sequence) latest = task;
                return latest;
            }
        }
    }

    public static int Count
    {
        get { lock (Gate) return Running.Count; }
    }

    /// <summary>Forgets everything. For tests; the editor never needs it.</summary>
    public static void Clear()
    {
        lock (Gate) Running.Clear();
    }
}
