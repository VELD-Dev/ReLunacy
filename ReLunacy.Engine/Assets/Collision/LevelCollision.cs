using System.Buffers.Binary;
using System.Numerics;

namespace ReLunacy.Engine.Assets.Collision;

/// <summary>
/// A level's (old engine) or a region's (new engine) static collision: a list of instances placing
/// shapes stored once in a shared blob. See docs/reverse/collision.md for the layout and how each field
/// was checked.
///
/// Both engines store the same thing. Tools of Destruction and Quest for Booty ship it as a raw
/// big-endian collision.dat next to main.dat; QForce, All 4 One, Into the Nexus and A Crack in Time
/// wrap it in section 0x2900 of each region's collision.dat. The header and the triangle mesh are
/// identical, the instance record holds the same fields in a different order, and the new engine adds
/// a second shape kind, sets of analytic primitives (<see cref="CollisionPrimitiveSet"/>).
///
/// Parser first written for ToD by @sowelilili in ReLunacy-ToD-tools; extended here to QfB and the
/// new engine.
/// </summary>
public sealed class LevelCollision
{
    public IReadOnlyList<CollisionInstance> Instances { get; }
    public IReadOnlyList<CollisionShape> Shapes { get; }
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }

    /// <summary>Header +0x20: edge of a cell of the spatial grid the game queries through (10 on ToD,
    /// 12 on the new engine). +0x24 is its reciprocal.</summary>
    public float GridCellSize { get; }

    /// <summary>The new engine's instance layout (see <see cref="Read"/>), which a save writes back in.</summary>
    public bool IsNewEngine { get; private init; }

    private LevelCollision(IReadOnlyList<CollisionInstance> instances, IReadOnlyList<CollisionShape> shapes,
        Vector3 boundsMin, Vector3 boundsMax, float gridCellSize)
    {
        Instances = instances;
        Shapes = shapes;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        GridCellSize = gridCellSize;
    }

    private const int HeaderSize = 0x80;
    private const int InstanceStride = 0x80;

    /// <summary>Instance flags-and-id word, bit 29: the instance is a Tie placement's collision (its
    /// matrix is the placement's). Clear on world chunks, whose matrix is identity.</summary>
    private const uint TieInstanceFlag = 0x20000000;

    /// <summary>Instance shape flags, bit 0x400 (new engine only): the shape is a primitive set rather
    /// than a triangle mesh. Matches the shape's own header on every instance of all four games.</summary>
    private const ushort PrimitiveSetFlag = 0x400;

    /// <summary>Tools of Destruction / Quest for Booty: the whole collision.dat.</summary>
    public static LevelCollision? ReadOldEngine(byte[] data) => Read(data, newEngine: false);

    /// <summary>New engine: the payload of a region collision.dat's section 0x2900.</summary>
    public static LevelCollision? ReadNewEngine(byte[] data) => Read(data, newEngine: true);

    private static LevelCollision? Read(byte[] data, bool newEngine)
    {
        if (data.Length < HeaderSize) return null;
        ReadOnlySpan<byte> d = data;

        uint instanceOffset = U32(d, 0x00);
        uint instanceCount = U32(d, 0x04);
        uint shapeBase = U32(d, 0x08);
        uint shapeBlobLength = U32(d, 0x0C);
        if (instanceCount == 0) return null;
        if (instanceOffset + (ulong)instanceCount * InstanceStride > (ulong)data.Length ||
            shapeBase + (ulong)shapeBlobLength > (ulong)data.Length)
        {
            Console.WriteLine($"Collision: header points past the end of the data (0x{data.Length:X} bytes); skipped.");
            return null;
        }

        var shapes = new List<CollisionShape>();
        var shapeAt = new Dictionary<uint, CollisionShape>();
        var instances = new List<CollisionInstance>((int)instanceCount);
        int skipped = 0;
        for (int i = 0; i < instanceCount; i++)
        {
            int o = checked((int)(instanceOffset + i * InstanceStride));
            // Same fields in both engines, in a different order:
            //   ToD/QfB:    +0x00 matrix, +0x40 sphere, +0x50 box min, +0x60 box max,
            //               +0x70 flags-and-id, +0x74 shape offset, +0x78 length (low) | shape flags (high)
            //   new engine: +0x00 box min, +0x10 box max, +0x20 shape offset,
            //               +0x24 length (high) | shape flags (low), +0x28 flags-and-id,
            //               +0x30 matrix, +0x70 sphere
            int matrixAt, sphereAt, minAt, maxAt, idAt, shapeAtOffset;
            uint lengthAndFlags;
            ushort shapeLength, shapeFlags;
            if (newEngine)
            {
                (minAt, maxAt, shapeAtOffset, idAt, matrixAt, sphereAt) = (0x00, 0x10, 0x20, 0x28, 0x30, 0x70);
                lengthAndFlags = U32(d, o + 0x24);
                (shapeLength, shapeFlags) = ((ushort)(lengthAndFlags >> 16), (ushort)lengthAndFlags);
            }
            else
            {
                (matrixAt, sphereAt, minAt, maxAt, idAt, shapeAtOffset) = (0x00, 0x40, 0x50, 0x60, 0x70, 0x74);
                lengthAndFlags = U32(d, o + 0x78);
                (shapeLength, shapeFlags) = ((ushort)lengthAndFlags, (ushort)(lengthAndFlags >> 16));
            }

            uint shapeOffset = U32(d, o + shapeAtOffset);
            if (!shapeAt.TryGetValue(shapeOffset, out var shape))
            {
                int at = checked((int)(shapeBase + shapeOffset));
                shape = shapeOffset >= shapeBlobLength ? null
                    : (shapeFlags & PrimitiveSetFlag) != 0
                        ? CollisionPrimitiveSet.Read(d, at, shapeLength)
                        : CollisionMesh.Read(d, at, shapeLength);
                if (shape != null) shape.Offset = at;
                if (shape == null)
                {
                    skipped++;
                    continue;
                }
                shapeAt[shapeOffset] = shape;
                shapes.Add(shape);
            }

            uint idWord = U32(d, o + idAt);
            instances.Add(new CollisionInstance(
                Matrix(d, o + matrixAt),
                Vec4(d, o + sphereAt),
                Vec3(d, o + minAt),
                Vec3(d, o + maxAt),
                (idWord & TieInstanceFlag) != 0,
                idWord & 0x0FFFFFFF,
                shapeFlags,
                shape) { RecordOffset = o });
        }

        if (skipped != 0)
            Console.WriteLine($"Collision: {skipped} instance(s) point at a shape that does not parse; skipped.");

        return new LevelCollision(instances, shapes, Vec3(d, 0x40), Vec3(d, 0x50), F32(d, 0x20)) { IsNewEngine = newEngine };
    }

    internal static uint U32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d[o..]);
    internal static ushort U16(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt16BigEndian(d[o..]);
    internal static int S32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadInt32BigEndian(d[o..]);
    internal static float F32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadSingleBigEndian(d[o..]);
    private static Vector3 Vec3(ReadOnlySpan<byte> d, int o) => new(F32(d, o), F32(d, o + 4), F32(d, o + 8));
    private static Vector4 Vec4(ReadOnlySpan<byte> d, int o) => new(F32(d, o), F32(d, o + 4), F32(d, o + 8), F32(d, o + 12));

    /// <summary>Row-vector mat4, translation in the fourth row, like every other placement.</summary>
    private static Matrix4x4 Matrix(ReadOnlySpan<byte> d, int o) => new(
        F32(d, o + 0x00), F32(d, o + 0x04), F32(d, o + 0x08), F32(d, o + 0x0C),
        F32(d, o + 0x10), F32(d, o + 0x14), F32(d, o + 0x18), F32(d, o + 0x1C),
        F32(d, o + 0x20), F32(d, o + 0x24), F32(d, o + 0x28), F32(d, o + 0x2C),
        F32(d, o + 0x30), F32(d, o + 0x34), F32(d, o + 0x38), F32(d, o + 0x3C));
}

