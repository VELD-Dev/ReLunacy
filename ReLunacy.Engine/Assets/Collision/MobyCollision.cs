using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Assets.Collision;

/// <summary>
/// A Crack in Time: the collision a Moby class carries in its own container (mobys.dat), in bind-pose model space,
/// the space of the class's render meshes. Up to two sections, each one 0x80-byte instance record, the shape at
/// record + 0x80, then a palette of material words: 0xD900 a primitive set (D100 +0x30 points to it) and 0xDA00 a
/// triangle mesh (D100 +0x34). Checked over 3433 classes of 22 levels: 1501 have a primitive set, 590 a mesh, 165
/// both. The other new-engine games store it differently and are not read.
/// </summary>
public sealed class MobyCollision
{
    public const uint PrimitiveSectionId = 0xD900;
    public const uint MeshSectionId = 0xDA00;

    /// <summary>The 0xD900 section, or null when the class has none.</summary>
    public MobyCollisionSection? PrimitiveSection { get; private init; }

    /// <summary>The 0xDA00 section, or null when the class has none.</summary>
    public MobyCollisionSection? MeshSection { get; private init; }

    public IReadOnlyList<MobyCollisionPrimitive> Primitives { get; private init; } = [];

    public MobyCollisionMesh? Mesh { get; private init; }

    public bool IsEmpty => Primitives.Count == 0 && Mesh == null;

    /// <summary>Model-space box of both records.</summary>
    public (Vector3 Min, Vector3 Max) Bounds
    {
        get
        {
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (var s in new[] { PrimitiveSection, MeshSection })
            {
                if (s == null) continue;
                min = Vector3.Min(min, s.BoundsMin);
                max = Vector3.Max(max, s.BoundsMax);
            }
            return (min, max);
        }
    }

    private const int RecordSize = 0x80;
    private const int PrimitiveStride = 0x50;
    private const byte TypeMesh = 1;
    private const byte TypePrimitiveSet = 2;

    /// <summary>Reads the collision of one class container. Null when it has none or it does not parse.</summary>
    /// <param name="bindPose">The class skeleton's tms0 (bone to model, bind pose); a skinned mesh's vertices are
    /// bone-local and need it.</param>
    public static MobyCollision? Read(byte[] container, Matrix4x4[]? bindPose)
    {
        uint d100;
        using (var stream = new MemoryStream(container, writable: false))
        {
            var header = new IGFile(stream).QuerySection(0xD100);
            if (header.id != 0xD100) return null;
            d100 = header.offset;
        }
        ReadOnlySpan<byte> d = container;
        if (d100 + 0x38 > (uint)d.Length) return null;
        uint primitivesAt = U32(d, (int)d100 + 0x30);
        uint meshAt = U32(d, (int)d100 + 0x34);

        MobyCollisionSection? primitiveSection = null, meshSection = null;
        IReadOnlyList<MobyCollisionPrimitive> primitives = [];
        MobyCollisionMesh? mesh = null;
        if (primitivesAt != 0 && ReadSection(d, primitivesAt) is { Type: TypePrimitiveSet } ps)
        {
            primitives = ReadPrimitives(d, (int)ps.ShapeOffset, ps.ShapeSize) ?? [];
            if (primitives.Count > 0) primitiveSection = ps;
        }
        if (meshAt != 0 && ReadSection(d, meshAt) is { Type: TypeMesh } ms)
        {
            mesh = ReadMesh(d, (int)ms.ShapeOffset, ms.ShapeSize, bindPose);
            if (mesh != null) meshSection = ms;
        }

        if (primitiveSection == null && meshSection == null) return null;
        return new MobyCollision { PrimitiveSection = primitiveSection, MeshSection = meshSection, Primitives = primitives, Mesh = mesh };
    }

