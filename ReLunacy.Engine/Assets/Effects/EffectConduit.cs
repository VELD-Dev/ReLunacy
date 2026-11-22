// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
namespace ReLunacy.Engine.Assets.Effects;

/// <summary>The six effect subsystems of fxconduit_packed.dat, in file order.</summary>
public enum ConduitSubsystem
{
    Visual = 0,
    Sound = 1,
    Dialogue = 2,
    Rumble = 3,
    PostEffect = 4,
    Decal = 5,
}

/// <summary>
/// One conduit record (44 bytes, packed): "when an object of engine class C raises event E, play one of
/// these specifications" - what Lua's <c>trigger_effect_event(EventType.TELEPORTED, moby)</c> looks up.
/// +0x00 is the engine class id (PROVEN: 26 636 of 26 650 QForce records and 27 404 of 27 416 All 4 One
/// records are declared classes, the rest 0xFFFFFFFF = any class) and +0x04 the EventType (PROVEN: 69 030
/// of 69 030 records of QForce, All 4 One and Into the Nexus, every subsystem, are values of the game's
/// retail eventtypes.lua; 729 = TELEPORTED, 64 = CHARGING, 10162 = RELOADED). +0x20 points at an
/// EffectsConduit_EffectSpecificationChancePair list (u32 count at -4, 4-byte entries
/// { u16 specification index, u8 chance, u8 0 }; chance 100 on 744 of 750 visual pairs) - PROVEN: every
/// index of every pair on 33 levels is inside its subsystem's specification table. +0x0C is a value of the game's
/// Tags enum (<see cref="Tag"/>; PROVEN on 10 023 of 10 023 QForce visual records, see <see cref="EffectNames"/>).
/// +0x08 and +0x18 (u16 pairs) are OPEN.
/// </summary>
public sealed record ConduitRecord(
    ConduitSubsystem Subsystem,
    int Index,
    uint ClassId,
    uint EventType,
    uint Word08,
    uint Word0C,
    uint Word18,
    float Word24,
    uint Word28,
    IReadOnlyList<(int Spec, int Chance)> Specifications)
{
    /// <summary>+0x0C: the Tags value that selects this record among the class's records for the event - NONE (0) for
    /// most, HERO_JUMP / SKIN_* / UPGRADED... for variants, and a dynamic tag (10 000+) naming a placed effect, which is
    /// what an FxController's +0x18 holds.</summary>
    public uint Tag => Word0C;
}

/// <summary>
/// EffectsConduit_VisualEffectSpecification (56 bytes, the EBOOT class size). +0x30/+0x34 hold the
/// effect as <c>game-wide effect id + 9999</c> - STRONG: 639 of 640 on acid_refinery_pvp and 99.4-100 % on
/// every QForce, All 4 One and Into the Nexus level resolve through effect.dat's 0x500 table. +0x24/+0x28
/// are a second, optional pair in the same encoding (77 of 640 non-zero). Everything else is OPEN.
/// </summary>
public sealed record VisualEffectSpecification(int Index, uint NameHash, uint[] Words)
{
    public const int Size = 56;
    /// <summary>Tools of Destruction's visual specification: 36 bytes, the effect id + 9999 at +0x1C and an optional
    /// second one at +0x20 (STRONG: 313 of 313 +0x1C and 268 of the 268 non-zero +0x20 of metropolis resolve).</summary>
    public const int OldEngineSize = 36;
    public const int EffectIdBias = 9999;

    /// <summary>A Crack in Time's visual specification: 40 bytes, the effect id + 9999 at +0x1C and again at +0x20
    /// (equal on 9 611 of 9 795; docs/reverse/acit_vfx.md section 4). STRONG.</summary>
    public const int CrackInTimeSize = 40;

    public bool OldEngine { get; init; }
    public bool CrackInTime { get; init; }

    private static int Decode(uint word) => word == 0 || word == 0xFFFFFFFF ? -1 : (int)word - EffectIdBias;

