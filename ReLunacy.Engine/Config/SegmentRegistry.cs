using System.Globalization;

namespace ReLunacy.Engine.Config;

/// <summary>A new segment Pod waiting to be entered in level.csv (see <see cref="SegmentRegistry"/>). <paramref name="ModelPod"/>
/// is the segment whose column it copies (the model moon's); null takes the level's first SEG_MOON.</summary>
public sealed record SegmentRegistration(string NewPod, string? ModelPod);

/// <summary>
/// A segment is live in game only through level.csv (global_cached /data/configs/level.csv): the level's block has one
/// column per segment, matched to the level's Pod by name (Pod row), with its Type (SEG_MOON), bolt and XP pools. The
/// game keeps a save state per segment column, so a new column goes LAST (after SEGMENT_SS1_JUNKTRADER), never between.
/// Cells that differ per moon in retail: Pod (the name), NameTag (a text id, MNnMC) and the counts NumZoni / NumGoldBolts /
/// NumParts, which belong to what the moon holds: the new column copies every other cell of the model's and leaves those
/// counts empty. The level-wide column holds the sums of the pools (3300 = 2000 + 5 x 250 + 50 on sector 1), which grow by the
/// copied pools when they were sums. Pending registrations are written by the level save, into level.csv through the sink.
/// </summary>
public static class SegmentRegistry
{
    private static readonly List<SegmentRegistration> _pending = [];
    private static readonly string[] CountRows = ["NumZoni", "NumGoldBolts", "NumParts", "NumPlans"];
    private static readonly string[] PoolRows = ["BoltPool", "BoltLimit", "XPPool", "XPLimit"];
    private const int FirstSegment = 3;

    public static IReadOnlyList<SegmentRegistration> Pending => _pending;

    public static void Add(SegmentRegistration registration)
    {
        if (!_pending.Contains(registration)) _pending.Add(registration);
    }

    public static void Remove(SegmentRegistration registration) => _pending.Remove(registration);

    public static void Clear() { _pending.Clear(); _renames.Clear(); }

    private static readonly List<(string Old, string New)> _renames = [];

    /// <summary>Pods renamed in the editor (old name, new name), for the level save to carry into level.csv's Pod row: a
    /// segment is matched to its Pod by name. A rename of a rename is one rename; one back to the first name is none.</summary>
    public static IReadOnlyList<(string Old, string New)> PendingRenames => _renames;

    public static void RenamePod(string oldName, string newName)
    {
        if (oldName == newName) return;
        int i = _renames.FindIndex(r => r.New == oldName);
        if (i < 0) _renames.Add((oldName, newName));
        else if (_renames[i].Old == newName) _renames.RemoveAt(i);
        else _renames[i] = (_renames[i].Old, newName);
    }

    public static void RemoveRename((string Old, string New) rename) => _renames.Remove(rename);

    /// <summary>What <see cref="DropPod"/> took out of the pending lists, for <see cref="RestorePod"/>.</summary>
    public sealed record PodDrop(SegmentRegistration? Registration, int RegistrationAt, (string Old, string New)? Rename, int RenameAt);

    /// <summary>A Pod is deleted: its pending registration (a new segment not in level.csv yet) and its pending rename go, as
    /// there is nothing to enter or rename. The level save takes a column the file already has out (<see cref="ApplyRemoval"/>).</summary>
    public static PodDrop DropPod(string name)
    {
        int r = _renames.FindIndex(x => x.New == name);
        string first = r >= 0 ? _renames[r].Old : name;
        var rename = r >= 0 ? _renames[r] : ((string, string)?)null;
        if (r >= 0) _renames.RemoveAt(r);
        int g = _pending.FindIndex(x => x.NewPod == first);
        var registration = g >= 0 ? _pending[g] : null;
        if (g >= 0) _pending.RemoveAt(g);
        return new PodDrop(registration, g, rename, r);
    }

    public static void RestorePod(PodDrop drop)
    {
        if (drop.Registration != null && !_pending.Contains(drop.Registration)) _pending.Insert(Math.Clamp(drop.RegistrationAt, 0, _pending.Count), drop.Registration);
        if (drop.Rename is { } rename && !_renames.Contains(rename)) _renames.Insert(Math.Clamp(drop.RenameAt, 0, _renames.Count), rename);
    }

