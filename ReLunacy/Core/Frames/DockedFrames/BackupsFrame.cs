using System.Numerics;
using ReLunacy.Engine.Packing;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// File > Backups: the dated copies every save keeps (Backups) of the open level's archives, of the game's
/// game.psarc, and of any other packed/game archive a save wrote (global_cached.psarc: config tables, game-wide
/// scripts), newest first, each one restorable or deletable.
///
/// Restoring is itself kept safe: the file being replaced is backed up first, like any save, so a restore can
/// be restored away. The level's archives are released while the file is swapped and reopened after, game.psarc's
/// record of the archive's size is brought back in step, and since the editor still holds the level as it was, it
/// offers to reload it. The oldest backup of a file is marked: it is the one taken before the first save, the
/// file as it was before ReLunacy ever wrote it.
/// </summary>
public sealed class BackupsFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private List<(string File, List<Backups.Entry> Backups)> _files = [];
    private string? _scannedFor;
    private (string File, Backups.Entry Backup, bool Delete)? _confirm;
    private Task<string>? _working;
    private string? _status;
    private bool _offerReload;
    private GameCacheNotice? _cacheNotice;

    /// <summary>Backups ticked for a bulk delete, by path.</summary>
    private readonly HashSet<string> _ticked = new(StringComparer.Ordinal);
    private bool _confirmBulk;

    public BackupsFrame()
    {
        FrameName = LM.Get("GUI_Frame_Backups");
    }

    private void Scan()
    {
        _scannedFor = Program.ProvidedPath;
        var files = new List<string>();
        var fm = LunaWindow.Instance.fileManager;
        if (fm != null)
        {
            if (fm.ArchivePaths.Count > 0) files.AddRange(fm.ArchivePaths);
            else if (fm.folderPath.Length > 0 && Directory.Exists(fm.folderPath))
                // A level opened from a folder: its files, and the regions' beside them.
                files.AddRange(Directory.EnumerateFiles(fm.folderPath, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".bak", StringComparison.Ordinal) && Backups.Of(f).Count > 0));
            if (files.FirstOrDefault() is { } first && GameArchiveSizes.FindGameArchive(first) is { } game) files.Add(game);
            // The game-wide archives a config table or script save wrote (global_cached.psarc).
            if (Engine.Config.GameConfigs.GameFolderFor(Program.ProvidedPath) is { } gameFolder)
                files.AddRange(Directory.EnumerateFiles(gameFolder, "*.psarc").Where(f => Backups.Of(f).Count > 0));
        }
        _files = [.. files.Distinct().Select(f => (f, Backups.Of(f)))];
        // Ticks on backups that are gone (deleted, restored over) go with them.
        _ticked.IntersectWith(_files.SelectMany(f => f.Backups).Select(b => b.Path));
    }

    protected override void Render(double deltaTime)
    {
        if (_scannedFor != Program.ProvidedPath) Scan();
        if (_working is { IsCompleted: true } done)
        {
            _status = done.IsCompletedSuccessfully ? done.Result : done.Exception?.GetBaseException().Message;
            _working = null;
            Scan();
        }

        if (LunaWindow.Instance.fileManager == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_Backups_NoLevel"));
            return;
        }
        ImGui.TextWrapped(LM.Get("GUI_Backups_Hint"));
        if (ImGui.SmallButton(LM.Get("GUI_SaveLevel_Refresh"))) Scan();
        ImGui.SameLine();
        ImGui.BeginDisabled(_ticked.Count == 0 || _working != null);
        if (ImGui.SmallButton(LM.Get("GUI_Backups_DeleteTicked", _ticked.Count))) _confirmBulk = true;
        ImGui.SameLine();
        if (ImGui.SmallButton(LM.Get("GUI_Backups_Untick"))) _ticked.Clear();
        ImGui.EndDisabled();

        if (_status != null)
        {
            ImGui.Separator();
            ImGui.TextWrapped(_status);
            _cacheNotice?.Draw();
            if (_offerReload && !string.IsNullOrEmpty(Program.ProvidedPath) && ImGui.Button(LM.Get("GUI_Backups_Reload")))
            {
                _offerReload = false;
                _status = null;
                LunaWindow.Instance.LoadLevelDataAsync(Program.ProvidedPath);
            }
        }
        if (_working != null)
        {
            ImGui.TextUnformatted(LM.Get("GUI_Backups_Working"));
            return;
        }

        foreach (var (file, backups) in _files)
        {
            ImGui.PushID(file);
            var info = new FileInfo(file);
            bool open = ImGui.CollapsingHeader($"{Path.GetFileName(file)}  ({backups.Count})###{file}", ImGuiTreeNodeFlags.DefaultOpen);
            if (open)
            {
                ImGui.TextDisabled(LM.Get("GUI_Backups_Current", info.Exists ? info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") : "-", Size(info.Exists ? info.Length : 0)));
                ImGui.SameLine();
                if (ImGui.SmallButton(LM.Get("GUI_Backups_OpenFolder"))) ShellUtils.OpenFolder(Path.GetDirectoryName(file)!);
                if (backups.Count > 0)
                {
                    ImGui.SameLine();
                    // Every backup of this file but the oldest - the file before ReLunacy first wrote it.
                    if (ImGui.SmallButton(LM.Get("GUI_Backups_TickAllButOldest")))
                        foreach (var b in backups.Take(backups.Count - 1)) _ticked.Add(b.Path);
                }
                if (backups.Count == 0) ImGui.TextDisabled(LM.Get("GUI_Backups_None"));
                else if (ImGui.BeginTable("##backups", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
                {
                    for (int i = 0; i < backups.Count; i++)
                    {
                        var b = backups[i];
                        ImGui.PushID(i);
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        bool ticked = _ticked.Contains(b.Path);
                        if (ImGui.Checkbox("##tick", ref ticked))
                        {
                            if (ticked) _ticked.Add(b.Path); else _ticked.Remove(b.Path);
                        }
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(b.Taken.ToString("yyyy-MM-dd HH:mm:ss"));
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(b.Path);
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled(Size(b.Size));
                        ImGui.TableNextColumn();
                        // The oldest is the file before the first save: as shipped, or as it was before ReLunacy.
                        if (i == backups.Count - 1) ImGui.TextColored(new Vector4(0.6f, 0.9f, 0.6f, 1f), LM.Get("GUI_Backups_Oldest"));
                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton(LM.Get("GUI_Backups_Restore"))) _confirm = (file, b, false);
                        ImGui.SameLine();
                        if (ImGui.SmallButton(LM.Get("GUI_Backups_Delete"))) _confirm = (file, b, true);
                        ImGui.PopID();
                    }
                    ImGui.EndTable();
                }
            }
            ImGui.PopID();
        }

        DrawConfirm();
        DrawConfirmBulk();
    }

    private void DrawConfirmBulk()
    {
        if (!_confirmBulk) return;
        string id = LM.Get("GUI_Backups_ConfirmTitle") + "###backup_confirm_bulk";
        if (!ImGui.IsPopupOpen(id)) ImGui.OpenPopup(id);
        bool keep = true;
        if (!ImGui.BeginPopupModal(id, ref keep, ImGuiWindowFlags.AlwaysAutoResize)) { if (!keep) _confirmBulk = false; return; }
        ImGui.TextWrapped(LM.Get("GUI_Backups_ConfirmDeleteTicked", _ticked.Count));
        // The oldest backup of a file is the file as it was before ReLunacy ever wrote it: say so when it is ticked.
        int oldest = _files.Count(f => f.Backups.Count > 0 && _ticked.Contains(f.Backups[^1].Path));
        if (oldest > 0) ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), LM.Get("GUI_Backups_TickedOldest", oldest));
        if (ImGui.Button(LM.Get("GUI_Backups_Delete")))
        {
            string[] paths = [.. _ticked];
            _working = Task.Run(() => DeleteAll(paths));
            _cacheNotice = null;
            _offerReload = false;
            _ticked.Clear();
            _confirmBulk = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) { _confirmBulk = false; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }

    private static string DeleteAll(string[] paths)
    {
        int deleted = 0;
        var failed = new List<string>();
        foreach (string path in paths)
        {
            try { File.Delete(path); deleted++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add($"{Path.GetFileName(path)}: {e.Message}"); }
        }
        return LM.Get("GUI_Backups_DeletedTicked", deleted) + (failed.Count > 0 ? " " + string.Join("; ", failed) : "");
    }

    private void DrawConfirm()
    {
        if (_confirm is not { } c) return;
        string id = LM.Get("GUI_Backups_ConfirmTitle") + "###backup_confirm";
        if (!ImGui.IsPopupOpen(id)) ImGui.OpenPopup(id);
        bool keep = true;
        if (!ImGui.BeginPopupModal(id, ref keep, ImGuiWindowFlags.AlwaysAutoResize)) { if (!keep) _confirm = null; return; }
        ImGui.TextWrapped(c.Delete
            ? LM.Get("GUI_Backups_ConfirmDelete", Path.GetFileName(c.Backup.Path))
            : LM.Get("GUI_Backups_ConfirmRestore", Path.GetFileName(c.File), c.Backup.Taken.ToString("yyyy-MM-dd HH:mm:ss")));
        if (ImGui.Button(LM.Get(c.Delete ? "GUI_Backups_Delete" : "GUI_Backups_Restore")))
        {
            _working = c.Delete ? Task.Run(() => Delete(c.Backup)) : Task.Run(() => Restore(c.File, c.Backup));
            _cacheNotice = c.Delete ? null : new GameCacheNotice(c.File);
            _offerReload = !c.Delete;
            _confirm = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) { _confirm = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }

    private static string Delete(Backups.Entry backup)
    {
        File.Delete(backup.Path);
        return LM.Get("GUI_Backups_Deleted", Path.GetFileName(backup.Path));
    }

    /// <summary>Puts a backup in place of <paramref name="file"/>, keeping <paramref name="file"/> as a backup first.</summary>
    private static string Restore(string file, Backups.Entry backup)
    {
        var fm = LunaWindow.Instance.fileManager;
        string kept = Backups.Create(file);
        bool archive = fm != null && fm.ArchivePaths.Contains(file);
        if (archive) fm!.ReleaseArchives();
        try { File.Copy(backup.Path, file, overwrite: true); }
        finally { if (archive) fm!.ReopenArchives(); }

        string sizes = "";
        if (file.EndsWith(".psarc", StringComparison.OrdinalIgnoreCase) && GameArchiveSizes.FindGameArchive(file) is { } game && game != file)
        {
            try { sizes = GameArchiveSizes.Refresh(game).Count > 0 ? LM.Get("GUI_Backups_SizesRefreshed") : ""; }
            catch (Exception e) { sizes = " " + e.Message; }
        }
        return LM.Get("GUI_Backups_Restored", Path.GetFileName(file), backup.Taken.ToString("yyyy-MM-dd HH:mm:ss"), Path.GetFileName(kept)) + sizes;
    }

    private static string Size(long bytes) => bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MiB" : $"{bytes / 1024.0:0.#} KiB";
}
