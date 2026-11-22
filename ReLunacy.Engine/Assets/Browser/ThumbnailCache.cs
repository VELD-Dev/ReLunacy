using System.Text;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Assets.Browser;

/// <summary>A thumbnail's pixels: RGBA8, tightly packed, top row first.</summary>
public sealed record ThumbnailImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// The Assets Browser's thumbnails on disk, one PNG each, under
/// <c>root/game/level/v{format}_{size}/{kind}_{id}_{stamp}.png</c>. The stamp is a hash of whatever the
/// thumbnail was made from (the level's files, the asset's bytes), so an edit names a new file and the old one
/// is deleted when the new one is written. Reads and writes are safe from any thread; writes go through a
/// temporary file and a rename, so a reader never sees half a PNG.
/// </summary>
public sealed class ThumbnailCache
{
    /// <summary>Bumped when the way thumbnails are made changes, so older files stop matching.</summary>
    public const int FormatVersion = 1;

    public string Root { get; }
    public string Folder { get; }
    public int Size { get; }

    /// <param name="root">The cache's root folder (the project's or the user's).</param>
    /// <param name="game">The game's id.</param>
    /// <param name="levelSource">The level's path, which names its folder.</param>
    public ThumbnailCache(string root, string game, string levelSource, int size)
    {
        Root = root;
        Size = size;
        Folder = Path.Combine(root, Sanitize(game), LevelFolderName(levelSource), $"v{FormatVersion}_{size}");
    }

    /// <summary>The level's folder name with a hash of its full path, so two installs of one game do not share files.</summary>
    public static string LevelFolderName(string levelSource)
    {
        string full = Path.GetFullPath(levelSource).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(full);
        // "levels/foo/level_cached.psarc": the folder names the level.
        if (File.Exists(full) || Path.HasExtension(name)) name = Path.GetFileName(Path.GetDirectoryName(full) ?? full);
        return $"{Sanitize(name)}_{Hash(Encoding.UTF8.GetBytes(full)) & 0xFFFFFFFF:x8}";
    }

    public string PathFor(AssetKey key, ulong stamp, string extension = ".png") => Path.Combine(Folder, $"{FilePrefix(key)}{stamp:x16}{extension}");

    private static string FilePrefix(AssetKey key) => $"{key.Kind}_{key.Id:X16}_";

