using System.Numerics;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Passive diagnostics for authored SkyShell geometry. This does not modify any render state.
/// It exists specifically to distinguish "the lower sky is absent from the file" from
/// "the authored lower geometry exists but our transform/sampler presents it incorrectly".
/// </summary>
public static class SkyShellCoverageAudit
{
    public static void Log(VkSkyScene scene)
    {
        if (!scene.HasShellGeometry)
            return;

        int stride = checked((int)VkSkyScene.FloatsPerVertex);
        int vertexCount = scene.VertexData.Length / stride;
        Console.WriteLine(
            $"[Sky][Coverage] mesh vertices={vertexCount} indices={scene.Indices.Length} draws={scene.Draws.Length}");

        foreach (var draw in scene.Draws)
        {
            ulong end64 = (ulong)draw.FirstIndex + draw.IndexCount;
            if (draw.FirstIndex >= scene.Indices.Length || end64 > (ulong)scene.Indices.Length)
            {
                Console.WriteLine(
                    $"[Sky][Coverage] pass={draw.RetailPassId} order={draw.DrawOrder} " +
                    $"INVALID indexWindow=[{draw.FirstIndex},{end64})/{scene.Indices.Length}");
                continue;
            }

            var unique = new HashSet<uint>();
            int invalidVertexIndices = 0;
            for (uint i = 0; i < draw.IndexCount; i++)
            {
                uint vertexIndex = scene.Indices[checked((int)(draw.FirstIndex + i))];
                if (vertexIndex >= vertexCount)
                {
                    invalidVertexIndices++;
                    continue;
                }
                unique.Add(vertexIndex);
            }

            if (unique.Count == 0)
            {
                Console.WriteLine(
                    $"[Sky][Coverage] pass={draw.RetailPassId} order={draw.DrawOrder} " +
                    $"no valid referenced vertices; invalidIndices={invalidVertexIndices}");
                continue;
            }

            float yMin = float.PositiveInfinity;
            float yMax = float.NegativeInfinity;
            float rotatedYMin = float.PositiveInfinity;
            float rotatedYMax = float.NegativeInfinity;
            float wMin = float.PositiveInfinity;
            float wMax = float.NegativeInfinity;
            float uMin = float.PositiveInfinity;
            float uMax = float.NegativeInfinity;
            float vMin = float.PositiveInfinity;
            float vMax = float.NegativeInfinity;
            float elevationMin = float.PositiveInfinity;
            float elevationMax = float.NegativeInfinity;
            float rotatedElevationMin = float.PositiveInfinity;
            float rotatedElevationMax = float.NegativeInfinity;
            int below = 0;
            int rotatedBelow = 0;
            int nonUnitW = 0;

            foreach (uint vertexIndex in unique)
            {
                int o = checked((int)vertexIndex * stride);
                Vector4 p = new(
                    scene.VertexData[o + 0],
                    scene.VertexData[o + 1],
                    scene.VertexData[o + 2],
                    scene.VertexData[o + 3]);
                float u = scene.VertexData[o + 8];
                float v = scene.VertexData[o + 9];

                yMin = MathF.Min(yMin, p.Y);
                yMax = MathF.Max(yMax, p.Y);
                wMin = MathF.Min(wMin, p.W);
                wMax = MathF.Max(wMax, p.W);
                uMin = MathF.Min(uMin, u);
                uMax = MathF.Max(uMax, u);
                vMin = MathF.Min(vMin, v);
                vMax = MathF.Max(vMax, v);
                if (p.Y < 0f) below++;
                if (MathF.Abs(p.W - 1f) > 1e-5f) nonUnitW++;

                float horizontal = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
                float elevation = MathF.Atan2(p.Y, horizontal) * (180f / MathF.PI);
                elevationMin = MathF.Min(elevationMin, elevation);
                elevationMax = MathF.Max(elevationMax, elevation);

                Vector3 rotated = RotateRetailLocal(
                    new Vector3(p.X, p.Y, p.Z),
                    draw.InitialPhase,
                    draw.Rotation10,
                    draw.Rotation14);
                rotatedYMin = MathF.Min(rotatedYMin, rotated.Y);
                rotatedYMax = MathF.Max(rotatedYMax, rotated.Y);
                if (rotated.Y < 0f) rotatedBelow++;
                float rotatedHorizontal = MathF.Sqrt(rotated.X * rotated.X + rotated.Z * rotated.Z);
                float rotatedElevation = MathF.Atan2(rotated.Y, rotatedHorizontal) * (180f / MathF.PI);
                rotatedElevationMin = MathF.Min(rotatedElevationMin, rotatedElevation);
                rotatedElevationMax = MathF.Max(rotatedElevationMax, rotatedElevation);
            }

            string kind = draw.Bloom ? "Bloom" : "Color";
            Console.WriteLine(
                $"[Sky][Coverage] pass={draw.RetailPassId} {kind} order={draw.DrawOrder} texSlot={draw.TextureSlot} " +
                $"indices={draw.IndexCount} uniqueV={unique.Count} invalidV={invalidVertexIndices} " +
                $"authoredY={yMin:R}..{yMax:R} below0={below}/{unique.Count} " +
                $"authoredElev={elevationMin:R}..{elevationMax:R}deg " +
                $"localY={rotatedYMin:R}..{rotatedYMax:R} below0={rotatedBelow}/{unique.Count} " +
                $"localElev={rotatedElevationMin:R}..{rotatedElevationMax:R}deg " +
                $"uvU={uMin:R}..{uMax:R} uvV={vMin:R}..{vMax:R} " +
                $"w={wMin:R}..{wMax:R} nonUnitW={nonUnitW}/{unique.Count}");
        }
    }

    private static Vector3 RotateRetailLocal(Vector3 p, float phase, float rotation10, float rotation14)
    {
        // Same recovered 0x580260 local sequence as the active SkyShell vertex shader:
        // RY(phase) -> RZ(rotation10) -> RY(rotation14).
        float sp = MathF.Sin(phase);
        float cp = MathF.Cos(phase);
        p = new Vector3(cp * p.X + sp * p.Z, p.Y, -sp * p.X + cp * p.Z);

        float sz = MathF.Sin(rotation10);
        float cz = MathF.Cos(rotation10);
        p = new Vector3(cz * p.X - sz * p.Y, sz * p.X + cz * p.Y, p.Z);

        float sy = MathF.Sin(rotation14);
        float cy = MathF.Cos(rotation14);
        return new Vector3(cy * p.X + sy * p.Z, p.Y, -sy * p.X + cy * p.Z);
    }
}
