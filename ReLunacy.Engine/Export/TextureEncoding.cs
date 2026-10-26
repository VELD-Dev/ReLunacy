using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Export;

/// <summary>
/// Shared PNG-encoding helpers for the model exporters (glTF embeds PNG bytes directly, OBJ writes
/// them as loose sibling files).
/// </summary>
public static class TextureEncoding
{
    /// <summary>Decodes an ITexture and re-encodes it to PNG bytes, or null if it has no data / an unrecognized format.</summary>
    public static byte[]? DecodeToPng(ITexture texture)
    {
        byte[]? rgba = TextureUtils.DecodeToRgba8888(texture, out int width, out int height);
        return rgba == null ? null : EncodeRgbaToPng(rgba, width, height);
    }

    public static byte[] EncodeRgbaToPng(byte[] rgba, int width, int height)
    {
        return new Image(width, height, rgba).EncodeToPng();
    }
}
