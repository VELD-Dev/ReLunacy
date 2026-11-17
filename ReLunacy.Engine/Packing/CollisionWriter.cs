using System.Buffers.Binary;
using ReLunacy.Engine.Assets.Collision;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;

namespace ReLunacy.Engine.Packing;

/// <summary>
/// Writes the collision editor's changes back into a collision.dat, in place: what CollisionEditor edits and the
/// file holds at a fixed size.
///   - a mesh triangle's material: one byte per triangle at the mesh's material offset;
///   - a mesh triangle's flags: bits 27-31 of its triangle word, whose vertex indices are kept;
///   - an instance's shape flags: the 16 bits beside the shape length (new engine +0x26, old engine +0x78), with
///     0x400 (primitive set) kept as the file has it, since it says what the shape is.
/// A shape is stored once and placed by every instance using it, so a face edited once is written once.
///
///   - an instance moved with its Tie (CollisionEditor.FollowEntity): its matrix, box and sphere. On the new engine
///     the bounding-volume hierarchy the game finds collision through (payload +0x30, see <see cref="CollisionBvh"/>)
///     is refitted: the leaf holding a moved instance and every node above it get the union of their contents
///     again, and the header bounds the union of every instance. A move that grows the header bounds is reported
///     (the game sizes a run-time grid from them). The old engine's grid is not decoded: a move there is reported.
///
/// The data is the old engine's whole collision.dat, or the new engine's section 0x2900 of a region's.
/// </summary>
public static class CollisionWriter
{
    private const ushort PrimitiveSetFlag = 0x400;

