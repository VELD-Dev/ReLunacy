using System.Numerics;
using NeoVeldrid;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Old-engine Tie draw-state wiring kept separate from the large Vulkan renderer implementation.
/// The overload delegates all normal scene construction to the canonical constructor, then annotates
/// the already mapped per-instance SSBO with source-authoritative Tie metadata.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    public VulkanRenderer(
        GraphicsDevice graphicsDevice,
        List<float[]> geomVerts,
        List<uint[]> geomIndices,
        List<VkMaterialDesc> materials,
        List<(int geo, int mat, Matrix4x4 world, Vector4 sphere, object owner, float displayDistance, uint pickId)> instances,
        bool wireOldTieSourceMetadata,
        ReLunacy.Engine.Assets.Lighting.LightingEnvironment? oldLightingEnvironment,
        Texture? envCube,
        uint width,
        uint height,
        bool oldEngineMobys = true,
        IReadOnlyDictionary<ulong, int>? cubeResources = null,
        Loading.Shaders.NewEngineStandardShaders? standardShaders = null,
        int maxSkeletonBones = 0,
        int maxConcurrentAnimated = 0)
        : this(
            graphicsDevice,
            geomVerts,
            geomIndices,
            materials,
            instances,
            oldLightingEnvironment,
            envCube,
            width,
            height,
            oldEngineMobys,
            cubeResources,
            standardShaders,
            maxSkeletonBones,
            maxConcurrentAnimated)
    {
        if (wireOldTieSourceMetadata)
            WireOldTieSourceMetadata(geomVerts, geomIndices, instances);
    }

    private void WireOldTieSourceMetadata(
        List<float[]> geomVerts,
        List<uint[]> geomIndices,
        List<(int geo, int mat, Matrix4x4 world, Vector4 sphere, object owner, float displayDistance, uint pickId)> instances)
    {
        if (_ilMapped == null || instances.Count == 0)
            return;

        var firstIndex = new uint[geomIndices.Count];
        var baseVertex = new int[geomVerts.Count];
        uint runningIndex = 0;
        int runningVertex = 0;
        for (int g = 0; g < geomIndices.Count; g++)
        {
            firstIndex[g] = runningIndex;
            baseVertex[g] = runningVertex;
            runningIndex += (uint)geomIndices[g].Length;
            runningVertex += geomVerts[g].Length / VulkanSceneCapture.FloatsPerVertex;
        }

        var stateByGeometry = new VkOldTieDrawState[geomVerts.Count];
        for (int g = 0; g < geomVerts.Count; g++)
        {
            int sourceGeometry = VulkanSceneCapture.VertexData.FindIndex(v => ReferenceEquals(v, geomVerts[g]));
            if (sourceGeometry >= 0 && sourceGeometry < VulkanSceneCapture.OldTieDrawStates.Count)
                stateByGeometry[g] = VulkanSceneCapture.OldTieDrawStates[sourceGeometry];
        }

        var stateByDraw = new Dictionary<(uint FirstIndex, int BaseVertex, int Material, uint PickId), VkOldTieDrawState>();
        foreach (var instance in instances)
        {
            if ((uint)instance.geo >= (uint)stateByGeometry.Length)
                continue;
            var state = stateByGeometry[instance.geo];
            if (!state.SourceHadExplicitUse)
                continue;

            stateByDraw[(
                firstIndex[instance.geo],
                baseVertex[instance.geo],
                instance.mat,
                instance.pickId)] = state;
        }

        var gpu = (VkInstanceLight*)_ilMapped;
        int wired = 0;
        for (int i = 0; i < _instanceCount; i++)
        {
            if (!stateByDraw.TryGetValue((
                    _drawFirstIndex[i],
                    _drawVertexOffset[i],
                    _drawMatSlot[i],
                    _instPickId[i]),
                out var state))
                continue;

            gpu[i].ApplyOldTieDrawState(state);
            wired++;
        }

        if (wired > 0)
            Console.WriteLine($"[VkRenderer] old-engine Tie source shader-use wired to {wired} draw(s); callback52 terminal route remains unresolved.");
    }
}
