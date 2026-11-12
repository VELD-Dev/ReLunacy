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
