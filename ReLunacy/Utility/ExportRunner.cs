using ReLunacy.Core;
using ReLunacy.Core.Frames;
using ReLunacy.Engine.Export;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Utility;

/// <summary>Shared background-export plumbing behind every export button in the app: runs `action`
/// off the main thread behind a LoadingFrame progress bar, then reports success/failure via an
/// ExportResultModal. `action` must report completion through LunaWindow.QueueExportCompletion
/// rather than touching openFrames directly (not thread-safe against concurrent ImGui rendering).</summary>
public static class ExportRunner
{
    /// <summary>Steps on the bar - see LunaWindow.LoadBarSteps for why this is not 100.</summary>
    private const uint Steps = 1000;

    /// <param name="action">The export, handed the reporter to call as it goes. Stage labels are
    /// resolved before it starts (see <see cref="PhaseLabels{TPhase}"/>) and the bar itself is
    /// mutated in place rather than replaced per report.</param>
    public static void Run(string progressTitle, string outputPath, string outputDirectory, Action<Action<ExportProgressReport>> action)
    {
        string status = LM.Get("GUI_Frame_AssetViewer_ExportingStatus", Path.GetFileName(outputPath));
        var labels = PhaseLabels<ExportPhase>.Snapshot("ExportPhase");

        var bar = new LoadingProgress(status, Steps, true);
        var progressFrame = new LoadingFrame([bar]) { FrameName = progressTitle };
        LunaWindow.Instance.AddFrame(progressFrame);

        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                action(report => labels.Apply(bar, report.Phase, report.Overall, report.Done, report.Total));

                LunaWindow.Instance.QueueExportCompletion(new LunaWindow.ExportCompletion(progressFrame, true, outputPath, outputDirectory));
                LunaLog.LogInfo(LM.Get("GUI_Frame_AssetViewer_ExportSucceeded", outputPath));
            }
            catch (Exception ex)
            {
                LunaWindow.Instance.QueueExportCompletion(new LunaWindow.ExportCompletion(progressFrame, false, ex.Message, outputDirectory));
                LunaLog.LogError(LM.Get("GUI_Frame_AssetViewer_ExportFailed", ex.Message));
            }
        });
    }

    /// <summary>For an exporter that only counts its meshes (a single asset, where the write is a
    /// small part of a short job): its 0..1 becomes the mesh stage of the bar above.</summary>
    public static Action<float> MeshesOnly(Action<ExportProgressReport> report) =>
        fraction => report(ExportPhases.Report(ExportPhase.Meshes, fraction));
}
