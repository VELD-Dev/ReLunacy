using System.Runtime.CompilerServices;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering.Resources;
using RsxTextureChannelRemap = ReLunacy.Engine.Loading.Textures.RsxTextureChannelRemap;
using RsxTextureRemapComponent = ReLunacy.Engine.Loading.Textures.RsxTextureRemapComponent;
using RsxTextureRemapControl = ReLunacy.Engine.Loading.Textures.RsxTextureRemapControl;
using RsxTextureRemapSource = ReLunacy.Engine.Loading.Textures.RsxTextureRemapSource;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Proof-bounded bridge between native old-engine texture state and the current renderer upload
/// pipeline. It applies the hardware-defined, format-aware CONTROL1 component map and preserves
/// authored mip levels. ADDRESS signed/unsigned remap and gamma remain separate later sampling
/// stages and are not folded into stored RGBA here.
/// </summary>
internal static class TextureUploadPreparer
{
    private static readonly ConditionalWeakTable<byte[], TextureLevels> AuthoredByTopLevel = new();
    private static bool _loggedAuthored;
    private static bool _loggedControl1;
    private static readonly HashSet<string> _loggedUnsupportedControl1 = [];

    internal static byte[] PostprocessDecoded(ITexture texture, byte[] topLevelRgba)
    {
        byte[] top = ApplyNativeControl1(texture, topLevelRgba);

        if (texture is not Assets.Textures.Texture { HasAuthoredMipChain: true } || texture.MipmapCount <= 1)
            return top;

        int mipCount = Math.Max(1, texture.MipmapCount);
        var levels = new byte[mipCount][];
        levels[0] = top;
        int width = Math.Max(1, checked((int)texture.Width / 2));
        int height = Math.Max(1, checked((int)texture.Height / 2));

        for (int mip = 1; mip < mipCount; mip++)
        {
            byte[] raw = texture.GetMipmapData(mip);
            byte[] decoded = TextureUtils.DecodePhysicalToRgba8888(texture.Format, raw, width, height)
                ?? throw new NotSupportedException(
                    $"Texture {texture.Id:X} authored mip {mip} uses unsupported format {texture.Format}.");
            levels[mip] = ApplyNativeControl1(texture, decoded);
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }

        var prepared = TextureLevels.FromAuthored(texture.Width, texture.Height, levels);
        AuthoredByTopLevel.Remove(top);
        AuthoredByTopLevel.Add(top, prepared);

        if (!_loggedAuthored)
        {
            _loggedAuthored = true;
            Console.WriteLine(
                $"Native authored mip chains preserved for GPU upload " +
                $"({texture.Width}x{texture.Height}, {mipCount} levels, {texture.Format}).");
        }
        return top;
    }

    internal static bool TryConsumeAuthored(byte[] topLevelRgba, out TextureLevels levels)
    {
        if (AuthoredByTopLevel.TryGetValue(topLevelRgba, out levels!))
        {
            AuthoredByTopLevel.Remove(topLevelRgba);
            return true;
        }
        levels = null!;
        return false;
    }

    public static TextureLevels? Prepare(ITexture texture, bool generateMipsWhenUnauthored)
    {
        byte[]? rgba = TextureUtils.DecodeToRgba8888(texture, out int width, out int height);
        if (rgba == null) return null;
        if (TryConsumeAuthored(rgba, out TextureLevels authored)) return authored;
        return TextureLevels.Prepare((uint)width, (uint)height, rgba, generateMipsWhenUnauthored);
    }

    /// <summary>
    /// Applies only the proven native CONTROL1 component routing to an already-decoded RGBA level.
    /// If an old texture has been reformatted by Texture Editor, the current proven 0x5200 base
    /// format is used for format-dependent CONTROL1 semantics instead of the original descriptor's
    /// packing. Unsupported old formats are blocked before this path by TextureEditLimits.
    /// </summary>
    internal static byte[] ApplyNativeControl1(ITexture texture, byte[] rgba)
    {
        if (texture is not Assets.Textures.Texture assetTexture)
            return rgba;

        uint raw = assetTexture.NativeControl1;
        bool hasCurrentOldBase = Assets.Textures.TextureFormatCatalog.TryGetOldToolsOfDestructionBaseFormatByte(
            assetTexture.Format, out byte currentOldBase);
        byte? effectiveBase = assetTexture.SamplingDescriptor is { } descriptor
            ? hasCurrentOldBase ? currentOldBase : descriptor.BaseFormatByte
            : null;

        var remap = effectiveBase.HasValue
            ? RsxTextureChannelRemap.DecodeForFormat(raw, effectiveBase.Value)
            : RsxTextureChannelRemap.Decode(raw);

        if (remap.IsIdentity)
            return rgba;

        bool reserved = remap.Alpha.Control == RsxTextureRemapControl.Reserved
            || remap.Red.Control == RsxTextureRemapControl.Reserved
            || remap.Green.Control == RsxTextureRemapControl.Reserved
            || remap.Blue.Control == RsxTextureRemapControl.Reserved;

        bool unknownOverrideSemantics = assetTexture.SamplingDescriptor is null && remap.OverrideWord != 0;
        if (reserved || unknownOverrideSemantics)
        {
            string key = $"{raw:X8}/{effectiveBase?.ToString("X2") ?? "??"}";
            if (_loggedUnsupportedControl1.Add(key))
            {
                string reason = reserved
                    ? "reserved component control"
                    : "non-zero high-word override without the native base format";
                Console.WriteLine(
                    $"CONTROL1 0x{raw:X8} ({reason}); preserving physical RGBA for texture {texture.Id:X}.");
            }
            return rgba;
        }

        if ((rgba.Length & 3) != 0)
            throw new InvalidDataException($"Texture {texture.Id:X}: RGBA8 payload length 0x{rgba.Length:X} is not divisible by 4.");

        static byte Select(RsxTextureRemapSource source, byte r, byte g, byte b, byte a) => source switch
        {
            RsxTextureRemapSource.Alpha => a,
            RsxTextureRemapSource.Red => r,
            RsxTextureRemapSource.Green => g,
            RsxTextureRemapSource.Blue => b,
            _ => 0,
        };

        static byte Resolve(RsxTextureRemapComponent component, byte r, byte g, byte b, byte a) => component.Control switch
        {
            RsxTextureRemapControl.Zero => 0,
            RsxTextureRemapControl.One => 255,
            RsxTextureRemapControl.Remap => Select(component.Source, r, g, b, a),
            _ => throw new InvalidOperationException("Reserved CONTROL1 component reached resolved remap path."),
        };

        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte r = rgba[i + 0], g = rgba[i + 1], b = rgba[i + 2], a = rgba[i + 3];
            rgba[i + 0] = Resolve(remap.Red, r, g, b, a);
            rgba[i + 1] = Resolve(remap.Green, r, g, b, a);
            rgba[i + 2] = Resolve(remap.Blue, r, g, b, a);
            rgba[i + 3] = Resolve(remap.Alpha, r, g, b, a);
        }

        if (!_loggedControl1)
        {
            _loggedControl1 = true;
            Console.WriteLine("Native RSX format-aware CONTROL1 channel remap active before GPU upload/export.");
        }
        return rgba;
    }
}
