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

    public static void Clear() => _pending.Clear();

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
