using System.Numerics;
using ReLunacy.Engine.Config;
using ReLunacy.Engine.Packing;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// View > Config Tables: the game's configuration spreadsheets (/data/configs/*.csv in packed/game's
/// archives - level, weapon, enemy, vendor...), edited and written back into their archive.
///
/// A table opens on its structure (<see cref="ConfigLayout"/>): a sectioned table as its sections (a level, a
/// weapon, an enemy) with a form of fields per section, its value columns named by the header - or, in level.csv,
/// by the section's own Pod row - and a record table as a grid under its header. Values are checked against
/// what the field holds elsewhere in the table: a number field shows a value that is not a number, a name field
/// (SEG_MOON, WPN_ZURKON...) offers the names it uses. "Raw" shows every cell as the file has it.
///
/// Every game that has tables gets them, since the list is whatever .csv the archives hold. A save goes through
/// ArchiveEntryWriter: rebuilt, verified, backed up with the date and time, game.psarc's sizes refreshed; the
/// game reads these at boot, so the cache notice follows a save.
/// </summary>
public sealed class ConfigTablesFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private static readonly Vector4 SectionColour = new(1f, 0.8f, 0.35f, 1f);
    private static readonly Vector4 ChangedColour = new(0.35f, 0.8f, 1f, 0.25f);
    private static readonly Vector4 InvalidColour = new(1f, 0.3f, 0.25f, 0.35f);

    private string? _scannedFor;
    private string? _gameFolder;
    private List<ConfigTableRef> _tables = [];
    private ConfigTableRef? _open;
    private ConfigCsv? _table;
    private ConfigCsv? _loaded;
    private ConfigLayout? _layout;
    private bool _raw;
    private string _filter = "";
    private string _tableFilter = "";
    private string _sectionFilter = "";
    private int _section = -1;
    private int _selectedRow = -1;
    private int? _scrollTo;
    private string? _status;
    private bool _saving;
    private GameCacheNotice? _cacheNotice;

    public ConfigTablesFrame()
    {
        FrameName = LM.Get("GUI_Frame_ConfigTables");
    }

    /// <summary>Opens a table at a row - how Level Data shows a level's block: its section when the row opens one.</summary>
    public void Reveal(ConfigTableRef table, int row)
    {
        Scan();
        Open(_tables.FirstOrDefault(t => t == table) ?? table);
        _selectedRow = row;
        _scrollTo = row;
        _filter = "";
        _sectionFilter = "";
        if (_layout != null) _section = _layout.Sections.FindIndex(s => s.Row <= row && row < s.End);
    }

    private bool Dirty => _table != null && _loaded != null && _table.ToText() != _loaded.ToText();

    private void Scan()
    {
        if (_scannedFor == Program.ProvidedPath && _gameFolder != null) return;
        _scannedFor = Program.ProvidedPath;
        _gameFolder = string.IsNullOrEmpty(Program.ProvidedPath) ? null : GameConfigs.GameFolderFor(Program.ProvidedPath);
        _tables = _gameFolder != null ? GameConfigs.List(_gameFolder) : [];
    }

    private void Open(ConfigTableRef table)
    {
        try
        {
            _table = GameConfigs.Read(table);
            _loaded = GameConfigs.Read(table);
            _layout = ConfigLayout.Of(_table);
            _open = table;
            _section = _layout.Sectioned ? 0 : -1;
            _selectedRow = -1;
            _status = null;
            _cacheNotice = null;
        }
        catch (Exception e) { _status = e.Message; }
    }

    /// <summary>Rows were added or removed: section bounds move, so the structure is read again.</summary>
    private void Restructure()
    {
        string? name = _layout != null && _section >= 0 && _section < _layout.Sections.Count ? _layout.Sections[_section].Name : null;
        _layout = ConfigLayout.Of(_table!);
        if (name != null) _section = _layout.Sections.FindIndex(s => s.Name == name);
    }

    protected override void Render(double deltaTime)
    {
        Scan();
        if (_gameFolder == null) { ImGui.TextDisabled(LM.Get("GUI_ConfigTables_NoGame")); return; }
        if (_tables.Count == 0) { ImGui.TextDisabled(LM.Get("GUI_ConfigTables_None")); return; }

        if (ImGui.BeginChild("##tables", new Vector2(190f, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##tablefilter", LM.Get("GUI_ConfigTables_FilterTables"), ref _tableFilter, 64);
            string? archive = null;
            foreach (var t in _tables)
            {
                if (_tableFilter.Length > 0 && !t.Name.Contains(_tableFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (t.ArchivePath != archive)
                {
                    archive = t.ArchivePath;
                    ImGui.TextDisabled(Path.GetFileName(archive));
                }
                bool current = t == _open;
                if (ImGui.Selectable($"{t.Name}{(current && Dirty ? " *" : "")}##{t.ArchivePath}{t.EntryPath}", current) && !current)
                {
                    if (Dirty) _status = LM.Get("GUI_ConfigTables_Unsaved");
                    else Open(t);
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(t.EntryPath);
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();

        if (ImGui.BeginChild("##table", Vector2.Zero))
        {
            if (_table == null || _open == null || _layout == null) ImGui.TextDisabled(LM.Get("GUI_ConfigTables_Pick"));
            else RenderTable();
        }
        ImGui.EndChild();
    }

    private void RenderTable()
    {
        bool dirty = Dirty;
        ImGui.BeginDisabled(!dirty || _saving);
        if (ImGui.Button(LM.Get("GUI_ConfigTables_Save"))) Save();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_ConfigTables_Revert"))) { Open(_open!); return; }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.Checkbox(LM.Get("GUI_ConfigTables_Raw"), ref _raw);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ConfigTables_RawTip"));
        ImGui.SameLine();
        ImGui.TextDisabled($"{_open!.EntryPath}  ({Path.GetFileName(_open.ArchivePath)})");

        if (_status != null) ImGui.TextWrapped(_status);
        _cacheNotice?.Draw();

        if (_raw) RenderRaw();
        else if (_layout!.Sectioned) RenderSections();
        else RenderRecords();
    }

    // --- Structured: sectioned tables ---------------------------------------------------------------------

    private void RenderSections()
    {
        var table = _table!;
        var layout = _layout!;

        if (ImGui.BeginChild("##sections", new Vector2(220f, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##sectionfilter", LM.Get("GUI_ConfigTables_FilterSections"), ref _sectionFilter, 64);
            for (int i = 0; i < layout.Sections.Count; i++)
            {
                var s = layout.Sections[i];
                if (_sectionFilter.Length > 0 && !s.Name.Contains(_sectionFilter, StringComparison.OrdinalIgnoreCase)
                    && !SectionHas(table, s, _sectionFilter)) continue;
                bool changed = SectionChanged(table, s);
                if (ImGui.Selectable($"{s.Name}{(changed ? " *" : "")}##s{i}", _section == i)) { _section = i; _selectedRow = -1; }
                if (_scrollTo != null && _section == i) { ImGui.SetScrollHereY(); }
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();

        if (!ImGui.BeginChild("##form", Vector2.Zero)) { ImGui.EndChild(); return; }
        if (_section < 0 || _section >= layout.Sections.Count) { ImGui.TextDisabled(LM.Get("GUI_ConfigTables_PickSection")); ImGui.EndChild(); return; }
        var section = layout.Sections[_section];
        _scrollTo = null;

        // The section's name (its opening row's first cell) and the comment beside it.
        ImGui.PushStyleColor(ImGuiCol.Text, SectionColour);
        string name = table.Get(section.Row, 0);
        ImGui.SetNextItemWidth(260f);
        if (ImGui.InputText("##sectionname", ref name, 128) && name.Length > 0) table.Set(section.Row, 0, name);
        ImGui.PopStyleColor();
        if (table.Get(section.Row, 2) is { Length: > 0 } note) { ImGui.SameLine(); ImGui.TextDisabled(note); }

        if (ImGui.SmallButton(LM.Get("GUI_ConfigTables_AddField")))
        {
            int at = _selectedRow > section.Row && _selectedRow < section.End ? _selectedRow + 1 : section.End;
            table.Rows.Insert(at, [new ConfigCsv.Cell(""), new ConfigCsv.Cell("NewField")]);
            _selectedRow = at;
            Restructure();
            section = _layout!.Sections[_section];
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!(_selectedRow > section.Row && _selectedRow < section.End));
        if (ImGui.SmallButton(LM.Get("GUI_ConfigTables_DeleteField")))
        {
            table.Rows.RemoveAt(_selectedRow);
            _selectedRow = -1;
            Restructure();
            section = _layout!.Sections[_section];
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##fieldfilter", LM.Get("GUI_ConfigTables_FilterFields"), ref _filter, 64);

        // Value columns: from the first one to the widest row of the section, plus one free column to add a value.
        int first = layout.FirstValueColumn, last = first;
        for (int r = section.Row + 1; r < section.End; r++) last = Math.Max(last, table.Rows[r].Count);
        int values = last - first + 1;
        bool comments = first == 3;
        int columns = 2 + (comments ? 1 : 0) + values;

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        if (ImGui.BeginTable("##fields", columns, flags))
        {
            ImGui.TableSetupScrollFreeze(2, 1);
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 34f);
            ImGui.TableSetupColumn(Header(layout.ColumnName(null, 1), LM.Get("GUI_ConfigTables_Field")), ImGuiTableColumnFlags.WidthFixed, 170f);
            if (comments) ImGui.TableSetupColumn(Header(layout.ColumnName(null, 2), LM.Get("GUI_ConfigTables_Comment")), ImGuiTableColumnFlags.WidthFixed, 150f);
            for (int c = first; c < first + values; c++)
                ImGui.TableSetupColumn(Header(layout.ColumnName(section, c), $"{c}") + $"##col{c}", ImGuiTableColumnFlags.WidthFixed, 120f);
            ImGui.TableHeadersRow();

            for (int r = section.Row + 1; r < section.End; r++)
            {
                string field = table.Get(r, 1);
                if (_filter.Length > 0 && !table.Rows[r].Any(c => c.Text.Contains(_filter, StringComparison.OrdinalIgnoreCase))) continue;
                ImGui.TableNextRow();
                ImGui.PushID(r);
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{r + 1}", _selectedRow == r)) _selectedRow = r;

                // level.csv's ",,Totals:" marks where the per-segment rows begin: shown, not a field.
                bool marker = field.Length == 0 && table.Rows[r].Skip(2).Any(c => c.Text.EndsWith(':'));
                var kind = layout.FieldOf(field);
                ImGui.TableNextColumn();
                if (marker) ImGui.TextDisabled(string.Join(" ", table.Rows[r].Select(c => c.Text).Where(t => t.Length > 0)));
                else CellEditor(r, 1, (ConfigValueKind.Text, []), bold: true);
                if (comments)
                {
                    ImGui.TableNextColumn();
                    if (!marker) CellEditor(r, 2, (ConfigValueKind.Text, []), dim: true);
                }
                for (int c = first; c < first + values; c++)
                {
                    ImGui.TableNextColumn();
                    if (!marker) CellEditor(r, c, kind);
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    private static string Header(string name, string fallback) => name.Length > 0 ? name : fallback;

    private static bool SectionHas(ConfigCsv table, ConfigSection s, string text)
    {
        for (int r = s.Row; r < s.End; r++)
            if (table.Rows[r].Any(c => c.Text.Contains(text, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    private bool SectionChanged(ConfigCsv table, ConfigSection s)
    {
        for (int r = s.Row; r < s.End; r++)
            for (int c = 0; c < table.Rows[r].Count; c++)
                if (Changed(r, c, table.Get(r, c))) return true;
        return false;
    }

    // --- Structured: record tables -------------------------------------------------------------------------

    private void RenderRecords()
    {
        var table = _table!;
        var layout = _layout!;
        if (ImGui.SmallButton(LM.Get("GUI_ConfigTables_InsertRow")))
        {
            int at = _selectedRow >= 0 ? _selectedRow + 1 : (layout.RecordRows.Count > 0 ? layout.RecordRows[^1] + 1 : table.Rows.Count);
            table.Rows.Insert(Math.Min(at, table.Rows.Count), [new ConfigCsv.Cell("")]);
            _selectedRow = at;
            Restructure();
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(_selectedRow < 0 || _selectedRow >= table.Rows.Count);
        if (ImGui.SmallButton(LM.Get("GUI_ConfigTables_DeleteRow")))
        {
            table.Rows.RemoveAt(_selectedRow);
            _selectedRow = -1;
            Restructure();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##recordfilter", LM.Get("GUI_ConfigTables_FilterRows"), ref _filter, 128);

        int width = Math.Max(table.ColumnCount, layout.ColumnNames.Length);
        while (width > 1 && layout.ColumnNames.Length >= width && layout.ColumnNames[width - 1].Length == 0 &&
               layout.RecordRows.All(r => table.Get(r, width - 1).Length == 0)) width--;

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("##records", width + 2, flags)) return;
        ImGui.TableSetupScrollFreeze(2, 1);
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 34f);
        for (int c = 0; c <= width; c++)
            ImGui.TableSetupColumn(Header(layout.ColumnName(null, c), $"{c}") + $"##col{c}", ImGuiTableColumnFlags.WidthFixed, c == 0 ? 170f : 120f);
        ImGui.TableHeadersRow();

        var rows = layout.RecordRows.Where(r => _filter.Length == 0 ||
            table.Rows[r].Any(c => c.Text.Contains(_filter, StringComparison.OrdinalIgnoreCase))).ToList();
        float rowHeight = ImGui.GetFrameHeightWithSpacing();
        var clipper = new ImGuiListClipper();
        clipper.Begin(rows.Count, rowHeight);
        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                int r = rows[i];
                ImGui.TableNextRow();
                ImGui.PushID(r);
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{r + 1}", _selectedRow == r)) _selectedRow = r;
                for (int c = 0; c <= width; c++)
                {
                    ImGui.TableNextColumn();
                    CellEditor(r, c, layout.ColumnOf(c), bold: c == 0);
                }
                ImGui.PopID();
            }
        }
        clipper.End();
        ImGui.EndTable();
    }

    // --- Cells ------------------------------------------------------------------------------------------------

    /// <summary>One cell: its text, tinted when edited or when it does not fit its field (a number field holding a
    /// non-number), with the field's names offered on a name field.</summary>
    private void CellEditor(int row, int column, (ConfigValueKind Kind, IReadOnlyList<string> Choices) field, bool bold = false, bool dim = false)
    {
        var table = _table!;
        string text = table.Get(row, column);
        if (!ConfigLayout.Fits(field.Kind, text))
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(InvalidColour));
        else if (_loaded != null && Changed(row, column, text))
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ChangedColour));

        bool choices = field.Kind == ConfigValueKind.Name && field.Choices.Count > 1;
        float button = choices ? ImGui.GetFrameHeight() : 0f;
        ImGui.SetNextItemWidth(-1f - button);
        if (bold) ImGui.PushStyleColor(ImGuiCol.Text, SectionColour);
        if (dim) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        if (ImGui.InputText($"##c{column}", ref text, 512)) table.Set(row, column, text);
        if (bold || dim) ImGui.PopStyleColor();
        if (ImGui.IsItemActivated()) _selectedRow = row;
        if (ImGui.IsItemHovered() && !ConfigLayout.Fits(field.Kind, text)) ImGui.SetTooltip(LM.Get("GUI_ConfigTables_NotANumber"));
        if (!choices) return;

        ImGui.SameLine(0f, 0f);
        if (ImGui.ArrowButton($"##pick{column}", ImGuiDir.Down)) ImGui.OpenPopup($"##choices{column}");
        if (ImGui.BeginPopup($"##choices{column}"))
        {
            foreach (string choice in field.Choices)
                if (ImGui.Selectable(choice, choice == text)) table.Set(row, column, choice);
            ImGui.EndPopup();
        }
    }

    // --- Raw grid ---------------------------------------------------------------------------------------------

    private void RenderRaw()
    {
        var table = _table!;
        ImGui.BeginDisabled(_selectedRow < 0 || _selectedRow >= table.Rows.Count);
        if (ImGui.SmallButton(LM.Get("GUI_ConfigTables_InsertRow")))
        {
            table.Rows.Insert(_selectedRow + 1, [new ConfigCsv.Cell("")]);
            _selectedRow++;
            Restructure();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(LM.Get("GUI_ConfigTables_DeleteRow")))
        {
            table.Rows.RemoveAt(_selectedRow);
            _selectedRow = Math.Min(_selectedRow, table.Rows.Count - 1);
            Restructure();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##rowfilter", LM.Get("GUI_ConfigTables_FilterRows"), ref _filter, 128);

        // Rows shown: all, or those a filter matches - plus, for a match inside a section, the section's
        // opening row, so a value is never shown without the level / weapon it belongs to.
        var rows = new List<int>();
        if (_filter.Length == 0) rows.AddRange(Enumerable.Range(0, table.Rows.Count));
        else
        {
            int lastSection = -1, shownSection = -1;
            for (int r = 0; r < table.Rows.Count; r++)
            {
                if (IsSectionStart(table, r)) lastSection = r;
                if (!table.Rows[r].Any(c => c.Text.Contains(_filter, StringComparison.OrdinalIgnoreCase))) continue;
                if (lastSection >= 0 && lastSection != r && shownSection != lastSection) { rows.Add(lastSection); shownSection = lastSection; }
                if (lastSection == r) shownSection = r;
                rows.Add(r);
            }
        }

        int columns = Math.Max(1, table.ColumnCount + 1);
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("##csv", columns + 1, flags)) return;
        ImGui.TableSetupScrollFreeze(1, 0);
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 40f);
        for (int c = 0; c < columns; c++) ImGui.TableSetupColumn($"{c}", ImGuiTableColumnFlags.WidthFixed, c == 0 ? 170f : 110f);

        float rowHeight = ImGui.GetFrameHeightWithSpacing();
        if (_scrollTo is { } target)
        {
            int index = rows.IndexOf(target);
            if (index >= 0) ImGui.SetScrollY(index * rowHeight);
            _scrollTo = null;
        }

        var clipper = new ImGuiListClipper();
        clipper.Begin(rows.Count, rowHeight);
        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                int r = rows[i];
                ImGui.TableNextRow();
                ImGui.PushID(r);
                ImGui.TableNextColumn();
                bool section = IsSectionStart(table, r);
                if (section) ImGui.PushStyleColor(ImGuiCol.Text, SectionColour);
                if (ImGui.Selectable($"{r + 1}", _selectedRow == r, ImGuiSelectableFlags.None)) _selectedRow = r;
                if (section) ImGui.PopStyleColor();

                for (int c = 0; c < columns; c++)
                {
                    ImGui.TableNextColumn();
                    string text = table.Get(r, c);
                    if (_loaded != null && Changed(r, c, text))
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ChangedColour));
                    ImGui.SetNextItemWidth(-1f);
                    if (section && c == 0) ImGui.PushStyleColor(ImGuiCol.Text, SectionColour);
                    if (ImGui.InputText($"##c{c}", ref text, 512)) table.Set(r, c, text);
                    if (section && c == 0) ImGui.PopStyleColor();
                    if (ImGui.IsItemActivated()) _selectedRow = r;
                }
                ImGui.PopID();
            }
        }
        clipper.End();
        ImGui.EndTable();
    }

    private static bool IsSectionStart(ConfigCsv table, int row) => table.Rows[row].Count > 0 && table.Rows[row][0].Text.Length > 0;

    /// <summary>Whether a cell differs from the table as loaded. Rows inserted or deleted shift what follows, so
    /// this compares by position - after such an edit the tint shows everything that moved.</summary>
    private bool Changed(int row, int column, string text) => _loaded!.Rows.Count <= row
        ? text.Length > 0
        : _loaded.Get(row, column) != text;

    private void Save()
    {
        var table = _open!;
        byte[] bytes = _table!.ToBytes();
        _saving = true;
        _status = LM.Get("GUI_ConfigTables_Saving", Path.GetFileName(table.ArchivePath));
        BackgroundJob.Run(
            LM.Get("GUI_ConfigTables_Save"),
            _status,
            _ => ArchiveEntryWriter.Replace(table.ArchivePath, new Dictionary<string, byte[]> { [table.EntryPath] = bytes }),
            result =>
            {
                _saving = false;
                _status = result.Succeeded && result.BackupPath != null
                    ? $"{result.Summary} (backup: {Path.GetFileName(result.BackupPath)})"
                    : result.Summary;
                if (!result.Succeeded) return;
                _loaded = ConfigCsv.Parse(bytes);
                _cacheNotice = new GameCacheNotice(table.ArchivePath);
            },
            e =>
            {
                _saving = false;
                _status = e.Message;
            });
    }
}
