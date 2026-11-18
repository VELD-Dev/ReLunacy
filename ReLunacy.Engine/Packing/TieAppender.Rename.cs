using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace ReLunacy.Engine.Packing;

/// <summary>One Tie placement of the level's files to give another name: its zone, its class, its matrix as the file holds it
/// (the in-place writers have already moved it) and its name as the editor shows it (the part after the last '/').</summary>
public sealed record TieRenameRequest(ulong ZoneTuid, ulong ClassTuid, Matrix4x4 Matrix, string OldName, string NewName);

/// <summary>What <see cref="TieAppender.Rename"/> did: the rewritten files (archive paths, only those that changed), what changed
/// and the requests refused, with why.</summary>
public sealed class TieRenameResult
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
    public List<(TieRenameRequest Request, string Reason)> Refused { get; } = [];
}

public static partial class TieAppender
{
    /// <summary>
    /// Gives Tie placements another name (zones.dat, the zone's blob): the row is found by its class, its matrix and its name
    /// (the rows of a zone move when others are deleted or added, so no index is kept), and its string in 0x11300 is rewritten
    /// in place when no other row points at it (a pointer into it from anywhere else counts), else the row gets a string of its
    /// own at the end of the pool and the others keep theirs. The blob is re-laid, the later blobs move, assetlookup 0x1DA00 and
    /// level_cached.toc follow (the same as <see cref="Delete"/>). A name that holds a '/' path keeps it. Nothing else names a
    /// placement by its name (collision, lighting and the lookups go by TUID, class and index). Run it last, on the output of
    /// the in-place writers, the deletions and the appends.
    /// </summary>
    public static TieRenameResult Rename(Func<string, byte[]?> read, IReadOnlyList<TieRenameRequest> requests)
    {
        var result = new TieRenameResult();
        if (requests.Count == 0) return result;
        var level = new Level { Read = read };
        foreach (var request in requests)
        {
            try { RenameOne(level, request, result); }
            catch (Exception e) when (e is Refusal or InvalidDataException) { result.Refused.Add((request, e.Message)); }
        }
        if (level.Changed.Count > 0)
        {
            UpdateToc(level, result.Problems);
            foreach (string name in level.Changed) result.Files[name] = level.Files[name];
        }
        return result;
    }

    private static string StringAt(byte[] blob, int at)
    {
        int end = Array.IndexOf(blob, (byte)0, at);
        return Encoding.Latin1.GetString(blob, at, (end < 0 ? blob.Length : end) - at);
    }

    private static void RenameOne(Level level, TieRenameRequest request, TieRenameResult result)
    {
        if (GpPriusDocument.NameProblem(request.NewName) is { } bad) throw new Refusal($"name: {bad}");
        byte[] zones = level.Get("zones.dat") ?? throw new Refusal("zones.dat not found");
        byte[] lookup = level.Get("assetlookup.dat") ?? throw new Refusal("assetlookup.dat not found");
        var zonePointers = Pointers(lookup, ZonePointers);
        int zi = zonePointers.FindIndex(p => p.Tuid == request.ZoneTuid);
        if (zi < 0) throw new Refusal($"zone {request.ZoneTuid:X16} is not in this level");
        var (_, offset, length) = zonePointers[zi];
        byte[] blob = zones.AsSpan(offset, length).ToArray();
        var secs = IghwRelayout.Sections(blob);
        var placements = IghwRelayout.Find(secs, Placements);
        var names = IghwRelayout.Find(secs, Names);
        var classes = IghwRelayout.Find(secs, ClassTable);
        var strings = IghwRelayout.Find(secs, Strings);
        if (placements == null || names == null || classes == null || strings == null || names.Count != placements.Count)
            throw new Refusal("the zone's Tie tables are not laid out as expected");
        if (placements.LengthWord != 0x80 || names.LengthWord != 0x10) throw new Refusal("the zone's Tie tables are not laid out as expected");

        // The row: its class, its matrix and its name.
        int row = -1;
        for (int i = 0; i < placements.Count && row < 0; i++)
        {
            uint classIndex = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(placements.Offset + 0x80 * i + 0x50));
            if (classIndex >= classes.Count || BinaryPrimitives.ReadUInt64BigEndian(blob.AsSpan(classes.Offset + 8 * (int)classIndex)) != request.ClassTuid) continue;
            if (!MatricesMatch(ReadAffine(blob, placements.Offset + 0x80 * i), request.Matrix)) continue;
            int p = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(names.Offset + 0x10 * i + 8));
            if (p < strings.Offset || p >= strings.End || StringAt(blob, p).Split('/')[^1] != request.OldName) continue;
            row = i;
        }
        if (row < 0) throw new Refusal("its record is not in the file as the editor loaded it");

        int field = names.Offset + 0x10 * row + 8;
        int at = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(field));
        string stored = StringAt(blob, at);
        string prefix = stored.Contains('/') ? stored[..(stored.LastIndexOf('/') + 1)] : "";
        byte[] fresh = [.. Encoding.Latin1.GetBytes(prefix + request.NewName), 0];
        int from = at - strings.Offset, to = from + Encoding.Latin1.GetByteCount(stored) + 1;
        byte[] pool = blob.AsSpan(strings.Offset, strings.Size).ToArray();

        // Another pointer into the string (or into its tail) keeps it where it is.
        int fixCount = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x14)), fixAt = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x10));
        bool shared = false;
        for (int f = 0; f < fixCount && !shared; f++)
        {
            int where = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(fixAt + 4 * f));
            if (where == field) continue;
            int target = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(where));
            shared = target >= at && target < at + (to - from);
        }

        byte[] newBlob;
        if (!shared)
        {
            int delta = fresh.Length - (to - from);
            byte[] payload = [.. pool.AsSpan(0, from), .. fresh, .. pool.AsSpan(to)];
            int? Map(int o) => o <= from ? o : o < to ? null : o + delta;
            newBlob = IghwRelayout.Relayout(blob, new Dictionary<uint, byte[]> { [Strings] = payload }, null,
                new Dictionary<uint, Func<int, int?>> { [Strings] = Map });
        }
        else
        {
            newBlob = IghwRelayout.Relayout(blob, new Dictionary<uint, byte[]> { [Strings] = [.. pool, .. fresh] });
            var after = IghwRelayout.Sections(newBlob);
            int fieldAt = IghwRelayout.Find(after, Names)!.Offset + 0x10 * row + 8;
            BinaryPrimitives.WriteInt32BigEndian(newBlob.AsSpan(fieldAt), IghwRelayout.Find(after, Strings)!.Offset + pool.Length);
        }

        byte[] newLookup = (byte[])lookup.Clone();
        zones = Splice(zones, zonePointers, zi, newBlob);
        Rewrite(newLookup, ZonePointers, zonePointers);
        level.Set("zones.dat", zones);
        level.Set("assetlookup.dat", newLookup);
        result.Changes.Add($"Tie {request.OldName} renamed {request.NewName} in zone {request.ZoneTuid:X16}{(shared ? " (its own string, the old one is shared)" : "")}");
    }
}
