using System.Globalization;
using ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;
using ReLunacy.Core.Project;
using ReLunacy.Core.Undo;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Scene;
using ReLunacy.Engine.Scripting;
using ReLunacy.Engine.Text;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

public partial class PropertyInspectorFrame
{
    private readonly LuaScriptBrowser _volumeScripts = new(includeProjectLevels: false);
    private (IReadOnlyList<LuaScriptSource> Sources, LuaEnvironmentFrame? Editor,
        (string Path, int Version)? Live)? _volumeScriptsStamp;
    private readonly Dictionary<string, (string Text, LuaVolumeHandler[] Handlers)> _fileHandlers = [];
    private ILookup<string, LuaVolumeHandler>? _entryHandlers;
    private EntityVolume? _chooseLocationFor;
    private string _locationFilter = "";

    public void Dispose() => _volumeScripts.Dispose();

    internal static bool CanLinkLocation(EntityRegion? region) =>
        ProjectSession.Current != null && ProjectSession.Game == GameId.ACrackInTime
        && region != null && LunaWindow.Instance.fileManager is { } files
        && GameplayWriter.CanWriteLocationVolumes(files, region);

    internal static void EditLocation(EntityVolume volume)
    {
        var window = LunaWindow.Instance;
        var inspector = window.GetFirstFrame<PropertyInspectorFrame>();
        if (inspector == null)
        {
            inspector = new PropertyInspectorFrame();
            window.AddFrame(inspector);
        }
        inspector._chooseLocationFor = volume;
        inspector.Focus();
    }

    private void RenderVolumeScripts(EntityVolume volume)
    {
        var lua = LunaWindow.Instance.GetFirstFrame<LuaEnvironmentFrame>();
        var sources = _volumeScripts.SharedSources("");
        var stamp = (sources, lua, lua?.LiveScriptVersion);
        if (_volumeScriptsStamp != stamp)
        {
            if (!ReferenceEquals(_volumeScriptsStamp?.Sources, sources)) _fileHandlers.Clear();
            _entryHandlers = sources.Select(source => lua?.WithLiveSource(source) ?? source)
                .Where(source => Path.GetFileName(source.Path) is not ("instances.lua" or "instance.lua"))
                .SelectMany(HandlersIn).ToLookup(handler => handler.Volume, StringComparer.Ordinal);
            _volumeScriptsStamp = stamp;
        }

        var handlers = _entryHandlers![volume.Name].ToList();
        var locations = handlers.Where(h => h.LocationTag != null || h.LocationId.HasValue).ToList();
        bool canLink = CanLinkLocation(RegionOf(volume));
        string main = GameplayWriter.MainScriptPath(GameLibraryScanner.GetLevelNameFromPath(Program.ProvidedPath));
        var editableLocation = canLink && locations is [{ LocationArgument: not null } location]
            && string.Equals(location.Path, main, StringComparison.OrdinalIgnoreCase) ? location : null;

        ImGui.Separator();
        ImGui.TextUnformatted(LM.Get("GUI_Volume_ScriptReferences"));
        if (handlers.Count == 0) ImGui.TextDisabled(LM.Get("GUI_Volume_NoScriptReferences"));
        if (handlers.Count == 0 && !canLink) return;

        ImGui.PushID(volume.GetHashCode());
        if (BeginInspectorFields("##scriptReferences"))
        {
            for (int i = 0; i < handlers.Count; i++)
            {
                var handler = handlers[i];
                ImGui.PushID(i);
                BeginScriptField(handler.Function);
                if (ImGui.Selectable($"{Path.GetFileName(handler.Path)}:{handler.Position.Line + 1}##source"))
                {
                    if (lua == null) LunaWindow.Instance.AddFrame(lua = new LuaEnvironmentFrame());
                    lua.RevealScript(handler.Path, handler.Position);
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(handler.Path);
                if (locations.Contains(handler))
                    RenderLocationField(volume, handler, handler == editableLocation);
                ImGui.PopID();
            }
            if (canLink && (locations.Count == 0 || (editableLocation == null && volume.PendingLocationTextId.HasValue)))
                RenderLocationField(volume, null, locations.Count == 0);
            ImGui.EndTable();
        }
        ImGui.PopID();
    }

    private static void BeginScriptField(string label, bool edited = false)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        RenderFieldLabel(label, edited);
        if (edited && ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Volume_LocationSaveHint"));
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
    }

    private void RenderLocationField(EntityVolume volume, LuaVolumeHandler? handler, bool canEdit)
    {
        int? pending = canEdit || handler == null ? volume.PendingLocationTextId : null;
        var savedId = handler?.LocationId;
        if (savedId == null && handler?.LocationTag is { } name)
            savedId = ProjectTextTags.List().FirstOrDefault(tag => tag.Name == name)?.Id;
        int? currentId = pending ?? savedId;
        BeginScriptField(LM.Get("GUI_Volume_LocationTag"), pending.HasValue);

        bool showControls = canEdit || pending.HasValue;
        float gap = ImGui.GetStyle().ItemInnerSpacing.X;
        float buttonSize = ImGui.GetFrameHeight();
        float controlsWidth = (canEdit ? buttonSize + gap : 0f) + (pending.HasValue ? buttonSize + gap : 0f);
        float controlsX = ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - controlsWidth);
        if (currentId is { } textId)
        {
            ImGui.TextDisabled(textId.ToString(CultureInfo.InvariantCulture));
            ImGui.SameLine();
            float width = showControls ? controlsX - ImGui.GetCursorPosX() - gap : ImGui.GetContentRegionAvail().X;
            if (width > 0f)
            {
                var window = LunaWindow.Instance;
                var openTable = window.GetFirstFrame<TextTablesFrame>();
                string? text = openTable?.ReferenceText(textId);
                if (text == null && ProjectSession.GameFolder is { } folder && GameText.Reference(folder) is { } reference
                    && textId >= 0 && textId < reference.Entries.Count)
                    text = reference.Entries[textId].Text;
                string? tag = openTable?.PendingTags.GetValueOrDefault(textId) ?? GameText.TagName(ProjectSession.Game, textId);
                RenderTextFieldLink(textId, text, tag, width, useTag: true);
            }
        }
        else ImGui.TextDisabled(handler?.LocationTag ?? LM.Get("GUI_Frame_InstanceInspector_ReferenceNone"));

        if (!showControls) return;

        ImGui.SameLine(0f, gap);
        ImGui.SetCursorPosX(controlsX + gap);
        if (canEdit)
        {
            bool pick = ImGui.ArrowButton("##pickLocation", ImGuiDir.Down);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(LM.Get(handler == null ? "GUI_Volume_LinkLocation" : "GUI_Volume_RetargetLocation"));
            if (pick || ReferenceEquals(_chooseLocationFor, volume))
            {
                _chooseLocationFor = null;
                _locationFilter = "";
                ImGui.OpenPopup("location_text_tag");
            }
        }

        if (pending.HasValue)
        {
            if (canEdit) ImGui.SameLine(0f, gap);
            if (ImGui.Button("x##unsetLocation", new System.Numerics.Vector2(buttonSize, 0f))) SetLocation(volume, null);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Volume_ClearLocation"));
        }
        if (canEdit) RenderLocationPicker(volume, currentId, savedId);
    }

