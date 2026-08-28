using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Textures;
using AssetTexture = ReLunacy.Engine.Assets.Textures.Texture;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Reads old-engine environment cubemaps: section 0x5920, one <see cref="TextureMetadataOld"/>
/// record per cubemap, with pixel data in MAIN.DAT (not textures.dat) at the record's own offset.
///
/// On-disk layout, established byte-for-byte against metropolis and matched to a RenderDoc capture
/// of the live cubemap:
///   - 6 faces, order +X,-X,+Y,-Y,+Z,-Z (GL/RSX), face-major with the full mip chain per face
///     (largest first), each face padded to a 128-byte STRIDE (5460 mip bytes -> 5504).
///   - Faces are Morton/GCM-SWIZZLED, despite the metadata's linear bit reading set - the bit does
///     not describe this texture correctly, so faces are always un-swizzled here.
///   - A small all-zero-alpha HEADER precedes the first face (0x380 on metropolis). Rather than
///     hard-code it, the first face is located by a short alignment search (see FindFaceBase):
///     the real faces are the smoothest coherent 32x32 images in the region, which pins the header
///     size without assuming it.
///
/// Only A8R8G8B8 is handled (the only format seen). Records with offset 0 are stubs (e.g. kerchu
/// city, which ships a placeholder and no cubemap pixels) and are skipped. New engine is not
/// handled - its cubemaps are an assetlookup resource (InsomniaToolset ResourceCubemap 0x1d200),
/// a different path entirely.</summary>
public sealed class CubemapReader
{
    public const uint ID = 0x5920;

    // Faces are aligned to 128 bytes; the leading header on the one confirmed sample is 0x380. The
    // search below walks 128-byte candidate offsets up to this cap and never depends on the exact
    // value.
    private const int FaceAlignment = 128;
    private const int MaxHeaderSearch = 0x1000;
    private const int FaceCount = 6;

    private readonly FileManager _fileManager;