    /// <summary>The record: +0x00 min, +0x10 max, +0x20 u32 shape offset, +0x24 u16 shape size, +0x26 type, +0x27
    /// flags, +0x30 matrix (identity on every class), +0x70 bounding sphere; the palette follows the shape, 16-aligned.</summary>
    private static MobyCollisionSection? ReadSection(ReadOnlySpan<byte> d, uint at)
    {
        int o = (int)at;
        if (o < 0 || o + RecordSize > d.Length) return null;
        uint shape = U32(d, o + 0x20);
        ushort size = U16(d, o + 0x24);
        if (shape + (ulong)size > (ulong)d.Length) return null;

        var palette = new List<uint>();
        int p = (int)((shape + size + 15) & ~15u);
        if (p + 4 <= d.Length)
        {
            uint n = U32(d, p);
            if (n <= 64 && p + 4 + n * 4 <= d.Length)
                for (int i = 0; i < n; i++) palette.Add(U32(d, p + 4 + i * 4));
        }

        return new MobyCollisionSection(
            at, Vec3(d, o + 0x00), Vec3(d, o + 0x10), shape, size, d[o + 0x26], d[o + 0x27],
            new Matrix4x4(
                F32(d, o + 0x30), F32(d, o + 0x34), F32(d, o + 0x38), F32(d, o + 0x3C),
                F32(d, o + 0x40), F32(d, o + 0x44), F32(d, o + 0x48), F32(d, o + 0x4C),
                F32(d, o + 0x50), F32(d, o + 0x54), F32(d, o + 0x58), F32(d, o + 0x5C),
                F32(d, o + 0x60), F32(d, o + 0x64), F32(d, o + 0x68), F32(d, o + 0x6C)),
            new Vector4(F32(d, o + 0x70), F32(d, o + 0x74), F32(d, o + 0x78), F32(d, o + 0x7C)),
            palette);
    }

    /// <summary>Header: +0x00 u16 offset of the first entry (0x10), +0x02 u16 count. Each entry is 0x50 bytes: a
    /// 16-byte descriptor, then four vec4 rows.</summary>
    private static List<MobyCollisionPrimitive>? ReadPrimitives(ReadOnlySpan<byte> d, int o, int size)
    {
        if (o + 0x10 > d.Length) return null;
        int first = U16(d, o);
        int count = U16(d, o + 2);
        if (first + count * PrimitiveStride > size || o + first + count * PrimitiveStride > d.Length) return null;

        var list = new List<MobyCollisionPrimitive>(count);
        for (int i = 0; i < count; i++)
        {
            int e = o + first + i * PrimitiveStride;
            CollisionPrimitiveKind? kind = d[e] switch
            {
                4 => CollisionPrimitiveKind.Box,
                5 => CollisionPrimitiveKind.Sphere,
                6 => CollisionPrimitiveKind.Capsule,
                8 => CollisionPrimitiveKind.Cylinder,
                _ => null,
            };
            if (kind == null) return null;
            var r0 = Vec4(d, e + 0x10);
            var r1 = Vec4(d, e + 0x20);
            var r2 = Vec4(d, e + 0x30);
            var r3 = Vec4(d, e + 0x40);
            var shape = kind.Value switch
            {
                CollisionPrimitiveKind.Box => new CollisionPrimitive(CollisionPrimitiveKind.Box, Xyz(r3), default, 0f,
                    new Matrix4x4(r0.X, r0.Y, r0.Z, 0f, r1.X, r1.Y, r1.Z, 0f, r2.X, r2.Y, r2.Z, 0f, 0f, 0f, 0f, 1f),
                    new Vector3(r0.W, r1.W, r2.W), d[e + 0x0A]),
                CollisionPrimitiveKind.Sphere => new CollisionPrimitive(CollisionPrimitiveKind.Sphere, Xyz(r0), default, r0.W,
                    Matrix4x4.Identity, default, d[e + 0x0A]),
                var k => new CollisionPrimitive(k, Xyz(r0), Xyz(r1), r0.W, Matrix4x4.Identity, default, d[e + 0x0A]),
            };
            list.Add(new MobyCollisionPrimitive(shape, d[e + 0x01], d[e + 0x03], U16(d, e + 0x04), U16(d, e + 0x06),
                U16(d, e + 0x08), d[e + 0x0A], F32(d, e + 0x0C)));
        }
        return list;
    }

