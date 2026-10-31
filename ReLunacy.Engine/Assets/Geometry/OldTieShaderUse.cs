namespace ReLunacy.Engine.Assets.Geometry;

/// <summary>
/// Lossless old-engine Tie shader-use snapshot carried per primitive.
///
/// PROVEN retail loader staging (canonical ToD EBOOT around 0x4E21E0..0x4E220C):
///   runtime TieFrag+0x2C = serialized TieFrag+0x2C & ShaderMetadataOld+0x10
///   runtime TieFrag+0x2D = ShaderMetadataOld+0x11
///   runtime TieFrag+0x2E = ShaderMetadataOld+0x13
///
/// Callback52.arg0 is proven to be this same TieFrag pointer with a low-nibble tag.
/// Callback52.arg2.low8 is proven to be shellFeatureKey.
///
/// CLOSED 2026-09-18 (runtime census at the retail shell, Tools/Reverse/evidence/TOD_TIE_Route_Key_2026-09-18.md):
/// shellFeatureKey == runtime TieFrag+0x2C (= EffectiveUseMask), except that bit 15 of the per-instance secondary
/// word clears feature bit 0x08. See TodRetailTieShaderCatalog.DeriveShellFeatureKey. The secondary word itself is
/// produced off the PPU and is not reconstructed by the editor.
/// </summary>
public readonly record struct OldTieShaderUse(
    byte SerializedUseMask,
    byte SerializedRenderMode,
    byte SerializedRenderFlags,
    byte SerializedRaw2F,
    byte MaterialFeatureMask,
    byte MaterialRenderMode,
    byte MaterialRenderFlags,
    float DistanceFadeScale = 0f,
    float DistanceFadeBias = 0f,
    int VertexBase = -1)
{
    // VertexBase: the primitive's first vertex in its Tie (TieFrag +0x04), which indexes the
    // placement's prelight words (IPlacedInstance.OldEnginePrelight).
    // DistanceFadeScale / DistanceFadeBias: TieFrag +0x30 / +0x34, the vertex shader's c[16].xy
    // (TieMesh.oldDistanceFadeScale).
    public byte EffectiveUseMask => (byte)(SerializedUseMask & MaterialFeatureMask);
    public byte EffectiveRenderMode => MaterialRenderMode;
    public byte EffectiveRenderFlags => MaterialRenderFlags;

    /// <summary>
    /// Full table index for the near/default case (secondary bit 15 clear): key | 0x20. Use
    /// TodRetailTieShaderCatalog.DeriveShellFeatureKey when the secondary word is known.
    /// </summary>
    public byte NormalFullTableCandidate => (byte)(EffectiveUseMask | 0x20);

    public bool SerializedModeMatchesMaterial => SerializedRenderMode == MaterialRenderMode;
    public bool SerializedFlagsMatchMaterial => SerializedRenderFlags == MaterialRenderFlags;
}
