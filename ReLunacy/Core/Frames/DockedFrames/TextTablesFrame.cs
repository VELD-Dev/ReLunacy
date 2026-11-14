using System.Numerics;
using ReLunacy.Engine.Config;
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
/// as {E004} and are typed the same way. A save goes through ArchiveEntryWriter like the configuration tables:
/// rebuilt, verified, backed up with the date and time; the game reads these at boot, so the cache notice follows.
/// </summary>
public sealed class TextTablesFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private static readonly Vector4 ChangedColour = new(0.35f, 0.8f, 1f, 0.25f);
    private static readonly Vector4 AddedColour = new(0.4f, 1f, 0.5f, 0.2f);

    private sealed class Language(TextTableRef source, TextTable table, byte[] loaded)
    {
        public TextTableRef Source = source;
        public TextTable Table = table;
        public byte[] LoadedBytes = loaded;
        /// <summary>The strings as loaded, by id, for the changed tint.</summary>
        public List<string?> Loaded = table.Entries.Select(e => e.Text).ToList();
        public bool Shown;
    }

    private string? _scannedFor;
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
    private bool _saving;
    private GameCacheNotice? _cacheNotice;

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

    private void Scan()
    {
        if (_scannedFor == Program.ProvidedPath && _gameFolder != null) return;
        _scannedFor = Program.ProvidedPath;
        _gameFolder = string.IsNullOrEmpty(Program.ProvidedPath) ? null : GameConfigs.GameFolderFor(Program.ProvidedPath);
        Load();
    }

    private void Load()
    {
        _languages = [];
        _status = null;
        if (_gameFolder == null) return;
        foreach (var source in GameText.List(_gameFolder))
        {
            try
            {
                byte[] bytes = GameText.ReadBytes(source);
                _languages.Add(new Language(source, TextTable.Parse(bytes), bytes));
            }
            catch (Exception e) when (e is IOException or InvalidDataException) { _status = $"{source.EntryPath}: {e.Message}"; }
        }
        // The game's own order, by language number; the first two shown to start with.
        _languages = _languages.OrderBy(l => l.Table.Language).ToList();
        for (int i = 0; i < _languages.Count; i++) _languages[i].Shown = i == 0 || _languages[i].Source.Language == "fr";
        _version++;
    }

    private int Count => _languages.Count == 0 ? 0 : _languages.Max(l => l.Table.Entries.Count);
    private static string? TextOf(Language language, int id) => id < language.Table.Entries.Count ? language.Table.Entries[id].Text : null;
    private static bool Changed(Language language, int id) => id >= language.Loaded.Count || language.Loaded[id] != TextOf(language, id);
    private bool Dirty => _languages.Any(l => Enumerable.Range(0, l.Table.Entries.Count).Any(id => Changed(l, id)));

    private void Filter()
    {
        var key = (_filter, _changedOnly, _version);
        if (_rowsFor == key) return;
        _rowsFor = key;
        var game = LunaWindow.Instance.Level?.GameDefinition?.Id;
        _rows = [];
        for (int id = 0; id < Count; id++)
        {
            if (_changedOnly && !_languages.Any(l => Changed(l, id))) continue;
            if (_filter.Length > 0
                && !id.ToString().Contains(_filter)
                && GameText.TagName(game, id)?.Contains(_filter, StringComparison.OrdinalIgnoreCase) != true
                && !_languages.Any(l => l.Shown && TextOf(l, id)?.Contains(_filter, StringComparison.OrdinalIgnoreCase) == true))
                continue;
            _rows.Add(id);
        }
    }

    protected override void Render(double deltaTime)
    {
        Scan();
        if (_gameFolder == null) { ImGui.TextDisabled(LM.Get("GUI_TextTables_NoGame")); return; }
        if (_languages.Count == 0) { ImGui.TextDisabled(_status ?? LM.Get("GUI_TextTables_None")); return; }
        var game = LunaWindow.Instance.Level?.GameDefinition?.Id;

        bool dirty = Dirty;
        ImGui.BeginDisabled(!dirty || _saving);
        if (ImGui.Button(LM.Get("GUI_ConfigTables_Save"))) Save();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_ConfigTables_Revert"))) { Load(); return; }
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
        ImGui.BeginDisabled(_newTag.Trim().Length == 0);
        if (ImGui.Button(LM.Get("GUI_TextTables_Add"))) Add(_newTag.Trim());
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_TextTables_AddHint"));
        ImGui.SameLine();
        ImGui.TextDisabled(LM.Get("GUI_TextTables_Count", Count, Path.GetFileName(_languages[0].Source.ArchivePath)));

        if (_status != null) ImGui.TextWrapped(_status);
        _cacheNotice?.Draw();

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
                // The tag is the project's own note of what the id is (Resources/Classes/<Game>.textids): editable,
                // and written to that file as soon as it is entered.
                string tag = GameText.TagName(game, id) ?? "";
                ImGui.SetNextItemWidth(-1f);
                if (game is { } named && ImGui.InputTextWithHint("##tag", _languages.Any(l => id >= l.Loaded.Count) ? LM.Get("GUI_TextTables_Custom") : "-",
                        ref tag, 96, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    GameText.SetTagName(named, id, new string(tag.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray()));
                    _version++;
                }
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
                    if (ImGui.InputText($"##{language.Source.Language}", ref text, 4096))
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
        int count = Count;
        // One hash for all languages: free in each of them.
        uint hash = _languages[0].Table.FreeHash(tag);
        while (_languages.Any(l => l.Table.Entries.Any(e => e.Hash == hash))) hash = (hash + 1) & 0xFFFFFF;
        foreach (var language in _languages)
        {
            while (language.Table.Entries.Count < count) language.Table.Entries.Add(new TextTable.Entry());
            language.Table.Entries.Add(new TextTable.Entry { Hash = hash, Text = tag });
        }
        _newTag = "";
        _filter = "";
        _selected = count;
        _scrollTo = count;
        _version++;
        _status = LM.Get("GUI_TextTables_Added", count, tag);
    }

    private void Save()
    {
        // Only the languages that changed, grouped by the archive they are in.
        var writes = new Dictionary<string, Dictionary<string, byte[]>>();
        var written = new List<(Language Language, byte[] Bytes)>();
        try
        {
            foreach (var language in _languages)
            {
                byte[] bytes = language.Table.ToBytes();
                if (bytes.AsSpan().SequenceEqual(language.LoadedBytes)) continue;
                if (!writes.TryGetValue(language.Source.ArchivePath, out var entries)) writes[language.Source.ArchivePath] = entries = [];
                entries[language.Source.EntryPath] = bytes;
                written.Add((language, bytes));
            }
        }
        catch (InvalidDataException e) { _status = e.Message; return; }
        if (writes.Count == 0) return;

        _saving = true;
        _status = LM.Get("GUI_ConfigTables_Saving", string.Join(", ", writes.Keys.Select(Path.GetFileName)));
        BackgroundJob.Run(
            LM.Get("GUI_ConfigTables_Save"),
            _status,
            _ => writes.Select(w => (Archive: w.Key, Result: ArchiveEntryWriter.Replace(w.Key, w.Value))).ToList(),
            results =>
            {
                _saving = false;
                _status = string.Join("\n", results.Select(r => r.Result.Succeeded && r.Result.BackupPath != null
                    ? $"{r.Result.Summary} (backup: {Path.GetFileName(r.Result.BackupPath)})"
                    : r.Result.Summary));
                foreach (var (language, bytes) in written)
                {
                    if (!results.First(r => r.Archive == language.Source.ArchivePath).Result.Succeeded) continue;
                    language.LoadedBytes = bytes;
                    language.Loaded = language.Table.Entries.Select(e => e.Text).ToList();
                }
                _version++;
                if (results.FirstOrDefault(r => r.Result.Succeeded) is { Archive: { } archive }) _cacheNotice = new GameCacheNotice(archive);
            },
            e =>
            {
                _saving = false;
                _status = e.Message;
            });
    }
}