    /// <summary>The thumbnail, or null when there is none for this stamp (or it does not decode).</summary>
    public ThumbnailImage? TryLoad(AssetKey key, ulong stamp)
    {
        if (TryLoadBytes(key, stamp, ".png") is not { } bytes) return null;
        try
        {
            var image = new Image(bytes);
            return image.Width > 0 && image.Height > 0 ? new ThumbnailImage(image.Width, image.Height, image.Data) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Writes the thumbnail and deletes the asset's thumbnails of other stamps. False when it could not be written.</summary>
    public bool Store(AssetKey key, ulong stamp, ThumbnailImage image) =>
        StoreBytes(key, stamp, ".png", new Image(image.Width, image.Height, image.Rgba).EncodeToPng());

    /// <summary>A cached file of another form (a sound's waveform), or null.</summary>
    public byte[]? TryLoadBytes(AssetKey key, ulong stamp, string extension)
    {
        try
        {
            string path = PathFor(key, stamp, extension);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Writes a cached file through a temporary file and a rename, then deletes the asset's files of the same
    /// form and other stamps. False when it could not be written.</summary>
    public bool StoreBytes(AssetKey key, ulong stamp, string extension, byte[] bytes)
    {
        string path = PathFor(key, stamp, extension);
        string temporary = $"{path}.{Environment.CurrentManagedThreadId}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
            string own = Path.GetFileName(path);
            foreach (string stale in Directory.EnumerateFiles(Folder, FilePrefix(key) + "*" + extension))
                if (!string.Equals(Path.GetFileName(stale), own, StringComparison.Ordinal))
                    try { File.Delete(stale); } catch (IOException) { }
            return true;
        }
        catch (Exception)
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception) { }
            return false;
        }
    }

    /// <summary>Deletes every thumbnail under <paramref name="root"/>. False when some could not be.</summary>
    public static bool Clear(string root)
    {
        if (!Directory.Exists(root)) return true;
        try
        {
            Directory.Delete(root, recursive: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (char c in name) builder.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
        return builder.Length > 0 ? builder.ToString() : "_";
    }

    // --- Stamps -----------------------------------------------------------------------------------

    private const ulong FnvOffset = 0xCBF29CE484222325, FnvPrime = 0x100000001B3;

    /// <summary>FNV-1a over the bytes.</summary>
    public static ulong Hash(ReadOnlySpan<byte> bytes, ulong seed = FnvOffset)
    {
        ulong hash = seed;
        foreach (byte b in bytes) hash = (hash ^ b) * FnvPrime;
        return hash;
    }

    /// <summary>Folds values into a stamp.</summary>
    public static ulong Combine(ulong hash, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(bytes, value);
        return Hash(bytes, hash == 0 ? FnvOffset : hash);
    }

    public static ulong Combine(ulong hash, string? value) => Hash(Encoding.UTF8.GetBytes(value ?? ""), hash == 0 ? FnvOffset : hash);

    /// <summary>A stamp of the level's files on disk: sizes and write times of the file, or of a folder's files.</summary>
    public static ulong LevelFilesStamp(string levelSource)
    {
        ulong stamp = Combine(0, Path.GetFullPath(levelSource));
        try
        {
            if (File.Exists(levelSource))
            {
                var info = new FileInfo(levelSource);
                return Combine(Combine(stamp, (ulong)info.Length), (ulong)info.LastWriteTimeUtc.Ticks);
            }
            if (!Directory.Exists(levelSource)) return stamp;
            foreach (string file in Directory.EnumerateFiles(levelSource).Order(StringComparer.Ordinal))
            {
                var info = new FileInfo(file);
                stamp = Combine(Combine(Combine(stamp, info.Name), (ulong)info.Length), (ulong)info.LastWriteTimeUtc.Ticks);
            }
        }
        catch (Exception)
        {
        }
        return stamp;
    }

    // --- Pixels -----------------------------------------------------------------------------------

    /// <summary>The image fitted into a <paramref name="size"/> square, aspect kept, box-filtered down and centred on
    /// transparent; <paramref name="flipRows"/> turns it upside down (the level's textures are stored bottom row first) and
    /// <paramref name="opaque"/> drops the alpha (often data, not coverage).</summary>
    public static ThumbnailImage Fit(byte[] rgba, int width, int height, int size, bool flipRows = false, bool opaque = false)
    {
        float scale = MathF.Min(1f, MathF.Min(size / (float)width, size / (float)height));
        int w = Math.Max(1, (int)MathF.Round(width * scale)), h = Math.Max(1, (int)MathF.Round(height * scale));
        int ox = (size - w) / 2, oy = (size - h) / 2;
        var output = new byte[size * size * 4];
        for (int y = 0; y < h; y++)
        {
            int sy0 = y * height / h, sy1 = Math.Max(sy0 + 1, (y + 1) * height / h);
            int dy = flipRows ? oy + h - 1 - y : oy + y;
            for (int x = 0; x < w; x++)
            {
                int sx0 = x * width / w, sx1 = Math.Max(sx0 + 1, (x + 1) * width / w);
                uint r = 0, g = 0, b = 0, a = 0, n = 0;
                for (int sy = sy0; sy < sy1; sy++)
                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        int i = (sy * width + sx) * 4;
                        r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2]; a += rgba[i + 3]; n++;
                    }
                int o = (dy * size + ox + x) * 4;
                output[o] = (byte)(r / n);
                output[o + 1] = (byte)(g / n);
                output[o + 2] = (byte)(b / n);
                output[o + 3] = opaque ? (byte)255 : (byte)(a / n);
            }
        }
        return new ThumbnailImage(size, size, output);
    }
}