    public int EffectId => Decode(Words[(OldEngine || CrackInTime ? 0x1C : 0x30) / 4]);
    public int EffectIdAlt => OldEngine ? -1 : Decode(Words[(CrackInTime ? 0x20 : 0x34) / 4]);
    public int SecondaryEffectId => CrackInTime ? -1 : Decode(Words[(OldEngine ? 0x20 : 0x24) / 4]);
    public int SecondaryEffectIdAlt => OldEngine || CrackInTime ? -1 : Decode(Words[0x28 / 4]);

    public IEnumerable<int> EffectIds()
    {
        foreach (int id in new[] { EffectId, EffectIdAlt, SecondaryEffectId, SecondaryEffectIdAlt })
            if (id >= 0) yield return id;
    }
}

/// <summary>
/// fxconduit_packed.dat: which objects play which effects. Same IGHW 0.2 container as effect.dat.
/// <code>
///   0x12000  { ptr -> 0x12300, u32 n, ptr subsystem[6] }
///   0x12300  n x u64 model TUIDs (the models this level can attach effects to)
///   subsystem descriptor, 36-48 bytes, common prefix (PROVEN by the array extents on 33 levels):
///     +0x00 ptr u32[records]        per-record link word (OPEN)
///     +0x04 ptr records (44 bytes)  +0x08 u32 record count
///     +0x0C ptr u32[specs] name hashes
///     +0x10 ptr ptr[specs] (count at -4)   +0x14 u32 spec count
///     +0x18 ptr, +0x1C ptr hash table { u32 buckets, u32 keys, bucket[] = record or -1 }, +0x20 ptr u32[specs]
///   specification sizes = the EBOOT class sizes: visual 56, sound 68, dialogue 96, rumble 92, decal 120
/// </code>
/// A Crack in Time keeps the same header with another descriptor (<see cref="ReadCrackInTime"/>).
/// </summary>
public sealed class EffectConduit
{
    public required IReadOnlyList<ulong> ModelTuids { get; init; }
    public required IReadOnlyList<ConduitRecord> VisualRecords { get; init; }
    public required IReadOnlyList<VisualEffectSpecification> VisualSpecifications { get; init; }
    public required IReadOnlyDictionary<ConduitSubsystem, (int Records, int Specifications)> Counts { get; init; }
    /// <summary>Read with A Crack in Time's descriptor layout.</summary>
    public bool CrackInTime { get; init; }

    private readonly Dictionary<int, List<(ConduitRecord Record, VisualEffectSpecification Spec, int Chance)>> _byEffect = [];

    /// <summary>
    /// The visual (record, specification, chance) an object of <paramref name="classId"/> plays on
    /// <paramref name="eventType"/> with record key <paramref name="key"/> (record +0x0C; 0 for most
    /// records, the instance's own selector for FxController - its +0x18 matches +0x0C on 1 426 of 1 426
    /// QForce instances). Records of class 0xFFFFFFFF (any class) match too.
    /// </summary>
    public IReadOnlyList<(ConduitRecord Record, VisualEffectSpecification Spec, int Chance)> FindVisual(uint classId, uint eventType, uint key)
    {
        var found = new List<(ConduitRecord, VisualEffectSpecification, int)>();
        foreach (ConduitRecord record in VisualRecords)
        {
            if (record.EventType != eventType || record.Word0C != key) continue;
            if (record.ClassId != classId && record.ClassId != 0xFFFFFFFF) continue;
            foreach (var (spec, chance) in record.Specifications)
                if ((uint)spec < (uint)VisualSpecifications.Count)
                    found.Add((record, VisualSpecifications[spec], chance));
        }
        return found;
    }

    /// <summary>Every (record, specification) that can play the given game-wide effect id.</summary>
    public IReadOnlyList<(ConduitRecord Record, VisualEffectSpecification Spec, int Chance)> UsesOf(int effectId) =>
        _byEffect.TryGetValue(effectId, out var list) ? list : [];

    public static EffectConduit? TryRead(byte[] data, out string? error)
    {
        error = null;
        try { return Read(data); }
        catch (Exception e) { error = e.Message; return null; }
    }