    /// <summary>The level's block loses the segment's column (the Pod row names it): the cells behind it move left, and a
    /// level-wide pool that is the sum of the segments' (as <see cref="Apply"/> keeps it) drops by the column's value.
    /// A segment in the middle renumbers the ones after it, which the game keeps a save state for. True with
    /// <paramref name="found"/> false when the level has no such segment (nothing to change).</summary>
    public static bool ApplyRemoval(ConfigCsv csv, string levelName, string pod, out bool found, out string? problem)
    {
        problem = null;
        found = false;
        if (GameConfigs.LevelSection(csv, levelName) is not { } block) { problem = $"level.csv has no block for {levelName}"; return false; }
        var rows = block.Rows;
        int pods = GameConfigs.FieldRow(csv, rows, "Pod");
        if (pods < 0) { problem = "its block has no Pod row"; return false; }
        int column = -1, last = FirstSegment - 1;
        for (int c = FirstSegment; c < csv.Rows[pods].Count; c++)
        {
            string cell = csv.Get(pods, c);
            if (cell.Length > 0) last = c;
            if (cell == pod && column < 0) column = c;
        }
        if (column < 0) return true;
        for (int r = rows.Start + 1; r < rows.End; r++)
        {
            string field = csv.Get(r, 1);
            if (r != pods && PoolRows.Contains(field)
                && double.TryParse(csv.Get(r, column), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && double.TryParse(csv.Get(r, GameConfigs.LevelValueColumn), NumberStyles.Float, CultureInfo.InvariantCulture, out double total)
                && Math.Abs(total - Sum(csv, r, FirstSegment, last)) < 1e-6)
                csv.Set(r, GameConfigs.LevelValueColumn, (total - value).ToString(CultureInfo.InvariantCulture));
        }
        for (int r = rows.Start + 1; r < rows.End; r++)
        {
            var cells = csv.Rows[r];
            if (cells.Count > column) cells.RemoveAt(column);
            // The last segment's column leaves: the empty cells a copied column padded a short row with go too.
            if (column == last)
                while (cells.Count > FirstSegment && cells[^1].Text.Length == 0 && !cells[^1].Quoted) cells.RemoveAt(cells.Count - 1);
        }
        found = true;
        return true;
    }

    public static void ClearRenames() => _renames.Clear();

    /// <summary>The level's block has the renamed Pod in its Pod row: that cell gets the new name. Returns false with
    /// <paramref name="problem"/> when it cannot; true with <paramref name="found"/> false when the Pod is no segment of the
    /// level (nothing to change).</summary>
    public static bool ApplyRename(ConfigCsv csv, string levelName, (string Old, string New) rename, out bool found, out string? problem)
    {
        problem = null;
        found = false;
        if (GameConfigs.LevelSection(csv, levelName) is not { } block) { problem = $"level.csv has no block for {levelName}"; return false; }
        int pods = GameConfigs.FieldRow(csv, block.Rows, "Pod");
        if (pods < 0) { problem = "its block has no Pod row"; return false; }
        int column = -1;
        for (int c = FirstSegment; c < csv.Rows[pods].Count; c++)
        {
            string cell = csv.Get(pods, c);
            if (cell == rename.New) { problem = $"another segment is already named {rename.New}"; return false; }
            if (cell == rename.Old) column = c;
        }
        if (column < 0) return true;
        csv.Set(pods, column, rename.New);
        found = true;
        return true;
    }

    /// <summary>The level's block gets the segment's column. Returns false with <paramref name="problem"/> when it cannot, true
    /// with it null otherwise (also when the Pod is already there, which changes nothing).</summary>
    public static bool Apply(ConfigCsv csv, string levelName, SegmentRegistration registration, out string? problem)
    {
        problem = null;
        if (GameConfigs.LevelSection(csv, levelName) is not { } block) { problem = $"level.csv has no block for {levelName}"; return false; }
        var rows = block.Rows;
        int pods = GameConfigs.FieldRow(csv, rows, "Pod"), types = GameConfigs.FieldRow(csv, rows, "Type");
        if (pods < 0) { problem = "its block has no Pod row"; return false; }
        int last = FirstSegment - 1;
        for (int c = FirstSegment; c < csv.Rows[pods].Count; c++)
            if (csv.Get(pods, c).Length > 0) last = c;
        for (int c = FirstSegment; c <= last; c++)
            if (csv.Get(pods, c) == registration.NewPod) return true;

        // Retail's largest level has this many segments; the game's own limit is not known.
        int most = csv.Rows.Where(r => r.Count > 1 && r[1].Text == "Pod").Select(r => r.Count(c => c.Text.Length > 0) - 1).DefaultIfEmpty(0).Max();
        int column = last + 1;
        if (column - FirstSegment + 1 > Math.Max(most, 9)) { problem = $"a level has {Math.Max(most, 9)} segments at most"; return false; }

        int model = -1;
        for (int c = FirstSegment; c <= last && model < 0; c++)
            if (registration.ModelPod != null && csv.Get(pods, c) == registration.ModelPod) model = c;
        for (int c = FirstSegment; c <= last && model < 0; c++)
            if (types >= 0 && csv.Get(types, c) == "SEG_MOON") model = c;
        if (model < 0) { problem = "no SEG_MOON segment to copy"; return false; }

        for (int r = rows.Start + 1; r < rows.End; r++)
        {
            string field = csv.Get(r, 1);
            if (r == pods) { csv.Set(r, column, registration.NewPod); continue; }
            if (field.Length == 0 || CountRows.Contains(field)) continue;
            string value = csv.Get(r, model);
            if (value.Length == 0) continue;
            csv.Set(r, column, value);
            // A level-wide pool that is the sum of the segments' stays the sum.
            if (PoolRows.Contains(field) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double add)
                && double.TryParse(csv.Get(r, GameConfigs.LevelValueColumn), NumberStyles.Float, CultureInfo.InvariantCulture, out double total)
                && Math.Abs(total - Sum(csv, r, FirstSegment, last)) < 1e-6)
                csv.Set(r, GameConfigs.LevelValueColumn, (total + add).ToString(CultureInfo.InvariantCulture));
        }
        return true;
    }

    private static double Sum(ConfigCsv csv, int row, int from, int to)
    {
        double sum = 0;
        for (int c = from; c <= to; c++)
            if (double.TryParse(csv.Get(row, c), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) sum += v;
        return sum;
    }
}
