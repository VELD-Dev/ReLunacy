using System.Buffers.Binary;
using System.Numerics;

namespace ReLunacy.Engine.Assets.Geometry;

/// <summary>
/// A primitive's animated UVs: Moby / TIE section 0x5C00, one 0x60-byte record per animated frag (the
/// ToD-era "VertAnim"). The game runs it on the SPU job "VertAnim" (EBOOT `0x00DBD000`, registered by
/// `0x00685A20`): each frame `0x006861F8` advances the record and the job rewrites the frag's half UVs
/// (vertex +0x08, or +0x10 in the skinned 0x1C format) from the base UVs kept in 0x5C01:
/// <code>
/// u' = cos a (u - px) - sin a (v - py) + px + U       a += rotation, every frame
/// v' = sin a (u - px) + cos a (v - py) + py + V       U += scroll.x dt, V += scroll.y dt (seconds), wrapped to [-1, 1]
/// </code>
/// Both move once per game frame, never in between: a scroll of 50 UV/s at 60 Hz is a 5/6 jump a frame (the
/// flipbooks of imperial fight fest), not a smear. The editor therefore samples them at whole frames of
/// <see cref="FrameRate"/> (Docs/reverse/tod_catchup_2026-10-10/01_textures_animees_vitesse.md).
/// Record: +0x00 u16 tag (high nibble: 0 TIE, 1 UFrag, 2 Moby, 3 skinned Moby) | flags, +0x02 vertex count
/// (rounded up to 4), +0x08 the frag's vertex offset (Moby: byte offset in 0xE200 = frag +0x04; TIE: the
/// primitive's vertex index), +0x0C payload offset in 0x5C01, +0x20 / +0x24 pivot, +0x30 / +0x34 scroll,
/// +0x38 rotation per frame (radians). PROVEN on jungle_ruins_pvp: every record's +0x08 is a frag's
/// offset and its count that frag's vertex count rounded up to 4. UFrag records (zones.dat, tag 0x1000)
/// name their UFrag by its index at +0x04 instead (+0x08 is relative to a block the editor does not
/// keep): PROVEN on the 280 retail records, whose counts all match that UFrag's vertex count
/// (Docs/reverse/QFORCE_FILE_STRUCTURE_2026-10-08.md §7).
/// </summary>
public readonly record struct UvAnimation(Vector2 Pivot, Vector2 ScrollPerSecond, float RotationPerFrame, float FrameRate = UvAnimation.FramesPerSecond)
{
    public const uint SectionId = 0x5C00;
    public const int RecordSize = 0x60;

    /// <summary>The new engine's frame rate, which the rotation is counted in (INFERRED: QForce renders at 30 Hz).</summary>
    public const float FramesPerSecond = 30f;

    public float RotationPerSecond => RotationPerFrame * FrameRate;

    /// <summary>Reads every record of a 0x5C00 section, keyed by its +0x08 (the frag's vertex offset or
    /// index), or by its +0x04 UFrag index for a zone's section.</summary>
    public static Dictionary<uint, UvAnimation> ReadAll(ReadOnlySpan<byte> section, bool byUFragIndex = false)
    {
        var result = new Dictionary<uint, UvAnimation>();
        for (int at = 0; at + RecordSize <= section.Length; at += RecordSize)
        {
            ReadOnlySpan<byte> r = section.Slice(at, RecordSize);
            var animation = new UvAnimation(
                new Vector2(BinaryPrimitives.ReadSingleBigEndian(r[0x20..]), BinaryPrimitives.ReadSingleBigEndian(r[0x24..])),
                new Vector2(BinaryPrimitives.ReadSingleBigEndian(r[0x30..]), BinaryPrimitives.ReadSingleBigEndian(r[0x34..])),
                BinaryPrimitives.ReadSingleBigEndian(r[0x38..]));
            if (!float.IsFinite(animation.RotationPerFrame) || !float.IsFinite(animation.ScrollPerSecond.X) || !float.IsFinite(animation.ScrollPerSecond.Y))
                continue;
            result.TryAdd(byUFragIndex ? BinaryPrimitives.ReadUInt16BigEndian(r[0x04..]) : BinaryPrimitives.ReadUInt32BigEndian(r[0x08..]), animation);
        }
        return result;
    }

    /// <summary>
    /// Tools of Destruction: one level-wide main.dat 0x5C00. A 0x20-byte header holds three counts - UFrag,
    /// TIE, Moby records, stored in that order - and 0x40-byte records follow: +0x00 u16 tag (high nibble 1
    /// UFrag, 0 TIE, 2 Moby, 3 skinned Moby), +0x02 vertex count rounded up to 4, +0x04 u16 the owner (UFrag
    /// = 0x6200 ordinal, TIE = 0x3400 class, Moby = 0xD100 class), +0x08 the frag (TIE: the primitive's
    /// vertex index; Moby: the mesh's vertex byte offset; UFrag: relative to a block the editor does not
    /// keep), +0x0C payload offset in 0x5C01, +0x10 / +0x14 scroll per second, +0x18 rotation per frame,
    /// then the run-time state (+0x1C angle, +0x20 / +0x30 the 2 x 2 rotation rows, +0x2C / +0x3C the
    /// offsets). PROVEN on Rykan V: every TIE record's +0x08 / count is a primitive's first vertex and
    /// vertex count (rounded to 4), every Moby record's a mesh's vertex offset and count.
    ///
    /// The updater `0x005C0718` (EBOOT NPUA80965), per frame and record: angle += +0x18; rows =
    /// (cos, sin) / (-sin, cos); offset += dt x scroll, wrapped to [-1, 1]. No pivot: the rotation is about
    /// UV (0, 0). Read here as the new engine's form: pivot 0, rotation negated (the editor turns by
    /// (cos, -sin) / (sin, cos) - INFERRED, the SPU job applying the rows is not read), one step per frame
    /// at 60 Hz. dt is in seconds (PROVEN: `0x00580524` caps the same frame dt at 1/15 s); 60 Hz is
    /// STRONG: the EBOOT's `-60fps` / `-lockfps` / `-capfps` switches (`0x004C3490..0x004C355C`) write the
    /// frame-time pair 0x1074DD08 +4 / +8, and the large scrolls are whole 60ths a frame (5/6, 1/6, 1/12).
    /// The wrap is a step of 1 (`0x005C0CA8..0x005C0CE4`: above 1 minus 1, below -1 plus 1), i.e. fract.
    /// </summary>
    public static OldEngineUvAnimations? ReadOldEngine(ReadOnlySpan<byte> section)
    {
        if (section.Length < 0x20)
            return null;
        uint ufrags = BinaryPrimitives.ReadUInt32BigEndian(section), ties = BinaryPrimitives.ReadUInt32BigEndian(section[4..]),
            mobys = BinaryPrimitives.ReadUInt32BigEndian(section[8..]);
        if (0x20 + ((long)ufrags + ties + mobys) * OldRecordSize > section.Length)
            return null;
        var result = new OldEngineUvAnimations();
        for (int k = 0; k < ufrags + ties + mobys; k++)
        {
            ReadOnlySpan<byte> r = section.Slice(0x20 + k * OldRecordSize, OldRecordSize);
            float rotation = BinaryPrimitives.ReadSingleBigEndian(r[0x18..]);
            var animation = new UvAnimation(
                Vector2.Zero,
                new Vector2(BinaryPrimitives.ReadSingleBigEndian(r[0x10..]), BinaryPrimitives.ReadSingleBigEndian(r[0x14..])),
                -rotation,
                OldEngineFramesPerSecond);
            if (!float.IsFinite(animation.RotationPerFrame) || !float.IsFinite(animation.ScrollPerSecond.X) || !float.IsFinite(animation.ScrollPerSecond.Y))
                continue;
            if (animation.ScrollPerSecond == Vector2.Zero && animation.RotationPerFrame == 0f)
                continue;
            uint owner = BinaryPrimitives.ReadUInt16BigEndian(r[0x04..]);
            uint frag = BinaryPrimitives.ReadUInt32BigEndian(r[0x08..]);
            switch (BinaryPrimitives.ReadUInt16BigEndian(r) >> 12)
            {
                case 1: result.UFrags.TryAdd(owner, animation); break;
                case 0: result.TieClass(owner).TryAdd(frag, animation); break;
                case 2 or 3: result.MobyClass(owner).TryAdd(frag, animation); break;
            }
        }
        return result;
    }

    public const int OldRecordSize = 0x40;

    /// <summary>Tools of Destruction's frame rate, which its rotation is counted in (STRONG, see <see cref="ReadOldEngine"/>).</summary>
    public const float OldEngineFramesPerSecond = 60f;
}

