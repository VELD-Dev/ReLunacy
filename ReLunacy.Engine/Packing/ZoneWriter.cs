using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Packing;

/// <summary>
/// Writes moved Tie placements and UFrags back into the file their zones were read from - zones.dat on the new
/// engine (each zone at its asset-lookup offset), main.dat on the old - in place.
///
/// A Tie placement's 0x80-byte record (TieInstance): +0x00 its row-major matrix, +0x40 its world bounding sphere,
/// and on the new engine +0x5C a byte that is 1 when the matrix is mirrored (the draw flips its face culling on
/// it). An edited placement's matrix is written as the file's matrix moved by the edit (file x saved^-1 x now), so
/// a mirrored or sheared placement keeps exactly what the editor's decompose would lose; its sphere is carried by
/// the same move; the mirrored byte follows the new matrix. The fourth column is left as the file has it.
///
/// A UFrag's record (UFragMetadata) holds a position - its anchor, x256 (+0x70 new engine, +0x60 old) - and a
/// world bounding sphere (+0x30), and no rotation or scale: a moved UFrag is written as both moved by how far its
/// anchor moved. A UFrag rotated or scaled in the editor is reported: that can not be written.
///
/// A record several entities share (a zone several regions list) is written from the one that was edited.
///
/// New engine: the game walks a zone's Tie placements only through its clusters (0x72D0 {sphere, first, first, 0,
/// u16 count, 0}, docs/reverse/acit_zones_coverage.md) and culls a cluster by its sphere, so a placement moved out
/// of its cluster's sphere would vanish with it: the sphere is grown to hold the moved placement's again.
/// </summary>
public static class ZoneWriter
{
    /// <summary>The file Ties and UFrags are read from.</summary>
    public static string FileName(bool oldEngine) => oldEngine ? "main.dat" : "zones.dat";

    public static (byte[] Bytes, List<string> Changes, List<string> Problems) Patch(
        byte[] original, IEnumerable<EntityTie> ties, IEnumerable<EntityUFrag> ufrags, bool oldEngine)
    {
        byte[] file = (byte[])original.Clone();
        var changes = new List<string>();
        var problems = new List<string>();
        var written = new HashSet<long>();

        int tieCount = 0;
        foreach (var tie in ties)
        {
            var now = tie.Transform.GetMatrix();
            if (tie.SourceOffset < 0 || Same(now, tie.SavedMatrix)) continue;
            long at = tie.SourceOffset;
            if (at + 0x80 > file.Length) { problems.Add($"Tie {tie.Name}: its record is outside the file, not written"); continue; }
            if (!written.Add(at)) { problems.Add($"Tie {tie.Name}: edited in two regions; the first edit was written"); continue; }

            var held = ReadMatrix(original, at);
            if (!Matrix4x4.Invert(tie.SavedMatrix, out var savedInverse) || !Matrix4x4.Invert(held, out var heldInverse))
            {
                problems.Add($"Tie {tie.Name}: its matrix can not be inverted, not written");
                continue;
            }
            var moved = held * savedInverse * now;
            WriteMatrix(file, at, moved);

            // The sphere goes where the move takes it; its radius grows with the largest axis's scale.
            var sphere = new Vector4(F(original, at + 0x40), F(original, at + 0x44), F(original, at + 0x48), F(original, at + 0x4C));
            var centre = Vector3.Transform(Vector3.Transform(new Vector3(sphere.X, sphere.Y, sphere.Z), heldInverse), moved);
            float growth = MaxAxis(moved) / MathF.Max(MaxAxis(held), 1e-6f);
            Put(file, at + 0x40, centre.X); Put(file, at + 0x44, centre.Y); Put(file, at + 0x48, centre.Z);
            Put(file, at + 0x4C, sphere.W * growth);
            if (!oldEngine) file[at + 0x5C] = (byte)(Determinant3(moved) < 0f ? 1 : 0);
            tieCount++;
        }
        if (tieCount > 0) changes.Add($"{tieCount} Tie placement(s): matrix and bounds");
        if (tieCount > 0 && !oldEngine)
        {
            int grown = GrowClusters(file, written);
            if (grown > 0) changes.Add($"{grown} Tie cluster sphere(s) grown to hold moved placements");
        }

        int ufragCount = 0, turned = 0;
        foreach (var ufrag in ufrags)
        {
            var t = ufrag.Transform;
            if (t.Rotation != Quaternion.Identity || MathF.Abs(t.Scale.X * EntityUFrag.UFragQuantisation - 1f) > 1e-4f
                || MathF.Abs(t.Scale.Y * EntityUFrag.UFragQuantisation - 1f) > 1e-4f || MathF.Abs(t.Scale.Z * EntityUFrag.UFragQuantisation - 1f) > 1e-4f)
                turned++;
            var delta = t.Translation - ufrag.SavedTranslation;
            long at = ufrag.UFrag.SourceOffset;
            if (at < 0 || delta.LengthSquared() < 1e-8f) continue;
            if (at + 0x80 > file.Length || !written.Add(at)) continue;
            long anchor = at + (oldEngine ? 0x60 : 0x70);
            for (int k = 0; k < 3; k++)
            {
                Put(file, anchor + k * 4, F(original, anchor + k * 4) + delta[k] * EntityUFrag.UFragQuantisation);
                Put(file, at + 0x30 + k * 4, F(original, at + 0x30 + k * 4) + delta[k]);
            }
            ufragCount++;
        }
        if (ufragCount > 0) changes.Add($"{ufragCount} UFrag(s): position and bounds");
        if (turned > 0) problems.Add($"{turned} UFrag(s) rotated or scaled: a UFrag's record holds only a position, so only moves are written");

        return (file, changes, problems);
    }

