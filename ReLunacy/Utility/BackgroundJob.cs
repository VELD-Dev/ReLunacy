using ReLunacy.Core;
using ReLunacy.Core.Frames;

namespace ReLunacy.Utility;

/// <summary>
/// Runs one long piece of work off the main thread behind a loading bar, and hands its result back
/// ON the main thread.
///
/// <see cref="ExportRunner"/> does this for exports, where the end of the job is always the same
/// (a result modal). This is the general case, for work whose completion has to go back into
/// whichever panel started it - installing a script into a game archive, say, which reads, rebuilds,
/// verifies and copies a file that can be a couple of hundred megabytes, and used to do all of it
/// inside one ImGui frame with the editor frozen and nothing to show for it.
///
/// The work is handed the bar to report into and must not touch anything else of the UI's: the
/// completion callback, which runs on the main thread, is where that belongs.
/// </summary>
public static class BackgroundJob
{
    /// <summary>Steps on the bar - see LunaWindow.LoadBarSteps for why this is not 100.</summary>
    private const uint Steps = 1000;

    /// <param name="frameName">The loading window's title.</param>
    /// <param name="status">What the bar says before the work has reported anything.</param>
    /// <param name="work">The job. Receives the bar it should report into (see
    /// <see cref="PhaseLabels{TPhase}"/> for turning an engine phase report into one of its lines).</param>
    /// <param name="onCompleted">Run on the main thread once the work returns.</param>
    /// <param name="onFailed">Run on the main thread if the work threw. The exception is logged
    /// either way.</param>
    public static void Run<TResult>(string frameName, string status,
        Func<LoadingProgress, TResult> work, Action<TResult> onCompleted, Action<Exception>? onFailed = null)
    {
        var bar = new LoadingProgress(status, Steps, true);
        var frame = new LoadingFrame([bar]) { FrameName = frameName };
        LunaWindow.Instance.AddFrame(frame);

        Task.Run(() =>
        {
            try
            {
                TResult result = work(bar);
                LunaWindow.Instance.QueueOnMainThread(() =>
                {
                    Finish(frame);
                    onCompleted(result);
                });
            }
            catch (Exception e)
            {
                LunaLog.LogError($"{frameName} failed: {e}");
                LunaWindow.Instance.QueueOnMainThread(() =>
                {
                    Finish(frame);
                    onFailed?.Invoke(e);
                });
            }
        });
    }

    /// <summary>Takes the job out of the status bar and closes its window. Marking it finished is
    /// what ends the tracking, whether or not the window is still on screen.</summary>
    private static void Finish(LoadingFrame frame)
    {
        frame.LoadEnd = DateTime.Now;
        frame.loadingFinished = true;
        frame.isOpen = false;
    }
}