/// <summary>Tools of Destruction's level-wide animated UVs (main.dat 0x5C00), by owner.</summary>
public sealed class OldEngineUvAnimations
{
    private static readonly IReadOnlyDictionary<uint, UvAnimation> None = new Dictionary<uint, UvAnimation>();

    /// <summary>By UFrag (0x6200 ordinal).</summary>
    public Dictionary<uint, UvAnimation> UFrags { get; } = [];
    private readonly Dictionary<uint, Dictionary<uint, UvAnimation>> _ties = [];
    private readonly Dictionary<uint, Dictionary<uint, UvAnimation>> _mobys = [];

    internal Dictionary<uint, UvAnimation> TieClass(uint index) =>
        _ties.TryGetValue(index, out var d) ? d : _ties[index] = [];

    internal Dictionary<uint, UvAnimation> MobyClass(uint index) =>
        _mobys.TryGetValue(index, out var d) ? d : _mobys[index] = [];

    /// <summary>A TIE class's records keyed by its primitives' first vertex index.</summary>
    public IReadOnlyDictionary<uint, UvAnimation> ForTie(uint classIndex) => _ties.TryGetValue(classIndex, out var d) ? d : None;

    /// <summary>A Moby class's records keyed by its meshes' vertex byte offset.</summary>
    public IReadOnlyDictionary<uint, UvAnimation> ForMoby(uint classIndex) => _mobys.TryGetValue(classIndex, out var d) ? d : None;

    /// <summary>Reads main.dat's 0x5C00, leaving the stream where it was; null when the level has none.</summary>
    public static OldEngineUvAnimations? Read(Loading.IO.IGFile main)
    {
        var section = main.QuerySection(UvAnimation.SectionId);
        if (section.id != UvAnimation.SectionId)
            return null;
        uint bytes = section.count <= 1 ? section.length : section.count * section.length;
        if (bytes < 0x20 || section.offset + (long)bytes > main.sh.BaseStream.Length)
            return null;
        long position = main.sh.BaseStream.Position;
        try
        {
            main.sh.Seek(section.offset);
            return UvAnimation.ReadOldEngine(main.sh.ReadBytes((int)bytes));
        }
        finally
        {
            main.sh.BaseStream.Position = position;
        }
    }
}