/// <summary>One placement of a collision shape (0x80 bytes in both engines, see
/// <see cref="LevelCollision"/> for where each field sits).</summary>
public sealed class CollisionInstance(Matrix4x4 transform, Vector4 boundingSphere, Vector3 boundsMin, Vector3 boundsMax,
    bool isTie, uint id, ushort shapeFlags, CollisionShape shape)
{
    /// <summary>Shape to world, row-vector convention (translation in M41..M43). Identity on world
    /// chunks, whose shape is already in world space. On a Tie's collision it equals the Tie
    /// placement's matrix, which is how the editor moves it with the Tie (with the box and sphere).</summary>
    public Matrix4x4 Transform { get; set; } = transform;

    /// <summary>World-space bounding sphere.</summary>
    public Vector4 BoundingSphere { get; set; } = boundingSphere;

    /// <summary>World-space box. Tight on every new-engine instance; on ToD about 2% have one side
    /// trimmed short of their transformed mesh.</summary>
    public Vector3 BoundsMin { get; set; } = boundsMin;
    public Vector3 BoundsMax { get; set; } = boundsMax;

    /// <summary>True for a Tie placement's collision, false for world (terrain and shell) chunks.</summary>
    public bool IsTie { get; } = isTie;

    /// <summary>Low 28 bits of the flags-and-id word. Counts up across the chunks of one Tie class;
    /// meaning not closed.</summary>
    public uint Id { get; } = id;

    /// <summary>The 16 flag bits beside the shape's length. 0x400 = primitive set (new engine).
    /// 0x02 and 0x08 are set on every instance of all six games, 0x40 on all but 0.2%; 0x01, 0x04,
    /// 0x20 and 0x100 vary. Not otherwise closed. Settable for the collision editor, which keeps 0x400
    /// as it is, since it says what the shape is.</summary>
    public ushort ShapeFlags { get; set; } = shapeFlags;

    public CollisionShape Shape { get; } = shape;

    /// <summary>Where the instance's record starts in the collision data it was read from (the old engine's whole
    /// file, the new engine's section 0x2900), for a save to write it back.</summary>
    public int RecordOffset { get; init; } = -1;
}

