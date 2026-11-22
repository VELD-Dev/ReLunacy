using System.Globalization;
using System.Numerics;
using ReLunacy.Core.Undo;
using ReLunacy.Engine.Config;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Packing;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Utility;

/// <summary>
/// A configuration table (<see cref="ConfigCsv"/>) open for editing, shared by everything that shows it: Config
/// Tables, Level Data's level block, and the level view's sector boundary, which reads BoundsRadius from it every
/// frame - so an edit made in one shows in the others at once. One per table, kept while the game stays the same.
///
/// A save goes through ArchiveEntryWriter: rebuilt, verified, the archive backed up with the date and time, then
/// replaced; rows nobody edited write back to their exact bytes (ConfigCsv).
/// </summary>
public sealed class ConfigDocument
{
    public ConfigTableRef Ref { get; }

    /// <summary>The table as edited.</summary>
    public ConfigCsv Table { get; private set; }

    /// <summary>The table as last read from or written to the archive.</summary>
    public ConfigCsv Loaded { get; private set; }

    /// <summary>Changes when rows are inserted or removed, or the table is read again: views holding row indices
    /// or a <see cref="ConfigLayout"/> rebuild them.</summary>
    public int Structure { get; private set; }

    public bool Saving { get; private set; }
    public string? Status { get; set; }
    public GameCacheNotice? CacheNotice { get; private set; }

    private ConfigDocument(ConfigTableRef table)
    {
        Ref = table;
        Table = GameConfigs.Read(table);
        Loaded = GameConfigs.Read(table);
    }

    public bool Dirty => Table.ToText() != Loaded.ToText();

    /// <summary>Whether a cell differs from the table as loaded. Rows inserted or deleted shift what follows, so
    /// this compares by position - after such an edit the tint shows everything that moved.</summary>
    public bool Changed(int row, int column, string text) => Loaded.Rows.Count <= row
        ? text.Length > 0
        : Loaded.Get(row, column) != text;

    /// <summary>Rows were inserted or removed.</summary>
    public void Restructured() => Structure++;

    public void Revert()
    {
        Table = GameConfigs.Read(Ref);
        Loaded = GameConfigs.Read(Ref);
        Structure++;
        Status = null;
        CacheNotice = null;
    }

    public void Save()
    {
        if (Saving) return;
        var table = Ref;
        byte[] bytes = Table.ToBytes();
        Saving = true;
        Status = LM.Get("GUI_ConfigTables_Saving", Path.GetFileName(table.ArchivePath));
        BackgroundJob.Run(
            LM.Get("GUI_ConfigTables_Save"),
            Status,
            _ => ArchiveEntryWriter.Replace(table.ArchivePath, new Dictionary<string, byte[]> { [table.EntryPath] = bytes }),
            result =>
            {
                Saving = false;
                Status = result.Succeeded && result.BackupPath != null
                    ? $"{result.Summary} (backup: {Path.GetFileName(result.BackupPath)})"
                    : result.Summary;
                if (!result.Succeeded) return;
                Loaded = ConfigCsv.Parse(bytes);
                // Only a write into the game's archive makes its cache stale; a project's install says so then.
                if (result.BackupPath != null) CacheNotice = new GameCacheNotice(table.ArchivePath);
            },
            e =>
            {
                Saving = false;
                Status = e.Message;
            });
    }

    /// <summary>Save and Revert, then the save's status and the cache notice.</summary>
    public void DrawSaveBar()
    {
        bool dirty = Dirty;
        ImGui.BeginDisabled(!dirty || Saving);
        if (ImGui.Button(LM.Get("GUI_ConfigTables_Save"))) Save();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_ConfigTables_Revert"))) Revert();
        ImGui.EndDisabled();
    }

    public void DrawStatus()
    {
        if (Status != null) ImGui.TextWrapped(Status);
        CacheNotice?.Draw();
    }

    // --- Cell edits ------------------------------------------------------------------------------------------

