using ReLunacy.Engine.Assets.Textures;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Raw-Vulkan translation of the old-engine NV4097 sampler state used by Sky shells and Clouds.
///
/// Proof boundary:
/// - RSX WRAP / MIRROR / CLAMP_TO_EDGE are represented directly by Vulkan.
/// - ordinary nearest/linear minification, magnification and mip selection are represented directly.
/// - fractional LOD clamp/bias is preserved.
/// - BORDER, RSX CLAMP and MIRROR_ONCE have no exact Vulkan equivalent and fall back PER AXIS.
/// - the convolution field and the anisotropy hints are reported but do not disqualify translation;
///   see below.
///
/// CORRECTED 2026-09-16 after a full-corpus descriptor census of the retail level tree
/// (19 Sky-bearing levels, 98,171 descriptors across 0x5200/0x5400/0x5410):
///
///   FILTER convolution field  = 1 on 98,171 / 98,171 descriptors
///   0x5200 ADDRESS wrapS/wrapT = REPEAT/REPEAT on 22,548 / 22,591
///   SkyShell descriptors        = ADDRESS 0x00030101, FILTER 0x02022000 on 194 / 194
///
/// Enum 1 is `CELL_GCM_TEXTURE_CONVOLUTION_QUINCUNX`, the value libgcm writes by default; RSX applies
/// convolution only to the specific multisample/convolution formats, so it is inert for the ordinary
/// textures here. Treating it as "unsupported" therefore disqualified EVERY texture in the game, so
/// the exact path was unreachable and 100% of Sky samplers took the compatibility branch.
///
/// That branch then overrode the authored REPEAT wrap with CLAMP_TO_EDGE. Authored SkyShell UVs tile
/// well outside [0,1] - up to U -3.697..4.699 on iris, -3.000..5.109 on apogee, 0..5.000 on meridian -
/// so clamping smeared a single edge texel across the whole shell. That is the SkyShell presentation
/// defect, and it was caused entirely by the fallback, not by authored data.
///
/// Rule going forward: fall back PER FIELD, never for the whole sampler. A field that translates
/// exactly must keep its authored value. Replacing a representable authored mode with a different one
/// is not a conservative fallback, it is a silent behaviour change.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private readonly record struct SkySamplerBuild(VkSampler Sampler, bool Exact, string Status);

    private SkySamplerBuild CreateSkySampler(in OldTextureDescriptorState state, int slot)
    {
        bool exact = true;
        var notes = new List<string>();

        VkSamplerAddressMode addressU = MapSkyAddress(state.WrapS, "S", ref exact, notes);
        VkSamplerAddressMode addressV = MapSkyAddress(state.WrapT, "T", ref exact, notes);
        VkSamplerAddressMode addressW = MapSkyAddress(state.WrapR, "R", ref exact, notes);

        VkFilter minFilter;
        VkSamplerMipmapMode mipMode;
        bool forceBaseLevel = false;
        switch (state.MinFilter)
        {
            // NV4097 minification enums recovered by the old-texture reverse.
            case 1:
                minFilter = VkFilter.Nearest;
                mipMode = VkSamplerMipmapMode.Nearest;
                forceBaseLevel = true;
                break;
            case 2:
                minFilter = VkFilter.Linear;
                mipMode = VkSamplerMipmapMode.Nearest;
                forceBaseLevel = true;
                break;
            case 3:
                minFilter = VkFilter.Nearest;
                mipMode = VkSamplerMipmapMode.Nearest;
                break;
            case 4:
                minFilter = VkFilter.Linear;
                mipMode = VkSamplerMipmapMode.Nearest;
                break;
            case 5:
                minFilter = VkFilter.Nearest;
                mipMode = VkSamplerMipmapMode.Linear;
                break;
            case 6:
                minFilter = VkFilter.Linear;
                mipMode = VkSamplerMipmapMode.Linear;
                break;
            default:
                minFilter = VkFilter.Linear;
                mipMode = VkSamplerMipmapMode.Linear;
                exact = false;
                notes.Add($"minFilter={state.MinFilter} unsupported");
                break;
        }

        VkFilter magFilter;
        switch (state.MagFilter)
        {
            case 1:
                magFilter = VkFilter.Nearest;
                break;
            case 2:
                magFilter = VkFilter.Linear;
                break;
            default:
                magFilter = VkFilter.Linear;
                exact = false;
                notes.Add($"magFilter={state.MagFilter} unsupported");
                break;
        }

        // Convolution: 1 = CELL_GCM_TEXTURE_CONVOLUTION_QUINCUNX is libgcm's default and is inert for
        // ordinary texture formats. The whole retail corpus uses it, so refusing it refuses every
        // texture. Only a genuinely different mode is worth reporting.
        if (state.ConvolutionFilter > 1)
        {
            exact = false;
            notes.Add($"convolution={state.ConvolutionFilter} not represented");
        }

        // Anisotropy is a filtering QUALITY hint. It cannot produce the wrong texel address, so it must
        // not be allowed to discard authored wrap/filter state. Report it and move on.
        if (state.MaxAnisotropy != 0 || state.AnisotropicBias != 0)
            notes.Add($"anisotropy hint maxAniso={state.MaxAnisotropy} bias={state.AnisotropicBias} not applied");

        float minLod = forceBaseLevel ? 0f : state.MinLod;
        float maxLod = forceBaseLevel ? 0f : state.MaxLod;
        float lodBias = forceBaseLevel ? 0f : state.LodBias;

        if (!float.IsFinite(minLod) || !float.IsFinite(maxLod) || !float.IsFinite(lodBias) || maxLod < minLod)
        {
            exact = false;
            notes.Add($"invalid LOD range/bias min={minLod:R} max={maxLod:R} bias={lodBias:R}; clamped");
        }

        // Only the LOD range is repaired wholesale, and only when the authored words are unusable.
        // Address modes and filters have already fallen back per field inside their own mappers, so
        // there is deliberately no blanket override here: the previous ClampToEdge+Linear reset was
        // what destroyed authored REPEAT tiling on every SkyShell.
        if (!float.IsFinite(minLod) || !float.IsFinite(maxLod) || !float.IsFinite(lodBias) || maxLod < minLod)
        {
            minLod = 0f;
            maxLod = Math.Max(0f, state.MipmapCount - 1f);
            lodBias = 0f;
        }

        var info = new VkSamplerCreateInfo
        {
            magFilter = magFilter,
            minFilter = minFilter,
            mipmapMode = mipMode,
            addressModeU = addressU,
            addressModeV = addressV,
            addressModeW = addressW,
            mipLodBias = lodBias,
            minLod = minLod,
            maxLod = maxLod,
            borderColor = VkBorderColor.FloatTransparentBlack,
        };

        VkSampler sampler;
        Check(_api.vkCreateSampler(&info, null, &sampler), $"vkCreateSampler(sky[{slot}])");

        string status = exact ? "EXACT" : $"COMPAT: {string.Join(", ", notes)}";
        LunaLog.LogInfo(
            $"[Sky][Sampler][{status}] slot={slot} wrap={state.WrapS}/{state.WrapT}/{state.WrapR} " +
            $"min/mag={state.MinFilter}/{state.MagFilter} mips={state.MipmapCount} " +
            $"lod={state.MinLod:R}..{state.MaxLod:R} bias={state.LodBias:R}");
        return new SkySamplerBuild(sampler, exact, status);
    }

    private static VkSamplerAddressMode MapSkyAddress(
        byte value,
        string axis,
        ref bool exact,
        List<string> notes)
    {
        switch (value)
        {
            case 1:
                return VkSamplerAddressMode.Repeat;
            case 2:
                return VkSamplerAddressMode.MirroredRepeat;
            case 3:
                return VkSamplerAddressMode.ClampToEdge;
            case 4:
                exact = false;
                notes.Add($"{axis}=BORDER missing native border colour; this axis only");
                return VkSamplerAddressMode.ClampToBorder;
            case 5:
                exact = false;
                notes.Add($"{axis}=RSX_CLAMP not exactly portable");
                return VkSamplerAddressMode.ClampToEdge;
            case 6:
            case 7:
            case 8:
                exact = false;
                notes.Add($"{axis}=MIRROR_ONCE({value}) not exactly portable");
                return VkSamplerAddressMode.MirroredRepeat;
            default:
                exact = false;
                notes.Add($"{axis}=unknown({value})");
                return VkSamplerAddressMode.Repeat;
        }
    }
}
