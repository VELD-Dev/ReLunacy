using ReLunacy.Core;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Utility;

/// <summary>Shared background-export plumbing behind every export button in the app (single-asset
/// and whole-level): runs `action` off the main thread behind a LoadingModal progress bar so a big
/// export doesn't freeze the UI, then reports success/failure via an ExportResultModal.
///
/// `action` only ever touches the progress modal through UpdateProgress (which locks internally)
/// and otherwise reports back through LunaWindow.QueueExportCompletion — a ConcurrentQueue drained
/// on the main thread — rather than mutating openFrames itself, same rule LoadLevelDataAsync
/// follows for the same reason (openFrames is a plain List&lt;Frame&gt;, not thread-safe against
/// concurrent enumeration during ImGui rendering).</summary>
public static class ExportRunner
{
    public static void Run(string progressTitle, string outputPath, string outputDirectory, Action<Action<float>> action)
    {
        var progressModal = new LoadingModal(LM.Get("GUI_Frame_AssetViewer_ExportingStatus", Path.GetFileName(outputPath)), 100)
        {
            FrameName = progressTitle
        };
        LunaWindow.Instance.AddFrame(progressModal);

        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                action(progress => progressModal.UpdateProgress(0,
                    new LoadingProgress(LM.Get("GUI_Frame_AssetViewer_ExportingStatus", Path.GetFileName(outputPath)), 100, true) { current = (uint)(progress * 100) }));

                LunaWindow.Instance.QueueExportCompletion(new LunaWindow.ExportCompletion(progressModal, true, outputPath, outputDirectory));
                LunaLog.LogInfo(LM.Get("GUI_Frame_AssetViewer_ExportSucceeded", outputPath));
            }
            catch (Exception ex)
            {
                LunaWindow.Instance.QueueExportCompletion(new LunaWindow.ExportCompletion(progressModal, false, ex.Message, outputDirectory));
                LunaLog.LogError(LM.Get("GUI_Frame_AssetViewer_ExportFailed", ex.Message));
            }
        });
    }
}