/// <summary>What a <see cref="CollisionInstance"/> places: a triangle mesh or a set of primitives.</summary>
public abstract class CollisionShape
{
    /// <summary>Where the shape starts in the collision data it was read from (see <see cref="CollisionInstance.RecordOffset"/>).</summary>
    public int Offset { get; internal set; } = -1;
}

/// <summary>
/// A collision triangle mesh. Header (big-endian u16): +0x00 vertex count, +0x02 vertex block offset,
/// +0x04 1 (ToD u16) or 0x100 (new engine), +0x06 triangle count, +0x08 triangle offset, +0x0C
/// material offset. ToD then has an oriented box at +0x14..+0x50 and its vertex block at 0x60; the new
/// engine has no box and its vertex block at 0x10.
///
/// The vertex block is an s32 x/y/z origin, 4 bytes of padding, then groups of 8 vertices stored
/// x[8], y[8], z[8] as u16, each line in the order 0,4,1,5,2,6,3,7; position = (origin + value) / 1024.
/// A triangle is one u32: indices in bits 18-26, 9-17 and 0-8, flags in 27-31. One material byte per
/// triangle follows.
/// </summary>
public sealed class CollisionMesh : CollisionShape
{
    public Vector3[] Positions { get; }

    /// <summary>Three vertex indices per triangle, in the file's winding (which is not consistent:
    /// cobalia's world floors face up and down about equally).</summary>
    public int[] Indices { get; }

    /// <summary>Collision material of each triangle, what the game reads as the surface type. Ids run
    /// 0..67 over ToD and QfB; their in-game meaning is not closed.</summary>
    public byte[] Materials { get; }

    /// <summary>Bits 27-31 of each triangle word. Bit 0 keeps Ratchet from wall jumping off the face
    /// (the wall probe rejects it); the others are not closed.</summary>
    public byte[] TriangleFlags { get; }

    public int TriangleCount => Materials.Length;

    /// <summary>Where the triangle words and the material bytes start, from the shape's own start.</summary>
    public int TriangleOffset { get; private init; }
    public int MaterialOffset { get; private init; }

    private CollisionMesh(Vector3[] positions, int[] indices, byte[] materials, byte[] triangleFlags)
    {
        Positions = positions;
        Indices = indices;
        Materials = materials;
        TriangleFlags = triangleFlags;
    }