    /// <summary>
    /// The low byte of the u16 at +0x04 is 0 on a rigid mesh, laid out like a collision.dat mesh (see
    /// <see cref="CollisionMesh"/>). Otherwise the mesh is skinned: header u16s +0x00 vertex count, +0x06 triangle
    /// count, +0x08 triangle offset, +0x0A bone-group table, +0x0C material offset; an s32 origin at +0x10, then
    /// u16 x/y/z per vertex (AoS) from +0x20, in 1/1024 units of the bone's space. The group table holds u16 group
    /// count, u16 bone[n] at +0x10 and u16 vertexCount[n] at +0x20: consecutive vertex runs, each moved to model
    /// space by its bone's tms0.
    /// </summary>
    private static MobyCollisionMesh? ReadMesh(ReadOnlySpan<byte> d, int o, int size, Matrix4x4[]? bindPose)
    {
        if (o + 0x20 > d.Length) return null;
        if ((U16(d, o + 0x04) & 0xFF) == 0)
        {
            var rigid = CollisionMesh.Read(d, o, (uint)size);
            return rigid == null ? null
                : new MobyCollisionMesh(rigid.Positions, rigid.Indices, rigid.Materials, rigid.TriangleFlags, null);
        }

        int vertexCount = U16(d, o + 0x00);
        int triangleCount = U16(d, o + 0x06);
        int triangleOffset = U16(d, o + 0x08);
        int groupTable = U16(d, o + 0x0A);
        int materialOffset = U16(d, o + 0x0C);
        if (vertexCount == 0 || triangleCount == 0 || 0x20 + vertexCount * 6 > size
            || triangleOffset + triangleCount * 4 > size || materialOffset + triangleCount > size || groupTable + 0x20 > size)
            return null;

        int groups = U16(d, o + groupTable);
        int bonesAt = o + groupTable + U16(d, o + groupTable + 2);
        int countsAt = o + groupTable + U16(d, o + groupTable + 4);
        if (groups == 0 || bonesAt + groups * 2 > o + size || countsAt + groups * 2 > o + size) return null;

        int ox = S32(d, o + 0x10), oy = S32(d, o + 0x14), oz = S32(d, o + 0x18);
        var positions = new Vector3[vertexCount];
        var bones = new int[vertexCount];
        int v = 0;
        for (int g = 0; g < groups && v < vertexCount; g++)
        {
            int bone = U16(d, bonesAt + g * 2);
            int run = U16(d, countsAt + g * 2);
            Matrix4x4 toModel = bindPose != null && bone < bindPose.Length ? bindPose[bone] : Matrix4x4.Identity;
            for (int k = 0; k < run && v < vertexCount; k++, v++)
            {
                int at = o + 0x20 + v * 6;
                var local = new Vector3((ox + U16(d, at)) * VertexScale, (oy + U16(d, at + 2)) * VertexScale, (oz + U16(d, at + 4)) * VertexScale);
                positions[v] = Vector3.Transform(local, toModel);
                bones[v] = bone;
            }
        }
        if (v != vertexCount) return null;

        var indices = new int[triangleCount * 3];
        var flags = new byte[triangleCount];
        for (int t = 0; t < triangleCount; t++)
        {
            uint word = U32(d, o + triangleOffset + t * 4);
            int a = (int)((word >> 18) & 0x1FF), b = (int)((word >> 9) & 0x1FF), c = (int)(word & 0x1FF);
            if (a >= vertexCount || b >= vertexCount || c >= vertexCount) return null;
            indices[t * 3] = a;
            indices[t * 3 + 1] = b;
            indices[t * 3 + 2] = c;
            flags[t] = (byte)(word >> 27);
        }
        byte[] materials = d.Slice(o + materialOffset, triangleCount).ToArray();
        return new MobyCollisionMesh(positions, indices, materials, flags, bones);
    }

