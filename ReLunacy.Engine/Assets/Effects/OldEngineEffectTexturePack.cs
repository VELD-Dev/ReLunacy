// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using LibreFios;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>
/// Tools of Destruction's game-wide particle textures: <c>processed/texpacks/effect.tph</c> + <c>effect.tp</c> in
/// <c>packed/game/global_cached.psarc</c> (69 DXT5 textures). Every level's effects use 14-16 of them through FxTexture
/// handles without the 0x8000 bit (ids 10..73), beside the level's own <c>system.tph</c>/<c>system.tp</c>. Found by walking
/// up from the level to the install's <c>packed/game</c>; an extracted copy (<c>packed/game/processed/texpacks</c>) is used
/// when the archive is not there. Read-only, cached per file.
/// </summary>
public static class OldEngineEffectTexturePack
{
    private const string HeaderMember = "processed/texpacks/effect.tph";
    private const string TexelMember = "processed/texpacks/effect.tp";

    private static readonly Dictionary<string, (byte[] Header, byte[] Texel)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The global pack of the install <paramref name="levelFolder"/> belongs to. False when none is found.</summary>
    public static bool TryRead(string levelFolder, out byte[] header, out byte[] texel, out string? source)
    {
        header = texel = [];
        source = null;
        if (string.IsNullOrEmpty(levelFolder)) return false;
        DirectoryInfo? current;
        try { current = new DirectoryInfo(levelFolder); }
        catch { return false; }

        for (int depth = 0; current != null && depth < 10; depth++, current = current.Parent)
        {
            foreach (string game in new[] { Path.Combine(current.FullName, "game"), Path.Combine(current.FullName, "packed", "game") })
            {
                string archive = Path.Combine(game, "global_cached.psarc");
                if (File.Exists(archive) && TryReadArchive(archive, out header, out texel))
                {
                    source = archive;
                    return true;
                }
                string loose = Path.Combine(game, "processed", "texpacks");
                if (File.Exists(Path.Combine(loose, "effect.tph")) && File.Exists(Path.Combine(loose, "effect.tp")))
                {
                    header = File.ReadAllBytes(Path.Combine(loose, "effect.tph"));
                    texel = File.ReadAllBytes(Path.Combine(loose, "effect.tp"));
                    source = loose;
                    return true;
                }
            }
            if (current.Name.Equals("USRDIR", StringComparison.OrdinalIgnoreCase)) break;
        }
        return false;
    }

    private static bool TryReadArchive(string path, out byte[] header, out byte[] texel)
    {
        string key = Path.GetFullPath(path) + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                (header, texel) = cached;
                return true;
            }
        }
        header = texel = [];
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var archive = new PSARC(stream);
            string? h = Find(archive, HeaderMember), t = Find(archive, TexelMember);
            if (h == null || t == null) return false;
            using (IPSARCBuffer b = archive.OpenFile(h)) header = b.Data.ToArray();
            using (IPSARCBuffer b = archive.OpenFile(t)) texel = b.Data.ToArray();
            if (header.Length == 0 || texel.Length == 0) return false;
            lock (Cache) Cache[key] = (header, texel);
            return true;
        }
        catch (IOException) { return false; }
        catch (InvalidDataException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string? Find(PSARC archive, string member) =>
        archive.Paths.FirstOrDefault(p => p.TrimStart('/').Equals(member, StringComparison.OrdinalIgnoreCase));
}
