using System.Runtime.Intrinsics;
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
    /// As <see cref="Prepare(ITexture, bool)"/>, with neither side above <paramref name="maxDimension"/>
    /// (0: no limit). A texture that carries its own mip chain is decoded from the first level that fits,
    /// so the levels above it are never decoded at all - what makes loading a level at low resolution
    /// cheap (texture streaming). Byte-identical to decoding everything and dropping the top levels.
    /// </summary>
    public static TextureLevels? Prepare(ITexture texture, bool generateMipsWhenUnauthored, uint maxDimension)
    {
        if (maxDimension == 0 || Math.Max(texture.Width, texture.Height) <= maxDimension)
            return Prepare(texture, generateMipsWhenUnauthored);

        int skip = 0;
        uint w = texture.Width, h = texture.Height;
        while (Math.Max(w, h) > maxDimension && (w > 1 || h > 1))
        {
            w = Math.Max(1u, w / 2);
            h = Math.Max(1u, h / 2);
            skip++;
        }
        if (texture is not Assets.Textures.Texture { HasAuthoredMipChain: true } || skip >= texture.MipmapCount)
            return Prepare(texture, generateMipsWhenUnauthored)?.LimitTo(maxDimension);

        var levels = new byte[texture.MipmapCount - skip][];
        for (int mip = skip; mip < texture.MipmapCount; mip++)
        {
            byte[]? decoded = TextureUtils.DecodeMipmapToRgba8888(texture, mip, out _, out _);
            if (decoded == null) return Prepare(texture, generateMipsWhenUnauthored)?.LimitTo(maxDimension);
            levels[mip - skip] = decoded;
        }
        return TextureLevels.FromAuthored(w, h, levels);
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

        // Each destination channel is a source channel or a constant, the same for every pixel: resolved
        // once into a byte shuffle (out-of-range index = 0) and an OR mask (the channels forced to 255),
        // and applied 4 pixels at a time. Resolving it per channel per pixel was 3.8 s of krell_canyon's
        // 4.6 s of texture decoding.
        Span<byte> shuffle = stackalloc byte[16];
        Span<byte> ones = stackalloc byte[16];
        ReadOnlySpan<RsxTextureRemapComponent> components = [remap.Red, remap.Green, remap.Blue, remap.Alpha];
        for (int c = 0; c < 4; c++)
        {
            var component = components[c];
            byte index = 0xFF;
            byte one = 0;
            switch (component.Control)
            {
                case RsxTextureRemapControl.One:
                    one = 0xFF;
                    break;
                case RsxTextureRemapControl.Remap:
                    index = component.Source switch
                    {
                        RsxTextureRemapSource.Red => 0,
                        RsxTextureRemapSource.Green => 1,
                        RsxTextureRemapSource.Blue => 2,
                        RsxTextureRemapSource.Alpha => 3,
                        _ => 0xFF,
                    };
                    break;
                case RsxTextureRemapControl.Zero:
                    break;
                default:
                    throw new InvalidOperationException("Reserved CONTROL1 component reached resolved remap path.");
            }
            for (int p = 0; p < 4; p++)
            {
                shuffle[p * 4 + c] = index == 0xFF ? (byte)0xFF : (byte)(p * 4 + index);
                ones[p * 4 + c] = one;
            }
        }

        var shuffleVector = Vector128.Create((ReadOnlySpan<byte>)shuffle);
        var onesVector = Vector128.Create((ReadOnlySpan<byte>)ones);
        int i = 0;
        for (; i + 16 <= rgba.Length; i += 16)
        {
            var pixels = Vector128.Create((ReadOnlySpan<byte>)rgba.AsSpan(i, 16));
            (Vector128.Shuffle(pixels, shuffleVector) | onesVector).CopyTo(rgba.AsSpan(i, 16));
        }
        for (; i < rgba.Length; i += 4)
        {
            byte r = rgba[i + 0], g = rgba[i + 1], b = rgba[i + 2], a = rgba[i + 3];
            ReadOnlySpan<byte> source = [r, g, b, a];
            for (int c = 0; c < 4; c++)
                rgba[i + c] = (byte)((shuffle[c] == 0xFF ? 0 : source[shuffle[c]]) | ones[c]);
        }

        if (!_loggedControl1)
        {
            _loggedControl1 = true;
            Console.WriteLine("Native RSX format-aware CONTROL1 channel remap active before GPU upload/export.");
        }
        return rgba;
    }
}
