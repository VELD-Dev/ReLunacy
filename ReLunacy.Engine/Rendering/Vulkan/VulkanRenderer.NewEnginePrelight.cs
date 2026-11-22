using System.Numerics;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// New-engine unbaked Tie prelight: one 4-byte word per vertex and per PLACEMENT, so it cannot live in
/// the shared mesh vertices. Every placement's words go into one storage buffer (set 0 binding 8), and
/// each draw carries the word index of its first vertex minus its base vertex, so the vertex shader
/// reads uPrelight[gl_VertexIndex + delta]. Format and lighting: Docs/reverse/
/// NEW_ENGINE_SHADER_SECTIONS_EBOOT_2026-09-27.md section 6 (VPs 52-59, FPs 210bbe6e06dc / af2e853165e2).
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private VkBuffer _prelightBuffer;
    private VkDeviceMemory _plMemory;

    /// <summary>ToD: each old Tie draw's shader key (its primitive's effective use bits, the shell's
    /// route key with secondary bit 15 clear), -1 for every other draw.</summary>
    private static int[] BuildTodKeys(
        List<float[]> geomVerts,
        List<(int geo, int mat, Matrix4x4 world, Vector4 sphere, object owner, float displayDistance, uint pickId)> instances,
        int[] order)
    {
        var keys = new int[order.Length];
        Array.Fill(keys, -1);
        if (TodShaders == null)
            return keys;
        var captureIndex = new Dictionary<float[], int>(ReferenceEqualityComparer.Instance);
        for (int k = 0; k < VulkanSceneCapture.VertexData.Count; k++)
            captureIndex.TryAdd(VulkanSceneCapture.VertexData[k], k);
        for (int i = 0; i < order.Length; i++)
        {
            var (g, _, _, _, owner, _, _) = instances[order[i]];
            if (owner is not Scene.EntityTie || (uint)g >= (uint)geomVerts.Count ||
                !captureIndex.TryGetValue(geomVerts[g], out int source) ||
                source >= VulkanSceneCapture.OldTieDrawStates.Count)
                continue;
            VkOldTieDrawState state = VulkanSceneCapture.OldTieDrawStates[source];
            if (state.IsOldEngine && state.SourceHadExplicitUse)
                keys[i] = state.EffectiveUseMask & 0x1F;
        }
        return keys;
    }

    /// <summary>Marks the draws of prelit placements and returns the words they read.</summary>
    private static uint[] BuildNewEnginePrelight(
        List<float[]> geomVerts,
        List<VkMaterialDesc> materials,
        List<(int geo, int mat, Matrix4x4 world, Vector4 sphere, object owner, float displayDistance, uint pickId)> instances,
        int[] order,
        int[] drawVertexOffset,
        VkInstanceLight[] lights)
    {
        var captureIndex = new Dictionary<float[], int>(ReferenceEqualityComparer.Instance);
        for (int k = 0; k < VulkanSceneCapture.VertexData.Count; k++)
            captureIndex.TryAdd(VulkanSceneCapture.VertexData[k], k);

        var words = new List<uint>();
        var baseOf = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        int oldPrelit = 0;
        int fallOfManDraws = 0;
        int fallOfManPrelit = 0;
        for (int i = 0; i < order.Length; i++)
        {
            var (g, mat, _, _, owner, _, _) = instances[order[i]];
            var fallOfMan = owner switch
            {
                Scene.EntityTie bakedTie => bakedTie.FallOfManBake,
                Scene.EntityUFrag { UFrag: Assets.UFrags.OldUFrag terrain } => terrain.FallOfManBake,
                _ => null,
            };
            if (fallOfMan?.VertexDirections is { } directions)
            {
                int vertexCount = geomVerts[g].Length / VulkanSceneCapture.FloatsPerVertex;
                byte[] chroma = fallOfMan.Chroma;
                if (directions.Length == vertexCount * 3 && chroma.Length == vertexCount * 2)
                {
                    int wordDelta = words.Count - drawVertexOffset[i] * 2;
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                    {
                        int direction = vertex * 3;
                        words.Add(chroma[vertex * 2] | ((uint)chroma[vertex * 2 + 1] << 8));
                        words.Add(directions[direction] | ((uint)directions[direction + 1] << 8)
                            | ((uint)directions[direction + 2] << 16));
                    }
                    lights[i] = VkInstanceLight.ForFallOfManPrelight(fallOfMan.Scale, wordDelta, terrain: true);
                    fallOfManPrelit++;
                }
                continue;
            }
            if (fallOfMan != null && materials[mat].HasBaked > 0.5f)
            {
                int firstChroma = owner is Scene.EntityUFrag ? 0
                    : captureIndex.TryGetValue(geomVerts[g], out int meshIndex) ? VulkanSceneCapture.SourceVertexBases[meshIndex] : -1;
                int vertexCount = geomVerts[g].Length / VulkanSceneCapture.FloatsPerVertex;
                if (firstChroma >= 0 && (firstChroma + vertexCount) * 2 <= fallOfMan.Chroma.Length)
                {
                    byte[] chroma = fallOfMan.Chroma;
                    if (!baseOf.TryGetValue(chroma, out int chromaBase))
                    {
                        chromaBase = words.Count;
                        baseOf[chroma] = chromaBase;
                        for (int b = 0; b + 1 < chroma.Length; b += 2)
                            words.Add(chroma[b] | ((uint)chroma[b + 1] << 8));
                    }
                    lights[i] = VkInstanceLight.ForFallOfManBake(fallOfMan.Scale, chromaBase + firstChroma - drawVertexOffset[i]);
                    fallOfManDraws++;
                }
                continue;
            }
            if (owner is Scene.EntityTie { FallOfManTiePrelight: { } prelight }
                && captureIndex.TryGetValue(geomVerts[g], out int prelightMesh)
                && VulkanSceneCapture.FallOfManTiePrimitives[prelightMesh] is { } fallOfManPrimitive)
            {
                int firstWord = fallOfManPrimitive.PrelightFirstWord(prelight.PlacementHasLightmap);
                int prelightVertexCount = geomVerts[g].Length / VulkanSceneCapture.FloatsPerVertex;
                float scale = fallOfManPrimitive.Lightmapped ? prelight.LightmappedScale : prelight.PrelightScale;
                byte[] packed = prelight.Words;
                if (firstWord >= 0 && firstWord + prelightVertexCount <= packed.Length / 4 && float.IsFinite(scale) && scale >= 0f)
                {
                    if (!baseOf.TryGetValue(packed, out int prelightBase))
                    {
                        prelightBase = words.Count;
                        baseOf[packed] = prelightBase;
                        for (int b = 0; b + 3 < packed.Length; b += 4)
                            words.Add(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(packed.AsSpan(b, 4)));
                    }
                    lights[i] = VkInstanceLight.ForFallOfManPrelight(scale, prelightBase + firstWord - drawVertexOffset[i]);
                    fallOfManPrelit++;
                }
                continue;
            }
            if ((owner as Scene.EntityUFrag)?.UFrag is Assets.UFrags.OldUFrag
                { Metadata: { OldEngineLightScales: { } scales, OldEngineMaterialUse: { } use } } oldUFrag)
            {
                // An authored lightmap with an unavailable atlas or invalid UVs keeps the fallback.
                // With no authored atlas, attr2 is prelight instead of a Half2 UV.
                bool prelit = oldUFrag.LightmapIndex == Loading.Objects.UFragMetadata.NoLightmap;
                var vertices = oldUFrag.GetRawVertices();
                int vertexCount = geomVerts[g].Length / VulkanSceneCapture.FloatsPerVertex;
                if ((prelit || materials[mat].HasBaked > 0.5f) && vertexCount > 0 && vertices.Length == vertexCount)
                {
                    int wordDelta = words.Count - drawVertexOffset[i] * 2;
                    foreach (var vertex in vertices)
                    {
                        words.Add((uint)Math.Abs((int)vertex.unk));
                        words.Add(vertex.PackedLightmapAttribute);
                    }
                    int features = use.Mask & (int)materials[mat].MaterialPermutation.W;
                    lights[i] = VkInstanceLight.ForOldUFrag(scales, prelit, wordDelta, features, use.CubeSlot == 0);
                }
                continue;
            }
            // ToD: the unbaked placement's words, indexed by the primitive's first vertex in its Tie.
            if (owner is Scene.EntityTie { OldEnginePrelight: { } oldBytes, OldBakedLightScale: { } f } oldTie && f > 0f)
            {
                if ((uint)g >= (uint)geomVerts.Count ||
                    !captureIndex.TryGetValue(geomVerts[g], out int oldSource) ||
                    oldSource >= VulkanSceneCapture.OldTieDrawStates.Count)
                    continue;
                int vertexBase = VulkanSceneCapture.OldTieDrawStates[oldSource].VertexBase;
                if (vertexBase < 0)
                    continue;
                int oldCount = geomVerts[g].Length / VulkanSceneCapture.FloatsPerVertex;
                if ((vertexBase + oldCount) * 4 > oldBytes.Length)
                    continue;
                if (!baseOf.TryGetValue(oldTie, out int oldBase))
                {
                    oldBase = words.Count;
                    baseOf[oldTie] = oldBase;
                    for (int b = 0; b + 3 < oldBytes.Length; b += 4)
                        words.Add(oldBytes[b] | ((uint)oldBytes[b + 1] << 8) | ((uint)oldBytes[b + 2] << 16) | ((uint)oldBytes[b + 3] << 24));
                    oldPrelit++;
                }
                lights[i] = VkInstanceLight.ForOldPrelit(f, oldBase + vertexBase - drawVertexOffset[i]);
                continue;
            }
            if (owner is not Scene.EntityTie { NewEnginePrelight: { } bytes, NewEngineBakedLight: { } c4 } tie || c4.Z <= 0f)
                continue;
            if ((uint)g >= (uint)geomVerts.Count ||
                !captureIndex.TryGetValue(geomVerts[g], out int source) ||
                source >= VulkanSceneCapture.NewEngineTiePrimitives.Count ||
                VulkanSceneCapture.NewEngineTiePrimitives[source] is not { } primitive)
                continue;

            int first = primitive.PrelightFirstWord(tie.NewEnginePrelightBaked);
            int count = geomVerts[g].Length / VulkanSceneCapture.FloatsPerVertex;
            if (first < 0 || (first + count) * 4 > bytes.Length)
                continue;

            if (!baseOf.TryGetValue(tie, out int wordBase))
            {
                wordBase = words.Count;
                baseOf[tie] = wordBase;
                // Memory order x, y, z, w - the RSX reads attribute 4 as four UB256 in that order.
                for (int b = 0; b + 3 < bytes.Length; b += 4)
                    words.Add(bytes[b] | ((uint)bytes[b + 1] << 8) | ((uint)bytes[b + 2] << 16) | ((uint)bytes[b + 3] << 24));
            }

            lights[i] = VkInstanceLight.ForNewEnginePrelight(c4, wordBase + first - drawVertexOffset[i]);
        }

        if (fallOfManDraws + fallOfManPrelit > 0)
            Console.WriteLine($"[VkRenderer] Fall of Man baked lighting: {fallOfManDraws} lightmapped and {fallOfManPrelit} vertex-lit draws.");
        else if (baseOf.Count > 0)
            Console.WriteLine($"[VkRenderer] Tie prelight: {baseOf.Count} placement(s) ({oldPrelit} old-engine), {words.Count} vertex word(s).");
        return words.Count > 0 ? [.. words] : [0u];
    }

    /// <summary>
    /// New-engine distance fades (c[19] = K / d, Docs/reverse/NEW_ENGINE_SHADER_SECTIONS_EBOOT_2026-09-27.md
    /// section 6): a UFrag draw takes its record's +0x60 distances, any other draw its primitive's +0x30
    /// (Tie primitive, Moby frag).
    /// Runs after BuildNewEnginePrelight, which replaces the instance light of prelit draws.
    /// </summary>
    private static void ApplyNewEngineFades(
        List<float[]> geomVerts,
        List<(int geo, int mat, Matrix4x4 world, Vector4 sphere, object owner, float displayDistance, uint pickId)> instances,
        int[] order,
        VkInstanceLight[] lights)
    {
        var captureIndex = new Dictionary<float[], int>(ReferenceEqualityComparer.Instance);
        for (int k = 0; k < VulkanSceneCapture.VertexData.Count; k++)
            captureIndex.TryAdd(VulkanSceneCapture.VertexData[k], k);

        for (int i = 0; i < order.Length; i++)
        {
            var (g, _, _, _, owner, _, _) = instances[order[i]];
            Vector3? distances = owner switch
            {
                Scene.EntityUFrag { UFrag.NewEngineFadeDistances: { } ufrag } => ufrag,
                Scene.EntityUFrag => null,
                _ when (uint)g < (uint)geomVerts.Count &&
                       captureIndex.TryGetValue(geomVerts[g], out int source) &&
                       source < VulkanSceneCapture.NewEngineFadeDistances.Count
                    => VulkanSceneCapture.NewEngineFadeDistances[source],
                _ => null,
            };
            if (distances is { } d)
                lights[i].ApplyNewEngineFades(d);

            Assets.Geometry.UvAnimation? uvAnimation = owner switch
            {
                Scene.EntityUFrag entityUFrag => entityUFrag.UFrag.UvAnimation,
                _ when (uint)g < (uint)geomVerts.Count &&
                       captureIndex.TryGetValue(geomVerts[g], out int uvSource) &&
                       uvSource < VulkanSceneCapture.UvAnimations.Count
                    => VulkanSceneCapture.UvAnimations[uvSource],
                _ => null,
            };
            if (uvAnimation is { } uv)
            {
                // Both per second: the shader runs on the editor's clock, not the game's frame
                // count, so the game's rate is spent here converting the rotation and goes no
                // further.
                lights[i].UvAnim = new Vector4(uv.Pivot, uv.RotationPerSecond, 1f);
                lights[i].UvScroll = new Vector4(uv.ScrollPerSecond, 0f, 0f);
            }

            // Feature mask ANDed with the material's +0x20 at load: Tie primitive +0x20 (0x006AE85C),
            // UFrag record +0x54 (0x006B7380). Mobys pick their shaders from other key bits: no mask.
            byte? mask = owner switch
            {
                Scene.EntityUFrag { UFrag.NewEngineFeatureMask: { } ufragMask } => ufragMask,
                Scene.EntityTie when (uint)g < (uint)geomVerts.Count &&
                                     captureIndex.TryGetValue(geomVerts[g], out int tieSource) &&
                                     tieSource < VulkanSceneCapture.NewEngineTiePrimitives.Count &&
                                     VulkanSceneCapture.NewEngineTiePrimitives[tieSource] is { } primitive
                    => primitive.FeatureMask,
                _ => null,
            };
            if (mask is { } m)
                lights[i].ApplyNewEngineFeatureMask(m);
        }
    }

    private void UploadNewEnginePrelight(uint[] words)
    {
        ulong size = (ulong)words.Length * sizeof(uint);
        (_prelightBuffer, _plMemory) = CreateBuffer(size, VkBufferUsageFlags.StorageBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* p; Check(_api.vkMapMemory(_plMemory, 0, size, 0, &p), "vkMapMemory(prelight)");
        fixed (uint* src = words)
            Buffer.MemoryCopy(src, p, (long)size, (long)size);
        _api.vkUnmapMemory(_plMemory);
    }
}