    /// <summary>What a save of <paramref name="tie"/> leaves in its record of <paramref name="zones"/> (the file as the
    /// editor loaded it, what <see cref="Patch"/> is given): the matrix (fourth column affine) and the 16 sphere bytes,
    /// moved as <see cref="Patch"/> moves them when the Tie was moved, else the file's. Null when the record is not in the
    /// file or its matrix can not be inverted.</summary>
    public static (Matrix4x4 Matrix, byte[] Sphere)? TiePose(byte[] zones, EntityTie tie)
    {
        long at = tie.SourceOffset;
        if (at < 0 || at + 0x80 > zones.Length) return null;
        var held = ReadMatrix(zones, at);
        var now = tie.Transform.GetMatrix();
        if (Same(now, tie.SavedMatrix)) return (held, zones.AsSpan((int)at + 0x40, 16).ToArray());
        if (!Matrix4x4.Invert(tie.SavedMatrix, out var savedInverse) || !Matrix4x4.Invert(held, out var heldInverse)) return null;
        var moved = held * savedInverse * now;
        var copy = new byte[0x50];
        WriteMatrix(copy, 0, moved);
        var sphere = new Vector4(F(zones, at + 0x40), F(zones, at + 0x44), F(zones, at + 0x48), F(zones, at + 0x4C));
        var centre = Vector3.Transform(Vector3.Transform(new Vector3(sphere.X, sphere.Y, sphere.Z), heldInverse), moved);
        float growth = MaxAxis(moved) / MathF.Max(MaxAxis(held), 1e-6f);
        Put(copy, 0x40, centre.X); Put(copy, 0x44, centre.Y); Put(copy, 0x48, centre.Z); Put(copy, 0x4C, sphere.W * growth);
        return (ReadMatrix(copy, 0), copy.AsSpan(0x40, 16).ToArray());
    }

    /// <summary>The 16 sphere bytes a save of <paramref name="ufrag"/> leaves in its record of <paramref name="zones"/>
    /// (moved as <see cref="Patch"/> moves it); null when the record is not in the file.</summary>
    public static byte[]? UFragSphere(byte[] zones, EntityUFrag ufrag)
    {
        long at = ufrag.UFrag.SourceOffset;
        if (at < 0 || at + 0x80 > zones.Length) return null;
        var sphere = zones.AsSpan((int)at + 0x30, 16).ToArray();
        var delta = ufrag.Transform.Translation - ufrag.SavedTranslation;
        if (delta.LengthSquared() < 1e-8f) return sphere;
        for (int k = 0; k < 3; k++) Put(sphere, k * 4, F(zones, at + 0x30 + k * 4) + delta[k]);
        return sphere;
    }

