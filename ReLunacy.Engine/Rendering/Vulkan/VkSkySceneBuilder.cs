using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Sky;
using ReLunacy.Engine.Rendering.Resources;
using AssetTexture = ReLunacy.Engine.Assets.Textures.Texture;
using OldTextureDescriptorState = ReLunacy.Engine.Assets.Textures.OldTextureDescriptorState;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Bridge from proof-bounded SkyData to the viewport packet. SkyShell vertices/UVs/indices stay
/// authored. Dome pass 14 is the one procedural exception because retail itself generates it on CPU
/// in 0x580574; pass 17 Clouds reuses that exact generated stream and its projected attributes.
/// </summary>
public static class VkSkySceneBuilder
{
    private const int DomeBands = 12;
    private const int DomeSectors = 24;
    private const float DomeBandStep = MathF.PI / 24f;   // 7.5 degrees
    private const float DomeSectorStep = MathF.PI / 12f; // 15 degrees
    private const float DomeTransitionNearOne = 0.9999899864196777f;

    public static VkSkyScene? Build(SkyData sky, IReadOnlyDictionary<ulong, GpuTexture> builtTextures)
    {
        var defs = sky.Definitions;
        if (defs == null)
            return null;

        VkSkyDome dome = BuildDome(defs);
        var mesh = sky.ShellMesh;

        float[] vertexData = [];
        uint[] indices = [];
        var textures = new List<NeoVeldrid.Texture>();
        var textureDescriptors = new List<OldTextureDescriptorState?>();
        var textureSlots = new Dictionary<ulong, int>();
        var draws = new List<VkSkyDraw>();

        int ResolveTextureSlot(ITexture? texture)
        {
            if (texture is null || !builtTextures.TryGetValue(texture.Id, out var gpu))
                return -1;
            if (textureSlots.TryGetValue(texture.Id, out int existing))
                return existing;

            int slot = textures.Count;
            textures.Add(gpu.DeviceTexture);
            textureDescriptors.Add(texture is AssetTexture oldTexture ? oldTexture.SamplingDescriptor : null);
            textureSlots.Add(texture.Id, slot);
            return slot;
        }

        int ResolveDefinitionTexture(int offset)
        {
            var definition = sky.DefinitionTextures.FirstOrDefault(x => x.Offset == offset);
            return ResolveTextureSlot(definition?.Texture);
        }

        VkSkyClouds? clouds = null;
        int texture4Slot = ResolveDefinitionTexture(0xE8);
        int texture5Slot = ResolveDefinitionTexture(0xEC);
        int texture6Slot = ResolveDefinitionTexture(0xF0);
        if (texture4Slot >= 0 && texture5Slot >= 0 && texture6Slot >= 0)
        {
            clouds = new VkSkyClouds(
                texture4Slot,
                texture5Slot,
                texture6Slot,
                new VkSkyCloudLayer(
                    defs.Cloud0Period,
                    defs.Cloud0Angle,
                    defs.Cloud0PrimaryScroll,
                    defs.Cloud0Bias0,
                    defs.Cloud0SecondaryScroll,
                    defs.Cloud0Bias1),
                new VkSkyCloudLayer(
                    defs.Cloud1Period,
                    defs.Cloud1Angle,
                    defs.Cloud1PrimaryScroll,
                    defs.Cloud1Bias0,
                    defs.Cloud1SecondaryScroll,
                    defs.Cloud1Bias1),
                defs.CloudTexture4PerturbScale,
                defs.CloudTexture5PerturbScale,
                defs.CloudLayerBlend,
                defs.CloudColourMultiplier,
                defs.DomeVertexConstant42);
        }

        if (mesh != null && mesh.Vertices.Count != 0 && mesh.Indices.Count != 0)
        {
            vertexData = BuildShellVertices(mesh);
            indices = mesh.Indices.ToArray();

            void Add(IReadOnlyList<SkyFamilyRecord> records)
            {
                foreach (var record in records)
                {
                    if (record.IndexCount == 0) continue;
                    int textureSlot = ResolveTextureSlot(record.Texture);
                    if (textureSlot < 0) continue;

                    draws.Add(new VkSkyDraw(
                        mesh.LocalFirstIndex(record),
                        record.IndexCount,
                        textureSlot,
                        record.RetailPassId,
                        record.DrawOrder,
                        record.Rotation10,
                        record.Rotation14,
                        record.SpinRate,
                        record.Phase,
                        record.IsBloomContribution));
                }
            }

            Add(sky.Family8160Records);
            Add(sky.Family8170Records);
            draws.Sort(static (a, b) =>
            {
                int pass = a.RetailPassId.CompareTo(b.RetailPassId);
                return pass != 0 ? pass : a.DrawOrder.CompareTo(b.DrawOrder);
            });
        }

        return new VkSkyScene(
            dome,
            clouds,
            vertexData,
            indices,
            textures.ToArray(),
            textureDescriptors.ToArray(),
            draws.ToArray(),
            defs.SkyTimeScale,
            defs.BloomShellScale);
    }