    public static EffectConduit Read(byte[] data)
    {
        EffectChunkFile file = EffectChunkFile.Parse(data);
        EffectChunkFile.Chunk header = file.ChunksWithId(0x12000).Cast<EffectChunkFile.Chunk?>().FirstOrDefault()
            ?? throw new InvalidDataException("fxconduit_packed.dat has no 0x12000 header.");
        if (header.Length == 0x14) return ReadOldEngine(file, header);
        if (header.Length < 0x20)
            throw new InvalidDataException($"0x12000 is {header.Length} bytes; the QForce layout has 0x20.");
        // QForce's descriptor +0x14 is the specification count, A Crack in Time's a pointer to its hash table.
        int firstDescriptor = file.Pointer(header.Offset + 8);
        if (firstDescriptor != 0 && file.IsPointer(firstDescriptor + 0x14)) return ReadCrackInTime(file, header);

        int tuidList = file.Pointer(header.Offset);
        int modelCount = (int)file.U32(header.Offset + 4);
        var tuids = new ulong[Math.Max(0, modelCount)];
        for (int i = 0; i < tuids.Length && tuidList != 0; i++)
            tuids[i] = ((ulong)file.U32(tuidList + 8 * i) << 32) | file.U32(tuidList + 8 * i + 4);

        var counts = new Dictionary<ConduitSubsystem, (int, int)>();
        var visualRecords = new List<ConduitRecord>();
        var visualSpecs = new List<VisualEffectSpecification>();

        for (int s = 0; s < 6; s++)
        {
            int descriptor = file.Pointer(header.Offset + 8 + 4 * s);
            if (descriptor == 0) { counts[(ConduitSubsystem)s] = (0, 0); continue; }
            int records = file.Pointer(descriptor + 4);
            int recordCount = (int)file.U32(descriptor + 8);
            int specPointers = file.Pointer(descriptor + 0x10);
            int specCount = (int)file.U32(descriptor + 0x14);
            counts[(ConduitSubsystem)s] = (recordCount, specCount);
            if (s != (int)ConduitSubsystem.Visual) continue;

            int hashes = file.Pointer(descriptor + 0x0C);
            for (int i = 0; i < specCount && specPointers != 0; i++)
            {
                int at = file.Pointer(specPointers + 4 * i);
                if (at == 0) continue;
                var words = new uint[VisualEffectSpecification.Size / 4];
                for (int w = 0; w < words.Length; w++) words[w] = file.U32(at + 4 * w);
                visualSpecs.Add(new VisualEffectSpecification(i, hashes != 0 ? file.U32(hashes + 4 * i) : 0, words));
            }

            for (int r = 0; r < recordCount && records != 0; r++)
            {
                int at = records + 44 * r;
                var pairs = new List<(int, int)>();
                int list = file.Pointer(at + 0x20);
                if (list != 0)
                {
                    int n = (int)file.U32(list - 4);
                    for (int k = 0; k < n && k < 256; k++)
                    {
                        uint w = file.U32(list + 4 * k);
                        pairs.Add(((int)(w >> 16), (int)((w >> 8) & 0xFF)));
                    }
                }
                visualRecords.Add(new ConduitRecord(ConduitSubsystem.Visual, r,
                    file.U32(at), file.U32(at + 4), file.U32(at + 8), file.U32(at + 0x0C), file.U32(at + 0x18),
                    file.F32(at + 0x24), file.U32(at + 0x28), pairs));
            }
        }

        return Index(new EffectConduit
        {
            ModelTuids = tuids,
            VisualRecords = visualRecords,
            VisualSpecifications = visualSpecs,
            Counts = counts,
        });
    }