    /// <summary>A copy of <paramref name="original"/> (a whole collision.dat) with <paramref name="collision"/>'s
    /// edits written in, and what changed.</summary>
    public static (byte[] Bytes, List<string> Changes, List<string> Problems) Patch(byte[] original, LevelCollision collision)
    {
        var changes = new List<string>();
        var problems = new List<string>();
        byte[] file = (byte[])original.Clone();

        int baseOffset = 0, payloadSize = file.Length;
        if (collision.IsNewEngine)
        {
            var ig = new IGFile(new MemoryStream(original, writable: false));
            var section = ig.QuerySection(CollisionReader.NewEngineSection);
            if (section.id != CollisionReader.NewEngineSection)
            {
                problems.Add("collision.dat has no section 0x2900, not written");
                return (file, changes, problems);
            }
            baseOffset = checked((int)section.offset);
            payloadSize = checked((int)(section.count * section.length));
        }

        // The collision's offsets are those of the data it was read from. A file that has grown since (a save that added
        // Ties, with no reload) holds its shapes elsewhere, and writing at the old offsets would put one shape's material
        // bytes and triangle flags into another's.
        if (BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(baseOffset + 0x04)) != collision.HeaderInstanceCount
            || BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(baseOffset + 0x08)) != collision.HeaderShapeBase)
        {
            problems.Add("collision.dat changed since the editor read it (a save added Ties): reload the level, its collision edits are not written");
            return (file, changes, problems);
        }

        int faces = 0, meshes = 0;
        foreach (var mesh in collision.Shapes.OfType<CollisionMesh>())
        {
            if (mesh.Offset < 0) continue;
            int triangles = baseOffset + mesh.Offset + mesh.TriangleOffset;
            int materials = baseOffset + mesh.Offset + mesh.MaterialOffset;
            if (materials + mesh.TriangleCount > file.Length || triangles + mesh.TriangleCount * 4 > file.Length) continue;
            int before = faces;
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                bool changed = false;
                if (file[materials + t] != mesh.Materials[t]) { file[materials + t] = mesh.Materials[t]; changed = true; }
                var word = file.AsSpan(triangles + t * 4, 4);
                uint held = BinaryPrimitives.ReadUInt32BigEndian(word);
                uint value = (held & 0x07FFFFFFu) | ((uint)(mesh.TriangleFlags[t] & 0x1F) << 27);
                if (value != held) { BinaryPrimitives.WriteUInt32BigEndian(word, value); changed = true; }
                if (changed) faces++;
            }
            if (faces > before) meshes++;
        }
        if (faces > 0) changes.Add($"{faces} collision face(s) in {meshes} shape(s): material or flags");

        int flagged = 0, moved = 0;
        var movedIndices = new List<int>();
        int instanceOffset = collision.IsNewEngine ? BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(baseOffset)) : 0;
        foreach (var instance in collision.Instances)
        {
            if (instance.RecordOffset < 0) continue;
            int record = baseOffset + instance.RecordOffset;
            int at = record + (collision.IsNewEngine ? 0x26 : 0x78);
            if (at + 2 > file.Length) continue;
            ushort held = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at));
            ushort value = (ushort)((instance.ShapeFlags & ~PrimitiveSetFlag) | (held & PrimitiveSetFlag));
            if (value != held) { BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(at), value); flagged++; }

            // Moved with its Tie: matrix, box and sphere, all as the editor carried them (CollisionScene.MovePlacement).
            var (matrixAt, sphereAt, minAt, maxAt) = collision.IsNewEngine ? (0x30, 0x70, 0x00, 0x10) : (0x00, 0x40, 0x50, 0x60);
            var m = instance.Transform;
            bool moves = false;
            for (int r = 0; r < 4 && !moves; r++)
                for (int c = 0; c < 4 && !moves; c++)
                    moves = MathF.Abs(m[r, c] - F(file, record + matrixAt + (r * 4 + c) * 4)) > 1e-4f * MathF.Max(1f, MathF.Abs(m[r, c]));
            if (!moves) continue;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    Put(file, record + matrixAt + (r * 4 + c) * 4, m[r, c]);
            // A primitive set keeps its uniform scale in the matrix's w (+0x6C) instead of 1.
            if (collision.IsNewEngine && file[record + 0x26] == 4)
                Put(file, record + matrixAt + 0x3C, new System.Numerics.Vector3(m.M11, m.M12, m.M13).Length());
            Put3(file, record + minAt, instance.BoundsMin);
            Put3(file, record + maxAt, instance.BoundsMax);
            Put3(file, record + sphereAt, new System.Numerics.Vector3(instance.BoundingSphere.X, instance.BoundingSphere.Y, instance.BoundingSphere.Z));
            Put(file, record + sphereAt + 12, instance.BoundingSphere.W);
            moved++;
            if (collision.IsNewEngine) movedIndices.Add((instance.RecordOffset - instanceOffset) / 0x80);
        }
        if (flagged > 0) changes.Add($"{flagged} collision instance(s): flags");
        if (moved > 0)
        {
            changes.Add($"{moved} collision instance(s): moved with their Tie");
            if (!collision.IsNewEngine)
                problems.Add($"{moved} collision instance(s) moved: the game finds collision through a spatial grid that is not decoded nor rewritten, so a Tie moved far from where it was may lose its collision there");
            else
            {
                if (CollisionBvh.Refit(file.AsSpan(baseOffset, payloadSize), movedIndices))
                    problems.Add("collision moved outside the region's collision bounds: the bounds were grown, untested in game");
            }
        }

        return (file, changes, problems);
    }

    private static float F(byte[] file, int at) => BinaryPrimitives.ReadSingleBigEndian(file.AsSpan(at));
    private static void Put(byte[] file, int at, float value) => BinaryPrimitives.WriteSingleBigEndian(file.AsSpan(at), value);
    private static void Put3(byte[] file, int at, System.Numerics.Vector3 v) { Put(file, at, v.X); Put(file, at + 4, v.Y); Put(file, at + 8, v.Z); }

    /// <summary>The file a level's (old engine) or a region's (new engine) collision is read from.</summary>
    public static string FileName(FileManager files, string region) =>
        files.isOld ? CollisionReader.FileName : $"{region}/{CollisionReader.FileName}";
}