    public CubemapReader(FileManager fileManager)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
    }

    public IReadOnlyList<Assets.Cubemaps.Cubemap> ReadAll()
    {
        if (!_fileManager.isOld) return [];
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null) return [];

        var section = main.QuerySection(ID);
        if (section.id != ID) return [];

        // The section's `count` field carries the unreliable flag the rest of the loader already
        // works around (kerchu city reports count=4 for a single stub record) - length / record
        // size is the real count.
        int recordCount = (int)(section.length / TextureMetadataOld.Size);
        var result = new List<Assets.Cubemaps.Cubemap>();

        for (int r = 0; r < recordCount; r++)
        {
            main.sh.Seek(section.offset + (long)r * TextureMetadataOld.Size);
            var meta = TextureMetadataOld.Read(main.sh);

            if (meta.offset == 0 || meta.Width == 0 || meta.Height == 0)
                continue; // stub / no pixel data (see class comment)
            if (meta.Format != Textures.TextureFormat.A8R8G8B8)
            {
                Console.WriteLine($"Cubemap {r}: unhandled format {meta.Format}, skipped.");
                continue;
            }

            var cubemap = ReadCubemap(main, meta, (uint)(section.offset + (long)r * TextureMetadataOld.Size));
            if (cubemap != null)
                result.Add(cubemap);
        }

        if (result.Count != 0)
            Console.WriteLine($"Cubemaps: {result.Count} loaded ({string.Join(", ", result.Select(c => $"{c.FaceSize}x{c.FaceSize}"))}).");
        return result;
    }

    private Assets.Cubemaps.Cubemap? ReadCubemap(IGFile main, in TextureMetadataOld meta, uint recordBase)
    {
        int size = (int)meta.Width;               // faces are square
        int faceBytes = size * size * 4;          // mip0, A8R8G8B8
        int stride = AlignUp(MipChainBytes(size, meta.MipmapCount), FaceAlignment);

        long streamLen = main.sh.BaseStream.Length;
        long available = streamLen - meta.offset;
        int needed = MaxHeaderSearch + FaceCount * stride;
        int toRead = (int)Math.Min(needed, available);
        if (toRead < FaceCount * stride) return null; // not enough data for six faces

        byte[] region = main.sh.ReadFromOffset(toRead, meta.offset);

        int header = FindFaceBase(region, size, stride);
        if (header < 0)
        {
            Console.WriteLine($"Cubemap 0x{recordBase:X}: could not locate faces, skipped.");
            return null;
        }

        var faces = new ITexture[FaceCount];
        for (int f = 0; f < FaceCount; f++)
        {
            int faceOffset = header + f * stride;
            // Un-swizzle mip0 into row-major ARGB, then hand it to the asset-facing Texture as
            // A8R8G8B8 so the shared DecodeToRgba8888 path (ARGB->RGBA) previews/exports it exactly
            // like any other texture.
            byte[] argb = Textures.Texture.Deswizzle(region.AsSpan(faceOffset, faceBytes), size, size, 4);
            faces[f] = AssetTexture.FromData(
                MakeFaceId(recordBase, f), (uint)size, (uint)size,
                Assets.Interfaces.TextureFormat.A8R8G8B8, argb);
        }

        return new Assets.Cubemaps.Cubemap(recordBase, size, faces);
    }

    /// <summary>Finds the first face's offset within the region. The leading header is all-zero
    /// alpha, so the real faces are the first 128-aligned position where all six un-swizzled faces
    /// carry actual data (alpha varies) and read as coherent images (low local alpha gradient).
    /// Returns the offset of face +X's mip0, or -1 if no plausible alignment was found.</summary>
    private static int FindFaceBase(byte[] region, int size, int stride)
    {
        int faceBytes = size * size * 4;
        int maxHeader = Math.Min(MaxHeaderSearch, region.Length - FaceCount * stride);

        int bestOffset = -1;
        double bestGradient = double.MaxValue;

        for (int header = 0; header <= maxHeader; header += FaceAlignment)
        {
            double worstStd = double.MaxValue;
            double totalGradient = 0;
            for (int f = 0; f < FaceCount; f++)
            {
                byte[] face = Textures.Texture.Deswizzle(
                    region.AsSpan(header + f * stride, faceBytes), size, size, 4);
                (double std, double grad) = AlphaStats(face, size);
                worstStd = Math.Min(worstStd, std);
                totalGradient += grad;
            }

            // Near-constant faces (the all-zero-alpha header, or padding) are not real faces even
            // though they are perfectly "smooth"; require every face to carry signal first, then
            // pick the alignment whose faces are the most image-like.
            if (worstStd < 5.0) continue;
            if (totalGradient < bestGradient)
            {
                bestGradient = totalGradient;
                bestOffset = header;
            }
        }

        return bestOffset;
    }

    /// <summary>Standard deviation and mean absolute neighbour-difference of a face's ALPHA channel
    /// (byte 0 of each ARGB texel) - the channel that carries the cubemap's signal.</summary>
    private static (double Std, double Gradient) AlphaStats(byte[] argbFace, int size)
    {
        int n = size * size;
        double sum = 0, sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            int a = argbFace[i * 4];
            sum += a;
            sumSq += a * (double)a;
        }
        double mean = sum / n;
        double std = Math.Sqrt(Math.Max(0, sumSq / n - mean * mean));

        double gradSum = 0;
        int gradCount = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int a = argbFace[(y * size + x) * 4];
                if (x + 1 < size) { gradSum += Math.Abs(a - argbFace[(y * size + x + 1) * 4]); gradCount++; }
                if (y + 1 < size) { gradSum += Math.Abs(a - argbFace[((y + 1) * size + x) * 4]); gradCount++; }
            }
        }
        return (std, gradCount == 0 ? 0 : gradSum / gradCount);
    }

    private static int MipChainBytes(int size, int mipCount)
    {
        int total = 0;
        for (int m = 0; m < Math.Max(1, mipCount); m++)
        {
            int w = Math.Max(1, size >> m);
            total += w * w * 4;
        }
        return total;
    }

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    // A stable, unique id per face for the texture table / caches: the cubemap record's own offset
    // in the high bits, the face index in the low bits.
    private static ulong MakeFaceId(uint recordBase, int face) => ((ulong)recordBase << 8) | (uint)face;
}
