using NeoVeldrid;
using ReLunacy.Engine.Assets.Textures;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Proof-bounded translation from the serialized NV4097 sampler words carried by
/// <see cref="OldTextureDescriptorState"/> to NeoVeldrid's portable sampler model.
///
/// This deliberately refuses states that the host abstraction cannot represent exactly. Refusal is
/// useful reverse-engineering information: it prevents a portable-API approximation from silently
/// becoming "retail behaviour" and identifies cases that eventually need the raw Vulkan renderer.
/// </summary>
public static class OldTextureSamplerTranslation
{
    public readonly record struct Result(
        bool Exact,
        SamplerDescription Description,
        string? Reason);

    /// <summary>
    /// Attempts an exact host representation for the subset of RSX state that affects ordinary
    /// colour-texture sampling. CONTROL1 remap and ADDRESS signed/unsigned/gamma conversion happen
    /// elsewhere in the pixel/shader path and are intentionally not duplicated here.
    /// </summary>
    public static Result TryTranslate(in OldTextureDescriptorState state)
    {
        if (!TryAddressMode(state.WrapS, out SamplerAddressMode u, out string? addressReason))
            return Fail(addressReason!);
        if (!TryAddressMode(state.WrapT, out SamplerAddressMode v, out addressReason))
            return Fail(addressReason!);
        if (!TryAddressMode(state.WrapR, out SamplerAddressMode w, out addressReason))
            return Fail(addressReason!);

        if (!TryFilter(state.MinFilter, state.MagFilter, state.MaxAnisotropy, state.MipmapCount,
                out SamplerFilter filter, out uint maximumAnisotropy, out string? filterReason))
            return Fail(filterReason!);

        // NV4097 stores 4.8 fixed-point values. NeoVeldrid 1.2.x exposes integral LOD limits and
        // integral bias, so fractional values cannot be represented without changing the abstraction
        // or using the raw Vulkan path.
        if (!TryExactInteger(state.MinLod, out uint minLod))
            return Fail($"fractional RSX min LOD {state.MinLod:R} is not representable by NeoVeldrid uint MinimumLod");
        if (!TryExactInteger(state.MaxLod, out uint maxLod))
            return Fail($"fractional RSX max LOD {state.MaxLod:R} is not representable by NeoVeldrid uint MaximumLod");
        if (!TryExactSignedInteger(state.LodBias, out int lodBias))
            return Fail($"fractional RSX LOD bias {state.LodBias:R} is not representable by NeoVeldrid int LodBias");

        // FILTER convolution = 1 (CELL_GCM_TEXTURE_CONVOLUTION_QUINCUNX) is libgcm's default and is set
        // on 98,171 / 98,171 retail descriptors (2026-09-16 corpus census). It is inert for ordinary
        // formats. Refusing it refused every texture in the game, so only a different mode is refused.
        if (state.ConvolutionFilter > 1)
            return Fail($"RSX convolution filter {state.ConvolutionFilter} has no exact NeoVeldrid sampler equivalent");

        // ADDRESS anisotropic bias (2 on 8,664 retail 0x5200 descriptors) is a quality hint that cannot
        // change which texel is addressed. It is not represented, and it no longer discards authored
        // wrap/filter/LOD state that is otherwise exact.

        // Border addressing requires the separate native border-colour state, which the serialized
        // 0x20 descriptor does not contain. TryAddressMode refuses BORDER before reaching here.
        var desc = new SamplerDescription(
            u,
            v,
            w,
            filter,
            comparisonKind: null,
            maximumAnisotropy,
            minimumLod: minLod,
            maximumLod: maxLod,
            lodBias,
            SamplerBorderColor.TransparentBlack);

        return new Result(true, desc, null);
    }

    private static Result Fail(string reason) => new(false, default, reason);

