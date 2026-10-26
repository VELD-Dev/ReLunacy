using ReLunacy.Core;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Utility;

/// <summary>Shared background-export plumbing behind every export button in the app: runs `action`
/// off the main thread behind a LoadingModal progress bar, then reports success/failure via an
/// ExportResultModal. `action` must report completion through LunaWindow.QueueExportCompletion
/// rather than touching openFrames directly (not thread-safe against concurrent ImGui rendering).</summary>
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