    private const float VertexScale = 1f / 1024f;

    private static uint U32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d[o..]);
    private static ushort U16(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt16BigEndian(d[o..]);
    private static int S32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadInt32BigEndian(d[o..]);
    private static float F32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadSingleBigEndian(d[o..]);
    private static Vector3 Vec3(ReadOnlySpan<byte> d, int o) => new(F32(d, o), F32(d, o + 4), F32(d, o + 8));
    private static Vector4 Vec4(ReadOnlySpan<byte> d, int o) => new(F32(d, o), F32(d, o + 4), F32(d, o + 8), F32(d, o + 12));
    private static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);
}

/// <summary>One collision section of a Moby class: its 0x80-byte instance record and its material palette.</summary>
public sealed record MobyCollisionSection(
    /// <summary>Where the record starts in the class container.</summary>
    uint Offset,
    Vector3 BoundsMin,
    Vector3 BoundsMax,
    /// <summary>Where the shape starts in the class container (record + 0x80).</summary>
    uint ShapeOffset,
    ushort ShapeSize,
    /// <summary>2 for a primitive set, 1 for a mesh.</summary>
    byte Type,
    /// <summary>0 on primitive sets (one class has 2), 0x60 on every mesh.</summary>
    byte Flags,
    Matrix4x4 Transform,
    Vector4 BoundingSphere,
    /// <summary>The material words a primitive's palette index or a triangle's material byte selects.</summary>
    IReadOnlyList<uint> Palette);

/// <summary>
/// One primitive of a Moby class's set. Its descriptor: +0x00 kind (4 box, 5 sphere, 6 capsule, 8 cylinder), +0x01
/// palette index, +0x03 bangle byte, +0x04 u16 bone A, +0x06 u16 bone B, +0x08 u16 own index, +0x0A mask, +0x0C f32.
/// The rows: a box's three axes with the half extent in w, then its centre; a sphere's centre and radius; a capsule's
/// or cylinder's end A with the radius, then end B.
/// </summary>
public readonly record struct MobyCollisionPrimitive(
    /// <summary>The shape in bind-pose model space.</summary>
    CollisionPrimitive Shape,
    byte PaletteIndex,
    /// <summary>0xFF on a primitive that is always on; otherwise it follows bangle (byte + 1).</summary>
    byte BangleByte,
    /// <summary>The bone that moves the shape (a capsule's or cylinder's end A).</summary>
    ushort BoneA,
    /// <summary>The bone that moves a capsule's or cylinder's end B.</summary>
    ushort BoneB,
    ushort Index,
    byte Mask,
    /// <summary>Descriptor +0x0C: 1.0 on most capsules and cylinders, 0.0 otherwise. Meaning not closed.</summary>
    float Value)
{
    public CollisionPrimitiveKind Kind => Shape.Kind;

    /// <summary>The class bangle (file index) the primitive goes with, or null when it is always on.</summary>
    public int? Bangle => BangleByte == 0xFF ? null : BangleByte + 1;
}

/// <summary>A Moby class's collision mesh, in bind-pose model space.</summary>
public sealed class MobyCollisionMesh(Vector3[] positions, int[] indices, byte[] materials, byte[] triangleFlags, int[]? vertexBones)
{
    public Vector3[] Positions { get; } = positions;
    /// <summary>Three per triangle.</summary>
    public int[] Indices { get; } = indices;
    /// <summary>Palette index of each triangle.</summary>
    public byte[] Materials { get; } = materials;
    /// <summary>Bits 27-31 of each triangle word, as on level collision.</summary>
    public byte[] TriangleFlags { get; } = triangleFlags;
    /// <summary>The bone each vertex follows; null on a rigid mesh.</summary>
    public int[]? VertexBones { get; } = vertexBones;
    public bool IsSkinned => VertexBones != null;
    public int TriangleCount => Materials.Length;
}
