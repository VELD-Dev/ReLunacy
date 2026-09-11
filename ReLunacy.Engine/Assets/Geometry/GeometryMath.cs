using System.Numerics;

namespace ReLunacy.Engine.Assets.Geometry;

/// <summary>Per-vertex normal/tangent derivation, used by GeometryData and export-only adapters
/// that need normals/tangents computed from raw geometry.</summary>
internal static class GeometryMath
{
    // Area-weighted vertex normals: accumulate each triangle's face normal onto its vertices, then normalize.
    // Triangle winding isn't independently confirmed; if normals point inward, flip the sign here.
    public static float[] ComputeNormals(float[] positions, uint[] indices)
    {
        int vertexCount = positions.Length / 3;
        var accum = new Vector3[vertexCount];

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var p0 = Get3(positions, i0);
            var p1 = Get3(positions, i1);
            var p2 = Get3(positions, i2);
            var faceNormal = Vector3.Cross(p1 - p0, p2 - p0);

            accum[i0] += faceNormal;
            accum[i1] += faceNormal;
            accum[i2] += faceNormal;
        }

        var result = new float[vertexCount * 3];
        for (int v = 0; v < vertexCount; v++)
        {
            var n = accum[v].LengthSquared() > 1e-12f ? Vector3.Normalize(accum[v]) : Vector3.UnitY;
            result[v * 3 + 0] = n.X;
            result[v * 3 + 1] = n.Y;
            result[v * 3 + 2] = n.Z;
        }
        return result;
    }

    /// <summary>Builds a per-vertex glTF-style tangent (Vector4: xyz direction, w = handedness
    /// sign), 4 floats per vertex. Uses `realTangents` when supplied, otherwise derives one from
    /// UV gradients (Lengyel method). The handedness sign is always derived from UV winding since
    /// the source data never carries one.</summary>
    public static float[] ComputeTangents(float[] positions, float[] uvs, float[] normals, uint[] indices, float[]? realTangents)
    {
        int vertexCount = positions.Length / 3;
        var tangentAccum = new Vector3[vertexCount];
        var bitangentAccum = new Vector3[vertexCount];
        bool hasReal = realTangents != null && realTangents.Length == vertexCount * 3;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var p0 = Get3(positions, i0);
            var p1 = Get3(positions, i1);
            var p2 = Get3(positions, i2);
            var uv0 = Get2(uvs, i0);
            var uv1 = Get2(uvs, i1);
            var uv2 = Get2(uvs, i2);

            var edge1 = p1 - p0;
            var edge2 = p2 - p0;
            var duv1 = uv1 - uv0;
            var duv2 = uv2 - uv0;

            float det = duv1.X * duv2.Y - duv2.X * duv1.Y;
            if (MathF.Abs(det) < 1e-12f)
                continue; // degenerate UV triangle (zero UV area) - no usable tangent/handedness info

            float r = 1f / det;
            var bitangent = (duv1.X * edge2 - duv2.X * edge1) * r;
            bitangentAccum[i0] += bitangent; bitangentAccum[i1] += bitangent; bitangentAccum[i2] += bitangent;

            // Accumulated even when hasReal, used only for the diagnostic check below.
            var tangent = (duv2.Y * edge1 - duv1.Y * edge2) * r;
            tangentAccum[i0] += tangent; tangentAccum[i1] += tangent; tangentAccum[i2] += tangent;
        }

        var result = new float[vertexCount * 4];
        float dotSum = 0f, orthoSum = 0f;
        int sampleCount = 0;

        for (int v = 0; v < vertexCount; v++)
        {
            var n = Get3(normals, (uint)v);
            n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;

            var t = hasReal ? Get3(realTangents!, (uint)v) : tangentAccum[v];

            if (hasReal && !_tangentDiagnosticLogged && t.LengthSquared() > 1e-12f && tangentAccum[v].LengthSquared() > 1e-12f)
            {
                dotSum += Vector3.Dot(Vector3.Normalize(t), Vector3.Normalize(tangentAccum[v]));
                orthoSum += Vector3.Dot(n, Vector3.Normalize(t));
                sampleCount++;
            }

            t -= n * Vector3.Dot(n, t);
            t = t.LengthSquared() > 1e-12f ? Vector3.Normalize(t) : ArbitraryPerpendicular(n);

            float w = Vector3.Dot(Vector3.Cross(n, t), bitangentAccum[v]) < 0f ? -1f : 1f;

            result[v * 4 + 0] = t.X;
            result[v * 4 + 1] = t.Y;
            result[v * 4 + 2] = t.Z;
            result[v * 4 + 3] = w;
        }

        if (hasReal && !_tangentDiagnosticLogged && sampleCount > 0)
            LogTangentDiagnostic(dotSum / sampleCount, orthoSum / sampleCount, sampleCount);

        return result;
    }

    // Set once a diagnostic comparing decoded vs UV-derived tangents has been logged.
    private static bool _tangentDiagnosticLogged;

    private static void LogTangentDiagnostic(float meanCos, float meanOrtho, int sampleCount)
    {
        _tangentDiagnosticLogged = true;
        Console.WriteLine(
            $"[GeometryMath] Tangent decode check ({sampleCount} vertices): " +
            $"mean cos(decoded tangent, UV-derived tangent) = {meanCos:0.###} " +
            $"(near +1 = tangent, correct handedness, as currently assumed; near -1 = tangent but " +
            $"needs negating; near 0 = this word is likely the bitangent, not the tangent). " +
            $"mean dot(normal, tangent) = {meanOrtho:0.###} (should be near 0).");
    }

    private static Vector3 Get3(float[] arr, uint i) => new(arr[i * 3], arr[i * 3 + 1], arr[i * 3 + 2]);
    private static Vector2 Get2(float[] arr, uint i) => new(arr[i * 2], arr[i * 2 + 1]);

    private static Vector3 ArbitraryPerpendicular(Vector3 n)
    {
        var fallback = MathF.Abs(n.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        return Vector3.Normalize(Vector3.Cross(n, fallback));
    }
}
