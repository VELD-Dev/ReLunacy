using System.Buffers.Binary;
using ReLunacy.Engine.Importing;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Packing;

/// <summary>A texture the editor encoded (<see cref="TextureImporter"/>) that is not in the level's files yet. Its TUID is
/// CRC32(<see cref="Name"/>) &lt;&lt; 32 | a texture id unused in the level, as retail TUIDs are.</summary>
public sealed class PendingTexture
{
    public required ulong Tuid { get; init; }
    public required EncodedTexture Texture { get; init; }
    public string Name => Texture.Name;
    /// <summary>The id a shader names it by (0x5D00 / 0x5A00): the TUID's low word.</summary>
    public uint Id => (uint)Tuid;
}

/// <summary>A shader the editor made (<see cref="TieShaderFactory"/>) that is not in the level's files yet, with the new textures it
/// binds. <see cref="ShaderRegistrar.Register"/> writes both at the next save, before the Tie classes that use it.</summary>
public sealed class PendingShader
{
    public required ulong Tuid { get; init; }
    public required string Name { get; init; }
    /// <summary>The shader resource (IGHW: 0x5A00, 0x5000, 0x5D00, 0x11300, 0x5200), its 0x5D00 +0x00 = <see cref="Tuid"/>.</summary>
    public required byte[] Bytes { get; init; }
    /// <summary>The new textures it binds; any other texture it names must already be in the level.</summary>
    public List<PendingTexture> Textures { get; init; } = [];
    /// <summary>The regions whose region.dat 0x1C100 lists it.</summary>
    public List<string> Regions { get; init; } = ["default"];
    /// <summary>The shader it was cloned from.</summary>
    public ulong TemplateTuid { get; init; }
    /// <summary>The values a Tie class copies for it (feature mask, fades, UV density), from the template.</summary>
    public required TieShaderCandidate Candidate { get; init; }
}

public sealed class ShaderRegistration
{
    /// <summary>Archive path -> new bytes, changed files only (shaders.dat, textures.dat, highmips.dat, assetlookup.dat,
    /// assetstats.dat, level_cached.toc, level_uncached.toc, each region's region.dat).</summary>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
    public List<ulong> Registered { get; } = [];
    public List<(PendingShader Shader, string Reason)> Refused { get; } = [];
}

/// <summary>
/// Puts new shaders and their textures into a new-engine level (A Crack in Time). Per texture: mip 0 at the end of highmips.dat
/// and mips 1..N at the end of textures.dat (0x80 aligned; a single-mip texture in textures.dat only, highmips length 0, as
/// retail ships them); assetlookup 0x1D140 {format, mips, log2 w, log2 h}, 0x1D180 / 0x1D1C0 {TUID, offset, length} and 0x1D181
/// (the regions' bits), appended (the texture table is not sorted on any retail level). Per shader: the resource at the end of
/// shaders.dat (0x80 aligned); assetlookup 0x1D100 {TUID, offset, length} and 0x1D101 inserted by TUID (sorted on every retail
/// level); each target region's region.dat 0x1C100 (sorted); assetstats 0x9800 +0x10 (the largest 0x1C100 list); and the sizes in
/// level_cached.toc and level_uncached.toc. Textures and shaders are not in the resident pool (+0x00).
/// </summary>
public static class ShaderRegistrar
{
    /// <summary>A shader TUID for a new shader: above every shader of the level when there is room (0x1D100 keeps ascending
    /// offsets, as retail has) and above <paramref name="taken"/> (pending shaders), unused by every lookup table and by
    /// <paramref name="avoid"/> (pending Tie classes).</summary>
    public static ulong FreshTuid(Func<string, byte[]?> read, IEnumerable<ulong>? taken = null, IEnumerable<ulong>? avoid = null)
    {
        byte[] lookup = read("assetlookup.dat") ?? throw new InvalidDataException("assetlookup.dat not found");
        var pending = (taken ?? []).ToList();
        var used = new HashSet<ulong>(pending.Concat(avoid ?? []));
        ulong max = 0;
        foreach (var sec in IghwRelayout.Sections(lookup).Where(x => x.Id is >= 0x1D100 and < 0x1DC00 && (x.Id & 0xFF) == 0 && x.Size % 16 == 0))
            for (int i = 0; i < sec.Size / 16; i++)
            {
                ulong t = BinaryPrimitives.ReadUInt64BigEndian(lookup.AsSpan(sec.Offset + 16 * i));
                used.Add(t);
                if (sec.Id == 0x1D100) max = Math.Max(max, t);
            }
        // Pending shaders sit above the level's; when the top is reached the new TUID is inserted below (0x1D100 stays sorted).
        foreach (ulong t in pending) if (t < ulong.MaxValue - 1) max = Math.Max(max, t);
        ulong next = max < ulong.MaxValue - 1 ? max + 1 : 1;
        while (next is 0 or ulong.MaxValue || used.Contains(next)) next = next >= ulong.MaxValue - 1 ? 1 : next + 1;
        return next;
    }

    /// <summary>The ids (TUID low words) of the level's textures (assetlookup 0x1D1C0): a new texture's id must be none of them.</summary>
    public static HashSet<uint> TextureIds(Func<string, byte[]?> read)
    {
        byte[] lookup = read("assetlookup.dat") ?? throw new InvalidDataException("assetlookup.dat not found");
        var ids = new HashSet<uint>();
        var s = IghwRelayout.Find(IghwRelayout.Sections(lookup), 0x1D1C0);
        if (s != null)
            for (int i = 0; i < s.Size / 16; i++) ids.Add((uint)BinaryPrimitives.ReadUInt64BigEndian(lookup.AsSpan(s.Offset + 16 * i)));
        return ids;
    }

    /// <summary>A texture TUID for <paramref name="name"/>: CRC32(name) &lt;&lt; 32 (the game's second lookup key) | an id not in
    /// <paramref name="takenIds"/>, which it is added to.</summary>
    public static ulong TextureTuid(string name, ISet<uint> takenIds)
    {
        uint id = InsomniacCrc32.Compute(name + "#relunacy");
        while (id is 0 or 0xFFFFFFFF || !takenIds.Add(id)) id = id * 1664525u + 1013904223u;
        return (ulong)InsomniacCrc32.Compute(name) << 32 | id;
    }

    /// <summary>Registers the shaders, lowest TUID first, each with its textures. A refused shader changes nothing and is listed
    /// in <see cref="ShaderRegistration.Refused"/>.</summary>
    public static ShaderRegistration Register(Func<string, byte[]?> read, IReadOnlyList<PendingShader> shaders) =>
        TieAppender.RegisterShaders(read, shaders);
}
