using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using ReLunacy.Engine.Assets.LevelElements;

namespace ReLunacy.Engine.Packing;

/// <summary>Where a new instance's settings block comes from.</summary>
public enum GpSettingsSource
{
    /// <summary>The template's: a private block (0x25020) is copied, a shared class default (0x25030) is shared.</summary>
    Template,
    /// <summary>The class default block (0x25030) another instance of the same class already shares.</summary>
    ClassDefault,
    /// <summary>A private copy (0x25020) of that class default block: for an instance whose settings are edited away from the
    /// default, which would otherwise write through the shared block.</summary>
    ClassDefaultCopy,
    /// <summary>No settings (offset 0, size 0); Mobys only.</summary>
    None,
}

/// <summary>A Moby to append at the end of a layer's Mobys. The class is a region.dat 0x1C600 index
/// (<see cref="ClassIndex"/>) or a class TUID looked up there (<see cref="ClassTuid"/>). The record's fields this
/// does not set (+0x30..+0x4C, unknown) are copied from <see cref="Template"/>, by default the first Moby of the
/// class in the region. Rotation is Euler angles in radians, applied Z, Y, X (GameplayWriter.FromEuler).</summary>
public sealed record GpMobySpec(ushort Layer, string Name, Vector3 Position, Vector3 Rotation, float Scale = 1f)
{
    public ushort? ClassIndex { get; init; }
    public ulong? ClassTuid { get; init; }
    public int? Template { get; init; }
    public float? DisplayDistance { get; init; }
    public float? UpdateDistance { get; init; }
    public GpSettingsSource Settings { get; init; } = GpSettingsSource.Template;
    public ulong? Tuid { get; init; }
}

/// <summary>A volume to append at the end of a layer's volumes: a row-major matrix (rotation-scale rows, then the
/// translation row; its fourth column is not stored, the last word holds the shape).</summary>
public sealed record GpVolumeSpec(ushort Layer, string Name, Matrix4x4 Matrix, VolumeShape Shape = VolumeShape.Box)
{
    public ulong? Tuid { get; init; }
}

/// <summary>A Clue to make of a new volume (same layer): its class and settings, copied from <see cref="Template"/>
/// (by default the first Clue of the class in the region; none there, the Clue is refused).</summary>
public sealed record GpClueSpec(string Name, uint Class)
{
    public int? Template { get; init; }
    public GpSettingsSource Settings { get; init; } = GpSettingsSource.Template;
    public ulong? Tuid { get; init; }
}

/// <summary>A path to append at the end of a layer's paths. Kind 0 / 1 / 2 as the file has them (1 is the common one).</summary>
public sealed record GpPathSpec(ushort Layer, string Name, IReadOnlyList<Vector3> Points, bool Closed = false, byte Kind = 1)
{
    public ulong? Tuid { get; init; }
}

/// <summary>A controller to append at the end of a layer's controllers: a copy of <see cref="Template"/>'s record
/// (its class lives in its settings) at a new placement.</summary>
public sealed record GpControllerSpec(ushort Layer, string Name, int Template, Vector3 Position, Vector3 Rotation, Vector3 Scale)
{
    public GpSettingsSource Settings { get; init; } = GpSettingsSource.Template;
    public ulong? Tuid { get; init; }
}

/// <summary>What an edit did, or why it did nothing (<see cref="Applied"/> false: the document is unchanged).</summary>
public sealed class GpEditResult
{
    public bool Applied { get; internal set; }
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
    /// <summary>The instance an append made (its index in its kind's table).</summary>
    public GpInstance? Created { get; internal set; }
    /// <summary>A Clue made with a volume.</summary>
    public GpInstance? CreatedClue { get; internal set; }
    /// <summary>The layer <see cref="GpPriusDocument.AddLayer"/> made (its index, the last).</summary>
    public int? CreatedLayer { get; internal set; }
    /// <summary>Instances whose (layer, index in layer) changed. A Crack in Time's references in the file are rewritten
    /// (<see cref="ReferencesRewritten"/>); instances.lua and the level scripts name instances the same way and are
    /// not: the caller warns.</summary>
    public List<GpIndexShift> Shifts { get; } = [];
    public int ReferencesRewritten { get; internal set; }
    /// <summary>Reference words set to "none" because the instance they named was deleted.</summary>
    public int ReferencesCleared { get; internal set; }

    internal static GpEditResult Refuse(string problem)
    {
        var r = new GpEditResult();
        r.Problems.Add(problem);
        return r;
    }
}

/// <summary>
/// The structural edits. Each checks everything it needs first and refuses (<see cref="GpEditResult.Applied"/> false,
/// document untouched) rather than write something unsafe.
///
///   - New instances go at the END of their layer's run of their kind: the file keeps every kind grouped by ascending
///     layer, and the end is the one place where no existing instance's (layer, index) changes.
///   - A layer change is a move to the end of the new layer, for the same reason: only the instances after it in its
///     old layer move up by one, and they are reported (<see cref="GpEditResult.Shifts"/>); in A Crack in Time's form
///     every reference word naming a moved handle is rewritten (Clue volumes, Pod / Area / Scent lists, settings
///     words). All 4 One / Into the Nexus reference by TUID through the pool, which a move does not change.
///   - Membership edits rewrite the list in place; a list that empties loses its pointer (an empty list is null in
///     every shipped file), one that was empty gets a new run at the end of its section.
///   - Every new name goes to the end of 0x11300, every copied private settings block to the end of 0x25020, every
///     new pool entry to the end of 0x25022. Sections are never created: a region without a kind's tables refuses
///     that kind.
/// </summary>
public sealed partial class GpPriusDocument
{
    /// <summary>The pointer fields of a kind's record (offsets), for zeroing a copied template's stale values.</summary>
    private static int[] PointerFields(GpKind kind, bool pooled) => kind switch
    {
        GpKind.Moby => [0x0C, 0x40],
        GpKind.Clue => pooled ? [0x00, 0x04] : [0x04],
        GpKind.Controller or GpKind.Path or GpKind.Pod or GpKind.Scent or GpKind.Squadron => [0x00],
        GpKind.Area => [0x00, 0x04],
        _ => [],
    };

