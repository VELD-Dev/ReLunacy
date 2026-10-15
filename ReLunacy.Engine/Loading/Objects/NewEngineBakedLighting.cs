using System.Numerics;
using ReLunacy.Engine.Loading.Meshes;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// How a new-engine (QForce / All 4 One) TIE or UFrag draw gets its baked lighting - read in the QForce
/// EBOOT, not inferred from the data. Addresses are the QForce EBOOT's; the full trace is in
/// Docs/reverse/NEW_ENGINE_BAKED_LIGHTING_EBOOT_2026-09-27.md.
///
/// WHICH PRIMITIVE, WHICH UVs (PROVEN). The TIE renderer (0x00746E90) binds vertex attribute 4 - the
/// lightmap UV the TIE vertex programs route to TEX0.zw - at tieUvArray + primitive[+0x06] * 4
/// (0x007474E8, tieUvArray being the resource's +0x18 copied at 0x0073DFC0), and binds the placement's
/// lightmap pair only for primitives whose +0x0E bit 0 is set (0x006E18BC; stream at 0x0073EE64). A
/// primitive whose bit is clear takes its placement's per-vertex colours instead, from
/// mainDataOffset + (verticesIndex - primitive[+0x06]) * 4 (0x0073F5D8). So +0x06 is the primitive's
/// first entry in the packed UV array, the same field the old engine uses.
///
/// HOW BRIGHT (PROVEN). The lightmapped vertex programs write TEX1.z = c[4].x (RNM, ids 46/47/49/50 of
/// the VP table at 0x00F2D1B0) or c[4].y (flat, ids 44/45), and the fragment programs multiply the
/// lightmap by it. c[4] is uploaded per draw:
///   TIE    0x0073E9C0, from the placement record's +0x64 / +0x66 / +0x68 (copied at 0x0073E02C);
///   UFrag  0x0074DC98, from the record's +0x68 / +0x6A (z = x);
///   c[4] = (a, b, c, c / 3) * <see cref="ScaleUnit"/>.
/// Retail, 12 QForce levels: c[4].x is non-zero on 53,448 / 53,448 lit TIE placements (median 1.74,
/// p10 0.68, p90 5.47, max 48.6) and +0x64/+0x66 are zero on the 1,585 unlit ones; UFrag c[4].x has
/// median 3.52 (p90 14.0) and one value per lightmap atlas (128 atlases, 0 conflicting records). The
/// editor used a fixed 3 for both: a tenth of the lit TIEs drew 4.4x too bright or more, a tenth of
/// the terrain 4.7x too dark or more.
///
/// The other factor on the lit colour, TEX1.x = fract(|position.w| * c[3].z) * c[20].z, is ~1: c[3] is
/// the global (1/255, 256, 1/32768, 1/128) uploaded with c[0..2] at 0x00687978, retail |position.w| is
/// 0x7Fxx, and c[20] is a distance fade that is 1 at rest (0x00745B04, 0x00747090).
/// </summary>
public static class NewEngineBakedLighting
{
    /// <summary>0x3B800080, the float the code multiplies each u16 by (pool 0x0073E9B8): 256 / 65535.</summary>
    public static readonly float ScaleUnit = BitConverter.Int32BitsToSingle(0x3B800080);

    /// <summary>0x3EAAAAAD, the code's 1/3 (pool 0x0073E9BC): c[4].w = c[4].z * this.</summary>
    public static readonly float OneThird = BitConverter.Int32BitsToSingle(0x3EAAAAAD);

    public static bool IsLightmappedPrimitive(in TieMesh primitive) =>
        (primitive.GetOldSecondaryUvGateRaw() & 1) != 0;

    public static int LightmapUvIndex(in TieMesh primitive) => primitive.GetOldSecondaryUvIndex();

    /// <summary>The per-draw vertex constant c[4] built from the record's three u16 scales.</summary>
    public static Vector4 VertexConstant(ushort x, ushort y, ushort z)
    {
        float scaledZ = z * ScaleUnit;
        return new Vector4(x * ScaleUnit, y * ScaleUnit, scaledZ, scaledZ * OneThird);
    }
}
