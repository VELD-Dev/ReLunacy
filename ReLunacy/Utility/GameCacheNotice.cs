using System.Numerics;
using ReLunacy.Engine.Packing;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Utility;

/// <summary>
/// Shown after ReLunacy rewrote a game archive: the game's disk cache of its archives may now hand it stale data (a
/// level then loads for ever), so it should be cleared before playing. The cache is looked for in the RPCS3 layout
/// and cleared only on a click (Engine.Packing.GameCaches); elsewhere the notice says what to look for.
/// </summary>
public sealed class GameCacheNotice(string archivePath)
{
    private IReadOnlyList<string>? _found;
    private string? _status;

    public void Draw()
    {
        _found ??= Find();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.35f, 1f), LM.Get("GUI_GameCache_Title"));
        ImGui.TextWrapped(LM.Get("GUI_GameCache_Hint"));
        if (_status != null) { ImGui.TextDisabled(_status); return; }
        if (_found.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_GameCache_NotFound"));
            return;
        }
        foreach (string cache in _found) ImGui.TextDisabled(cache);
        if (ImGui.Button(LM.Get("GUI_GameCache_Clear")))
        {
            try
            {
                GameCaches.Clear(_found);
                _status = LM.Get("GUI_GameCache_Cleared");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _status = LM.Get("GUI_GameCache_Failed", e.Message);
            }
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Backups_OpenFolder"))) ShellUtils.OpenFolder(_found[0]);
    }

    private IReadOnlyList<string> Find()
    {
        try { return GameCaches.Find(archivePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
