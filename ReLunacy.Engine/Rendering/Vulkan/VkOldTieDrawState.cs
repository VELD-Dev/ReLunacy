using ReLunacy.Engine.Assets.Geometry;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Proof-bounded per-draw snapshot of an old-engine Tie primitive's authored shader-use state.
///
/// This deliberately contains SOURCE metadata only. In particular, <see cref="EffectiveUseMask"/>
/// is not a retail callback52 shellFeatureKey and no Full/Reduced shader is selected from it here.
/// The terminal retail route remains separately unresolved until the producer chain is closed.
/// </summary>
public readonly record struct VkOldTieDrawState(
    bool IsOldEngine,
    bool SourceHadExplicitUse,
    byte SerializedUseMask,
    byte MaterialFeatureMask,
    byte EffectiveUseMask,
    byte SerializedRaw2F,
    byte SerializedRenderMode,
    byte EffectiveRenderMode,
    byte SerializedRenderFlags,
    byte EffectiveRenderFlags,
    float DistanceFadeScale = 0f,
    float DistanceFadeBias = 0f,
    int VertexBase = -1)
{
    public static VkOldTieDrawState From(OldTieShaderUse? source)
    {
        if (source is not { } value)
            return default;

        return new VkOldTieDrawState(
            IsOldEngine: true,
            SourceHadExplicitUse: true,
            SerializedUseMask: value.SerializedUseMask,
            MaterialFeatureMask: value.MaterialFeatureMask,
            EffectiveUseMask: value.EffectiveUseMask,
            SerializedRaw2F: value.SerializedRaw2F,
            SerializedRenderMode: value.SerializedRenderMode,
            EffectiveRenderMode: value.EffectiveRenderMode,
            SerializedRenderFlags: value.SerializedRenderFlags,
            EffectiveRenderFlags: value.EffectiveRenderFlags,
            DistanceFadeScale: value.DistanceFadeScale,
            DistanceFadeBias: value.DistanceFadeBias,
            VertexBase: value.VertexBase);
    }
}