    // ---- appends ----------------------------------------------------------------------------------------------

    /// <summary>Appends a Moby (see <see cref="GpMobySpec"/>). <paramref name="regionDat"/> is the region's region.dat:
    /// the class must be in its 0x1C600 table, the classes the region loads.</summary>
    public GpEditResult AppendMoby(GpMobySpec spec, byte[] regionDat, Random? random = null)
    {
        var records = RecordsOf(GpKind.Moby);
        if (records == null || MetadataOf(GpKind.Moby) == null) return GpEditResult.Refuse("this region has no Moby tables");
        if (CheckCommon(spec.Layer, spec.Name, spec.Tuid) is { } common) return GpEditResult.Refuse(common);
        var classes = RegionClasses(regionDat);
        if (classes == null) return GpEditResult.Refuse("region.dat has no class table (0x1C600)");
        int classIndex;
        if (spec.ClassTuid is { } classTuid)
        {
            classIndex = classes.IndexOf(classTuid);
            if (classIndex < 0) return GpEditResult.Refuse($"class 0x{classTuid:X16} is not in this region's class table (region.dat 0x1C600), so the region does not load it");
            if (spec.ClassIndex is { } given && given != classIndex) return GpEditResult.Refuse("the class index and class TUID disagree");
        }
        else if (spec.ClassIndex is { } index)
        {
            if (index >= classes.Count) return GpEditResult.Refuse($"class index {index} is past the region's {classes.Count} classes (region.dat 0x1C600)");
            classIndex = index;
        }
        else return GpEditResult.Refuse("no class given");

        int count = Count(GpKind.Moby);
        int template = spec.Template ?? Enumerable.Range(0, count).FirstOrDefault(i => Half(records, i * 0x50) == classIndex, -1);
        if (template < 0 || template >= count)
            return GpEditResult.Refuse($"no Moby of class index {classIndex} in this region to copy the record's unknown fields from; give a template");
        var templateInstance = new GpInstance(GpKind.Moby, template);
        var settings = PlanSettings(templateInstance, spec.Settings, i => Half(records, i.Index * 0x50) == classIndex, $"class index {classIndex}");
        if (settings.Problem != null) return GpEditResult.Refuse(settings.Problem);

        var result = new GpEditResult();
        if (Half(records, template * 0x50) != classIndex)
            result.Changes.Add($"record fields +0x30..+0x4C copied from {Name(templateInstance)}, a Moby of another class");
        byte[] record = records.Data.AsSpan(template * 0x50, 0x50).ToArray();
        var span = record.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)classIndex);
        BinaryPrimitives.WriteUInt16BigEndian(span[0x02..], spec.Layer);
        if (spec.DisplayDistance is { } display) BinaryPrimitives.WriteSingleBigEndian(span[0x04..], display);
        if (spec.UpdateDistance is { } update) BinaryPrimitives.WriteSingleBigEndian(span[0x08..], update);
        WriteVector(span[0x14..], spec.Position);
        WriteVector(span[0x20..], spec.Rotation);
        BinaryPrimitives.WriteSingleBigEndian(span[0x2C..], spec.Scale);

        var pointers = new List<(int, GpSection, int)>();
        ApplySettings(settings, record, 0x0C, pointers, result);
        // Matrices (0x25049) the template carries: copied to the end of the table.
        int matrices = Half(records, template * 0x50 + 0x44);
        if (matrices > 0 && PointerAt(records, template * 0x50 + 0x40) is { } m)
        {
            byte[] copy = m.Target.Data.AsSpan(m.TargetAt, matrices * 0x40).ToArray();
            int at = m.Target.Data.Length;
            Splice(m.Target, at, 0, copy);
            pointers.Add((0x40, m.Target, at));
            result.Changes.Add($"{matrices} matrices (0x25049) copied from {Name(templateInstance)}");
        }
        var created = InsertInstance(GpKind.Moby, spec.Layer, record, pointers, spec.Tuid ?? NewTuid(random), spec.Name);
        return Done(result, created, $"Moby {spec.Name} (class index {classIndex}) appended to layer {LayerName(spec.Layer)}");
    }

    /// <summary>Appends a volume and, with <paramref name="clue"/>, a Clue of it (same layer).</summary>
    public GpEditResult AppendVolume(GpVolumeSpec spec, GpClueSpec? clue = null, Random? random = null)
    {
        if (RecordsOf(GpKind.Volume) == null || MetadataOf(GpKind.Volume) == null) return GpEditResult.Refuse("this region has no volume tables");
        if (CheckCommon(spec.Layer, spec.Name, spec.Tuid) is { } common) return GpEditResult.Refuse(common);
        SettingsPlan clueSettings = default;
        int clueTemplate = -1;
        if (clue != null)
        {
            var clues = RecordsOf(GpKind.Clue);
            if (clues == null || MetadataOf(GpKind.Clue) == null) return GpEditResult.Refuse("this region has no Clue tables");
            if (CheckCommon(spec.Layer, clue.Name, clue.Tuid) is { } clueProblem) return GpEditResult.Refuse(clueProblem);
            if (Pooled && Section(ReferencePool) == null) return GpEditResult.Refuse("this region has no reference pool (0x25022) for the Clue's volume reference");
            clueTemplate = clue.Template ?? Enumerable.Range(0, Count(GpKind.Clue)).FirstOrDefault(i => Word(clues, i * 0x10 + 0x0C) == clue.Class, -1);
            if (clueTemplate < 0 || clueTemplate >= Count(GpKind.Clue))
                return GpEditResult.Refuse($"no Clue of class 0x{clue.Class:X} in this region to copy settings from; it needs a template");
            if (Word(clues, clueTemplate * 0x10 + 0x0C) != clue.Class)
                return GpEditResult.Refuse("the template Clue is of another class");
            clueSettings = PlanSettings(new GpInstance(GpKind.Clue, clueTemplate), clue.Settings,
                i => Word(clues, i.Index * 0x10 + 0x0C) == clue.Class, $"Clue class 0x{clue.Class:X}");
            if (clueSettings.Problem != null) return GpEditResult.Refuse(clueSettings.Problem);
            if (clueSettings.Size == 0) return GpEditResult.Refuse("a Clue needs settings");
        }

        var result = new GpEditResult();
        byte[] record = new byte[0x40];
        var m = spec.Matrix;
        float[] cells = [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43];
        for (int c = 0; c < cells.Length; c++) BinaryPrimitives.WriteSingleBigEndian(record.AsSpan(c * 4), cells[c]);
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(0x3C), (uint)spec.Shape << 24);
        ulong volumeTuid = spec.Tuid ?? NewTuid(random);
        var volume = InsertInstance(GpKind.Volume, spec.Layer, record, [], volumeTuid, spec.Name);
        Done(result, volume, $"volume {spec.Name} ({spec.Shape}) appended to layer {LayerName(spec.Layer)}");
        if (clue == null) return result;

        byte[] clueRecord = new byte[0x10];
        BinaryPrimitives.WriteUInt32BigEndian(clueRecord.AsSpan(0x0C), clue.Class);
        var pointers = new List<(int, GpSection, int)>();
        if (Pooled)
        {
            var pool = Section(ReferencePool)!;
            int entry = AppendPoolEntry(pool, volumeTuid, GpKind.Volume);
            pointers.Add((0x00, pool, entry));
        }
        else BinaryPrimitives.WriteUInt32BigEndian(clueRecord, InlineReference(volume));
        ApplySettings(clueSettings, clueRecord, 0x04, pointers, result);
        var made = InsertInstance(GpKind.Clue, spec.Layer, clueRecord, pointers, clue.Tuid ?? NewTuid(random), clue.Name);
        result.CreatedClue = made;
        result.Changes.Add($"Clue {clue.Name} (class 0x{clue.Class:X}) of volume {spec.Name}, settings from {Name(new GpInstance(GpKind.Clue, clueTemplate))}");
        int references = CountReferenceWords(clueSettings);
        if (references > 0)
            result.Problems.Add($"Clue {clue.Name}: its copied settings name {references} instance(s) as the template's do; check them");
        return result;
    }

    /// <summary>Appends a path: its points (each with its distance along the path) go before the next path's.</summary>
    public GpEditResult AppendPath(GpPathSpec spec, Random? random = null)
    {
        var paths = RecordsOf(GpKind.Path);
        var points = Section(PathPoints);
        if (paths == null || MetadataOf(GpKind.Path) == null || points == null) return GpEditResult.Refuse("this region has no path tables");
        if (CheckCommon(spec.Layer, spec.Name, spec.Tuid) is { } common) return GpEditResult.Refuse(common);
        if (spec.Points.Count is 0 or > 0xFFFF) return GpEditResult.Refuse($"a path holds 1 to 65535 points, not {spec.Points.Count}");

        var (first, n) = LayerRun(GpKind.Path, spec.Layer);
        int position = first + n;
        // The run goes where the path now at that position has its points (back to back in record order), else last.
        int at = position < Count(GpKind.Path) && PointerAt(paths, position * 0x10) is { } next && next.Target == points
            ? next.TargetAt : points.Data.Length;
        var (bytes, length) = PathBytes(spec.Points);
        Splice(points, at, 0, bytes);
        byte[] record = new byte[0x10];
        uint version = Count(GpKind.Path) > 0 ? Word(paths, 4) : 0x07041776u;
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(4), version);
        BinaryPrimitives.WriteSingleBigEndian(record.AsSpan(8), length);
        record[0x0C] = spec.Closed ? (byte)1 : (byte)0;
        record[0x0D] = spec.Kind;
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x0E), (ushort)spec.Points.Count);
        var created = InsertInstance(GpKind.Path, spec.Layer, record, [(0x00, points, at)], spec.Tuid ?? NewTuid(random), spec.Name);
        return Done(new GpEditResult(), created, $"path {spec.Name} ({spec.Points.Count} points) appended to layer {LayerName(spec.Layer)}");
    }

    /// <summary>Appends a controller copied from a template (class, settings) at a new placement.</summary>
    public GpEditResult AppendController(GpControllerSpec spec, Random? random = null)
    {
        var controllers = RecordsOf(GpKind.Controller);
        if (controllers == null || MetadataOf(GpKind.Controller) == null) return GpEditResult.Refuse("this region has no controller tables");
        if (CheckCommon(spec.Layer, spec.Name, spec.Tuid) is { } common) return GpEditResult.Refuse(common);
        if (spec.Template < 0 || spec.Template >= Count(GpKind.Controller)) return GpEditResult.Refuse("the template controller is not in this region");
        var template = new GpInstance(GpKind.Controller, spec.Template);
        var (block, size) = SettingsOf(template);
        uint cls = SettingsClass(block, size);
        var settings = PlanSettings(template, spec.Settings, i => { var (b, s) = SettingsOf(i); return SettingsClass(b, s) == cls; }, $"controller class 0x{cls:X}");
        if (settings.Problem != null) return GpEditResult.Refuse(settings.Problem);

        var result = new GpEditResult();
        byte[] record = controllers.Data.AsSpan(spec.Template * 0x30, 0x30).ToArray();
        WriteVector(record.AsSpan(0x08), spec.Position);
        WriteVector(record.AsSpan(0x14), spec.Rotation);
        WriteVector(record.AsSpan(0x20), spec.Scale);
        var pointers = new List<(int, GpSection, int)>();
        ApplySettings(settings, record, 0x00, pointers, result);
        var created = InsertInstance(GpKind.Controller, spec.Layer, record, pointers, spec.Tuid ?? NewTuid(random), spec.Name);
        int references = CountReferenceWords(settings);
        if (references > 0)
            result.Problems.Add($"controller {spec.Name}: its copied settings name {references} instance(s) as {Name(template)}'s do; check them");
        return Done(result, created, $"controller {spec.Name} (class 0x{cls:X}, from {Name(template)}) appended to layer {LayerName(spec.Layer)}");
    }

    // ---- path points ------------------------------------------------------------------------------------------

    /// <summary>Gives a path new points (any count): its run is replaced, every later run moves, and its length and
    /// each point's distance are recomputed (the distance along the path through the points, the length the last).</summary>
    public GpEditResult SetPathPoints(int path, IReadOnlyList<Vector3> points)
    {
        var paths = RecordsOf(GpKind.Path);
        if (paths == null || path < 0 || path >= Count(GpKind.Path)) return GpEditResult.Refuse("no such path");
        if (points.Count is 0 or > 0xFFFF) return GpEditResult.Refuse($"a path holds 1 to 65535 points, not {points.Count}");
        int record = path * 0x10, old = Half(paths, record + 0x0E);
        var owner = PointerAt(paths, record);
        var section = owner?.Target ?? Section(PathPoints);
        if (section == null) return GpEditResult.Refuse("this region has no path points section");
        if (owner != null && owner.TargetAt + old * 0x10 > section.Data.Length) return GpEditResult.Refuse("the path's points lie outside their section");
        var (bytes, length) = PathBytes(points);
        int at = owner?.TargetAt ?? section.Data.Length;
        Splice(section, at, owner != null ? old * 0x10 : 0, bytes, owner);
        if (owner == null) SetPointer(paths, record, section, at);
        PutFloat(paths, record + 8, length);
        PutHalf(paths, record + 0x0E, (ushort)points.Count);
        var result = new GpEditResult { Applied = true, Created = null };
        result.Changes.Add($"path {Name(new GpInstance(GpKind.Path, path))}: {points.Count} points (was {old})");
        return result;
    }

    private static (byte[] Bytes, float Length) PathBytes(IReadOnlyList<Vector3> points)
    {
        byte[] bytes = new byte[points.Count * 0x10];
        float distance = 0;
        for (int k = 0; k < points.Count; k++)
        {
            if (k > 0) distance += Vector3.Distance(points[k - 1], points[k]);
            WriteVector(bytes.AsSpan(k * 0x10), points[k]);
            BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(k * 0x10 + 0x0C), distance);
        }
        return (bytes, distance);
    }

    // ---- membership -------------------------------------------------------------------------------------------

    /// <summary>The list of a group a member kind goes in: the record field holding its pointer, its count field and
    /// width, and its section. Pods list Mobys (any kind is accepted), Areas paths and volumes, Scents Clues.</summary>
    private (int Pointer, int Count, int CountSize, uint List)? ListOf(GpKind group, GpKind member) => (group, member) switch
    {
        (GpKind.Pod, not GpKind.Squadron) => (0x00, 0x04, 4, PodLists),
        (GpKind.Scent, GpKind.Clue) => (0x00, 0x04, 4, ScentLists),
        (GpKind.Area, GpKind.Path) => (0x00, 0x08, 2, AreaLists),
        (GpKind.Area, GpKind.Volume) => (0x04, 0x0A, 2, AreaLists),
        _ => null,
    };

    /// <summary>The members a group's list names (for <paramref name="member"/>'s list), null where a reference does
    /// not resolve.</summary>
    public List<GpInstance?> Members(GpInstance group, GpKind member)
    {
        var list = new List<GpInstance?>();
        if (ListOf(group.Kind, member) is not { } field || RecordsOf(group.Kind) is not { } records) return list;
        int record = RecordAt(group);
        int n = field.CountSize == 4 ? (int)Word(records, record + field.Count) : Half(records, record + field.Count);
        if (PointerAt(records, record + field.Pointer) is not { } p) return list;
        for (int k = 0; k < n; k++) list.Add(ResolveReference(p.Target, p.TargetAt + 4 * k));
        return list;
    }

    /// <summary>Adds an instance to a Pod (Mobys), an Area (paths, volumes) or a Scent (Clues), at the end of the list.</summary>
    public GpEditResult AddMember(GpInstance group, GpInstance member)
    {
        if (ListOf(group.Kind, member.Kind) is not { } field) return GpEditResult.Refuse($"a {group.Kind} does not list {member.Kind}s");
        var records = RecordsOf(group.Kind);
        if (records == null || group.Index < 0 || group.Index >= Count(group.Kind)) return GpEditResult.Refuse($"no such {group.Kind}");
        if (member.Index < 0 || member.Index >= Count(member.Kind)) return GpEditResult.Refuse($"no such {member.Kind}");
        if (Members(group, member.Kind).Contains(member)) return GpEditResult.Refuse($"{Name(member)} is already in {Name(group)}");
        int record = RecordAt(group);
        int n = field.CountSize == 4 ? (int)Word(records, record + field.Count) : Half(records, record + field.Count);
        var owner = PointerAt(records, record + field.Pointer);
        var section = owner?.Target ?? Section(field.List);
        if (section == null) return GpEditResult.Refuse($"this region has no list section 0x{field.List:X}");
        if (n > 0 && owner == null) return GpEditResult.Refuse($"{Name(group)}'s list has a count but no pointer");
        GpSection? pool = null;
        if (Pooled && (pool = Section(ReferencePool)) == null) return GpEditResult.Refuse("this region has no reference pool (0x25022)");
        if (Pooled && Enumerable.Range(0, Count(member.Kind)).Count(i => Tuid(new GpInstance(member.Kind, i)) == Tuid(member)) > 1)
            return GpEditResult.Refuse($"{Name(member)} shares its TUID with another {member.Kind}; a pool reference (by TUID) could name either");
        byte[] word = new byte[4];
        if (!Pooled)
        {
            var (layer, index) = Handle(member);
            if (layer > 0x3FF || index > 0xFFF) return GpEditResult.Refuse($"{Name(member)}'s handle ({layer}, {index}) does not fit a reference");
            BinaryPrimitives.WriteUInt32BigEndian(word, InlineReference(member));
        }

        int at = owner != null ? owner.TargetAt + 4 * n : section.Data.Length;
        Splice(section, at, 0, word, owner);
        if (owner == null) SetPointer(records, record + field.Pointer, section, at);
        if (pool != null) SetPointer(section, at, pool, AppendPoolEntry(pool, Tuid(member), member.Kind));
        if (field.CountSize == 4) PutWord(records, record + field.Count, (uint)(n + 1));
        else PutHalf(records, record + field.Count, (ushort)(n + 1));
        var result = new GpEditResult { Applied = true };
        result.Changes.Add($"{group.Kind} {Name(group)}: {Name(member)} added ({n + 1} {member.Kind} member(s))");
        return result;
    }

    /// <summary>Removes an instance from a Pod, Area or Scent list (its pool entry too, on All 4 One / Into the Nexus).</summary>
    public GpEditResult RemoveMember(GpInstance group, GpInstance member)
    {
        if (ListOf(group.Kind, member.Kind) is not { } field) return GpEditResult.Refuse($"a {group.Kind} does not list {member.Kind}s");
        var records = RecordsOf(group.Kind);
        if (records == null || group.Index < 0 || group.Index >= Count(group.Kind)) return GpEditResult.Refuse($"no such {group.Kind}");
        int k = Members(group, member.Kind).IndexOf(member);
        if (k < 0) return GpEditResult.Refuse($"{Name(member)} is not in {Name(group)}");
        int record = RecordAt(group);
        int n = field.CountSize == 4 ? (int)Word(records, record + field.Count) : Half(records, record + field.Count);
        var owner = PointerAt(records, record + field.Pointer)!;
        var section = owner.Target;
        int at = owner.TargetAt + 4 * k;
        var entry = Pooled ? PointerAt(section, at) : null;
        if (entry != null && _pointers.Count(p => p.Target == entry.Target && p.TargetAt == entry.TargetAt) > 1) entry = null; // shared: keep it
        string name = Name(member);
        var (poolSection, poolAt) = entry != null ? (entry.Target, entry.TargetAt) : (null, 0);
        Splice(section, at, 4, [], owner);
        if (poolSection != null) Splice(poolSection, poolAt, 0x10, []);
        if (n == 1) ClearPointer(records, record + field.Pointer);
        if (field.CountSize == 4) PutWord(records, record + field.Count, (uint)(n - 1));
        else PutHalf(records, record + field.Count, (ushort)(n - 1));
        var result = new GpEditResult { Applied = true };
        result.Changes.Add($"{group.Kind} {Name(group)}: {name} removed ({n - 1} {member.Kind} member(s))");
        return result;
    }

    // ---- layer change -----------------------------------------------------------------------------------------

    /// <summary>
    /// Moves an instance to another layer: to the END of that layer's run, so no instance of the new layer is renumbered;
    /// the ones after it in its old layer move up by one. Every shift is in <see cref="GpEditResult.Shifts"/>. In A Crack
    /// in Time's form every reference word (Clue volume references, Pod / Area / Scent lists, the words of every settings
    /// block that decode to a moved handle) is rewritten to the new handle first.
    /// </summary>
    public GpEditResult ChangeLayer(GpInstance instance, ushort layer)
    {
        if (instance.Kind == GpKind.Squadron) return GpEditResult.Refuse("Squadrons are not moved");
        var records = RecordsOf(instance.Kind);
        var meta = MetadataOf(instance.Kind);
        if (records == null || meta == null || instance.Index < 0 || instance.Index >= Count(instance.Kind)) return GpEditResult.Refuse($"no such {instance.Kind}");
        if (layer >= LayerCount) return GpEditResult.Refuse($"layer {layer} does not exist (the region has {LayerCount})");
        var (oldLayer, oldIndex) = Handle(instance);
        if (oldLayer == layer) return GpEditResult.Refuse("the instance is already in that layer");
        var (_, oldCount) = LayerRun(instance.Kind, oldLayer);
        var (_, newCount) = LayerRun(instance.Kind, layer);
        if (newCount >= 0x1000) return GpEditResult.Refuse("the new layer already holds 4096 of this kind (a handle's index is 12 bits)");

        var result = new GpEditResult();
        result.Shifts.Add(new GpIndexShift(instance.Kind, Name(instance), Tuid(instance), oldLayer, oldIndex, layer, newCount));
        for (int j = oldIndex + 1; j < oldCount; j++)
        {
            var other = new GpInstance(instance.Kind, instance.Index + (j - oldIndex));
            result.Shifts.Add(new GpIndexShift(instance.Kind, Name(other), Tuid(other), oldLayer, j, oldLayer, j - 1));
        }

        if (!Pooled)
        {
            uint Encode(ushort l, int i) => (uint)((((int)instance.Kind << 10) | l) << 16) | 0xF000u | (uint)i;
            var map = result.Shifts.ToDictionary(s => Encode(s.OldLayer, s.OldIndex), s => Encode(s.NewLayer, s.NewIndex));
            foreach (var (section, at) in ReferenceWords())
                if (map.TryGetValue(Word(section, at), out uint now))
                {
                    PutWord(section, at, now);
                    result.ReferencesRewritten++;
                }
        }

        // Out of the old layer ...
        int size = Info(instance.Kind).RecordSize, from = instance.Index * size;
        byte[] record = records.Data.AsSpan(from, size).ToArray();
        byte[] metadata = meta.Data.AsSpan(instance.Index * 0x10, 0x10).ToArray();
        var recordPointers = _pointers.Where(p => p.Field == records && p.At >= from && p.At < from + size)
            .Select(p => (p.At - from, p.Target, p.TargetAt)).ToList();
        var namePointer = PointerAt(meta, instance.Index * 0x10 + 8);
        Splice(records, from, size, []);
        Splice(meta, instance.Index * 0x10, 0x10, []);
        AdjustLayerCount(instance.Kind, oldLayer, -1, 0);

        // ... to the end of the new one.
        var (first, count) = LayerRun(instance.Kind, layer);
        int position = first + count;
        if (instance.Kind == GpKind.Moby) BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x02), layer);
        BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(0x0C), layer);
        Splice(records, position * size, 0, record);
        Splice(meta, position * 0x10, 0, metadata);
        foreach (var (offset, target, targetAt) in recordPointers) SetPointer(records, position * size + offset, target, targetAt);
        if (namePointer != null) SetPointer(meta, position * 0x10 + 8, namePointer.Target, namePointer.TargetAt);
        AdjustLayerCount(instance.Kind, layer, +1, position);

        result.Applied = true;
        result.Created = new GpInstance(instance.Kind, position);
        result.Changes.Add($"{instance.Kind} {Name(result.Created.Value)}: layer {LayerName(oldLayer)} -> {LayerName(layer)} (handle ({oldLayer}, {oldIndex}) -> ({layer}, {newCount}))");
        if (result.Shifts.Count > 1) result.Changes.Add($"{result.Shifts.Count - 1} later {instance.Kind}(s) of {LayerName(oldLayer)} renumbered");
        if (result.ReferencesRewritten > 0) result.Changes.Add($"{result.ReferencesRewritten} reference word(s) rewritten");
        return result;
    }

    /// <summary>
    /// Every word of the file that may hold an inline reference (A Crack in Time's form): each Clue's +0x00, every entry
    /// of every Pod / Area / Scent list, and the words of every settings block (once per block): a flat block's from
    /// +4 (the first is the class), an inner IGHW's struct (from +4) and extra data, without its own pointer words.
    /// Settings words are a reference only where they decode to one, which is how the rewrite uses them.
    /// </summary>
    public List<(GpSection Section, int At)> ReferenceWords()
    {
        var words = new List<(GpSection, int)>();
        if (RecordsOf(GpKind.Clue) is { } clues)
            for (int i = 0; i < Count(GpKind.Clue); i++) words.Add((clues, i * 0x10));
        foreach (var (group, member) in new[] { (GpKind.Pod, GpKind.Moby), (GpKind.Scent, GpKind.Clue), (GpKind.Area, GpKind.Path), (GpKind.Area, GpKind.Volume) })
        {
            if (RecordsOf(group) is not { } records || ListOf(group, member) is not { } field) continue;
            for (int i = 0; i < Count(group); i++)
            {
                int record = i * 0x10;
                int n = field.CountSize == 4 ? (int)Word(records, record + field.Count) : Half(records, record + field.Count);
                if (PointerAt(records, record + field.Pointer) is { } p)
                    for (int k = 0; k < n; k++) words.Add((p.Target, p.TargetAt + 4 * k));
            }
        }
        var seen = new HashSet<(GpSection, int)>();
        foreach (var kind in new[] { GpKind.Moby, GpKind.Clue, GpKind.Controller })
            for (int i = 0; i < Count(kind); i++)
            {
                var (block, size) = SettingsOf(new GpInstance(kind, i));
                if (block == null || size < 8 || !seen.Add((block.Target, block.TargetAt))) continue;
                if (block.TargetAt + size > block.Target.Data.Length) continue;
                foreach (int w in SettingsWords(block.Target.Data.AsSpan(block.TargetAt, size)))
                    words.Add((block.Target, block.TargetAt + w));
            }
        return words;
    }

    /// <summary>The block-relative offsets of a settings block's words that may be references.</summary>
    private static List<int> SettingsWords(ReadOnlySpan<byte> block)
    {
        var list = new List<int>();
        var sections = InnerSections(block);
        if (sections.Count == 0)
        {
            for (int w = 4; w + 4 <= block.Length; w += 4) list.Add(w);
            return list;
        }
        var pointers = InnerFixups(block);
        foreach (var (id, offset, length) in sections)
        {
            if (id == ReferencePool || offset + length > block.Length) continue;
            for (int w = offset + (id == 0x2501C ? 4 : 0); w + 4 <= offset + length; w += 4)
                if (!pointers.Contains(w) && (w & 3) == 0) list.Add(w);
        }
        return list;
    }

    // ---- shared steps -----------------------------------------------------------------------------------------

    private string? CheckCommon(ushort layer, string name, ulong? tuid)
    {
        if (layer >= LayerCount) return $"layer {layer} does not exist (the region has {LayerCount})";
        if (name.Contains('\0')) return "a name cannot hold a NUL";
        if (Encoding.Latin1.GetByteCount(name) != name.Length || name.Any(c => c > 0xFF)) return "a name must be Latin-1";
        if (Section(Names) == null) return "this region has no name table (0x11300)";
        if (tuid is { } t && AllTuids().Contains(t)) return $"TUID 0x{t:X16} is already used in this region";
        return null;
    }

    /// <summary>Where a new instance's settings come from, decided before anything is written.</summary>
    private readonly record struct SettingsPlan(string? Problem, GpPointer? Source, int Size, bool Copy);

    private SettingsPlan PlanSettings(GpInstance template, GpSettingsSource source, Func<GpInstance, bool> sameClass, string what)
    {
        switch (source)
        {
            case GpSettingsSource.None:
                return template.Kind == GpKind.Moby ? new(null, null, 0, false) : new($"a {template.Kind} needs settings", null, 0, false);
            case GpSettingsSource.ClassDefault:
            case GpSettingsSource.ClassDefaultCopy:
                for (int i = 0; i < Count(template.Kind); i++)
                {
                    var other = new GpInstance(template.Kind, i);
                    if (!sameClass(other)) continue;
                    var (b, s) = SettingsOf(other);
                    if (b is not { Target: { Id: SharedSettings, IsTail: false } }) continue;
                    if (source == GpSettingsSource.ClassDefault) return new(null, b, s, false);
                    return Section(PrivateSettings) == null ? new("no private settings section", null, 0, false) : new(null, b, s, true);
                }
                return new($"no instance of {what} in this region shares a class-default settings block (0x25030); use a template's", null, 0, false);
            default:
                var (block, size) = SettingsOf(template);
                if (block == null || size == 0) return new(null, null, 0, false);
                if (block.TargetAt + size > block.Target.Data.Length) return new("the template's settings lie outside their section", null, 0, false);
                if (block.Target is { Id: SharedSettings, IsTail: false }) return new(null, block, size, false);
                if (block.Target is { Id: PrivateSettings, IsTail: false })
                    return Section(PrivateSettings) == null ? new("no private settings section", null, 0, false) : new(null, block, size, true);
                return new($"the template's settings are in section {block.Target}, not 0x25020 / 0x25030", null, 0, false);
        }
    }

    /// <summary>Points a new record's settings field (offset, size words at <paramref name="field"/>) at the planned
    /// block: a private one copied to the end of 0x25020 (self-relocating: its pointers are block-relative).</summary>
    private void ApplySettings(SettingsPlan plan, byte[] record, int field, List<(int, GpSection, int)> pointers, GpEditResult result)
    {
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(field), 0);
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(field + 4), (uint)plan.Size);
        if (plan.Source == null) return;
        if (!plan.Copy) { pointers.Add((field, plan.Source.Target, plan.Source.TargetAt)); return; }
        var section = Section(PrivateSettings)!;
        byte[] copy = plan.Source.Target.Data.AsSpan(plan.Source.TargetAt, plan.Size).ToArray();
        int at = section.Data.Length;
        int padded = (copy.Length + 3) & ~3;
        Splice(section, at, 0, padded == copy.Length ? copy : [.. copy, .. new byte[padded - copy.Length]]);
        pointers.Add((field, section, at));
        result.Changes.Add($"settings block copied ({plan.Size} bytes)");
    }

    private int CountReferenceWords(SettingsPlan plan)
    {
        if (plan.Source == null || Pooled) return 0;
        var block = plan.Source.Target.Data.AsSpan(plan.Source.TargetAt, plan.Size);
        int n = 0;
        foreach (int w in SettingsWords(block))
            if (DecodeInline(Word(block, w)) != null) n++;
        return n;
    }

    private int AppendPoolEntry(GpSection pool, ulong tuid, GpKind kind)
    {
        byte[] entry = new byte[0x10];
        BinaryPrimitives.WriteUInt64BigEndian(entry, tuid);
        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(8), (uint)kind);
        int at = pool.Data.Length;
        Splice(pool, at, 0, entry);
        return at;
    }

    /// <summary>
    /// Inserts a record and its metadata at the end of the layer's run of the kind, with the name appended to 0x11300.
    /// <paramref name="pointers"/> are the record's pointer fields (offset in the record, target); every other pointer
    /// field of the record is zeroed (a template's old values are stale). The layer's count goes up, and a kind new to
    /// the layer gets its two layer pointers.
    /// </summary>
    private GpInstance InsertInstance(GpKind kind, ushort layer, byte[] record, List<(int Offset, GpSection Target, int TargetAt)> pointers,
        ulong tuid, string name)
    {
        var info = Info(kind);
        var records = RecordsOf(kind)!;
        var meta = MetadataOf(kind)!;
        foreach (int field in PointerFields(kind, Pooled))
            if (!pointers.Any(p => p.Offset == field)) BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(field), 0);

        var names = Section(Names)!;
        int nameAt = names.Data.Length;
        Splice(names, nameAt, 0, [.. Encoding.Latin1.GetBytes(name), 0]);

        var (first, count) = LayerRun(kind, layer);
        int position = first + count;
        Splice(records, position * info.RecordSize, 0, record);
        byte[] metadata = new byte[0x10];
        BinaryPrimitives.WriteUInt64BigEndian(metadata, tuid);
        BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(0x0C), layer);
        Splice(meta, position * 0x10, 0, metadata);
        foreach (var (offset, target, targetAt) in pointers) SetPointer(records, position * info.RecordSize + offset, target, targetAt);
        SetPointer(meta, position * 0x10 + 8, names, nameAt);
        AdjustLayerCount(kind, layer, +1, position);
        return new GpInstance(kind, position);
    }

    /// <summary>A layer's count of a kind up or down by one; a kind the layer gains gets its record and metadata
    /// pointers (to <paramref name="position"/>), one it loses has them cleared.</summary>
    private void AdjustLayerCount(GpKind kind, int layer, int delta, int position)
    {
        var info = Info(kind);
        var layers = Section(LayerTable)!;
        var layerMeta = Section(LayerMetaTable)!;
        int pair = layer * layers.Element + 0x40 + 8 * info.LayerSlot;
        int metaPointer = layer * layerMeta.Element + 4 * info.LayerSlot;
        uint count = (uint)((int)Word(layers, pair + 4) + delta);
        PutWord(layers, pair + 4, count);
        if (delta > 0 && count == 1)
        {
            SetPointer(layers, pair, RecordsOf(kind)!, position * info.RecordSize);
            SetPointer(layerMeta, metaPointer, MetadataOf(kind)!, position * 0x10);
        }
        else if (delta < 0 && count == 0)
        {
            ClearPointer(layers, pair);
            ClearPointer(layerMeta, metaPointer);
        }
    }

    private GpEditResult Done(GpEditResult result, GpInstance created, string change)
    {
        result.Applied = true;
        result.Created ??= created;
        var (layer, index) = Handle(created);
        result.Changes.Insert(0, $"{change}: handle ({layer}, {index}), TUID 0x{Tuid(created):X16}");
        return result;
    }

    private static void WriteVector(Span<byte> at, Vector3 v)
    {
        BinaryPrimitives.WriteSingleBigEndian(at, v.X);
        BinaryPrimitives.WriteSingleBigEndian(at[4..], v.Y);
        BinaryPrimitives.WriteSingleBigEndian(at[8..], v.Z);
    }

    /// <summary>The class TUIDs a region loads, in order (region.dat 0x1C600, 8 bytes each), or null.</summary>
    public static List<ulong>? RegionClasses(byte[]? regionDat)
    {
        if (regionDat == null || regionDat.Length < 0x20 || Word(regionDat, 0) != Magic) return null;
        var span = regionDat.AsSpan();
        bool v11 = Word(span, 4) == Version11;
        int n = (int)Word(span, 8), table = v11 ? 0x20 : 0x10;
        for (int k = 0; k < n; k++)
        {
            int e = table + 16 * k;
            if (Word(span, e) != 0x1C600) continue;
            uint offset = Word(span, e + 4), countWord = Word(span, e + 8), length = Word(span, e + 12);
            int count = (countWord & 0x10000000) != 0 ? (int)(countWord & 0xFFFFFF) : (int)(length / 8);
            var list = new List<ulong>(count);
            for (int i = 0; i < count && offset + 8 * (i + 1) <= regionDat.Length; i++)
                list.Add(BinaryPrimitives.ReadUInt64BigEndian(span[(int)(offset + 8 * i)..]));
            return list;
        }
        return null;
    }
}