    /// <summary>Every zone blob of zones.dat (each an IGHW at a 0x80 boundary): the Tie clusters holding a record of
    /// <paramref name="records"/> get a sphere that holds the record's sphere. Returns how many grew.</summary>
    private static int GrowClusters(byte[] file, HashSet<long> records)
    {
        int grown = 0;
        int at = 0;
        while (at + 0x20 <= file.Length)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at)) != IghwRelayout.Magic) { at += 0x80; continue; }
            var blob = file.AsSpan(at);
            int length = BinaryPrimitives.ReadInt32BigEndian(blob[0x10..]) + 4 * BinaryPrimitives.ReadInt32BigEndian(blob[0x14..]);
            var sections = IghwRelayout.Sections(blob);
            var placements = IghwRelayout.Find(sections, 0x7240);
            var clusters = IghwRelayout.Find(sections, 0x72D0);
            if (placements != null && clusters != null && placements.LengthWord == 0x80 && clusters.LengthWord == 0x20)
                for (int c = 0; c < clusters.Count; c++)
                {
                    long cluster = at + clusters.Offset + 0x20L * c;
                    int first = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan((int)cluster + 0x10));
                    int count = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan((int)cluster + 0x1C));
                    var sphere = new Vector4(F(file, cluster), F(file, cluster + 4), F(file, cluster + 8), F(file, cluster + 12));
                    bool changed = false;
                    for (int k = first; k < first + count && k < placements.Count; k++)
                    {
                        long record = at + placements.Offset + 0x80L * k;
                        if (!records.Contains(record)) continue;
                        var s = new Vector4(F(file, record + 0x40), F(file, record + 0x44), F(file, record + 0x48), F(file, record + 0x4C));
                        var d = Vector3.Distance(new Vector3(sphere.X, sphere.Y, sphere.Z), new Vector3(s.X, s.Y, s.Z));
                        if (d + s.W <= sphere.W) continue;
                        float r = (d + sphere.W + s.W) * 0.5f;
                        if (s.W >= d + sphere.W) sphere = s;
                        else
                        {
                            var c0 = new Vector3(sphere.X, sphere.Y, sphere.Z);
                            sphere = new Vector4(c0 + (new Vector3(s.X, s.Y, s.Z) - c0) * ((r - sphere.W) / d), r);
                        }
                        sphere.W = sphere.W * 1.0001f + 0.001f;
                        changed = true;
                    }
                    if (!changed) continue;
                    Put(file, cluster, sphere.X); Put(file, cluster + 4, sphere.Y); Put(file, cluster + 8, sphere.Z); Put(file, cluster + 12, sphere.W);
                    grown++;
                }
            at += Math.Max(0x80, (length + 0x7F) & ~0x7F);
        }
        return grown;
    }

    private static bool Same(Matrix4x4 a, Matrix4x4 b)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                if (MathF.Abs(a[r, c] - b[r, c]) > 1e-4f * MathF.Max(1f, MathF.Abs(b[r, c]))) return false;
        return true;
    }

    private static float MaxAxis(Matrix4x4 m) => MathF.Max(new Vector3(m.M11, m.M12, m.M13).Length(),
        MathF.Max(new Vector3(m.M21, m.M22, m.M23).Length(), new Vector3(m.M31, m.M32, m.M33).Length()));

    private static float Determinant3(Matrix4x4 m) =>
        m.M11 * (m.M22 * m.M33 - m.M23 * m.M32) - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31) + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);

    private static float F(byte[] file, long at) => BinaryPrimitives.ReadSingleBigEndian(file.AsSpan((int)at));
    private static void Put(byte[] file, long at, float value) => BinaryPrimitives.WriteSingleBigEndian(file.AsSpan((int)at), value);

    /// <summary>The record's matrix with its fourth column as an affine one, for the arithmetic.</summary>
    private static Matrix4x4 ReadMatrix(byte[] file, long at)
    {
        var m = Matrix4x4.Identity;
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 3; c++)
                m[r, c] = F(file, at + (r * 4 + c) * 4);
        return m;
    }

    /// <summary>The first three columns; the fourth keeps the file's bytes.</summary>
    private static void WriteMatrix(byte[] file, long at, Matrix4x4 m)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 3; c++)
                Put(file, at + (r * 4 + c) * 4, m[r, c]);
    }
}
