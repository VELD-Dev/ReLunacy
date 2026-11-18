using System.Numerics;
using ReLunacy.Core.Undo;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>
/// The level's occlusion survey (<see cref="OcclusionBake.Survey"/>), made on the UI thread and kept until the level, the
/// scene's structure, the edit history or a bake changes it, so the menu, the viewer and the modal share one.
/// </summary>
internal static class OcclusionSurveys
{
    /// <summary>Counted when a bake stored results (the bakes are not an edit, so they have no undo step).</summary>
    public static int BakeVersion;

    private static OcclusionBakeSurvey? _survey;
    private static (object? Level, int Structure, int Revision, int Bake) _key;

    public static OcclusionBakeSurvey? Current
    {
        get
        {
            var window = LunaWindow.Instance;
            if (window.Level is not { } level || window.fileManager is not { } files) return null;
            var key = (level, EntityManager.StructureVersion, UndoManager.Singleton.Revision, BakeVersion);
            if (_survey == null || key != _key)
            {
                _key = key;
                _survey = OcclusionBake.Survey(files, EntityManager.Singleton.Regions.ToList());
            }
            return _survey;
        }
    }

    public static void Invalidate() => _survey = null;
}

/// <summary>
/// Bake > Bake occlusion: surveys what needs a planet occlusion bake, then runs it on a task with a progress bar, an
/// estimate and a Cancel (what finished is kept). Cannot be closed while it runs.
/// </summary>
public sealed class BakeOcclusionModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    public static void Open()
    {
        if (LunaWindow.Instance.IsAnyFrameOpened<BakeOcclusionModal>()) return;
        OcclusionSurveys.Invalidate();
        if (OcclusionSurveys.Current is { } survey) LunaWindow.Instance.AddFrame(new BakeOcclusionModal(survey));
    }

    /// <summary>Keeps the latest progress for the UI thread: the callbacks arrive on pool threads.</summary>
    private sealed class Latest : IProgress<OcclusionBakeProgress>
    {
        private readonly object _gate = new();
        private OcclusionBakeProgress? _value;

        public void Report(OcclusionBakeProgress value)
        {
            lock (_gate) _value = value;
        }

        public OcclusionBakeProgress? Value
        {
            get { lock (_gate) return _value; }
        }
    }

    private readonly OcclusionBakeSurvey _survey;
    private readonly Latest _progress = new();
    private CancellationTokenSource? _cancel;
    private Task? _task;
    private volatile OcclusionBakeResult? _result;
    private volatile string? _error;
    private bool _cancelling;

    private BakeOcclusionModal(OcclusionBakeSurvey survey)
    {
        FrameName = LM.Get("GUI_Bake_Title");
        _survey = survey;
    }

    private bool Running => _task != null && !_task.IsCompleted;

    private void Start()
    {
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        _task = Task.Run(() =>
        {
            try { _result = _survey.Run(_progress, token); }
            catch (Exception e) { _error = e.Message; }
            finally { OcclusionSurveys.BakeVersion++; }
        });
    }

    protected override void Render(double deltaTime)
    {
        if (_task == null)
        {
            RenderIntro();
            return;
        }
        if (Running) RenderRunning();
        else RenderDone();
    }

    private void RenderIntro()
    {
        int ties = _survey.Ties, ufrags = _survey.UFrags;
        if (ties + ufrags == 0)
        {
            ImGui.TextUnformatted(LM.Get("GUI_Bake_Nothing"));
            if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
            return;
        }
        string what = ties > 0 && ufrags > 0 ? LM.Get("GUI_Bake_TiesAndUFrags", ties, ufrags)
            : ties > 0 ? LM.Get("GUI_Bake_TiesOnly", ties) : LM.Get("GUI_Bake_UFragsOnly", ufrags);
        ImGui.TextUnformatted(LM.Get("GUI_Bake_Intro", what));
        ImGui.Spacing();
        if (ImGui.Button(LM.Get("GUI_Bake_Start"))) Start();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
    }

    private void RenderRunning()
    {
        var p = _progress.Value;
        int done = p?.Done ?? 0, total = p?.Total ?? _survey.Pending.Count;
        ImGui.ProgressBar(total > 0 ? done / (float)total : 0f, new Vector2(420f, 0f), $"{done} / {total}");
        if (p is { } latest)
        {
            if (latest.Region.Length > 0) ImGui.TextUnformatted(LM.Get("GUI_Bake_Region", latest.Region));
            if (latest.Current != null) ImGui.TextDisabled(latest.Current);
            ImGui.TextUnformatted(latest.Remaining is { } left ? LM.Get("GUI_Bake_Eta", (int)Math.Ceiling(left.TotalSeconds)) : LM.Get("GUI_Bake_EtaUnknown"));
        }
        else ImGui.TextUnformatted(LM.Get("GUI_Bake_EtaUnknown"));
        ImGui.Spacing();
        ImGui.BeginDisabled(_cancelling);
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord")))
        {
            _cancelling = true;
            _cancel?.Cancel();
        }
        ImGui.EndDisabled();
    }

    private void RenderDone()
    {
        if (_error != null) ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), LM.Get("GUI_Bake_Error", _error));
        else if (_result is { } r)
            ImGui.TextUnformatted(r.Cancelled ? LM.Get("GUI_Bake_Cancelled", r.Baked, r.Failed) : LM.Get("GUI_Bake_Done", r.Baked, r.Failed));
        ImGui.Spacing();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (Running)
        {
            // No close button while the task runs; Cancel is the way out.
            if (ImGui.BeginPopupModal(frameName, WindowFlags))
            {
                Render(deltaTime);
                ImGui.EndPopup();
            }
            return;
        }
        base.RenderAsWindow(deltaTime);
    }
}