    /// <summary>Sets a cell as one undo step.</summary>
    public void Edit(int row, int column, string text)
    {
        var cells = Table.Rows[row];
        string before = Table.Get(row, column);
        if (before == text) return;
        ConfigCsv.Set(cells, column, text);
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_ConfigEdit"),
            () => ConfigCsv.Set(cells, column, before), () => ConfigCsv.Set(cells, column, text));
    }

    // A text field being typed into: the cell's row, column and text when it was clicked into, pushed as one
    // step when it is let go.
    private static List<ConfigCsv.Cell>? _editRow;
    private static int _editColumn;
    private static string _editBefore = "";

    /// <summary>Call right after a widget that edits a cell in place: opens the undo step when the widget is
    /// clicked into and pushes it when it is let go with a change.</summary>
    public void TrackEdit(int row, int column)
    {
        if (ImGui.IsItemActivated())
        {
            _editRow = Table.Rows[row];
            _editColumn = column;
            _editBefore = Table.Get(row, column);
        }
        if (!ImGui.IsItemDeactivated() || _editRow == null) return;
        var cells = _editRow;
        int c = _editColumn;
        string before = _editBefore, after = c < cells.Count ? cells[c].Text : "";
        _editRow = null;
        if (before == after) return;
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_ConfigEdit"),
            () => ConfigCsv.Set(cells, c, before), () => ConfigCsv.Set(cells, c, after));
    }

    public static readonly Vector4 SectionColour = new(1f, 0.8f, 0.35f, 1f);
    private static readonly Vector4 ChangedColour = new(0.35f, 0.8f, 1f, 0.25f);
    private static readonly Vector4 InvalidColour = new(1f, 0.3f, 0.25f, 0.35f);

    /// <summary>One cell in a table: its text, tinted when edited or when it does not fit its field (a number field
    /// holding a non-number), with the field's names offered on a name field. True when it was clicked into.</summary>
    public bool CellEditor(int row, int column, (ConfigValueKind Kind, IReadOnlyList<string> Choices) field, bool bold = false, bool dim = false)
    {
        string text = Table.Get(row, column);
        if (!ConfigLayout.Fits(field.Kind, text))
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(InvalidColour));
        else if (Changed(row, column, text))
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ChangedColour));

        bool choices = field.Kind == ConfigValueKind.Name && field.Choices.Count > 1;
        float button = choices ? ImGui.GetFrameHeight() : 0f;
        ImGui.SetNextItemWidth(-1f - button);
        if (bold) ImGui.PushStyleColor(ImGuiCol.Text, SectionColour);
        if (dim) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        if (ImGui.InputText($"##c{column}", ref text, 512)) Table.Set(row, column, text);
        if (bold || dim) ImGui.PopStyleColor();
        bool activated = ImGui.IsItemActivated();
        TrackEdit(row, column);
        if (ImGui.IsItemHovered() && !ConfigLayout.Fits(field.Kind, text)) ImGui.SetTooltip(LM.Get("GUI_ConfigTables_NotANumber"));
        if (!choices) return activated;

        ImGui.SameLine(0f, 0f);
        if (ImGui.ArrowButton($"##pick{column}", ImGuiDir.Down)) ImGui.OpenPopup($"##choices{column}");
        if (ImGui.BeginPopup($"##choices{column}"))
        {
            foreach (string choice in field.Choices)
                if (ImGui.Selectable(choice, choice == text)) Edit(row, column, choice);
            ImGui.EndPopup();
        }
        return activated;
    }

    /// <summary>A number cell as a drag: written back in the shortest form that reads back the same ("2650", not
    /// "2650.0"), one undo step per drag.</summary>
    public bool NumberDrag(string label, int row, int column, float speed, float min, float max)
    {
        string text = Table.Get(row, column);
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
        bool changed = ImGui.DragFloat(label, ref value, speed, min, max, "%.0f");
        if (changed) Table.Set(row, column, Format(value));
        TrackEdit(row, column);
        return changed;
    }

    private static string Format(float value) =>
        MathF.Abs(value - MathF.Round(value)) < 1e-4f
            ? ((long)MathF.Round(value)).ToString(CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);

    // --- Shared documents ------------------------------------------------------------------------------------

    private static readonly Dictionary<ConfigTableRef, ConfigDocument> Open_ = [];
    private static string? _gameFolder;

    /// <summary>The shared document for a table, read on first use. Documents of another game are dropped.</summary>
    public static ConfigDocument Open(ConfigTableRef table)
    {
        string game = Path.GetDirectoryName(table.ArchivePath) ?? "";
        if (_gameFolder != game) { Open_.Clear(); _gameFolder = game; }
        if (!Open_.TryGetValue(table, out var document)) Open_[table] = document = new ConfigDocument(table);
        return document;
    }

    /// <summary>Reads the open tables again after something else (a level save) wrote one; tables with unsaved edits stay.</summary>
    public static void ReloadClean()
    {
        foreach (var document in Open_.Values)
            if (!document.Dirty && !document.Saving) document.Revert();
    }

    private static string? _levelFor;
    private static ConfigTableRef? _levelTable;
    private static string? _levelError;

    /// <summary>level.csv of the open level's game, or null (with <paramref name="error"/> when reading it failed).
    /// Looked up once per level.</summary>
    public static ConfigDocument? LevelTable(out string? error)
    {
        string source = Program.ProvidedPath;
        if (_levelFor != source)
        {
            _levelFor = source;
            _levelTable = null;
            _levelError = null;
            try
            {
                if (!string.IsNullOrEmpty(source) && GameConfigs.GameFolderFor(source) is { } game)
                    _levelTable = GameConfigs.LevelTable(game);
            }
            catch (Exception e) { _levelError = e.Message; }
        }
        error = _levelError;
        if (_levelTable == null) return null;
        try { return Open(_levelTable); }
        catch (Exception e)
        {
            _levelError = error = e.Message;
            _levelTable = null;
            return null;
        }
    }

    /// <summary>The open level's name as level.csv knows it (space_sector_1).</summary>
    public static string LevelName => GameLibraryScanner.GetLevelNameFromPath(Program.ProvidedPath);

    /// <summary>The open level's BoundsRadius from the shared level.csv, as edited; null when it has none.</summary>
    public static float? LevelBoundsRadius()
    {
        if (string.IsNullOrEmpty(Program.ProvidedPath)) return null;
        var document = LevelTable(out _);
        return document == null ? null : GameConfigs.BoundsRadius(document.Table, LevelName);
    }
}