    private void RenderLocationPicker(EntityVolume volume, int? currentId, int? savedId)
    {
        if (!ImGui.BeginPopup("location_text_tag")) return;
        ImGui.SetNextItemWidth(300f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputTextWithHint("##location_filter", LM.Get("GUI_Volume_FindTextTag"), ref _locationFilter, 96);
        ImGui.TextDisabled(LM.Get("GUI_Volume_LocationSaveHint"));
        if (ImGui.BeginChild("location_tags", new System.Numerics.Vector2(310f, 220f)))
        {
            var tags = ProjectTextTags.List()
                .Where(tag => tag.Name.Contains(_locationFilter, StringComparison.OrdinalIgnoreCase)
                    || tag.Id.ToString(CultureInfo.InvariantCulture).Contains(_locationFilter))
                .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var clipper = new ImGuiListClipper();
            clipper.Begin(tags.Count);
            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var tag = tags[i];
                    if (!ImGui.Selectable($"{tag.Name} ({tag.Id})##{tag.Id}", currentId == tag.Id)) continue;
                    SetLocation(volume, savedId == tag.Id ? null : tag.Id);
                    ImGui.CloseCurrentPopup();
                }
            }
            clipper.End();
        }
        ImGui.EndChild();
        ImGui.EndPopup();
    }

    private LuaVolumeHandler[] HandlersIn(LuaScriptSource source)
    {
        if (_fileHandlers.TryGetValue(source.Path, out var cached) && cached.Text == source.Text)
            return cached.Handlers;
        var handlers = LuaVolumeHandlers.Find(source).ToArray();
        _fileHandlers[source.Path] = (source.Text, handlers);
        return handlers;
    }

    private static void SetLocation(EntityVolume volume, int? id)
    {
        int? previous = volume.PendingLocationTextId;
        if (previous == id) return;
        volume.PendingLocationTextId = id;
        UndoManager.Singleton.Push(LM.Get("GUI_Volume_LinkLocation"),
            () => volume.PendingLocationTextId = previous, () => volume.PendingLocationTextId = id);
    }

}