    /// <summary>Mesh vertices are 16-bit offsets from a 32-bit origin, both in 1/1024 world units.</summary>
    private const float VertexScale = 1f / 1024f;

    internal static CollisionMesh? Read(ReadOnlySpan<byte> d, int o, uint length)
    {
        if (o < 0 || o + 0x10 > d.Length) return null;
        int vertexCount = LevelCollision.U16(d, o + 0x00);
        int vertexOffset = LevelCollision.U16(d, o + 0x02);
        int triangleCount = LevelCollision.U16(d, o + 0x06);
        int triangleOffset = LevelCollision.U16(d, o + 0x08);
        int materialOffset = LevelCollision.U16(d, o + 0x0C);
        int groups = (vertexCount + 7) / 8;
        int end = Math.Max(Math.Max(vertexOffset + 0x10 + groups * 0x30, triangleOffset + triangleCount * 4), materialOffset + triangleCount);
        if (vertexCount == 0 || triangleCount == 0 || o + end > d.Length || (length != 0 && end > length))
            return null;

        int ox = LevelCollision.S32(d, o + vertexOffset);
        int oy = LevelCollision.S32(d, o + vertexOffset + 4);
        int oz = LevelCollision.S32(d, o + vertexOffset + 8);
        var positions = new Vector3[vertexCount];
        for (int v = 0; v < vertexCount; v++)
        {
            // Within a group, vertices 0-3 sit in the even u16 slots and 4-7 in the odd ones. Read in
            // plain order instead, flat quads come out non-planar (46% planar on metropolis against
            // 96.5%), and new-engine vertices leave their instance's box.
            int k = v & 7;
            int slot = k < 4 ? k * 2 : (k - 4) * 2 + 1;
            int g = o + vertexOffset + 0x10 + (v >> 3) * 0x30 + slot * 2;
            positions[v] = new Vector3(
                (ox + LevelCollision.U16(d, g)) * VertexScale,
                (oy + LevelCollision.U16(d, g + 0x10)) * VertexScale,
                (oz + LevelCollision.U16(d, g + 0x20)) * VertexScale);
        }

        var indices = new int[triangleCount * 3];
        var flags = new byte[triangleCount];
        for (int t = 0; t < triangleCount; t++)
        {
            uint word = LevelCollision.U32(d, o + triangleOffset + t * 4);
            int a = (int)((word >> 18) & 0x1FF), b = (int)((word >> 9) & 0x1FF), c = (int)(word & 0x1FF);
            if (a >= vertexCount || b >= vertexCount || c >= vertexCount) return null;
            indices[t * 3 + 0] = a;
            indices[t * 3 + 1] = b;
            indices[t * 3 + 2] = c;
            flags[t] = (byte)(word >> 27);
        }

        byte[] materials = d.Slice(o + materialOffset, triangleCount).ToArray();
        return new CollisionMesh(positions, indices, materials, flags) { TriangleOffset = triangleOffset, MaterialOffset = materialOffset };
    }
}

public enum CollisionPrimitiveKind : byte
{
    Box = 0,
    Sphere = 1,
    Capsule = 2,
    Cylinder = 3,
}

/// <summary>One analytic collision shape of a <see cref="CollisionPrimitiveSet"/>, in the set's space.</summary>
public readonly record struct CollisionPrimitive(
    CollisionPrimitiveKind Kind,
    /// <summary>Box and sphere: the centre. Capsule and cylinder: one end of the axis.</summary>
    Vector3 A,
    /// <summary>Capsule and cylinder: the other end of the axis. Unused otherwise.</summary>
    Vector3 B,
    /// <summary>Sphere, capsule and cylinder radius. Unused for a box.</summary>
    float Radius,
    /// <summary>Box: rows are its local axes, as a rotation matrix (row-vector convention).</summary>
    Matrix4x4 Rotation,
    /// <summary>Box: half extent along each of its axes.</summary>
    Vector3 HalfExtents,
    /// <summary>Descriptor byte +0x0A: 0x1F on most shapes, 0x17 on most cylinders, also 0x0F, 0x27,
    /// 0x2F, 0x37, 0x3F. Looks like a mask; not closed.</summary>
    byte Mask);

