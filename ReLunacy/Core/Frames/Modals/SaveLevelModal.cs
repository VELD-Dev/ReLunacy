using System.Numerics;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>
/// File > Save Level: lists what the editor changed in the level's gameplay (Moby, volume, camera and path
/// placements, Clue and camera settings) before anything is written, then writes it into the level's archive
/// (GameplayWriter) off the main thread and says what happened: the changes, what could not be written, and where
/// the original was kept.
/// </summary>
public sealed class SaveLevelModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private List<string>? _preview;
    private List<string> _previewProblems = [];
    private string? _error;
    private Task<LevelSaveResult>? _saving;
    private LevelSaveResult? _result;

    public SaveLevelModal()
    {
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
            _preview = [];
            _previewProblems = [];
            if (!files.isOld)
                foreach (var region in EntityManager.Singleton.Regions)
                {
                    if (files.ReadOriginal($"{region.RegionName}/gp_prius.dat") is not { } original) continue;
                    var (_, changes, problems) = GameplayWriter.Patch(original, region);
                    _preview.AddRange(changes.Select(c => $"{region.RegionName}: {c}"));
                    _previewProblems.AddRange(problems.Select(p => $"{region.RegionName}: {p}"));
                }
            // The collision of every loaded region (the level's, on the old engine): what the collision editor changed.
            foreach (var (region, collision) in Collisions())
            {
                if (files.ReadOriginal(CollisionWriter.FileName(files, region)) is not { } original) continue;
                var (_, changes, problems) = CollisionWriter.Patch(original, collision);
                string label = files.isOld ? "collision" : $"{region} collision";
                _preview.AddRange(changes.Select(c => $"{label}: {c}"));
                _previewProblems.AddRange(problems.Select(p => $"{label}: {p}"));
            }
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
                _result = _saving.IsCompletedSuccessfully ? _saving.Result : LevelSaveResult.Refused(_saving.Exception?.GetBaseException().Message ?? "?");
            ImGui.TextUnformatted(LM.Get("GUI_SaveLevel_Saving"));
            return;
        }

        var preview = _preview ?? [];
        ImGui.TextWrapped(preview.Count == 0 ? LM.Get("GUI_SaveLevel_NothingChanged") : LM.Get("GUI_SaveLevel_Changes", preview.Count));
        List(preview, new Vector4(0.6f, 0.9f, 0.6f, 1f));
        List(_previewProblems, new Vector4(1f, 0.7f, 0.3f, 1f));
        ImGui.TextDisabled(LM.Get("GUI_SaveLevel_Hint"));
        ImGui.Spacing();

        ImGui.BeginDisabled(preview.Count == 0);
        if (ImGui.Button(LM.Get("GUI_SaveLevel_Save")))
        {
            var files = LunaWindow.Instance.fileManager!;
            var regions = EntityManager.Singleton.Regions.ToList();
            var collisions = Collisions();
            _saving = Task.Run(() => GameplayWriter.Save(files, regions, collisions: collisions));
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
        if (result.BackupPath != null) ImGui.TextWrapped(LM.Get("GUI_SaveLevel_Backup", result.BackupPath));
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
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
