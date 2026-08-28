using System.Numerics;
using ReLunacy.Engine.Diagnostics;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>Live per-frame CPU breakdown fed by <see cref="FrameProfiler"/>. Answers "where does the
/// frame go?" - command recording, submission, the GPU-idle stall, present - and prints a verdict
/// naming the dominant cost so the next optimisation target is obvious.
///
/// The numbers are CPU wall-clock: there is no GPU timestamp query in NeoVeldrid, so the GPU's own
/// per-pass time can't be read. The "GPU Wait" phase (the per-frame WaitForIdle) is the stand-in -
/// it is exactly how long the CPU sat blocked for the GPU to finish, which is the honest measure of
/// the GPU tail as long as the frame ends with a full sync. See FrameProfiler's class summary.</summary>
public class ProfilerFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    public ProfilerFrame() : base()
    {
        FrameName = LM.Get("GUI_Frame_Profiler");
    }

    protected override void Render(double deltaTime)
    {
        var profiler = FrameProfiler.Singleton;

        double frameMs = profiler.FrameAvgMs;
        double fps = frameMs > 0 ? 1000.0 / frameMs : 0;
        ImGui.Text(LM.Get("GUI_Frame_Profiler_FrameTotal", frameMs, fps));

        DrawVerdict(profiler.Verdict());

        ImGui.Separator();

        var phases = profiler.Snapshot();
        // Only the root ("Frame") present means no real work has been sampled yet (level not loaded,
        // or the first couple of frames). Say so rather than showing a lone 100 % row.
        if (phases.Count <= 1)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_Profiler_Collecting"));
            return;
        }

        if (ImGui.BeginTable("profiler_phases", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(LM.Get("GUI_Frame_Profiler_Phase"), ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn(LM.Get("GUI_Frame_Profiler_Last"), ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn(LM.Get("GUI_Frame_Profiler_Avg"), ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn(LM.Get("GUI_Frame_Profiler_Percent"), ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableHeadersRow();

            foreach (var p in phases)
            {
                if (p.Name == FrameProfiler.RootPhase) continue; // the total is already the header line

                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                // Depth-1 phases are the top-level frame stages; deeper ones (3D Record/Submit) are
                // indented so the containment reads at a glance.
                if (p.Depth > 1) ImGui.Indent((p.Depth - 1) * 14f);
                if (p.Name == FrameProfiler.GpuWaitPhase)
                    ImGui.TextColored(GpuWaitColour, p.Name);
                else
                    ImGui.Text(p.Name);
                if (p.Depth > 1) ImGui.Unindent((p.Depth - 1) * 14f);

                ImGui.TableNextColumn();
                ImGui.Text($"{p.LastMs:0.00}");

                ImGui.TableNextColumn();
                ImGui.Text($"{p.AvgMs:0.00}");

                ImGui.TableNextColumn();
                ImGui.ProgressBar((float)(p.Percent / 100.0), new Vector2(-1, 0), $"{p.Percent:0.0}%");
            }

            ImGui.EndTable();
        }

        var counters = profiler.Counters();
        if (counters.Count > 0)
        {
            ImGui.SeparatorText(LM.Get("GUI_Frame_Profiler_Counters"));
            foreach (var (name, value) in counters)
                ImGui.Text($"{name}: {value:N0}");
        }

        ImGui.Spacing();
        ImGui.TextDisabled(LM.Get("GUI_Frame_Profiler_GpuNote"));
    }

    private static readonly Vector4 GpuWaitColour = new(0.55f, 0.75f, 1f, 1f);
    private static readonly Vector4 CpuColour = new(1f, 0.75f, 0.4f, 1f);
    private static readonly Vector4 GpuColour = new(0.55f, 0.75f, 1f, 1f);
    private static readonly Vector4 PresentColour = new(0.7f, 0.7f, 0.7f, 1f);

    private static void DrawVerdict(FrameProfiler.FrameVerdict verdict)
    {
        var colour = verdict.Bound switch
        {
            FrameProfiler.Bound.Cpu => CpuColour,
            FrameProfiler.Bound.GpuOrSync => GpuColour,
            FrameProfiler.Bound.Present => PresentColour,
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1f),
        };

        ImGui.TextColored(colour, verdict.Headline);
        if (!string.IsNullOrEmpty(verdict.Detail))
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextDisabled(verdict.Detail);
            ImGui.PopTextWrapPos();
        }
    }
}