    /// <summary>
    /// Exact mappings supported by the portable host API:
    /// 1 WRAP -> Wrap, 2 MIRROR -> Mirror, 3 CLAMP_TO_EDGE -> Clamp.
    ///
    /// RSX BORDER needs separately authored border colour. RSX CLAMP and the three MIRROR_ONCE modes
    /// have semantics distinct from Clamp/Mirror and are therefore refused rather than approximated.
    /// </summary>
    private static bool TryAddressMode(byte rsx, out SamplerAddressMode mode, out string? reason)
    {
        switch (rsx)
        {
            case 1:
                mode = SamplerAddressMode.Wrap;
                reason = null;
                return true;
            case 2:
                mode = SamplerAddressMode.Mirror;
                reason = null;
                return true;
            case 3:
                mode = SamplerAddressMode.Clamp;
                reason = null;
                return true;
            case 4:
                mode = default;
                reason = "RSX BORDER needs the separate native border-colour register, absent from the 0x20 descriptor";
                return false;
            case 5:
                mode = default;
                reason = "RSX CLAMP is distinct from CLAMP_TO_EDGE; NeoVeldrid exposes only one Clamp mode";
                return false;
            case 6:
            case 7:
            case 8:
                mode = default;
                reason = $"RSX MIRROR_ONCE address mode {rsx} has no NeoVeldrid equivalent";
                return false;
            default:
                mode = default;
                reason = $"unknown/invalid RSX address mode {rsx}";
                return false;
        }
    }

    private static bool TryFilter(
        byte min,
        byte mag,
        byte maxAnisoEnum,
        int mipmapCount,
        out SamplerFilter filter,
        out uint maximumAnisotropy,
        out string? reason)
    {
        filter = default;
        maximumAnisotropy = 0;
        reason = null;

        // RSX minification enums:
        // 1 nearest base, 2 linear base, 3 nearest/nearest, 4 linear/nearest,
        // 5 nearest/linear, 6 linear/linear, 7 convolution.
        // NeoVeldrid has no explicit base-level-only minifier. When the authored texture has exactly
        // one mip, however, a point mip selector can only ever select level 0, so modes 1/2 are
        // exactly representable rather than an approximation.
        bool minLinear;
        bool mipLinear;
        switch (min)
        {
            case 3: minLinear = false; mipLinear = false; break;
            case 4: minLinear = true;  mipLinear = false; break;
            case 5: minLinear = false; mipLinear = true;  break;
            case 6: minLinear = true;  mipLinear = true;  break;
            case 1:
            case 2:
                if (mipmapCount != 1)
                {
                    reason = $"RSX base-level-only min filter {min} needs level-0-only sampling, but descriptor has {mipmapCount} mip(s)";
                    return false;
                }
                minLinear = min == 2;
                mipLinear = false;
                break;
            case 7:
                reason = "RSX convolution min filter has no exact NeoVeldrid equivalent";
                return false;
            default:
                reason = $"unknown/invalid RSX min filter {min}";
                return false;
        }

        bool magLinear;
        switch (mag)
        {
            case 1: magLinear = false; break;
            case 2: magLinear = true; break;
            case 4:
                reason = "RSX convolution mag filter has no exact NeoVeldrid equivalent";
                return false;
            default:
                reason = $"unknown/invalid RSX mag filter {mag}";
                return false;
        }

        // RSX CONTROL0 max-anisotropy 0..7 represents 1x,2x,4x,6x,8x,10x,12x,16x. NeoVeldrid's
        // portable API expresses anisotropy as a separate all-axis filter mode, not as the RSX cap
        // combined with the ordinary min/mag selection. Only 1x is therefore exact here.
        if (maxAnisoEnum != 0)
        {
            reason = $"RSX max-anisotropy enum {maxAnisoEnum} cannot be combined exactly with NeoVeldrid's portable filter model";
            return false;
        }

        filter = (minLinear, magLinear, mipLinear) switch
        {
            (false, false, false) => SamplerFilter.MinPoint_MagPoint_MipPoint,
            (false, false, true)  => SamplerFilter.MinPoint_MagPoint_MipLinear,
            (false, true, false)  => SamplerFilter.MinPoint_MagLinear_MipPoint,
            (false, true, true)   => SamplerFilter.MinPoint_MagLinear_MipLinear,
            (true, false, false)  => SamplerFilter.MinLinear_MagPoint_MipPoint,
            (true, false, true)   => SamplerFilter.MinLinear_MagPoint_MipLinear,
            (true, true, false)   => SamplerFilter.MinLinear_MagLinear_MipPoint,
            (true, true, true)    => SamplerFilter.MinLinear_MagLinear_MipLinear,
        };
        return true;
    }

    private static bool TryExactInteger(float value, out uint result)
    {
        if (float.IsFinite(value) && value >= 0 && value <= uint.MaxValue && value == MathF.Truncate(value))
        {
            result = (uint)value;
            return true;
        }
        result = 0;
        return false;
    }

    private static bool TryExactSignedInteger(float value, out int result)
    {
        if (float.IsFinite(value) && value >= int.MinValue && value <= int.MaxValue && value == MathF.Truncate(value))
        {
            result = (int)value;
            return true;
        }
        result = 0;
        return false;
    }
}
