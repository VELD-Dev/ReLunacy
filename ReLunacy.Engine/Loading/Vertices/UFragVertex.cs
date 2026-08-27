using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Vertices;

public record struct UFragVertex
{
    public const uint ID = 0x6000, OldID = 0x9000;
    public const uint Size = 0x18;

    public (short, short, short) position;
    public short unk;
    public (Half, Half) UVs;

    /// <summary>LIGHTMAP UVs - atlas coordinates into the zone's baked light colour / direction
    /// textures (sections 0x5400 / 0x5410). HALF-FLOATS, same as the base UVs above.
    ///
    /// This is not inferred, it is read off the hardware: RPCS3's captured DrawParametersBuffer
    /// (set 0, binding 2) for a metropolis UFrag draw describes the vertex stream attribute by
    /// attribute, and 2498 draws in that frame carry exactly this shape -
    ///     attr0  stride=24 offset=+0   SINT16 x4      -> position + <see cref="unk"/>
    ///     attr1  stride=24 offset=+8   SFLOAT16 x2    -> <see cref="UVs"/>
    ///     attr2  stride=24 offset=+12  SFLOAT16 x2    -> THIS FIELD
    ///     attr3  stride=24 offset=+16  CMP 11:11:10   -> <see cref="normal"/>
    ///     attr4  stride=24 offset=+20  CMP 11:11:10   -> <see cref="tangent"/>
    /// all five in one non-volatile stream with swap_bytes set (big-endian source), which is this
    /// struct field for field. The game's vertex program routes attr2 straight into tc0.zw, and its
    /// fragment program samples BOTH baked atlases - tex4 (0x5400) and tex14 (0x5410) - at tc0.zw.
    /// So attr2 is the lightmap UV, and RSX type 3 is a half float (elem size 2, scale 1.0).
    ///
    /// An earlier reading here as normalised u16 was wrong, and wrong in a way that looked fine:
    /// u16/65535 is unconditionally inside [0,1], so "100% in range" confirmed nothing. Measured
    /// over the 1377 lightmapped UFrags of metropolis, the two decodes separate cleanly -
    ///     as u16/65535 : median island extent 0.008 x 0.009, atlas coverage  2.5%
    ///     as half      : median island extent 0.148 x 0.180, atlas coverage 88.6%
    /// A packed atlas is nearly fully covered by construction, so 2.5% alone falsifies the u16
    /// reading: it was addressing islands about two texels wide on a 256x256 atlas, which is why
    /// terrain sampled a single near-arbitrary pixel (often a black gutter) instead of its bake.
    /// The triangle-level control is what makes this conclusive rather than merely better. Raster
    /// the real indexed triangles into each atlas and measure how often two different UFrags claim
    /// the same texel: 0.3% mean, exactly 0.0% on 9 of the 23 atlases, at 61.7% mean coverage. 1377
    /// independently unwrapped fragments do not pack into 23 shared atlases without colliding by
    /// accident, and it also rules out a missing per-UFrag sub-rect - there is no room left for one.
    /// (Do not use bounding-box overlap for this. Roughly 60 UFrags per atlas at a median bbox of
    /// 0.148 x 0.180 sums to about 160% of the atlas, so bbox overlap reads ~75% no matter whether
    /// the decode is right. It measures the boxes, not the packing.)
    ///
    /// Halves land 98.3% inside [0,1], and the 1.7% is NOT sampler edge bleed - it is bimodal. Per
    /// UFrag, 1354 are wholly in range and 23 are wholly out, with nothing in between. Those 23
    /// carry one constant on every single vertex, u = -0.0 (half 0x8000) and v = 18.344 (half
    /// 0x4C96), across 2 atlases and 7 different shaders. That is a never-written UV2 slot, not a
    /// coordinate: whatever the addressing mode, it resolves to an arbitrary atlas edge pixel. So
    /// those 23 are expected to render with a wrong flat tint and are candidates for being treated
    /// as unlit outright - deliberately NOT done here, since it is a rendering policy rather than a
    /// decode fact, and silently hiding them would also hide the next thing that produces them.
    ///
    /// The reason the half decode was originally dismissed as "nonsense, values like 420.75" is
    /// that it was measured across the whole of vertices.dat section 0x9000 at stride 24. That
    /// section is one shared blob holding moby and tie vertices too, at other strides - so most of
    /// those reads were straddling unrelated records. Restricted to the byte ranges the UFrag
    /// metadata actually points at, the same decode is 100% finite. The control that would have
    /// caught it earlier is cheap: the base UVs at +8 are known-good halves, and they score 86%
    /// finite over the whole blob versus 100% over real UFrag ranges. Any decode test on this
    /// section has to be scoped by vertexOffset/vertexCount first.</summary>
    public (float, float) UVs2;

    public uint normal;
    public uint tangent;

    public UFragVertex(StreamHelper sh)
    {
        // Offsets below are relative to THIS vertex's own record start, not the file's -
        // StreamHelper's ReadXxx(offset)/Seek(offset) all seek absolutely from the start of the
        // stream, so the record's actual position has to be added in. Without this, every vertex
        // past the first in a UFrag reads from the wrong place in the file entirely (same bug
        // class as UFragMetadata - see its constructor comment).
        uint recordBase = (uint)sh.Offset;

        position.Item1 = sh.ReadInt16(recordBase + 0x00);
        position.Item2 = sh.ReadInt16(recordBase + 0x02);
        position.Item3 = sh.ReadInt16(recordBase + 0x04);
        unk = sh.ReadInt16(recordBase + 0x06);
        sh.Seek(recordBase + 0x08);
        UVs.Item1 = sh.ReadHalf();
        UVs.Item2 = sh.ReadHalf();
        // Halves, matching the RSX attribute descriptor - see the field comment.
        UVs2.Item1 = (float)sh.ReadHalf();
        UVs2.Item2 = (float)sh.ReadHalf();
        normal = sh.ReadUInt32(recordBase + 0x10);
        tangent = sh.ReadUInt32(recordBase + 0x14);
    }
}
