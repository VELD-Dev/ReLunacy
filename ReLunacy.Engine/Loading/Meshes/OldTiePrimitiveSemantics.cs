namespace ReLunacy.Engine.Loading.Meshes;

/// <summary>
/// Evidence-backed accessors for the old-engine 0x3300 Tie primitive record.
///
/// TieMesh intentionally keeps several historical field names because the same struct is also used by
/// newer-engine readers. These accessors expose the old ToD byte layout without forcing a risky shared
/// rename. The values here are serialized-file semantics unless a property explicitly says runtime.
/// </summary>
public static class OldTiePrimitiveSemantics
{
    public const int NativeDrawRangeCapacity = 4;

    /// <summary>
    /// 0x3300 +0x06. Index, in Half2 elements, into the compact secondary UV stream stored after the
    /// prototype's primary VertexFormat0 block. When +0x0E is non-zero, retail corpus data advances this
    /// index by exactly verticesCount for every secondary-UV primitive.
    /// </summary>
    public static ushort GetOldSecondaryUvIndex(this in TieMesh primitive) => primitive.Unk1;

    /// <summary>
    /// 0x3300 +0x0D. Number of immediately following primitives in the same (material,+0x0E) batch,
    /// capped at 16 and never crossing the owning 0x3400 prototype boundary.
    /// </summary>
    public static byte GetOldBatchLookahead(this in TieMesh primitive) =>
        (byte)(primitive.Unk2 >> 32);

    /// <summary>
    /// 0x3300 +0x0E. Native gate for the secondary Half2 UV stream.
    /// </summary>
    public static byte GetOldSecondaryUvGateRaw(this in TieMesh primitive) =>
        (byte)(primitive.Unk2 >> 24);

    public static bool UsesOldSecondaryUv(this in TieMesh primitive) =>
        primitive.GetOldSecondaryUvGateRaw() != 0;

    /// <summary>
    /// 0x3300 +0x0F. Number of usable native geometry draw/LOD ranges, 1..4 in the audited retail
    /// corpus. This is geometry LOD and is separate from shader LOD/reduction.
    /// </summary>
    public static byte GetOldDrawRangeCount(this in TieMesh primitive) =>
        (byte)(primitive.Unk2 >> 16);

    /// <summary>0x3300 +0x10, range0 relative index offset.</summary>
    public static ushort GetOldRange0RelativeIndexOffset(this in TieMesh primitive) =>
        (ushort)primitive.Unk2;

    /// <summary>
    /// Serialized 0x3300 +0x2D. Retail ToD files in the audited corpus store zero here; the loader
    /// later replaces this byte with material 0x5000 +0x11. It is therefore not an independent
    /// authored render-mode byte even though runtime consumers read it as one after load.
    /// </summary>
    public static byte GetOldSerializedRuntimeMaterialModeByte(this in TieMesh primitive) =>
        primitive.oldAuthoredRaw2D;

    /// <summary>
    /// Serialized 0x3300 +0x2E. Retail ToD files in the audited corpus store zero here; the loader
    /// later replaces it with material 0x5000 +0x13.
    /// </summary>
    public static byte GetOldSerializedRuntimeMaterialFlagsByte(this in TieMesh primitive) =>
        primitive.oldAuthoredRaw2E;

    /// <summary>
    /// 0x3300 +0x2F. Native render-path routing byte. The low three bits are directly tested by the
    /// canonical Tie renderer before the primitive enters one render path. Individual bit meanings
    /// remain OPEN; keep the raw byte authoritative.
    /// </summary>
    public static byte GetOldRenderRouteFlags(this in TieMesh primitive) =>
        primitive.oldAuthoredRaw2F;
}
