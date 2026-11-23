using System.Numerics;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Projects;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Text;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// View > Text Tables: the game's strings in every language side by side (core_text_tags.&lt;language&gt;.pkg in
/// packed/game's archives; <see cref="TextTable"/>), searched, edited, added to and written back.
///
/// A row is a text id - the number a moon's name field or a prompt holds - with its tag from the game's code where
/// the EBOOT names it. A new string takes the next id in every language at once, so the tables stay in step and a
/// language left untranslated still has an entry. Button glyphs and other characters the editor's font lacks show
/// as {E004} and are typed the same way. Text edits and tag names save to the open project.
/// </summary>
public sealed class TextTablesFrame : DockedFrame, IDisposable
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private static readonly Vector4 ChangedColour = new(0.35f, 0.8f, 1f, 0.25f);
    private static readonly Vector4 AddedColour = new(0.4f, 1f, 0.5f, 0.2f);

    private sealed class Language(TextTableRef source, TextTable table)
    {
        public TextTableRef Source = source;
        public TextTable Table = table;
        /// <summary>The strings as loaded, by id, for the changed tint.</summary>
        public List<string?> Loaded = table.Entries.Select(e => e.Text).ToList();
        public bool Shown;
    }

    private bool _scanned;
    private LunaProject? _scannedProject;
    private readonly Dictionary<int, string> _pendingTags = [];
    private string? _gameFolder;
    private List<Language> _languages = [];
    private string _filter = "";
    private bool _changedOnly;
    private string _newTag = "";
    private List<int> _rows = [];
    private (string Filter, bool ChangedOnly, int Version)? _rowsFor;
    private int _version;
    private int? _scrollTo;
    private int _selected = -1;
    private string? _status;
    public bool Focused { get; private set; }

    public TextTablesFrame()
    {
        FrameName = LM.Get("GUI_Frame_TextTables");
    }

    /// <summary>Shows a text id - how the inspector opens the string a field holds.</summary>
    public void Reveal(int id)
    {
        _filter = "";
        _changedOnly = false;
        _selected = id;
        _scrollTo = id;
    }

    /// <summary>Shows a language's column, by its code (us, fr...).</summary>
    public void ShowLanguage(string code)
    {
        Scan();
        foreach (var language in _languages)
            if (string.Equals(language.Source.Language, code, StringComparison.OrdinalIgnoreCase)) language.Shown = true;
    }

    private void Scan()
    {
        var project = ProjectSession.Current;
        string? folder = ProjectSession.GameFolder;
        if (_scanned && _gameFolder == folder && ReferenceEquals(_scannedProject, project)) return;
        _scanned = true;
        _scannedProject = project;
        _gameFolder = folder;
        Load();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Focused = false;
        base.RenderAsWindow(deltaTime);
    }

    internal int EditVersion => _version;

    internal string? ReferenceText(int id)
    {
        if (!ReferenceEquals(_scannedProject, ProjectSession.Current) || _gameFolder != ProjectSession.GameFolder) return null;
        var language = _languages.Find(l => l.Source.Language == "us") ?? _languages.FirstOrDefault();
        return language != null && id >= 0 ? TextOf(language, id) : null;
    }

    public IReadOnlyDictionary<int, string> PendingTags => _pendingTags;

    public void Revert() => Load();

    public void Dispose()
    {
        if (!Save()) isOpen = true;
    }

    private void Load()
    {
        _languages = [];
        _pendingTags.Clear();
        _status = null;
        _version++;
        if (_gameFolder == null) return;
        foreach (var source in GameText.List(_gameFolder))
        {
            try
            {
                byte[] bytes = GameText.ReadBytes(source);
                _languages.Add(new Language(source, TextTable.Parse(bytes)));
            }
            catch (Exception e) when (e is IOException or InvalidDataException) { _status = $"{source.EntryPath}: {e.Message}"; }
        }
        // The game's own order, by language number; the first two shown to start with.
        _languages = _languages.OrderBy(l => l.Table.Language).ToList();
        for (int i = 0; i < _languages.Count; i++) _languages[i].Shown = i == 0 || _languages[i].Source.Language == "fr";
    }

    private int Count => _languages.Count == 0 ? 0 : _languages.Max(l => l.Table.Entries.Count);
    private static string? TextOf(Language language, int id) => id < language.Table.Entries.Count ? language.Table.Entries[id].Text : null;
    private static bool Changed(Language language, int id) => id >= language.Loaded.Count || language.Loaded[id] != TextOf(language, id);
    private int _dirtyVersion = -1;
    private bool _dirty;
    public bool Dirty
    {
        get
        {
            if (_dirtyVersion != _version)
            {
                _dirty = _pendingTags.Count > 0 || _languages.Any(l =>
                    Enumerable.Range(0, l.Table.Entries.Count).Any(id => Changed(l, id)));
                _dirtyVersion = _version;
            }
            return _dirty;
        }
    }

    private string? TagName(int id) => _pendingTags.GetValueOrDefault(id) ?? GameText.TagName(ProjectSession.Game, id);

    private void SetTag(int id, string tag)
    {
        if ((GameText.TagName(ProjectSession.Game, id) ?? "") == tag) _pendingTags.Remove(id);
        else _pendingTags[id] = tag;
        _version++;
    }

    private void Filter()
    {
        var key = (_filter, _changedOnly, _version);
        if (_rowsFor == key) return;
        _rowsFor = key;
        _rows = [];
        for (int id = 0; id < Count; id++)
        {
            if (_changedOnly && !_pendingTags.ContainsKey(id) && !_languages.Any(l => Changed(l, id))) continue;
            if (_filter.Length > 0
                && !id.ToString().Contains(_filter)
                && TagName(id)?.Contains(_filter, StringComparison.OrdinalIgnoreCase) != true
                && !_languages.Any(l => l.Shown && TextOf(l, id)?.Contains(_filter, StringComparison.OrdinalIgnoreCase) == true))
                continue;
            _rows.Add(id);
        }
    }

    protected override void Render(double deltaTime)
    {
        Focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        Scan();
        if (_gameFolder == null) { ImGui.TextDisabled(LM.Get("GUI_TextTables_NoGame")); return; }
        if (_languages.Count == 0) { ImGui.TextDisabled(_status ?? LM.Get("GUI_TextTables_None")); return; }

        bool dirty = Dirty;
        ImGui.BeginDisabled(!dirty);
        if (ImGuiPlus.IconButton(Icons.Save, "save_text")) Save();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(LM.Get("GUI_ConfigTables_Save") + " (Ctrl+S)");
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_ConfigTables_Revert"))) Load();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(260f);
        ImGui.InputTextWithHint("##filter", LM.Get("GUI_TextTables_Filter"), ref _filter, 128);
        ImGui.SameLine();
        ImGui.Checkbox(LM.Get("GUI_TextTables_ChangedOnly"), ref _changedOnly);
        foreach (var language in _languages)
        {
            ImGui.SameLine();
            if (ImGui.Checkbox(language.Source.Language, ref language.Shown)) _version++;
        }

        // A new string: one id, in every language.
        ImGui.SetNextItemWidth(260f);
        ImGui.InputTextWithHint("##newtag", LM.Get("GUI_TextTables_NewTag"), ref _newTag, 96);
        ImGui.SameLine();
        ImGui.BeginDisabled(_scannedProject == null || _newTag.Trim().Length == 0);
        if (ImGui.Button(LM.Get("GUI_TextTables_Add"))) Add(_newTag.Trim());
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_TextTables_AddHint"));
        ImGui.SameLine();
        ImGui.TextDisabled(LM.Get("GUI_TextTables_Count", Count, Path.GetFileName(_languages[0].Source.ArchivePath)));

        if (_status != null) ImGui.TextWrapped(_status);
        if (_scannedProject == null) ImGui.TextDisabled(LM.Get("GUI_Install_NoProject"));

        Filter();
        var shown = _languages.Where(l => l.Shown).ToList();
        if (!ImGui.BeginTable("##text", 2 + shown.Count,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(LM.Get("GUI_TextTables_Id"), ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn(LM.Get("GUI_TextTables_Tag"), ImGuiTableColumnFlags.WidthFixed, 280f);
        foreach (var language in shown) ImGui.TableSetupColumn(language.Source.Language, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        float rowHeight = ImGui.GetFrameHeightWithSpacing();
        if (_scrollTo is { } target)
        {
            int index = _rows.IndexOf(target);
            if (index >= 0) ImGui.SetScrollY(index * rowHeight);
            _scrollTo = null;
        }
        var clipper = new ImGuiListClipper();
        clipper.Begin(_rows.Count, rowHeight);
        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                int id = _rows[i];
                ImGui.TableNextRow();
                ImGui.PushID(id);
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{id}", _selected == id)) { _selected = id; ImGui.SetClipboardText(id.ToString()); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_TextTables_IdHint", $"0x{id:X}"));
                ImGui.TableNextColumn();
                // Pending names are available to Lua completion as soon as they are entered.
                string tag = TagName(id) ?? "";
                ImGui.BeginDisabled(_scannedProject == null);
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputTextWithHint("##tag", _languages.Any(l => id >= l.Loaded.Count) ? LM.Get("GUI_TextTables_Custom") : "-",
                        ref tag, 96))
                {
                    SetTag(id, new string(tag.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_').ToArray()));
                }
                ImGui.EndDisabled();
                foreach (var language in shown)
                {
                    ImGui.TableNextColumn();
                    if (id >= language.Table.Entries.Count) { ImGui.TextDisabled("-"); continue; }
                    var entry = language.Table.Entries[id];
                    if (entry.Text == null) { ImGui.TextDisabled(LM.Get("GUI_TextTables_NoString")); continue; }
                    if (id >= language.Loaded.Count) ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(AddedColour));
                    else if (Changed(language, id)) ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ChangedColour));
                    string text = TextTable.Escape(entry.Text);
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.InputText($"##{language.Source.Language}", ref text, 4096,
                            _scannedProject == null ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None))
                    {
                        entry.Text = TextTable.Unescape(text);
                        _version++;
                    }
                    if (ImGui.IsItemActivated()) _selected = id;
                    if (ImGui.IsItemHovered() && entry.Text.Length > 60) ImGui.SetTooltip(TextTable.Escape(entry.Text));
                }
                ImGui.PopID();
            }
        }
        clipper.End();
        ImGui.EndTable();
    }

    /// <summary>A new id at the end of every language, with the same hash, holding the tag as its text until each
    /// language is written.</summary>
    private void Add(string tag)
    {
        tag = new string(tag.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_').ToArray());
        if (tag.Length == 0) return;
        if (Enumerable.Range(0, Count).FirstOrDefault(id => string.Equals(TagName(id), tag, StringComparison.OrdinalIgnoreCase), -1) is var taken and >= 0) { _status = LM.Get("GUI_TextTables_TagTaken", tag, taken); return; }
        int count = Count;
        // One hash for all languages: free in each of them.
        uint hash = _languages[0].Table.FreeHash(tag);
        while (_languages.Any(l => l.Table.Entries.Any(e => e.Hash == hash))) hash = (hash + 1) & 0xFFFFFF;
        foreach (var language in _languages)
        {
            while (language.Table.Entries.Count < count) language.Table.Entries.Add(new TextTable.Entry());
            language.Table.Entries.Add(new TextTable.Entry { Hash = hash, Text = tag });
        }
        // The tag is the new id's name, kept with the project.
        SetTag(count, tag);
        _newTag = "";
        _filter = "";
        _selected = count;
        _scrollTo = count;
        _version++;
        _status = LM.Get("GUI_TextTables_Added", count, tag);
    }

    public bool Save()
    {
        if (!Dirty) return true;
        if (_scannedProject is not { } project || !ReferenceEquals(project, ProjectSession.Current)) return false;
        var summaries = new List<string>();
        bool success = true;
        try
        {
            foreach (var archive in _languages.Where(l =>
                         Enumerable.Range(0, l.Table.Entries.Count).Any(id => Changed(l, id)))
                     .GroupBy(l => l.Source.ArchivePath))
            {
                var entries = archive.ToDictionary(l => l.Source.EntryPath, l => l.Table.ToBytes());
                var result = ArchiveEntryWriter.Replace(archive.Key, entries, project.Sink);
                summaries.Add(result.Summary);
                success &= result.Succeeded;
                if (!result.Succeeded) continue;
                foreach (var language in archive)
                {
                    language.Loaded = language.Table.Entries.Select(e => e.Text).ToList();
                }
            }
            if (success && _pendingTags.Count > 0)
            {
                project.SetTextTags(_pendingTags);
                _pendingTags.Clear();
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            summaries.Add(e.Message);
            success = false;
        }
        _status = summaries.Count == 0 ? LM.Get("GUI_TextTables_SavedTags") : string.Join("\n", summaries);
        _version++;
        return success;
    }
}