    /// <summary>
    /// Tools of Destruction's fxconduit (Docs/reverse/TOD_EFFECTS_2026-10-09.md): the same records and specification tables,
    /// with five subsystems and no model list in the header.
    /// <code>
    ///   0x12000  5 x ptr subsystem descriptor (subsystem 0 = visual)
    ///   descriptor  +0x00 ptr records (44 bytes)   +0x04 u32 record count   +0x08 ptr ptr[specs] (count at -4)
    ///               +0x0C, +0x10, +0x14 tables (OPEN)
    ///   record   +0x00 engine class   +0x04 EventType (the game's eventtypes.lua: 1 = ACTIVE)   +0x0C Tags value
    ///            +0x10 ptr (OPEN)   +0x20 ptr { u16 specification, u16 OPEN } list (count at -4)
    ///   0x12300  u64 model TUIDs, not referenced by the header
    /// </code>
    /// STRONG: every FxController (class 384) record of metropolis names, through its pairs, a specification whose +0x1C
    /// resolves into effect.dat (19 of 19). The pair's second half is not a chance (0, 1, 14, 122...): every pair is
    /// read as certain.
    /// </summary>
    private static EffectConduit ReadOldEngine(EffectChunkFile file, EffectChunkFile.Chunk header)
    {
        var counts = new Dictionary<ConduitSubsystem, (int, int)>();
        var visualRecords = new List<ConduitRecord>();
        var visualSpecs = new List<VisualEffectSpecification>();
        for (int s = 0; s < 5; s++)
        {
            int descriptor = file.Pointer(header.Offset + 4 * s);
            if (descriptor == 0) { counts[(ConduitSubsystem)s] = (0, 0); continue; }
            int records = file.Pointer(descriptor);
            int recordCount = (int)file.U32(descriptor + 4);
            int specPointers = file.Pointer(descriptor + 8);
            int specCount = specPointers != 0 ? (int)file.U32(specPointers - 4) : 0;
            counts[(ConduitSubsystem)s] = (recordCount, specCount);
            if (s != (int)ConduitSubsystem.Visual) continue;

            for (int i = 0; i < specCount; i++)
            {
                int at = file.Pointer(specPointers + 4 * i);
                if (at == 0) continue;
                var words = new uint[VisualEffectSpecification.OldEngineSize / 4];
                for (int w = 0; w < words.Length; w++) words[w] = file.U32(at + 4 * w);
                visualSpecs.Add(new VisualEffectSpecification(i, 0, words) { OldEngine = true });
            }
            for (int r = 0; r < recordCount && records != 0; r++)
            {
                int at = records + 44 * r;
                var pairs = new List<(int, int)>();
                int list = file.Pointer(at + 0x20);
                if (list != 0)
                {
                    int n = (int)file.U32(list - 4);
                    for (int k = 0; k < n && k < 256; k++) pairs.Add(((int)(file.U32(list + 4 * k) >> 16), 100));
                }
                visualRecords.Add(new ConduitRecord(ConduitSubsystem.Visual, r,
                    file.U32(at), file.U32(at + 4), file.U32(at + 8), file.U32(at + 0x0C), file.U32(at + 0x18),
                    file.F32(at + 0x24), file.U32(at + 0x28), pairs));
            }
        }

        var tuids = new List<ulong>();
        if (file.ChunksWithId(0x12300).Cast<EffectChunkFile.Chunk?>().FirstOrDefault() is { } models)
            for (int i = 0; i + 8 <= models.Length; i += 8)
                tuids.Add(((ulong)file.U32(models.Offset + i) << 32) | file.U32(models.Offset + i + 4));

        return Index(new EffectConduit
        {
            ModelTuids = tuids,
            VisualRecords = visualRecords,
            VisualSpecifications = visualSpecs,
            Counts = counts,
        });
    }

