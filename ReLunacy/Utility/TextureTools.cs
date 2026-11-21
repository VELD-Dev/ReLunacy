using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Utility;

/// <summary>What the texture frames share: export file names, PNG and raw export, memory size.</summary>
public static class TextureTools
{
    /// <summary>The folder exports go to.</summary>
    public static string ExtractedFolder()
    {
        var path = Path.Combine(Program.EditorPath, "Extracted");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Texture names are often slash-delimited asset paths: only the last segment names the file, and a texture
    /// without a name falls back to its index.</summary>
    public static string ExportFileName(string? textureName, int index) =>
        string.IsNullOrEmpty(textureName) ? $"Tex_{index}" : textureName.Split('/')[^1];

    public static void ExportRaw(ITexture texture, string fileName) =>
        File.WriteAllBytes(Path.Combine(ExtractedFolder(), fileName + ".raw"), texture.GetPixelData());

    /// <summary>Re-decodes from the source texture: the GPU copy has a mip chain and no CPU-side pixels.</summary>
    public static bool ExportPng(ITexture texture, string fileName)
    {
        byte[]? pixels = TextureUtils.DecodeToRgba8888(texture, out int width, out int height);
        if (pixels == null) return false;
        new Image(width, height, pixels).SaveAsPng(Path.Combine(ExtractedFolder(), fileName + ".png"));
        return true;
    }

    /// <summary>Bytes of every authored mip level.</summary>
    public static long MemorySize(ITexture texture)
    {
        long total = 0;
        for (int mip = 0; mip < Math.Max(1, texture.MipmapCount); mip++)
            total += mip == 0 ? texture.GetPixelData().Length : texture.GetMipmapData(mip).Length;
        return total;
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1 << 20 => $"{bytes / (double)(1 << 20):0.##} MB",
        >= 1 << 10 => $"{bytes / (double)(1 << 10):0.##} KB",
        _ => $"{bytes} B",
    };
}
