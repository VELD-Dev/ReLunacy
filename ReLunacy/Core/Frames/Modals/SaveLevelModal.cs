using System.Numerics;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>
/// File > Save: lists what the editor changed in the level (placements, settings, new instances, layer, Area,
/// Scent and Pod changes, new Ties) and what cannot be written, then saves it into the open project
/// (GameplayWriter through the project's sink) off the main thread and says what happened; File > Install to Game puts
/// it into the game. A save that changed the files' structure reloads the level through the project, so every index,
/// offset and TUID comes from the saved files again.
/// </summary>
public sealed class SaveLevelModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private List<string>? _preview;
    private List<string> _previewProblems = [];
    private string? _error;
    private Task<LevelSaveResult>? _saving;
    private LevelSaveResult? _result;
    private int _shifted;
    private bool _reloaded;
    // What the unsaved-changes prompt was about to do; run when the dialog closes after a successful save.
    private readonly Action? _afterSave;

    public SaveLevelModal(Action? afterSave = null)
    {
        _afterSave = afterSave;
        FrameName = LM.Get("GUI_SaveLevel_Title");
        Preview();
    }

    /// <summary>The changes as a save would write them, from the same patch the save runs, without writing.</summary>
    private void Preview()
    {
        var files = LunaWindow.Instance.fileManager;
        if (files == null) { _error = LM.Get("GUI_SaveLevel_NoLevel"); return; }
        try
        {
            GameplayWriter.Game = LunaWindow.Instance.Level?.GameDefinition?.Id;
            var plan = GameplayWriter.PreviewPlan(files, EntityManager.Singleton.Regions.ToList(), Collisions());
            (_preview, _previewProblems, _shifted) = ([.. plan.Changes], [.. plan.Problems], plan.ShiftedHandles);
        }
        catch (Exception e) { _error = e.Message; }
    }

    protected override void Render(double deltaTime)
    {
        if (_error != null)
        {
            ImGui.TextWrapped(_error);
            if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
            return;
        }

        if (_result != null) { RenderResult(_result); return; }
        if (_saving != null)
        {
            if (_saving.IsCompleted)
            {
                _result = _saving.IsCompletedSuccessfully ? _saving.Result : LevelSaveResult.Refused(_saving.Exception?.GetBaseException().Message ?? "?");
                if (_result.Succeeded) ReLunacy.Utility.ConfigDocument.ReloadClean();
                if (_result.Succeeded) Project.ProjectSession.MarkSaved();
            }
            ImGui.TextUnformatted(LM.Get("GUI_SaveLevel_Saving"));
            return;
        }

        var preview = _preview ?? [];
        if (Project.ProjectSession.Current is { } project) ImGui.TextDisabled(LM.Get("GUI_SaveLevel_IntoProject", project.Name));
        ImGui.TextWrapped(preview.Count == 0 ? LM.Get("GUI_SaveLevel_NothingChanged") : LM.Get("GUI_SaveLevel_Changes", preview.Count));
        List(preview, new Vector4(0.6f, 0.9f, 0.6f, 1f));
        List(_previewProblems, new Vector4(1f, 0.7f, 0.3f, 1f));
        if (_shifted > 0) ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_SaveLevel_Shifted", _shifted));
        ImGui.TextDisabled(LM.Get("GUI_SaveLevel_Hint"));
        ImGui.Spacing();

        ImGui.BeginDisabled(preview.Count == 0 || Project.ProjectSession.Current == null);
        if (ImGui.Button(LM.Get("GUI_SaveLevel_Save")))
        {
            var files = LunaWindow.Instance.fileManager!;
            var regions = EntityManager.Singleton.Regions.ToList();
            var collisions = Collisions();
            var sink = Project.ProjectSession.Current!.Sink;
            _saving = Task.Run(() => GameplayWriter.Save(files, regions, collisions: collisions, sink: sink));
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_SaveLevel_Refresh"))) Preview();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
    }

    private static IReadOnlyDictionary<string, Engine.Assets.Collision.LevelCollision> Collisions() =>
        LunaWindow.Instance.Level?.CollisionByRegion ?? new Dictionary<string, Engine.Assets.Collision.LevelCollision>();

    private void RenderResult(LevelSaveResult result)
    {
        ImGui.TextColored(result.Succeeded ? new Vector4(0.6f, 0.9f, 0.6f, 1f) : new Vector4(1f, 0.45f, 0.4f, 1f), result.Summary);
        List(result.Changes, new Vector4(0.8f, 0.8f, 0.8f, 1f));
        List(result.Problems, new Vector4(1f, 0.7f, 0.3f, 1f));
        if (result.ShiftedHandles > 0) ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_SaveLevel_Shifted", result.ShiftedHandles));
        if (result.BackupPath != null) ImGui.TextWrapped(LM.Get("GUI_SaveLevel_Backup", result.BackupPath));
        if (result.Succeeded && result.Structural && _afterSave == null)
        {
            // Every index, offset and TUID of the new files is read again. With something left unsaved the editor's
            // state would be thrown away, so the user reloads once they have looked.
            if (result.Problems.Count == 0 && !_reloaded) Reload();
            else if (!_reloaded)
            {
                ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_SaveLevel_ReloadNeeded"));
                if (ImGui.Button(LM.Get("GUI_SaveLevel_Reload"))) Reload();
                ImGui.SameLine();
            }
        }
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord")))
        {
            isOpen = false;
            // The editor's offsets and new instances are stale after a structural save: saving again would append twice.
            if (result.Succeeded && result.Structural && _afterSave == null && !_reloaded) Reload();
            if (result.Succeeded) _afterSave?.Invoke();
        }
    }

    private void Reload()
    {
        _reloaded = true;
        Project.ProjectSession.Reload();
    }

    private static void List(IReadOnlyList<string> lines, Vector4 colour)
    {
        if (lines.Count == 0) return;
        ImGui.BeginChild($"##lines{lines.GetHashCode()}", new Vector2(560f, MathF.Min(lines.Count, 12) * ImGui.GetTextLineHeightWithSpacing() + 8f), ImGuiChildFlags.Borders);
        foreach (var line in lines) ImGui.TextColored(colour, line);
        ImGui.EndChild();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