    /// <summary>
    /// A Crack in Time's fxconduit (docs/reverse/acit_vfx.md section 4): QForce's header and records, another descriptor.
    /// <code>
    ///   descriptor  +0x00 ptr u32[records]   +0x04 ptr records (44 bytes)   +0x08 u32 record count
    ///               +0x0C ptr ptr[specs] (count at -4)   +0x10 explicit-spec id table   +0x14 hash table   +0x18 OPEN
    ///   record      +0x00 class (-1 any)   +0x04 EventType (0 = ACTIVE)   +0x0C Tags key   +0x20 ptr chance pairs
    /// </code>
    /// PROVEN: every non-zero FxController key of the 22 levels resolves through it to an effect of effect.dat.
    /// </summary>
    private static EffectConduit ReadCrackInTime(EffectChunkFile file, EffectChunkFile.Chunk header)
    {
        int tuidList = file.Pointer(header.Offset);
        int modelCount = (int)file.U32(header.Offset + 4);
        var tuids = new ulong[Math.Max(0, modelCount)];
        for (int i = 0; i < tuids.Length && tuidList != 0; i++)
            tuids[i] = ((ulong)file.U32(tuidList + 8 * i) << 32) | file.U32(tuidList + 8 * i + 4);

        var counts = new Dictionary<ConduitSubsystem, (int, int)>();
        var visualRecords = new List<ConduitRecord>();
        var visualSpecs = new List<VisualEffectSpecification>();
        for (int s = 0; s < 6; s++)
        {
            int descriptor = file.Pointer(header.Offset + 8 + 4 * s);
            if (descriptor == 0) { counts[(ConduitSubsystem)s] = (0, 0); continue; }
            int records = file.Pointer(descriptor + 4);
            int recordCount = (int)file.U32(descriptor + 8);
            int specPointers = file.Pointer(descriptor + 0x0C);
            int specCount = specPointers != 0 ? (int)file.U32(specPointers - 4) : 0;
            counts[(ConduitSubsystem)s] = (recordCount, specCount);
            if (s != (int)ConduitSubsystem.Visual) continue;

            for (int i = 0; i < specCount; i++)
            {
                int at = file.Pointer(specPointers + 4 * i);
                var words = new uint[VisualEffectSpecification.CrackInTimeSize / 4];
                if (at != 0)
                    for (int w = 0; w < words.Length; w++) words[w] = file.U32(at + 4 * w);
                // Kept even when null, so a pair's index stays the table's.
                visualSpecs.Add(new VisualEffectSpecification(i, 0, words) { CrackInTime = true });
            }
            for (int r = 0; r < recordCount && records != 0; r++)
            {
                int at = records + 44 * r;
                var pairs = new List<(int, int)>();
                int list = file.Pointer(at + 0x20);
                if (list != 0)
                {
                    int n = (int)file.U32(list - 4);
                    for (int k = 0; k < n && k < 256; k++)
                    {
                        uint w = file.U32(list + 4 * k);
                        pairs.Add(((int)(w >> 16), (int)((w >> 8) & 0xFF)));
                    }
                }
                visualRecords.Add(new ConduitRecord(ConduitSubsystem.Visual, r,
                    file.U32(at), file.U32(at + 4), file.U32(at + 8), file.U32(at + 0x0C), file.U32(at + 0x18),
                    file.F32(at + 0x24), file.U32(at + 0x28), pairs));
            }
        }

        return Index(new EffectConduit
        {
            ModelTuids = tuids,
            VisualRecords = visualRecords,
            VisualSpecifications = visualSpecs,
            Counts = counts,
            CrackInTime = true,
        });
    }

    private static EffectConduit Index(EffectConduit conduit)
    {
        IReadOnlyList<ConduitRecord> visualRecords = conduit.VisualRecords;
        IReadOnlyList<VisualEffectSpecification> visualSpecs = conduit.VisualSpecifications;
        foreach (ConduitRecord record in visualRecords)
        {
            foreach (var (spec, chance) in record.Specifications)
            {
                if ((uint)spec >= (uint)visualSpecs.Count) continue;
                VisualEffectSpecification s = visualSpecs[spec];
                foreach (int id in s.EffectIds().Distinct())
                {
                    if (!conduit._byEffect.TryGetValue(id, out var list))
                        conduit._byEffect[id] = list = [];
                    if (!list.Any(e => ReferenceEquals(e.Record, record) && e.Spec.Index == s.Index))
                        list.Add((record, s, chance));
                }
            }
        }
        return conduit;
    }
}
