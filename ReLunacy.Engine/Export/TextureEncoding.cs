using Bliss.CSharp.Images;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;

namespace ReLunacy.Engine.Export;

/// <summary>
/// Shared PNG-encoding helpers for the model exporters (glTF embeds PNG bytes directly, OBJ
/// writes them as loose sibling files). Bliss's Image only exposes SaveAsPng(path), so encoding
/// to an in-memory byte[] round-trips through a temp file rather than reimplementing a PNG encoder.
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
        var image = new Image(width, height, rgba);
        string tempPath = Path.Combine(Path.GetTempPath(), $"relunacy_export_{Guid.NewGuid():N}.png");
        try
        {
            image.SaveAsPng(tempPath);
            return File.ReadAllBytes(tempPath);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