    private static float[] BuildShellVertices(SkyShellMesh mesh)
    {
        var result = new float[checked(mesh.Vertices.Count * (int)VkSkyScene.FloatsPerVertex)];
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            int o = i * (int)VkSkyScene.FloatsPerVertex;
            result[o + 0] = v.Position.X;
            result[o + 1] = v.Position.Y;
            result[o + 2] = v.Position.Z;

            // PROVEN source contract: the native SkyShell position is authored HALF4 and retail VP
            // DP4s consume all four components. Do not replace W with a compatibility constant here;
            // projection diagnostics must observe the authored value that the file actually carries.
            result[o + 3] = v.Position.W;

            result[o + 4] = v.Color.X;
            result[o + 5] = v.Color.Y;
            result[o + 6] = v.Color.Z;
            result[o + 7] = v.Color.W;
            result[o + 8] = v.Uv.X;
            result[o + 9] = v.Uv.Y;
        }
        return result;
    }

    private static VkSkyDome BuildDome(SkyDefs defs)
    {
        const int pointsPerBand = DomeSectors + 1;
        const int verticesPerBand = pointsPerBand * 2;
        const int vertexCount = DomeBands * verticesPerBand; // retail: 600
        const int indexCount = DomeBands * DomeSectors * 6;

        var vertices = new float[checked(vertexCount * (int)VkSkyDome.FloatsPerVertex)];
        var indices = new uint[indexCount];
        int vertex = 0;
        int index = 0;

        for (int band = 0; band < DomeBands; band++)
        {
            float theta0 = band * DomeBandStep;
            float theta1 = (band + 1) * DomeBandStep;
            float sin0 = MathF.Sin(theta0);
            float cos0 = MathF.Cos(theta0);
            float sin1 = MathF.Sin(theta1);
            float cos1 = MathF.Cos(theta1);
            Vector4 color0 = RetailDomeColour(defs, cos0);
            Vector4 color1 = RetailDomeColour(defs, cos1);
            int bandBase = vertex;

            for (int sector = 0; sector <= DomeSectors; sector++)
            {
                float phi = sector * DomeSectorStep;
                float cosPhi = MathF.Cos(phi);
                float sinPhi = MathF.Sin(phi);

                WriteDomeVertex(vertices, vertex++, defs, sin0, cos0, cosPhi, sinPhi, color0);
                WriteDomeVertex(vertices, vertex++, defs, sin1, cos1, cosPhi, sinPhi, color1);
            }

            for (int sector = 0; sector < DomeSectors; sector++)
            {
                uint a = (uint)(bandBase + sector * 2);
                uint b = a + 1;
                uint c = a + 2;
                uint d = a + 3;

                // Native submits the same 600 generated vertices as a triangle strip. The explicit
                // triangle-list expansion is topology-equivalent for the 12 authored bands and is
                // shared by Dome and Clouds in the editor.
                indices[index++] = a;
                indices[index++] = b;
                indices[index++] = c;
                indices[index++] = c;
                indices[index++] = b;
                indices[index++] = d;
            }
        }

        // Retail copies SkyDefs +0x44..+0x50 as raw floats to Sky runtime +0x90..+0x9C.
        // Dome handler 0x5816B8 then patches that float4 into the embedded C0 slot of FP 0x100C1930.
        Vector4 c0 = defs.DomeRuntimeColourSource44;
        return new VkSkyDome(
            vertices,
            indices,
            defs.DomeVertexConstant42,
            c0,
            fragmentConstant0JoinProven: true);
    }

    private static void WriteDomeVertex(
        float[] dst,
        int vertex,
        SkyDefs defs,
        float sinTheta,
        float cosTheta,
        float cosPhi,
        float sinPhi,
        Vector4 color)
    {
        float x = sinTheta * cosPhi;
        float z = sinTheta * sinPhi;
        float y = cosTheta * defs.DomeVerticalProfileScale - defs.DomeVerticalProfileOffset;
        float d = defs.DomeProfileScalar;
        float k = d / (sinTheta + d * cosTheta);
        float projected0 = x * k;
        float projected1 = z * k;

        int o = checked(vertex * (int)VkSkyDome.FloatsPerVertex);
        dst[o + 0] = x;
        dst[o + 1] = y;
        dst[o + 2] = z;
        dst[o + 3] = 1f;
        dst[o + 4] = color.X;
        dst[o + 5] = color.Y;
        dst[o + 6] = color.Z;
        dst[o + 7] = color.W;
        dst[o + 8] = projected0;
        dst[o + 9] = projected1;
    }

    private static Vector4 RetailDomeColour(SkyDefs defs, float x)
    {
        float p = defs.DomeColourTransitionParameter;
        Vector4 src;
        Vector4 dst;
        float t;

        if (p > DomeTransitionNearOne)
        {
            src = defs.DomeColourVector34;
            dst = defs.DomeColourVector24;
            t = x;
        }
        else if (x > p)
        {
            src = defs.DomeColourVector24;
            dst = defs.DomeColourVector14;
            t = (x - p) / (1f - p);
        }
        else
        {
            // This intentionally mirrors the retail fmuls at 0x580DCC. It is x*p, not x/p.
            src = defs.DomeColourVector34;
            dst = defs.DomeColourVector24;
            t = x * p;
        }

        return QuantizeUnorm8(src + (dst - src) * t);
    }

    private static Vector4 QuantizeUnorm8(Vector4 value) => new(
        QuantizeChannel(value.X),
        QuantizeChannel(value.Y),
        QuantizeChannel(value.Z),
        QuantizeChannel(value.W));

    private static float QuantizeChannel(float value)
    {
        float clamped = Math.Clamp(value, 0f, 1f);
        int packed = (int)(clamped * 255f + 0.5f);
        return packed / 255f;
    }
}
