using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Importing;

namespace ReLunacy.Engine.Packing;

/// <summary>A Tie class the editor built (<see cref="TieClassBuilder"/>, <see cref="CollisionCooker"/>) that is not in the
/// level's files yet. <see cref="TieClassRegistrar.Register"/> writes it at the next save.</summary>
public sealed class PendingTieClass
{
    public required TieClassResult Render { get; init; }

    /// <summary>The cooked collision (null: the class is not solid).</summary>
    public CollisionCookResult? Collision { get; init; }

    /// <summary>The regions that load the class (their region.dat lists it, their collision.dat holds its shapes): the one
    /// it was imported into, and every region that loads a zone it is placed in.</summary>
    public List<string> Regions { get; init; } = ["default"];

    public ulong Tuid => Render.Tuid;
    public string Name => Render.Name;
}

/// <summary>Where a registered class's cooked chunks went in one region's collision.dat.</summary>
/// <param name="OwnerIndex">The class's index in the region's collision TUID table (instance owner = 0x20000000 | index).</param>
/// <param name="ShapeOffsets">Each chunk's offset in the shape blob, in <see cref="CollisionCookResult.Chunks"/> order.</param>
public sealed record RegisteredCollision(int OwnerIndex, IReadOnlyList<uint> ShapeOffsets, CollisionCookResult Cooked);

/// <summary>A class that is now in the level's files, and what placing it needs (see <see cref="TieAppender.Append(Func{string, byte[]?}, IReadOnlyList{TieAppendRequest}, IReadOnlyDictionary{ulong, RegisteredTieClass})"/>).</summary>
/// <param name="LightmappedPrimitives">Primitives with the lightmap gate: a placement then gets a lightmap pair.</param>
/// <param name="Primitives">All primitives: when more than the lightmapped ones, a placement also gets prelight words.</param>
public sealed record RegisteredTieClass(ulong Tuid, string Name, int VertexCount, Vector4 Sphere,
    IReadOnlyDictionary<string, RegisteredCollision> Collision, int LightmappedPrimitives = 0, int Primitives = 0);

public sealed class TieClassRegistration
{
    /// <summary>Archive path -> new bytes, changed files only (ties.dat, assetlookup.dat, assetstats.dat, level_cached.toc, each
    /// region's region.dat and collision.dat).</summary>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
    public List<RegisteredTieClass> Registered { get; } = [];
    public List<(PendingTieClass Class, string Reason)> Refused { get; } = [];

    public IReadOnlyDictionary<ulong, RegisteredTieClass> ByTuid => Registered.ToDictionary(c => c.Tuid);
}

/// <summary>
/// Puts new Tie classes into a new-engine level (docs/reverse/newengine_tie_mesh_splitting.md section 9). Per class: the container
/// at the end of ties.dat (0x80 aligned); assetlookup 0x1D300 {TUID, offset, length} and 0x1D301 (the bits of the target regions);
/// each target region's region.dat 0x1C300 (sorted; refused when a shader of the class is not in its 0x1C100 or in 0x1D100);
/// assetstats 0x9800 (+0x2C Tie classes, +0x08 region.dat, +0x00 resident pool, +0x0C collision.dat); level_cached.toc; and
/// per region the collision class entry (TUID table +0x68 / +0x6C, its u16 table, the cooked shapes at the end of the shape
/// blob, 16-aligned, palette after each). No placement is written: <see cref="TieAppender.Append(Func{string, byte[]?}, IReadOnlyList{TieAppendRequest}, IReadOnlyDictionary{ulong, RegisteredTieClass})"/>
/// does that, with the instance records of the cooked chunks.
/// </summary>
public static class TieClassRegistrar
{
    /// <summary>A class TUID for a new class: above every Tie class of the level (assetlookup 0x1D300 stays sorted) and every
    /// TUID the lookup lists, and above <paramref name="taken"/> (pending classes); never one of <paramref name="avoid"/> (pending shaders).</summary>
    public static ulong FreshTuid(Func<string, byte[]?> read, IEnumerable<ulong>? taken = null, IEnumerable<ulong>? avoid = null)
    {
        byte[] lookup = read("assetlookup.dat") ?? throw new InvalidDataException("assetlookup.dat not found");
        var used = new HashSet<ulong>(taken ?? []);
        ulong max = used.Count == 0 ? 0 : used.Max();
        used.UnionWith(avoid ?? []);
        foreach (var sec in IghwRelayout.Sections(lookup).Where(x => x.Id is >= 0x1D100 and < 0x1DC00 && (x.Id & 0xFF) == 0 && x.Size % 16 == 0))
            for (int i = 0; i < sec.Size / 16; i++)
            {
                ulong t = BinaryPrimitives.ReadUInt64BigEndian(lookup.AsSpan(sec.Offset + 16 * i));
                used.Add(t);
                if (sec.Id == 0x1D300) max = Math.Max(max, t);
            }
        ulong next = max + 1;
        while (used.Contains(next) || next == 0 || next == ulong.MaxValue) next++;
        return next;
    }

    /// <summary>Registers the classes, lowest TUID first. A refused class changes nothing and is listed in
    /// <see cref="TieClassRegistration.Refused"/>.</summary>
    public static TieClassRegistration Register(Func<string, byte[]?> read, IReadOnlyList<PendingTieClass> classes) =>
        TieAppender.RegisterClasses(read, classes);
}
