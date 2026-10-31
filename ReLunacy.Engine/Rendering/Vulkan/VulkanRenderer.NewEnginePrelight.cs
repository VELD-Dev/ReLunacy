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
        for (int i = 0; i < order.Length; i++)
        {
            var (g, _, _, _, owner, _, _) = instances[order[i]];
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

        if (baseOf.Count > 0)
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
                lights[i].UvAnim = new Vector4(uv.Pivot, uv.RotationPerFrame, 1f);
                lights[i].UvScroll = new Vector4(uv.ScrollPerSecond, uv.FrameRate, 0f);
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