/// <summary>
/// New engine only: a set of up to 16 boxes, spheres, capsules and cylinders, flagged by 0x400 in the
/// instance's shape flags. 20-50% of instances on QForce, All 4 One and Into the Nexus use one; 1% on
/// A Crack in Time.
///
/// Header: +0x00 u16 offset of the first block (0x20), +0x02 u16 primitive count, +0x10 four u8 counts,
/// one per kind (box, sphere, capsule, cylinder), summing to the total. Then for each kind, for each
/// group of four of that kind: four 16-byte descriptors, then the group's data as SoA vec4 rows (one
/// lane per primitive, unused lanes padded with 1.0). A box group is 15 rows (centre xyz, 3x3 rotation,
/// half extents xyz), a sphere 4 (centre, radius), a capsule and a cylinder 7 (two ends, radius).
///
/// A descriptor is: +0x00 kind code (4 box, 5 sphere, 6 capsule, 8 cylinder), +0x01 0..2, +0x03 0xFF,
/// +0x08 u16 index in the set, +0x0A mask byte, +0x0C 1.0 or 0.0.
///
/// Checked over every primitive set of all four games: the length this layout gives equals the
/// instance's recorded shape length on all of them, and the shapes' combined box equals the
/// instance's stored box (to 0.02) on 131082, and lies inside it on the other 51. Capsule and cylinder
/// are told apart the same way: kind 2 is tight only as a capsule, kind 3 only as a cylinder.
/// </summary>
public sealed class CollisionPrimitiveSet : CollisionShape
{
    public IReadOnlyList<CollisionPrimitive> Primitives { get; }

    private CollisionPrimitiveSet(IReadOnlyList<CollisionPrimitive> primitives) => Primitives = primitives;

    /// <summary>SoA rows per group of four, by kind.</summary>
    private static readonly int[] RowsPerGroup = [15, 4, 7, 7];

    internal static CollisionPrimitiveSet? Read(ReadOnlySpan<byte> d, int o, uint length)
    {
        if (o < 0 || o + 0x20 > d.Length) return null;
        int p = o + LevelCollision.U16(d, o + 0x00);
        int total = LevelCollision.U16(d, o + 0x02);
        var primitives = new List<CollisionPrimitive>(total);
        var row = new float[15];
        int counted = 0;
        for (int kind = 0; kind < 4; kind++)
        {
            int count = d[o + 0x10 + kind];
            counted += count;
            int groupSize = 0x40 + RowsPerGroup[kind] * 0x10;
            int groups = (count + 3) / 4;
            if (p + groups * groupSize > d.Length) return null;
            for (int k = 0; k < count; k++)
            {
                int group = p + (k >> 2) * groupSize;
                int lane = k & 3;
                byte mask = d[group + lane * 0x10 + 0x0A];
                for (int r = 0; r < RowsPerGroup[kind]; r++)
                    row[r] = LevelCollision.F32(d, group + 0x40 + r * 0x10 + lane * 4);
                Vector3 Rows(int r) => new(row[r], row[r + 1], row[r + 2]);
                primitives.Add((CollisionPrimitiveKind)kind switch
                {
                    CollisionPrimitiveKind.Box => new CollisionPrimitive(CollisionPrimitiveKind.Box, Rows(0), default, 0f,
                        new Matrix4x4(row[3], row[4], row[5], 0f, row[6], row[7], row[8], 0f, row[9], row[10], row[11], 0f, 0f, 0f, 0f, 1f),
                        Rows(12), mask),
                    CollisionPrimitiveKind.Sphere => new CollisionPrimitive(CollisionPrimitiveKind.Sphere, Rows(0), default, row[3],
                        Matrix4x4.Identity, default, mask),
                    var k2 => new CollisionPrimitive(k2, Rows(0), Rows(3), row[6], Matrix4x4.Identity, default, mask),
                });
            }
            p += groups * groupSize;
        }

        if (counted != total || primitives.Count == 0 || (length != 0 && p - o != length))
            return null;
        return new CollisionPrimitiveSet(primitives);
    }
}
