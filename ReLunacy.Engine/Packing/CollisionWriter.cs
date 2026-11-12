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
/// Not written: an instance moved with its Tie (matrix, box, sphere). The Tie itself is not saved yet
/// (zones.dat), and the spatial grid the game finds collision through (header +0x30) is not decoded, so a
/// moved instance could fall out of the cells it is listed in. Those are reported instead.
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

        int baseOffset = 0;
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
        foreach (var instance in collision.Instances)
        {
            if (instance.RecordOffset < 0) continue;
            int record = baseOffset + instance.RecordOffset;
            int at = record + (collision.IsNewEngine ? 0x26 : 0x78);
            if (at + 2 > file.Length) continue;
            ushort held = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at));
            ushort value = (ushort)((instance.ShapeFlags & ~PrimitiveSetFlag) | (held & PrimitiveSetFlag));
            if (value != held) { BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(at), value); flagged++; }

            int matrix = record + (collision.IsNewEngine ? 0x30 : 0x00);
            float heldX = BinaryPrimitives.ReadSingleBigEndian(file.AsSpan(matrix + 0x30));
            float heldY = BinaryPrimitives.ReadSingleBigEndian(file.AsSpan(matrix + 0x34));
            float heldZ = BinaryPrimitives.ReadSingleBigEndian(file.AsSpan(matrix + 0x38));
            var t = instance.Transform.Translation;
            if (MathF.Abs(t.X - heldX) > 1e-3f || MathF.Abs(t.Y - heldY) > 1e-3f || MathF.Abs(t.Z - heldZ) > 1e-3f) moved++;
        }
        if (flagged > 0) changes.Add($"{flagged} collision instance(s): flags");
        if (moved > 0)
            problems.Add($"{moved} collision instance(s) moved with their Tie, not written: Ties are not saved yet, and the collision grid is not decoded");

        return (file, changes, problems);
    }

    /// <summary>The file a level's (old engine) or a region's (new engine) collision is read from.</summary>
    public static string FileName(FileManager files, string region) =>
        files.isOld ? CollisionReader.FileName : $"{region}/{CollisionReader.FileName}";
}
