using System.Buffers.Binary;
using System.Numerics;

namespace ReLunacy.Engine.Assets.Geometry;

/// <summary>
/// A primitive's animated UVs: Moby / Tie section 0x5C00, one 0x60-byte record per animated frag (the
/// ToD-era "VertAnim"). The game runs it on the SPU job "VertAnim" (EBOOT `0x00DBD000`, registered by
/// `0x00685A20`): each frame `0x006861F8` advances the record and the job rewrites the frag's half UVs
/// (vertex +0x08, or +0x10 in the skinned 0x1C format) from the base UVs kept in 0x5C01:
/// <code>
/// u' = cos a (u - px) - sin a (v - py) + px + U       a += rotation, every frame
/// v' = sin a (u - px) + cos a (v - py) + py + V       U += scroll.x dt, V += scroll.y dt (seconds), wrapped to [-1, 1]
/// </code>
/// The game moves both once per game frame, never in between (Docs/reverse/tod_catchup_2026-10-10/
/// 01_textures_animees_vitesse.md). The editor does NOT reproduce that stepping: it advances them
/// continuously on its own renderer clock, so they run at the right SPEED whatever rate either side
/// draws at. Quantising to the game's rate instead made every animation only as smooth as that rate
/// and, whenever the assumed rate was wrong, run at the wrong speed outright. The cost is that a
/// large scroll the game showed as a flipbook (50 UV/s at 60 Hz is a 5/6 jump a frame) smears here
/// rather than jumping.
/// Record: +0x00 u16 tag (high nibble: 0 Tie, 1 UFrag, 2 Moby, 3 skinned Moby) | flags, +0x02 vertex count
/// (rounded up to 4), +0x08 the frag's vertex offset (Moby: byte offset in 0xE200 = frag +0x04; Tie: the
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

    /// <summary>The record as QForce, All 4 One and Into the Nexus write it.</summary>
    public const int RecordSize = 0x60;

    /// <summary>
    /// The record as A Crack in Time writes it: 0x50 long, and NOT merely the 0x60 one trimmed -
    /// it leaves out the pivot and the 99999 pair that sit at +0x20..+0x2F, so everything after
    /// them moves down by 0x10.
    ///
    /// Established by profiling every offset of every record and asking which ones VARY. An
    /// authored field differs between records; run-time state written at rest does not:
    ///
    ///   QForce, 14 records      +0x20/+0x24 pivot 0 . +0x28/+0x2C both 99999 . +0x34 varies
    ///                           (0.2, 0.3) . state 1.0 at +0x40 and +0x54
    ///   ACiT, 245 records       +0x20 varies (0, 0.1, 1, 0.02) . +0x24 varies (1.5, 0.5, 0, 2)
    ///                           . +0x28 varies (0, -0.0524, 0.096) . state 1.0 at +0x30 and +0x44
    ///
    /// The two state markers keep their 0x14 spacing in both and move together by 0x10, which
    /// places the shift independently of the authored fields. ACiT also has no pivot, as Tools of
    /// Destruction has none: the rotation is about UV (0, 0).
    ///
    /// Read at the wrong layout, +0x30 is the resting rotation row - a constant 1.0 - so every
    /// animation scrolled at exactly 1 UV/s, all at the same speed and far too fast, while the
    /// mis-stride pointed the records at the wrong frags.
    /// </summary>
    public static readonly UvAnimationLayout CrackInTimeLayout =
        new(0x50, PivotOffset: -1, ScrollOffset: 0x20, RotationOffset: 0x28, FramesPerSecond: CrackInTimeFramesPerSecond);

    /// <summary>The layout for everything else on the new engine.</summary>
    public static readonly UvAnimationLayout DefaultLayout = new(RecordSize, PivotOffset: 0x20, ScrollOffset: 0x30, RotationOffset: 0x38);

    /// <summary>The layout a game writes this section with.</summary>
    public static UvAnimationLayout LayoutFor(bool crackInTime) => crackInTime ? CrackInTimeLayout : DefaultLayout;

    /// <summary>QForce's frame rate, which its rotation is counted in (INFERRED: QForce renders at 30 Hz).</summary>
    public const float FramesPerSecond = 30f;

    /// <summary>A Crack in Time's frame rate. Not 30: it is not a 30 Hz title, which is why every one
    /// of its rotations ran at half speed while it shared QForce's rate (REPORTED by the user,
    /// 2026-11-02; no ACiT EBOOT to hand to confirm it the way Tools of Destruction's was).</summary>
    public const float CrackInTimeFramesPerSecond = 60f;

    /// <summary>
    /// The rotation in radians per second.
    ///
    /// The file counts rotation per GAME frame, so the game's rate is needed to read that as a
    /// speed - but only as a unit conversion. The editor does NOT step the animation at that rate:
    /// it runs on its own renderer clock, so the speed is right whatever either side renders at.
    /// </summary>
    public float RotationPerSecond => RotationPerFrame * FrameRate;

    /// <summary>Reads every record of a 0x5C00 section, keyed by its +0x08 (the frag's vertex offset or
    /// index), or by its +0x04 UFrag index for a zone's section.</summary>
    public static Dictionary<uint, UvAnimation> ReadAll(ReadOnlySpan<byte> section, bool byUFragIndex = false,
                                                       UvAnimationLayout? layout = null)
    {
        var fields = layout ?? DefaultLayout;
        var result = new Dictionary<uint, UvAnimation>();
        for (int at = 0; at + fields.RecordSize <= section.Length; at += fields.RecordSize)
        {
            ReadOnlySpan<byte> r = section.Slice(at, fields.RecordSize);
            var pivot = fields.PivotOffset < 0
                ? Vector2.Zero
                : new Vector2(BinaryPrimitives.ReadSingleBigEndian(r[fields.PivotOffset..]),
                              BinaryPrimitives.ReadSingleBigEndian(r[(fields.PivotOffset + 4)..]));
            var animation = new UvAnimation(
                pivot,
                new Vector2(BinaryPrimitives.ReadSingleBigEndian(r[fields.ScrollOffset..]),
                            BinaryPrimitives.ReadSingleBigEndian(r[(fields.ScrollOffset + 4)..])),
                BinaryPrimitives.ReadSingleBigEndian(r[fields.RotationOffset..]),
                fields.FramesPerSecond);
            if (!float.IsFinite(animation.RotationPerFrame) || !float.IsFinite(animation.ScrollPerSecond.X) || !float.IsFinite(animation.ScrollPerSecond.Y))
                continue;
            result.TryAdd(byUFragIndex ? BinaryPrimitives.ReadUInt16BigEndian(r[0x04..]) : BinaryPrimitives.ReadUInt32BigEndian(r[0x08..]), animation);
        }
        return result;
    }

    /// <summary>
    /// Tools of Destruction: one level-wide main.dat 0x5C00. A 0x20-byte header holds three counts - UFrag,
    /// Tie, Moby records, stored in that order - and 0x40-byte records follow: +0x00 u16 tag (high nibble 1
    /// UFrag, 0 Tie, 2 Moby, 3 skinned Moby), +0x02 vertex count rounded up to 4, +0x04 u16 the owner (UFrag
    /// = 0x6200 ordinal, Tie = 0x3400 class, Moby = 0xD100 class), +0x08 the frag (Tie: the primitive's
    /// vertex index; Moby: the mesh's vertex byte offset; UFrag: relative to a block the editor does not
    /// keep), +0x0C payload offset in 0x5C01, +0x10 / +0x14 scroll per second, +0x18 rotation per frame,
    /// then the run-time state (+0x1C angle, +0x20 / +0x30 the 2 x 2 rotation rows, +0x2C / +0x3C the
    /// offsets). PROVEN on Rykan V: every Tie record's +0x08 / count is a primitive's first vertex and
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

/// <summary>
/// Where a 0x5C00 record keeps its fields. The new engine writes two shapes of this record, and
/// they differ by more than length, so a size on its own is not enough to read one.
/// </summary>
/// <param name="PivotOffset">-1 where the game has no pivot and turns about UV (0, 0).</param>
/// <param name="FramesPerSecond">The rate the GAME advanced this record at. Used only to turn
/// rotation-per-frame into rotation-per-second: it is a unit of the data, not a cap on how often
/// the editor may draw. See the note on <see cref="UvAnimation.RotationPerSecond"/>.</param>
public readonly record struct UvAnimationLayout(int RecordSize, int PivotOffset, int ScrollOffset, int RotationOffset,
                                                float FramesPerSecond = UvAnimation.FramesPerSecond);

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

    /// <summary>A Tie class's records keyed by its primitives' first vertex index.</summary>
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
