using System.Numerics;
using Veldrith;
using Veldrith.SPIRV;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>Stage 14e of the from-scratch raw-Vulkan renderer (Docs/NewRenderer.md): the complete LIT
/// path with WEIGHTED BLENDED ORDER-INDEPENDENT TRANSPARENCY.
///
/// Glass and other translucent surfaces get the full lit shading (normal mapping, analytic + baked
/// lighting, parallax, cubemap reflections) AND correct-looking transparency without sorting - which
/// the old renderer could never do together. Three passes, all recorded ONCE and replayed for one
/// submit: (1) OPAQUE into the display colour + depth; (2) ACCUMULATE - translucent fragments are
/// blended commutatively (McGuire/Bavoil weighted-blended OIT) into an RGBA16F accum and an R16F reveal
/// target, depth-tested against the opaque scene but not writing depth, so draw order does not matter
/// and no per-frame re-record/sort is needed; (3) RESOLVE - a fullscreen pass composites accum/reveal
/// over the opaque colour. The lit shading is shared between the opaque and accumulate fragment
/// shaders. Bliss lit shader untouched - this renderer compiles its own SPIR-V.</summary>
public sealed unsafe class VulkanRenderer : IDisposable
{
    private const VkFormat ColorFormat = VkFormat.R8G8B8A8Unorm;
    private const VkFormat DepthFormat = VkFormat.D32Sfloat;
    private const VkFormat AccumFormat = VkFormat.R16G16B16A16Sfloat;
    private const VkFormat RevealFormat = VkFormat.R16Sfloat;
    private const uint VertexStride = VulkanSceneCapture.FloatsPerVertex * sizeof(float); // 56
    private const int TexPerMaterial = 5;

    private const string VertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(set = 0, binding = 1) readonly buffer Transforms { mat4 uT[]; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inUV;
layout(location = 2) in vec3 inNormal;
layout(location = 3) in vec4 inTangent;
layout(location = 4) in vec2 inUV2;
layout(location = 5) in vec4 inColor;
layout(location = 0) out vec2 fUV;
layout(location = 1) out vec3 fWorldNormal;
layout(location = 2) out vec3 fWorldTangent;
layout(location = 3) out float fHandedness;
layout(location = 4) out vec3 fWorldPos;
layout(location = 5) out vec2 fUV2;
layout(location = 6) out vec4 fColor;
void main() {
    mat4 m = uT[gl_InstanceIndex];
    mat3 m3 = mat3(m);
    mat3 nrm = transpose(inverse(m3));
    fWorldNormal = normalize(nrm * inNormal);
    fWorldTangent = normalize(nrm * inTangent.xyz);
    fHandedness = inTangent.w * sign(determinant(m3));
    vec4 world = m * vec4(inPos, 1.0);
    fWorldPos = world.xyz;
    fUV = inUV;
    fUV2 = inUV2;
    fColor = inColor;
    gl_Position = uMvp * world;
}";

    // Shared lit shading: everything except the final output. Both the opaque and the accumulate
    // fragment shaders append their own main() and output declarations to this.
    private const string LitFragCommon = @"#version 450
layout(set = 0, binding = 2, std140) uniform LightBuffer {
    vec3 uLightDirection; float uAmbient;
    vec3 uLightColor; float uSpecularPower;
    vec3 uCameraPosition; float uReflectionDebug;
    vec3 uEnvironmentColour; float uEnvironmentIntensity;
    vec2 uLightmapUVScale; vec2 uLightmapUVOffset;
    float uBakedLightScale; float uBakedBumpFade; float uBakedDebugView; float uReflectionBase;
    vec2 uLightmapUVPivot; float uLightmapUVRotation; float _reserved2;
    vec3 uEnvDirection0; float uEnvHasLighting;
    vec3 uEnvDirection1; float _p4;
    vec3 uEnvAmbient; float _p5;
    vec3 uEnvLight0Colour; float _p6;
    vec3 uEnvLight1Colour; float _p7;
};
layout(set = 0, binding = 3) uniform samplerCube uEnvCube;
layout(set = 1, binding = 0) uniform sampler2D uAlbedo;
layout(set = 1, binding = 1) uniform sampler2D uNormal;
layout(set = 1, binding = 2) uniform sampler2D uProps;
layout(set = 1, binding = 3) uniform sampler2D uLightColour;
layout(set = 1, binding = 4) uniform sampler2D uLightDir;
layout(push_constant) uniform PC { vec4 uMat0; vec4 uMat1; }; // uMat0=(hasBaked,pScale,pBias,alphaThr); uMat1.x=renderMode
layout(location = 0) in vec2 fUV;
layout(location = 1) in vec3 fWorldNormal;
layout(location = 2) in vec3 fWorldTangent;
layout(location = 3) in float fHandedness;
layout(location = 4) in vec3 fWorldPos;
layout(location = 5) in vec2 fUV2;
layout(location = 6) in vec4 fColor;
void shade(out vec3 litColor, out float litAlpha) {
    vec3 n = normalize(fWorldNormal);
    vec3 t = normalize(fWorldTangent - n * dot(fWorldTangent, n));
    vec3 b = cross(n, t) * fHandedness;
    mat3 tbn = mat3(t, b, n);
    vec3 viewDir = normalize(uCameraPosition - fWorldPos);
    vec3 viewDirTS = transpose(tbn) * viewDir;
    float height = texture(uProps, fUV).g * uMat0.y + uMat0.z;
    vec2 uv = fUV + viewDirTS.xy * height;
    vec4 albedoTex = texture(uAlbedo, uv);
    if (uMat1.x > 0.5 && uMat1.x < 1.5 && albedoTex.a <= uMat0.w) discard; // cutout
    vec4 nrmSample = texture(uNormal, uv);
    vec2 derivativeSum = vec2(nrmSample.a * 2.0 - 1.0, nrmSample.g * 2.0 - 1.0);
    vec3 worldNormal = normalize(tbn * normalize(vec3(derivativeSum, 1.0)));
    vec4 props = texture(uProps, uv);
    float specIntensity = props.r;
    float emissive = props.b;
    vec3 albedo = pow(albedoTex.rgb, vec3(2.2)); // (Color.rgb is always white in this engine - no vertex tint)
    vec3 envDiffuse = uEnvAmbient
        + uEnvLight0Colour * max(dot(worldNormal, uEnvDirection0), 0.0)
        + uEnvLight1Colour * max(dot(worldNormal, uEnvDirection1), 0.0);
    vec3 undecodedFill = mix(vec3(uAmbient), envDiffuse, uEnvHasLighting);
    vec2 uvCentred = fUV2 - uLightmapUVPivot;
    float sr = sin(radians(uLightmapUVRotation));
    float cr = cos(radians(uLightmapUVRotation));
    vec2 uvRot = vec2(uvCentred.x * cr - uvCentred.y * sr, uvCentred.x * sr + uvCentred.y * cr) + uLightmapUVPivot;
    vec2 bakedUV = uvRot * uLightmapUVScale + uLightmapUVOffset;
    vec4 bakedColour = texture(uLightColour, bakedUV);
    vec4 bakedDirSample = texture(uLightDir, bakedUV);
    vec3 bakedLightDirTS = vec3(bakedDirSample.r, bakedDirSample.b, bakedDirSample.g);
    float bl = length(bakedLightDirTS);
    bakedLightDirTS = bl > 0.0 ? bakedLightDirTS / bl : vec3(0.0, 0.0, 1.0);
    vec3 bakedNormalTS = normalize(vec3(derivativeSum * uBakedBumpFade, 1.0));
    float bakedNdotL = clamp(dot(bakedLightDirTS, bakedNormalTS), 0.0, 1.0);
    float bakedDiffuse = bakedLightDirTS.z > 0.0 ? bakedNdotL / bakedLightDirTS.z : bakedNdotL;
    vec3 bakedDiffuseLight = bakedColour.rgb * bakedDiffuse * uBakedLightScale;
    float hasBaked = uMat0.x;
    vec3 lighting = mix(undecodedFill, bakedDiffuseLight, hasBaked) + emissive;
    float bakedSpecLight = mix(1.0, bakedColour.a, hasBaked);
    vec3 reflDir = reflect(-viewDir, worldNormal);
    vec4 envTexel = texture(uEnvCube, reflDir);
    float envExposure = exp2((envTexel.a * 255.0 - 128.0) / 16.0);
    vec3 envColour = envTexel.rgb * envExposure;
    float NdotV = clamp(dot(worldNormal, viewDir), 0.0, 1.0);
    float fresnel = uReflectionBase + (1.0 - uReflectionBase) * pow(1.0 - NdotV, 5.0);
    float reflectivity = clamp(specIntensity + fresnel, 0.0, 1.0);
    vec3 envFill = envColour * albedo * uEnvironmentIntensity * reflectivity * bakedSpecLight;
    litColor = pow(albedo * lighting + envFill, vec3(1.0 / 2.2));
    litAlpha = albedoTex.a; // vertex-alpha opacity handled separately (this engine's Color.a is not plain opacity)
}";
    private const string FragmentOpaqueGlsl = LitFragCommon + @"
layout(location = 0) out vec4 o;
void main() { vec3 c; float a; shade(c, a); o = vec4(c, 1.0); }";
    // Weighted-blended OIT accumulation. accum sums premultiplied colour * weight; reveal multiplies
    // down by (1 - alpha). Weight favours nearer, more-opaque fragments (McGuire's depth+alpha form).
    private const string FragmentAccumGlsl = LitFragCommon + @"
layout(location = 0) out vec4 accum;
layout(location = 1) out float reveal;
void main() {
    vec3 c; float a; shade(c, a);
    float w = clamp(pow(min(1.0, a * 10.0) + 0.01, 3.0) * 1e8 * pow(1.0 - gl_FragCoord.z * 0.9, 3.0), 1e-2, 3e3);
    accum = vec4(c * a, a) * w;
    reveal = a;
}";
    private const string ResolveVertexGlsl = @"#version 450
void main() {
    vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}";
    // Composite: averageColor = accum.rgb / accum.a; blended over the opaque colour as
    // averageColor*(1-reveal) + dst*reveal (see the resolve pipeline's blend factors).
    private const string ResolveFragmentGlsl = @"#version 450
layout(set = 0, binding = 0) uniform sampler2D uAccum;
layout(set = 0, binding = 1) uniform sampler2D uReveal;
layout(location = 0) out vec4 o;
void main() {
    ivec2 c = ivec2(gl_FragCoord.xy);
    vec4 accum = texelFetch(uAccum, c, 0);
    float reveal = texelFetch(uReveal, c, 0).r;
    vec3 avg = accum.rgb / max(accum.a, 1e-5);
    o = vec4(avg, reveal);
}";

    // Volumes: a unit-cube wireframe drawn per volume, depth-tested against the opaque scene (so they
    // occlude correctly), coloured by a per-volume push constant (box matrix + colour). Provided fresh
    // each frame by View3D, so selection colour / edits / culling just work.
    private const string VolumeVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC { mat4 uModel; vec4 uColor; };
layout(location = 0) in vec3 inPos;
void main() { gl_Position = uMvp * (uModel * vec4(inPos, 1.0)); }";
    private const string VolumeFragmentGlsl = @"#version 450
layout(push_constant) uniform PC { mat4 uModel; vec4 uColor; };
layout(location = 0) out vec4 o;
void main() { o = vec4(uColor.rgb, 1.0); }";

    private readonly VulkanContext _ctx;
    private readonly VkDeviceApi _api;

    private readonly int _instanceCount;
    private readonly uint[] _drawIndexCount;
    private readonly uint[] _drawFirstIndex;
    private readonly int[] _drawVertexOffset;
    private readonly int[] _drawMatSlot;
    private readonly Vector4[] _matPC0;
    private readonly float[] _matRenderMode;
    private readonly int _translucentStart;

    // Frustum culling: static per-instance world bounding spheres, plus per-frame scratch. The visible
    // lists hold indices into the sorted static arrays, ordered (so material-run batching still holds);
    // culling is threaded over chunks that compact into disjoint regions of _cullScratch, then merged.
    private readonly Vector3[] _instCenter;
    private readonly float[] _instRadius;
    private readonly Vector4[] _planes = new Vector4[6];
    private readonly int[] _cullScratch;
    private readonly int[] _chunkCounts;
    private readonly int[] _visibleOpaque;
    private readonly int[] _visibleTranslucent;
    private int _visOpaqueCount, _visTransCount;

    private uint _width, _height;
    private readonly Texture _envCube;

    private VkCommandPool _pool; private VkCommandBuffer _cmd; private VkFence _fence;
    private VkBuffer _vertexBuffer; private VkDeviceMemory _vbMemory;
    private VkBuffer _indexBuffer; private VkDeviceMemory _ibMemory;
    private VkBuffer _uniformBuffer; private VkDeviceMemory _ubMemory; private void* _ubMapped;
    private VkBuffer _lightBuffer; private VkDeviceMemory _lightMemory; private void* _lightMapped;
    private VkBuffer _transformBuffer; private VkDeviceMemory _tbMemory;
    private VkDescriptorSetLayout _descLayout; private VkDescriptorSetLayout _matSetLayout; private VkDescriptorSetLayout _resolveSetLayout;
    private VkDescriptorPool _descPool; private VkDescriptorSet _descSet; private VkDescriptorSet _resolveSet;
    private VkSampler _sampler;
    private VkImageView _envCubeView;
    private VkImageView[] _texViews = [];
    private VkDescriptorSet[] _matSets = [];
    private VkRenderPass _rpOpaque, _rpAccum, _rpResolve;
    private VkShaderModule _vs, _fsOpaque, _fsAccum, _resolveVs, _resolveFs, _volumeVs, _volumeFs;
    private VkPipelineLayout _layout, _resolveLayout, _volumeLayout;
    private VkPipeline _pipelineOpaque, _pipelineAccum, _pipelineResolve, _volumePipeline;

    // Thin-box edge geometry (a unit-length cross of two thin quads, matching Primitives.CreateWireEdge:
    // 8 verts, 12 tri indices; thickness-dependent, host-mapped so it rebuilds when the setting changes)
    // + the per-frame volume-edge list (one (worldMatrix, colour) per edge, 12 per volume).
    private VkBuffer _edgeVertexBuffer; private VkDeviceMemory _edgeVbMemory; private void* _edgeVbMapped;
    private VkBuffer _edgeIndexBuffer; private VkDeviceMemory _edgeIbMemory; private uint _edgeIndexCount;
    private float _edgeThickness = -1f;
    private IReadOnlyList<(Matrix4x4 world, Vector4 color)> _volumes = System.Array.Empty<(Matrix4x4, Vector4)>();
    private int _volumeCount;

    // Size-dependent targets.
    private Texture _colorTex = null!; private VkImage _colorImage; private VkImageView _colorView;
    private VkImage _depthImage; private VkDeviceMemory _depthMemory; private VkImageView _depthView;
    private VkImage _accumImage; private VkDeviceMemory _accumMemory; private VkImageView _accumView;
    private VkImage _revealImage; private VkDeviceMemory _revealMemory; private VkImageView _revealView;
    private VkFramebuffer _fbOpaque, _fbAccum, _fbResolve;

    private long _submits; private bool _loggedInit;

    /// <summary>The Veldrith texture the scene is rendered into - display this in ImGui.</summary>
    public Texture ColorTexture => _colorTex;

    public VulkanRenderer(GraphicsDevice graphicsDevice, List<float[]> geomVerts, List<uint[]> geomIndices, List<VkMaterialDesc> materials, List<(int geo, int mat, Matrix4x4 world, Vector4 sphere)> instances, Texture? envCube, uint width, uint height)
    {
        _ctx = new VulkanContext(graphicsDevice);
        _api = _ctx.DeviceApi;
        _instanceCount = instances.Count;
        _width = Math.Max(width, 1u);
        _height = Math.Max(height, 1u);
        _envCube = envCube ?? throw new InvalidOperationException("[VkRenderer] No environment cubemap.");

        _matPC0 = new Vector4[materials.Count];
        _matRenderMode = new float[materials.Count];
        for (int i = 0; i < materials.Count; i++)
        {
            _matPC0[i] = new Vector4(materials[i].HasBaked, materials[i].ParallaxScale, materials[i].ParallaxBias, materials[i].AlphaThreshold);
            _matRenderMode[i] = materials[i].RenderMode;
        }

        int geoCount = geomVerts.Count;
        var baseVertex = new int[geoCount];
        var firstIndex = new uint[geoCount];
        var idxCount = new uint[geoCount];
        int vTotal = 0; uint iTotal = 0;
        for (int g = 0; g < geoCount; g++)
        {
            baseVertex[g] = vTotal;
            firstIndex[g] = iTotal;
            idxCount[g] = (uint)geomIndices[g].Length;
            vTotal += geomVerts[g].Length / VulkanSceneCapture.FloatsPerVertex;
            iTotal += (uint)geomIndices[g].Length;
        }
        var mergedVerts = new float[vTotal * VulkanSceneCapture.FloatsPerVertex];
        var mergedIdx = new uint[iTotal];
        int vo = 0; uint io = 0;
        for (int g = 0; g < geoCount; g++)
        {
            var p = geomVerts[g];
            Array.Copy(p, 0, mergedVerts, vo, p.Length); vo += p.Length;
            var ix = geomIndices[g];
            Array.Copy(ix, 0, mergedIdx, (int)io, ix.Length); io += (uint)ix.Length;
        }

        // Instances: opaque/cutout first, then translucent; sorted by material within each span.
        var matTranslucent = new bool[materials.Count];
        for (int i = 0; i < materials.Count; i++) matTranslucent[i] = materials[i].RenderMode > 1.5f;
        var order = new int[_instanceCount];
        for (int i = 0; i < _instanceCount; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int ta = matTranslucent[instances[a].mat] ? 1 : 0;
            int tb = matTranslucent[instances[b].mat] ? 1 : 0;
            return ta != tb ? ta - tb : instances[a].mat.CompareTo(instances[b].mat);
        });

        _drawIndexCount = new uint[_instanceCount];
        _drawFirstIndex = new uint[_instanceCount];
        _drawVertexOffset = new int[_instanceCount];
        _drawMatSlot = new int[_instanceCount];
        _instCenter = new Vector3[_instanceCount];
        _instRadius = new float[_instanceCount];
        var worlds = new Matrix4x4[_instanceCount];
        int translucentStart = _instanceCount;
        for (int i = 0; i < _instanceCount; i++)
        {
            var (g, mat, world, sphere) = instances[order[i]];
            _drawIndexCount[i] = idxCount[g];
            _drawFirstIndex[i] = firstIndex[g];
            _drawVertexOffset[i] = baseVertex[g];
            _drawMatSlot[i] = mat;
            worlds[i] = world;
            // The game's own world bounding sphere (Entity.WorldBoundingSphere): xyz centre, w radius.
            _instCenter[i] = new Vector3(sphere.X, sphere.Y, sphere.Z);
            _instRadius[i] = sphere.W;
            if (matTranslucent[mat] && i < translucentStart) translucentStart = i;
        }
        _translucentStart = translucentStart;

        // Per-frame culling scratch (no per-frame allocation).
        _cullScratch = new int[_instanceCount];
        _visibleOpaque = new int[_instanceCount];
        _visibleTranslucent = new int[_instanceCount];
        _chunkCounts = new int[Environment.ProcessorCount];

        var poolInfo = new VkCommandPoolCreateInfo { flags = VkCommandPoolCreateFlags.ResetCommandBuffer, queueFamilyIndex = _ctx.GraphicsQueueFamilyIndex };
        VkCommandPool pool; Check(_api.vkCreateCommandPool(&poolInfo, &pool), "vkCreateCommandPool"); _pool = pool;
        var cbAlloc = new VkCommandBufferAllocateInfo { commandPool = _pool, level = VkCommandBufferLevel.Primary, commandBufferCount = 1 };
        VkCommandBuffer cmd; Check(_api.vkAllocateCommandBuffers(&cbAlloc, &cmd), "vkAllocateCommandBuffers"); _cmd = cmd;
        var fenceInfo = new VkFenceCreateInfo();
        VkFence fence; Check(_api.vkCreateFence(&fenceInfo, &fence), "vkCreateFence"); _fence = fence;

        UploadGeometry(mergedVerts, mergedIdx);
        CreateVolumeGeometry();
        UploadTransforms(worlds);
        CreateUniformBuffers();
        CreateSampler();
        CreateDescriptors(materials);
        CreateRenderPasses();
        CreatePipelines();
        CreateTargets(graphicsDevice);
        // Command buffer is recorded per-frame in Frame() (only the visible, frustum-culled draws).

        Console.WriteLine($"[VkRenderer] Stage 14f init OK - scene: {geoCount} geometries, {materials.Count} materials, {_instanceCount} draws ({_translucentStart} opaque + {_instanceCount - _translucentStart} translucent/OIT) recorded once, into a {_width}x{_height} texture.");
    }

    private void UploadGeometry(float[] mergedVerts, uint[] mergedIdx)
    {
        ulong vSize = (ulong)(mergedVerts.Length * sizeof(float));
        (_vertexBuffer, _vbMemory) = CreateBuffer(vSize, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* vp; Check(_api.vkMapMemory(_vbMemory, 0, vSize, 0, &vp), "vkMapMemory(vb)");
        fixed (float* s = mergedVerts) Buffer.MemoryCopy(s, vp, vSize, vSize);
        _api.vkUnmapMemory(_vbMemory);

        ulong iSize = (ulong)(mergedIdx.Length * sizeof(uint));
        (_indexBuffer, _ibMemory) = CreateBuffer(iSize, VkBufferUsageFlags.IndexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* ip; Check(_api.vkMapMemory(_ibMemory, 0, iSize, 0, &ip), "vkMapMemory(ib)");
        fixed (uint* s = mergedIdx) Buffer.MemoryCopy(s, ip, iSize, iSize);
        _api.vkUnmapMemory(_ibMemory);
    }

    private void CreateVolumeGeometry()
    {
        // Two quads (thin in Y, thin in Z) → a unit-length "+" cross section, 4 verts + 2 tris each.
        uint[] idx = { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
        _edgeIndexCount = (uint)idx.Length;
        ulong iSize = (ulong)(idx.Length * sizeof(uint));
        (_edgeIndexBuffer, _edgeIbMemory) = CreateBuffer(iSize, VkBufferUsageFlags.IndexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* ip; Check(_api.vkMapMemory(_edgeIbMemory, 0, iSize, 0, &ip), "vkMapMemory(edgeIb)");
        fixed (uint* s = idx) Buffer.MemoryCopy(s, ip, iSize, iSize);
        _api.vkUnmapMemory(_edgeIbMemory);

        // Vertex buffer stays host-mapped so WriteEdgeVertices can rebuild it when the thickness changes.
        ulong vSize = 8 * 3 * sizeof(float);
        (_edgeVertexBuffer, _edgeVbMemory) = CreateBuffer(vSize, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* vp; Check(_api.vkMapMemory(_edgeVbMemory, 0, vSize, 0, &vp), "vkMapMemory(edgeVb)"); _edgeVbMapped = vp;
    }

    // Rebuilds the unit edge's 8 vertices for a given world-space thickness (t = thickness/2), matching
    // Primitives.CreateWireEdge: length ±0.5 along X, thin ±t on Y (quad 1) and Z (quad 2).
    private void WriteEdgeVertices(float thickness)
    {
        float t = MathF.Max(thickness, 0.001f) * 0.5f;
        float* p = (float*)_edgeVbMapped;
        // quad 1 (thin in Y)
        p[0] = -0.5f; p[1] = -t; p[2] = 0f;   p[3] = 0.5f; p[4] = -t; p[5] = 0f;
        p[6] = 0.5f;  p[7] = t;  p[8] = 0f;    p[9] = -0.5f; p[10] = t; p[11] = 0f;
        // quad 2 (thin in Z)
        p[12] = -0.5f; p[13] = 0f; p[14] = -t; p[15] = 0.5f; p[16] = 0f; p[17] = -t;
        p[18] = 0.5f;  p[19] = 0f; p[20] = t;  p[21] = -0.5f; p[22] = 0f; p[23] = t;
        _edgeThickness = thickness;
    }

    private void UploadTransforms(Matrix4x4[] worlds)
    {
        ulong size = (ulong)(Math.Max(worlds.Length, 1) * 64);
        (_transformBuffer, _tbMemory) = CreateBuffer(size, VkBufferUsageFlags.StorageBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* p; Check(_api.vkMapMemory(_tbMemory, 0, size, 0, &p), "vkMapMemory(tb)");
        var dst = (Matrix4x4*)p;
        for (int i = 0; i < worlds.Length; i++) dst[i] = worlds[i];
        _api.vkUnmapMemory(_tbMemory);
    }

    private void CreateUniformBuffers()
    {
        (_uniformBuffer, _ubMemory) = CreateBuffer(64, VkBufferUsageFlags.UniformBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* p; Check(_api.vkMapMemory(_ubMemory, 0, 64, 0, &p), "vkMapMemory(ub)"); _ubMapped = p;
        *(Matrix4x4*)_ubMapped = Matrix4x4.Identity;

        ulong lightSize = (ulong)sizeof(LightData);
        (_lightBuffer, _lightMemory) = CreateBuffer(lightSize, VkBufferUsageFlags.UniformBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* lp; Check(_api.vkMapMemory(_lightMemory, 0, lightSize, 0, &lp), "vkMapMemory(light)"); _lightMapped = lp;
        *(LightData*)_lightMapped = default;
    }

    private void CreateSampler()
    {
        var info = new VkSamplerCreateInfo
        {
            magFilter = VkFilter.Linear, minFilter = VkFilter.Linear, mipmapMode = VkSamplerMipmapMode.Linear,
            addressModeU = VkSamplerAddressMode.Repeat, addressModeV = VkSamplerAddressMode.Repeat, addressModeW = VkSamplerAddressMode.Repeat,
            minLod = 0f, maxLod = 16f, mipLodBias = 0f, maxAnisotropy = 1f,
        };
        VkSampler s; Check(_api.vkCreateSampler(&info, &s), "vkCreateSampler"); _sampler = s;
    }

    private void CreateDescriptors(List<VkMaterialDesc> materials)
    {
        VkDescriptorSetLayoutBinding* set0 = stackalloc VkDescriptorSetLayoutBinding[4];
        set0[0] = new VkDescriptorSetLayoutBinding { binding = 0, descriptorType = VkDescriptorType.UniformBuffer, descriptorCount = 1, stageFlags = VkShaderStageFlags.Vertex };
        set0[1] = new VkDescriptorSetLayoutBinding { binding = 1, descriptorType = VkDescriptorType.StorageBuffer, descriptorCount = 1, stageFlags = VkShaderStageFlags.Vertex };
        set0[2] = new VkDescriptorSetLayoutBinding { binding = 2, descriptorType = VkDescriptorType.UniformBuffer, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        set0[3] = new VkDescriptorSetLayoutBinding { binding = 3, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        var set0Info = new VkDescriptorSetLayoutCreateInfo { bindingCount = 4, pBindings = set0 };
        VkDescriptorSetLayout dl0; Check(_api.vkCreateDescriptorSetLayout(&set0Info, &dl0), "vkCreateDescriptorSetLayout(0)"); _descLayout = dl0;

        VkDescriptorSetLayoutBinding* set1 = stackalloc VkDescriptorSetLayoutBinding[TexPerMaterial];
        for (uint i = 0; i < TexPerMaterial; i++)
            set1[i] = new VkDescriptorSetLayoutBinding { binding = i, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        var set1Info = new VkDescriptorSetLayoutCreateInfo { bindingCount = TexPerMaterial, pBindings = set1 };
        VkDescriptorSetLayout dl1; Check(_api.vkCreateDescriptorSetLayout(&set1Info, &dl1), "vkCreateDescriptorSetLayout(1)"); _matSetLayout = dl1;

        VkDescriptorSetLayoutBinding* setR = stackalloc VkDescriptorSetLayoutBinding[2];
        setR[0] = new VkDescriptorSetLayoutBinding { binding = 0, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        setR[1] = new VkDescriptorSetLayoutBinding { binding = 1, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        var setRInfo = new VkDescriptorSetLayoutCreateInfo { bindingCount = 2, pBindings = setR };
        VkDescriptorSetLayout dlR; Check(_api.vkCreateDescriptorSetLayout(&setRInfo, &dlR), "vkCreateDescriptorSetLayout(resolve)"); _resolveSetLayout = dlR;

        int nMat = materials.Count;
        VkDescriptorPoolSize* sizes = stackalloc VkDescriptorPoolSize[3];
        sizes[0] = new VkDescriptorPoolSize { type = VkDescriptorType.UniformBuffer, descriptorCount = 2 };
        sizes[1] = new VkDescriptorPoolSize { type = VkDescriptorType.StorageBuffer, descriptorCount = 1 };
        sizes[2] = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = (uint)(TexPerMaterial * nMat + 1 + 2) }; // +cube +resolve(accum,reveal)
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = (uint)(1 + nMat + 1), poolSizeCount = 3, pPoolSizes = sizes };
        VkDescriptorPool dp; Check(_api.vkCreateDescriptorPool(&poolInfo, &dp), "vkCreateDescriptorPool"); _descPool = dp;

        VkImage cubeImage = _ctx.BackendInfo.GetVkImage(_envCube);
        var cubeViewInfo = new VkImageViewCreateInfo { image = cubeImage, viewType = VkImageViewType.ImageCube, format = ColorFormat, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Color, baseMipLevel = 0, levelCount = Math.Max(1u, _envCube.MipLevels), baseArrayLayer = 0, layerCount = 6 } };
        VkImageView cubeView; Check(_api.vkCreateImageView(&cubeViewInfo, &cubeView), "vkCreateImageView(cube)"); _envCubeView = cubeView;

        VkDescriptorSetLayout l0 = _descLayout;
        var alloc0 = new VkDescriptorSetAllocateInfo { descriptorPool = _descPool, descriptorSetCount = 1, pSetLayouts = &l0 };
        VkDescriptorSet ds0; Check(_api.vkAllocateDescriptorSets(&alloc0, &ds0), "vkAllocateDescriptorSets(0)"); _descSet = ds0;
        var uboInfo = new VkDescriptorBufferInfo { buffer = _uniformBuffer, offset = 0, range = 64 };
        var ssboInfo = new VkDescriptorBufferInfo { buffer = _transformBuffer, offset = 0, range = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE };
        var lightInfo = new VkDescriptorBufferInfo { buffer = _lightBuffer, offset = 0, range = (ulong)sizeof(LightData) };
        var cubeInfo = new VkDescriptorImageInfo { sampler = _sampler, imageView = _envCubeView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        VkWriteDescriptorSet* w0 = stackalloc VkWriteDescriptorSet[4];
        w0[0] = new VkWriteDescriptorSet { dstSet = _descSet, dstBinding = 0, descriptorCount = 1, descriptorType = VkDescriptorType.UniformBuffer, pBufferInfo = &uboInfo };
        w0[1] = new VkWriteDescriptorSet { dstSet = _descSet, dstBinding = 1, descriptorCount = 1, descriptorType = VkDescriptorType.StorageBuffer, pBufferInfo = &ssboInfo };
        w0[2] = new VkWriteDescriptorSet { dstSet = _descSet, dstBinding = 2, descriptorCount = 1, descriptorType = VkDescriptorType.UniformBuffer, pBufferInfo = &lightInfo };
        w0[3] = new VkWriteDescriptorSet { dstSet = _descSet, dstBinding = 3, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &cubeInfo };
        _api.vkUpdateDescriptorSets(4, w0, 0, null);

        VkDescriptorSetLayout lR = _resolveSetLayout;
        var allocR = new VkDescriptorSetAllocateInfo { descriptorPool = _descPool, descriptorSetCount = 1, pSetLayouts = &lR };
        VkDescriptorSet dsR; Check(_api.vkAllocateDescriptorSets(&allocR, &dsR), "vkAllocateDescriptorSets(resolve)"); _resolveSet = dsR;
        // Written in CreateTargets once accum/reveal exist (and re-written on resize).

        Texture? fallback = null;
        foreach (var m in materials) { fallback = m.Albedo ?? m.Normal ?? m.Props ?? m.LightColour ?? m.LightDir; if (fallback != null) break; }
        if (fallback == null) throw new InvalidOperationException("[VkRenderer] Scene has no material textures.");

        var viewOf = new Dictionary<Texture, VkImageView>(ReferenceEqualityComparer.Instance);
        VkImageView ViewFor(Texture? t)
        {
            t ??= fallback;
            if (viewOf.TryGetValue(t, out var existing)) return existing;
            VkImage image = _ctx.BackendInfo.GetVkImage(t);
            uint mips = Math.Max(1u, t.MipLevels);
            var vi = new VkImageViewCreateInfo { image = image, viewType = VkImageViewType.Image2D, format = ColorFormat, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Color, baseMipLevel = 0, levelCount = mips, baseArrayLayer = 0, layerCount = 1 } };
            VkImageView view; Check(_api.vkCreateImageView(&vi, &view), "vkCreateImageView(tex)");
            viewOf[t] = view;
            return view;
        }

        _matSets = new VkDescriptorSet[nMat];
        for (int i = 0; i < nMat; i++)
        {
            var m = materials[i];
            VkImageView* v = stackalloc VkImageView[TexPerMaterial] { ViewFor(m.Albedo), ViewFor(m.Normal), ViewFor(m.Props), ViewFor(m.LightColour), ViewFor(m.LightDir) };
            VkDescriptorSetLayout l1 = _matSetLayout;
            var alloc1 = new VkDescriptorSetAllocateInfo { descriptorPool = _descPool, descriptorSetCount = 1, pSetLayouts = &l1 };
            VkDescriptorSet ds; Check(_api.vkAllocateDescriptorSets(&alloc1, &ds), "vkAllocateDescriptorSets(mat)"); _matSets[i] = ds;
            VkDescriptorImageInfo* imgs = stackalloc VkDescriptorImageInfo[TexPerMaterial];
            VkWriteDescriptorSet* w = stackalloc VkWriteDescriptorSet[TexPerMaterial];
            for (uint bnd = 0; bnd < TexPerMaterial; bnd++)
            {
                imgs[bnd] = new VkDescriptorImageInfo { sampler = _sampler, imageView = v[bnd], imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
                w[bnd] = new VkWriteDescriptorSet { dstSet = ds, dstBinding = bnd, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &imgs[bnd] };
            }
            _api.vkUpdateDescriptorSets(TexPerMaterial, w, 0, null);
        }
        _texViews = [.. viewOf.Values];
    }

    private VkRenderPass MakeColorDepthPass(bool depthWrites, VkImageLayout colorFinal)
    {
        VkAttachmentDescription* a = stackalloc VkAttachmentDescription[2];
        a[0] = new VkAttachmentDescription { format = ColorFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Clear, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.Undefined, finalLayout = colorFinal };
        a[1] = new VkAttachmentDescription { format = DepthFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Clear, storeOp = depthWrites ? VkAttachmentStoreOp.Store : VkAttachmentStoreOp.DontCare, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.Undefined, finalLayout = VkImageLayout.DepthStencilAttachmentOptimal };
        var colorRef = new VkAttachmentReference { attachment = 0, layout = VkImageLayout.ColorAttachmentOptimal };
        var depthRef = new VkAttachmentReference { attachment = 1, layout = VkImageLayout.DepthStencilAttachmentOptimal };
        var subpass = new VkSubpassDescription { pipelineBindPoint = VkPipelineBindPoint.Graphics, colorAttachmentCount = 1, pColorAttachments = &colorRef, pDepthStencilAttachment = &depthRef };
        VkSubpassDependency* deps = stackalloc VkSubpassDependency[2];
        deps[0] = new VkSubpassDependency { srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, dstSubpass = 0, srcStageMask = VkPipelineStageFlags.FragmentShader, srcAccessMask = VkAccessFlags.ShaderRead, dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.EarlyFragmentTests, dstAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentWrite };
        deps[1] = new VkSubpassDependency { srcSubpass = 0, dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.LateFragmentTests, srcAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.EarlyFragmentTests, dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.DepthStencilAttachmentRead };
        var info = new VkRenderPassCreateInfo { attachmentCount = 2, pAttachments = a, subpassCount = 1, pSubpasses = &subpass, dependencyCount = 2, pDependencies = deps };
        VkRenderPass rp; Check(_api.vkCreateRenderPass(&info, &rp), "vkCreateRenderPass"); return rp;
    }

    private void CreateRenderPasses()
    {
        // Opaque: clears + writes colour and depth; colour left as attachment (resolve writes it later).
        _rpOpaque = MakeColorDepthPass(depthWrites: true, colorFinal: VkImageLayout.ColorAttachmentOptimal);

        // Accumulate: two colour targets (accum, reveal) + read-only depth from the opaque pass.
        VkAttachmentDescription* a = stackalloc VkAttachmentDescription[3];
        a[0] = new VkAttachmentDescription { format = AccumFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Clear, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.Undefined, finalLayout = VkImageLayout.ShaderReadOnlyOptimal };
        a[1] = new VkAttachmentDescription { format = RevealFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Clear, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.Undefined, finalLayout = VkImageLayout.ShaderReadOnlyOptimal };
        a[2] = new VkAttachmentDescription { format = DepthFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Load, storeOp = VkAttachmentStoreOp.DontCare, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.DepthStencilAttachmentOptimal, finalLayout = VkImageLayout.DepthStencilAttachmentOptimal };
        VkAttachmentReference* colorRefs = stackalloc VkAttachmentReference[2];
        colorRefs[0] = new VkAttachmentReference { attachment = 0, layout = VkImageLayout.ColorAttachmentOptimal };
        colorRefs[1] = new VkAttachmentReference { attachment = 1, layout = VkImageLayout.ColorAttachmentOptimal };
        var depthRefRO = new VkAttachmentReference { attachment = 2, layout = VkImageLayout.DepthStencilReadOnlyOptimal };
        var subA = new VkSubpassDescription { pipelineBindPoint = VkPipelineBindPoint.Graphics, colorAttachmentCount = 2, pColorAttachments = colorRefs, pDepthStencilAttachment = &depthRefRO };
        VkSubpassDependency* depsA = stackalloc VkSubpassDependency[2];
        depsA[0] = new VkSubpassDependency { srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, dstSubpass = 0, srcStageMask = VkPipelineStageFlags.LateFragmentTests, srcAccessMask = VkAccessFlags.DepthStencilAttachmentWrite, dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.EarlyFragmentTests, dstAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentRead };
        depsA[1] = new VkSubpassDependency { srcSubpass = 0, dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput, srcAccessMask = VkAccessFlags.ColorAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader, dstAccessMask = VkAccessFlags.ShaderRead };
        var infoA = new VkRenderPassCreateInfo { attachmentCount = 3, pAttachments = a, subpassCount = 1, pSubpasses = &subA, dependencyCount = 2, pDependencies = depsA };
        VkRenderPass rpA; Check(_api.vkCreateRenderPass(&infoA, &rpA), "vkCreateRenderPass(accum)"); _rpAccum = rpA;

        // Resolve: load the opaque colour, blend the resolved translucent over it, leave it ShaderRead.
        var c = new VkAttachmentDescription { format = ColorFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Load, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.ColorAttachmentOptimal, finalLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var cRef = new VkAttachmentReference { attachment = 0, layout = VkImageLayout.ColorAttachmentOptimal };
        var subR = new VkSubpassDescription { pipelineBindPoint = VkPipelineBindPoint.Graphics, colorAttachmentCount = 1, pColorAttachments = &cRef };
        VkSubpassDependency* depsR = stackalloc VkSubpassDependency[2];
        depsR[0] = new VkSubpassDependency { srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, dstSubpass = 0, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput, srcAccessMask = VkAccessFlags.ColorAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ColorAttachmentOutput, dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ColorAttachmentWrite };
        depsR[1] = new VkSubpassDependency { srcSubpass = 0, dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput, srcAccessMask = VkAccessFlags.ColorAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader, dstAccessMask = VkAccessFlags.ShaderRead };
        var infoR = new VkRenderPassCreateInfo { attachmentCount = 1, pAttachments = &c, subpassCount = 1, pSubpasses = &subR, dependencyCount = 2, pDependencies = depsR };
        VkRenderPass rpR; Check(_api.vkCreateRenderPass(&infoR, &rpR), "vkCreateRenderPass(resolve)"); _rpResolve = rpR;
    }

    private VkShaderModule Module(string glsl, ShaderStages stage)
    {
        byte[] spirv = SpirvCompilation.CompileGlslToSpirv(glsl, "vk", stage, new GlslCompileOptions()).SpirvBytes;
        fixed (byte* p = spirv)
        {
            var info = new VkShaderModuleCreateInfo { codeSize = (nuint)spirv.Length, pCode = (uint*)p };
            VkShaderModule m; Check(_api.vkCreateShaderModule(&info, &m), "vkCreateShaderModule");
            return m;
        }
    }

    private void CreatePipelines()
    {
        _vs = Module(VertexGlsl, ShaderStages.Vertex);
        _fsOpaque = Module(FragmentOpaqueGlsl, ShaderStages.Fragment);
        _fsAccum = Module(FragmentAccumGlsl, ShaderStages.Fragment);
        _resolveVs = Module(ResolveVertexGlsl, ShaderStages.Vertex);
        _resolveFs = Module(ResolveFragmentGlsl, ShaderStages.Fragment);

        // Lit pipeline layout: set0 (scene) + set1 (material) + 32-byte fragment push constant.
        VkDescriptorSetLayout* litSets = stackalloc VkDescriptorSetLayout[2] { _descLayout, _matSetLayout };
        var pushRange = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Fragment, offset = 0, size = 32 };
        var litLayoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 2, pSetLayouts = litSets, pushConstantRangeCount = 1, pPushConstantRanges = &pushRange };
        VkPipelineLayout litLayout; Check(_api.vkCreatePipelineLayout(&litLayoutInfo, &litLayout), "vkCreatePipelineLayout(lit)"); _layout = litLayout;

        VkDescriptorSetLayout rl = _resolveSetLayout;
        var resolveLayoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 1, pSetLayouts = &rl };
        VkPipelineLayout resolveLayout; Check(_api.vkCreatePipelineLayout(&resolveLayoutInfo, &resolveLayout), "vkCreatePipelineLayout(resolve)"); _resolveLayout = resolveLayout;

        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        var mask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A;
        VkDynamicState* dyn = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
        var dynState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dyn };
        var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
        var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };

        // --- Lit vertex input (opaque + accumulate).
        var vbinding = new VkVertexInputBindingDescription { binding = 0, stride = VertexStride, inputRate = VkVertexInputRate.Vertex };
        VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[6];
        attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
        attrs[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32Sfloat, offset = 12 };
        attrs[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 20 };
        attrs[3] = new VkVertexInputAttributeDescription { location = 3, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 32 };
        attrs[4] = new VkVertexInputAttributeDescription { location = 4, binding = 0, format = VkFormat.R32G32Sfloat, offset = 48 };
        attrs[5] = new VkVertexInputAttributeDescription { location = 5, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 56 };
        var litVertexInput = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbinding, vertexAttributeDescriptionCount = 6, pVertexAttributeDescriptions = attrs };
        var rasterCullNone = new VkPipelineRasterizationStateCreateInfo { polygonMode = VkPolygonMode.Fill, cullMode = VkCullModeFlags.None, frontFace = VkFrontFace.CounterClockwise, lineWidth = 1f };

        // Opaque: depth write, no blend, into _rpOpaque.
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _vs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _fsOpaque, pName = entry };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.LessOrEqual };
            var blendAttach = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = mask };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _layout, renderPass = _rpOpaque, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(default, 1, &info, &p), "vkCreateGraphicsPipelines(opaque)"); _pipelineOpaque = p;
        }

        // Accumulate: depth test only, two attachments - accum additive, reveal multiplicative - into _rpAccum.
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _vs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _fsAccum, pName = entry };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.LessOrEqual };
            VkPipelineColorBlendAttachmentState* atts = stackalloc VkPipelineColorBlendAttachmentState[2];
            atts[0] = new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.One, dstColorBlendFactor = VkBlendFactor.One, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.One, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add, colorWriteMask = mask };
            atts[1] = new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.Zero, dstColorBlendFactor = VkBlendFactor.OneMinusSrcColor, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.OneMinusSrcColor, alphaBlendOp = VkBlendOp.Add, colorWriteMask = VkColorComponentFlags.R };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 2, pAttachments = atts };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _layout, renderPass = _rpAccum, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(default, 1, &info, &p), "vkCreateGraphicsPipelines(accum)"); _pipelineAccum = p;
        }

        // Resolve: fullscreen triangle, no vertex input, blend over the opaque colour, into _rpResolve.
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _resolveVs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _resolveFs, pName = entry };
            var emptyVertexInput = new VkPipelineVertexInputStateCreateInfo();
            var rasterCullNoneR = new VkPipelineRasterizationStateCreateInfo { polygonMode = VkPolygonMode.Fill, cullMode = VkCullModeFlags.None, frontFace = VkFrontFace.CounterClockwise, lineWidth = 1f };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = false, depthWriteEnable = false, depthCompareOp = VkCompareOp.Always };
            // final = avg*(1-reveal) + dst*reveal; keep dst alpha (=1). src.a = reveal.
            var blendAttach = new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha, dstColorBlendFactor = VkBlendFactor.SrcAlpha, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add, colorWriteMask = mask };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &emptyVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNoneR, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _resolveLayout, renderPass = _rpResolve, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(default, 1, &info, &p), "vkCreateGraphicsPipelines(resolve)"); _pipelineResolve = p;
        }

        // Volumes: line-list wireframe cube, depth-tested + writing (occluded by opaque scene), drawn in
        // the opaque pass. Own layout = set0 (for uMvp) + a Vertex|Fragment push constant (mat4 + colour).
        _volumeVs = Module(VolumeVertexGlsl, ShaderStages.Vertex);
        _volumeFs = Module(VolumeFragmentGlsl, ShaderStages.Fragment);
        VkDescriptorSetLayout vl = _descLayout;
        var volPush = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, offset = 0, size = 80 };
        var volLayoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 1, pSetLayouts = &vl, pushConstantRangeCount = 1, pPushConstantRanges = &volPush };
        VkPipelineLayout volLayout; Check(_api.vkCreatePipelineLayout(&volLayoutInfo, &volLayout), "vkCreatePipelineLayout(volume)"); _volumeLayout = volLayout;
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _volumeVs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _volumeFs, pName = entry };
            var vbind = new VkVertexInputBindingDescription { binding = 0, stride = 3 * sizeof(float), inputRate = VkVertexInputRate.Vertex };
            var vattr = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
            var vin = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbind, vertexAttributeDescriptionCount = 1, pVertexAttributeDescriptions = &vattr };
            var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.LessOrEqual };
            var blendAttach = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = mask };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _volumeLayout, renderPass = _rpOpaque, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(default, 1, &info, &p), "vkCreateGraphicsPipelines(volume)"); _volumePipeline = p;
        }
    }

    private (VkImage, VkDeviceMemory, VkImageView) CreateAttachmentImage(VkFormat format, VkImageUsageFlags usage, VkImageAspectFlags aspect)
    {
        var info = new VkImageCreateInfo { imageType = VkImageType.Image2D, format = format, extent = new VkExtent3D { width = _width, height = _height, depth = 1 }, mipLevels = 1, arrayLayers = 1, samples = VkSampleCountFlags.Count1, tiling = VkImageTiling.Optimal, usage = usage, sharingMode = VkSharingMode.Exclusive, initialLayout = VkImageLayout.Undefined };
        VkImage image; Check(_api.vkCreateImage(&info, &image), "vkCreateImage");
        VkMemoryRequirements reqs; _api.vkGetImageMemoryRequirements(image, &reqs);
        VkDeviceMemory memory = Allocate(reqs, VkMemoryPropertyFlags.DeviceLocal);
        Check(_api.vkBindImageMemory(image, memory, 0), "vkBindImageMemory");
        var viewInfo = new VkImageViewCreateInfo { image = image, viewType = VkImageViewType.Image2D, format = format, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = aspect, baseMipLevel = 0, levelCount = 1, baseArrayLayer = 0, layerCount = 1 } };
        VkImageView view; Check(_api.vkCreateImageView(&viewInfo, &view), "vkCreateImageView");
        return (image, memory, view);
    }

    private void CreateTargets(GraphicsDevice gd)
    {
        _colorTex = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(_width, _height, 1, 1, PixelFormat.R8G8B8A8UNorm, TextureUsage.Sampled | TextureUsage.RenderTarget));
        _colorImage = _ctx.BackendInfo.GetVkImage(_colorTex);
        var colorViewInfo = new VkImageViewCreateInfo { image = _colorImage, viewType = VkImageViewType.Image2D, format = ColorFormat, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Color, baseMipLevel = 0, levelCount = 1, baseArrayLayer = 0, layerCount = 1 } };
        VkImageView cv; Check(_api.vkCreateImageView(&colorViewInfo, &cv), "vkCreateImageView(color)"); _colorView = cv;

        (_depthImage, _depthMemory, _depthView) = CreateAttachmentImage(DepthFormat, VkImageUsageFlags.DepthStencilAttachment, VkImageAspectFlags.Depth);
        (_accumImage, _accumMemory, _accumView) = CreateAttachmentImage(AccumFormat, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color);
        (_revealImage, _revealMemory, _revealView) = CreateAttachmentImage(RevealFormat, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color);

        VkImageView* oViews = stackalloc VkImageView[2] { _colorView, _depthView };
        var fbO = new VkFramebufferCreateInfo { renderPass = _rpOpaque, attachmentCount = 2, pAttachments = oViews, width = _width, height = _height, layers = 1 };
        VkFramebuffer fo; Check(_api.vkCreateFramebuffer(&fbO, &fo), "vkCreateFramebuffer(opaque)"); _fbOpaque = fo;

        VkImageView* aViews = stackalloc VkImageView[3] { _accumView, _revealView, _depthView };
        var fbA = new VkFramebufferCreateInfo { renderPass = _rpAccum, attachmentCount = 3, pAttachments = aViews, width = _width, height = _height, layers = 1 };
        VkFramebuffer fa; Check(_api.vkCreateFramebuffer(&fbA, &fa), "vkCreateFramebuffer(accum)"); _fbAccum = fa;

        VkImageView rView = _colorView;
        var fbR = new VkFramebufferCreateInfo { renderPass = _rpResolve, attachmentCount = 1, pAttachments = &rView, width = _width, height = _height, layers = 1 };
        VkFramebuffer fr; Check(_api.vkCreateFramebuffer(&fbR, &fr), "vkCreateFramebuffer(resolve)"); _fbResolve = fr;

        // (Re)point the resolve descriptor set at the current accum/reveal views.
        var accumInfo = new VkDescriptorImageInfo { sampler = _sampler, imageView = _accumView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var revealInfo = new VkDescriptorImageInfo { sampler = _sampler, imageView = _revealView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        VkWriteDescriptorSet* wr = stackalloc VkWriteDescriptorSet[2];
        wr[0] = new VkWriteDescriptorSet { dstSet = _resolveSet, dstBinding = 0, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &accumInfo };
        wr[1] = new VkWriteDescriptorSet { dstSet = _resolveSet, dstBinding = 1, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &revealInfo };
        _api.vkUpdateDescriptorSets(2, wr, 0, null);
    }

    private void DestroyTargets()
    {
        _api.vkDestroyFramebuffer(_fbResolve);
        _api.vkDestroyFramebuffer(_fbAccum);
        _api.vkDestroyFramebuffer(_fbOpaque);
        _api.vkDestroyImageView(_revealView); _api.vkDestroyImage(_revealImage); _api.vkFreeMemory(_revealMemory);
        _api.vkDestroyImageView(_accumView); _api.vkDestroyImage(_accumImage); _api.vkFreeMemory(_accumMemory);
        _api.vkDestroyImageView(_depthView); _api.vkDestroyImage(_depthImage); _api.vkFreeMemory(_depthMemory);
        _api.vkDestroyImageView(_colorView);
        _colorTex.Dispose();
    }

    private void SetFullViewport()
    {
        // Negative-height viewport (Vulkan 1.1) flips Y to match Bliss's Cam3D projection. Set once;
        // it persists across the render passes in this command buffer.
        var vp = new VkViewport { x = 0, y = _height, width = _width, height = -(float)_height, minDepth = 0, maxDepth = 1 };
        var sc = new VkRect2D { offset = default, extent = new VkExtent2D { width = _width, height = _height } };
        _api.vkCmdSetViewport(_cmd, 0, 1, &vp);
        _api.vkCmdSetScissor(_cmd, 0, 1, &sc);
    }

    // Re-recorded every frame (implicit reset via the pool's ResetCommandBuffer flag) with only the
    // frustum-visible draws. Safe because Frame() waits the fence before the next record.
    private void RecordCommands()
    {
        Check(_api.vkResetCommandBuffer(_cmd, VkCommandBufferResetFlags.None), "vkResetCommandBuffer");
        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(_api.vkBeginCommandBuffer(_cmd, &begin), "vkBeginCommandBuffer");
        SetFullViewport();

        VkDescriptorSet set0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _layout, 0, 1, &set0, 0, null);
        VkBuffer vb = _vertexBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_cmd, _indexBuffer, 0, VkIndexType.Uint32);
        var area = new VkRect2D { offset = default, extent = new VkExtent2D { width = _width, height = _height } };

        // Pass 1: OPAQUE -> colour + depth.
        VkClearValue* clearsO = stackalloc VkClearValue[2];
        clearsO[0] = new VkClearValue { color = new VkClearColorValue(0.05f, 0.06f, 0.09f, 1f) };
        clearsO[1] = new VkClearValue { depthStencil = new VkClearDepthStencilValue(1f, 0) };
        var rpO = new VkRenderPassBeginInfo { renderPass = _rpOpaque, framebuffer = _fbOpaque, renderArea = area, clearValueCount = 2, pClearValues = clearsO };
        _api.vkCmdBeginRenderPass(_cmd, &rpO, VkSubpassContents.Inline);
        DrawVisible(_pipelineOpaque, _visibleOpaque, _visOpaqueCount);
        DrawVolumes();
        _api.vkCmdEndRenderPass(_cmd);

        // Pass 2: ACCUMULATE translucent -> accum (=0) + reveal (=1), depth-tested (no write).
        VkClearValue* clearsA = stackalloc VkClearValue[2];
        clearsA[0] = new VkClearValue { color = new VkClearColorValue(0f, 0f, 0f, 0f) };
        clearsA[1] = new VkClearValue { color = new VkClearColorValue(1f, 0f, 0f, 0f) };
        var rpA = new VkRenderPassBeginInfo { renderPass = _rpAccum, framebuffer = _fbAccum, renderArea = area, clearValueCount = 2, pClearValues = clearsA };
        _api.vkCmdBeginRenderPass(_cmd, &rpA, VkSubpassContents.Inline);
        DrawVisible(_pipelineAccum, _visibleTranslucent, _visTransCount);
        _api.vkCmdEndRenderPass(_cmd);

        // Pass 3: RESOLVE -> composite over the opaque colour (fullscreen triangle).
        var rpR = new VkRenderPassBeginInfo { renderPass = _rpResolve, framebuffer = _fbResolve, renderArea = area, clearValueCount = 0, pClearValues = null };
        _api.vkCmdBeginRenderPass(_cmd, &rpR, VkSubpassContents.Inline);
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _pipelineResolve);
        VkDescriptorSet rset = _resolveSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _resolveLayout, 0, 1, &rset, 0, null);
        _api.vkCmdDraw(_cmd, 3, 1, 0, 0);
        _api.vkCmdEndRenderPass(_cmd);

        Check(_api.vkEndCommandBuffer(_cmd), "vkEndCommandBuffer");
    }

    // Draws the given ordered visible-index list (indices into the sorted static arrays), binding each
    // material's set + push constant once per run. firstInstance stays the STATIC index so it still
    // addresses that instance's transform in the (whole, unculled) SSBO.
    // Draws the per-frame volume list as depth-tested wireframe cubes (line list), one push constant
    // (box matrix + colour) each. Cheap (few volumes); provided fresh each frame so selection/edits show.
    private void DrawVolumes()
    {
        if (_volumeCount == 0) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _volumePipeline);
        VkDescriptorSet s0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _volumeLayout, 0, 1, &s0, 0, null);
        VkBuffer evb = _edgeVertexBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &evb, &offset);
        _api.vkCmdBindIndexBuffer(_cmd, _edgeIndexBuffer, 0, VkIndexType.Uint32);
        byte* pc = stackalloc byte[80];
        for (int i = 0; i < _volumeCount; i++)
        {
            var v = _volumes[i];
            *(Matrix4x4*)pc = v.world;
            *(Vector4*)(pc + 64) = v.color;
            _api.vkCmdPushConstants(_cmd, _volumeLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pc);
            _api.vkCmdDrawIndexed(_cmd, _edgeIndexCount, 1, 0, 0, 0);
        }
    }

    private void DrawVisible(VkPipeline pipeline, int[] visible, int count)
    {
        if (count == 0) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, pipeline);
        int boundMat = -1;
        for (int k = 0; k < count; k++)
        {
            int i = visible[k];
            if (_drawMatSlot[i] != boundMat)
            {
                boundMat = _drawMatSlot[i];
                VkDescriptorSet ms = _matSets[boundMat];
                _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _layout, 1, 1, &ms, 0, null);
                Vector4* pc = stackalloc Vector4[2] { _matPC0[boundMat], new Vector4(_matRenderMode[boundMat], 0f, 0f, 0f) };
                _api.vkCmdPushConstants(_cmd, _layout, VkShaderStageFlags.Fragment, 0, 32, pc);
            }
            _api.vkCmdDrawIndexed(_cmd, _drawIndexCount[i], 1, _drawFirstIndex[i], _drawVertexOffset[i], (uint)i);
        }
    }

    public void Frame(Matrix4x4 viewProj, LightData light, IReadOnlyList<(Matrix4x4 world, Vector4 color)> volumes, float volumeThickness)
    {
        *(Matrix4x4*)_ubMapped = viewProj;
        *(LightData*)_lightMapped = light;
        _volumes = volumes;
        _volumeCount = volumes.Count;
        if (volumeThickness != _edgeThickness) WriteEdgeVertices(volumeThickness);

        // Frustum-cull (threaded) then re-record only the visible draws. Safe to reset/re-record the
        // command buffer here: the previous frame's fence wait (below) already drained the GPU.
        using (Diagnostics.FrameProfiler.Sample("Vk Cull"))
        {
            ExtractFrustumPlanes(viewProj);
            _visOpaqueCount = CullRange(0, _translucentStart, _visibleOpaque);
            _visTransCount = CullRange(_translucentStart, _instanceCount, _visibleTranslucent);
        }
        using (Diagnostics.FrameProfiler.Sample("Vk Record"))
            RecordCommands();

        VkCommandBuffer cmd = _cmd;
        var submit = new VkSubmitInfo { commandBufferCount = 1, pCommandBuffers = &cmd };
        Check(_api.vkQueueSubmit(_ctx.GraphicsQueue, 1, &submit, _fence), "vkQueueSubmit");
        VkFence fence = _fence;
        _api.vkWaitForFences(1, &fence, true, ulong.MaxValue);
        _api.vkResetFences(1, &fence);
        _submits++;

        _ctx.BackendInfo.OverrideImageLayout(_colorTex, (uint)VkImageLayout.ShaderReadOnlyOptimal);

        if (!_loggedInit) { _loggedInit = true; Console.WriteLine($"[VkRenderer] Stage 14f: culling live - {_visOpaqueCount}/{_translucentStart} opaque + {_visTransCount}/{_instanceCount - _translucentStart} translucent visible frame 1."); }
        Diagnostics.FrameProfiler.SetCounter("Vk visible draws", _visOpaqueCount + _visTransCount);
        Diagnostics.FrameProfiler.SetCounter("Vk total draws", _instanceCount);
    }

    // Six frustum planes (left,right,bottom,top,near,far) in world space from the row-vector viewProj
    // (Gribb-Hartmann; D3D/Vulkan clip with z in [0,1]). Plane (a,b,c,d): a·x+b·y+c·z+d >= 0 is inside.
    private void ExtractFrustumPlanes(Matrix4x4 m)
    {
        _planes[0] = NormalizePlane(new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41));
        _planes[1] = NormalizePlane(new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41));
        _planes[2] = NormalizePlane(new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42));
        _planes[3] = NormalizePlane(new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42));
        _planes[4] = NormalizePlane(new Vector4(m.M13, m.M23, m.M33, m.M43));
        _planes[5] = NormalizePlane(new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43));
    }

    private static Vector4 NormalizePlane(Vector4 p)
    {
        float len = new Vector3(p.X, p.Y, p.Z).Length();
        return len > 0f ? p / len : p;
    }

    // Culls [lo,hi) of the sorted instances into outBuf (ordered, indices preserved). Threaded for large
    // ranges: each chunk compacts into its own disjoint region of _cullScratch (no locks), then the
    // per-chunk runs are concatenated in order - so material-run batching in DrawVisible still holds.
    private int CullRange(int lo, int hi, int[] outBuf)
    {
        int n = hi - lo;
        if (n <= 0) return 0;
        if (n < 4096)
        {
            int c = 0;
            for (int i = lo; i < hi; i++) if (Visible(i)) outBuf[c++] = i;
            return c;
        }
        int threads = Math.Min(_chunkCounts.Length, Math.Max(2, n / 4096));
        int chunkLen = (n + threads - 1) / threads;
        System.Threading.Tasks.Parallel.For(0, threads, t =>
        {
            int s = lo + t * chunkLen; int e = Math.Min(s + chunkLen, hi);
            int w = s;
            for (int i = s; i < e; i++) if (Visible(i)) _cullScratch[w++] = i;
            _chunkCounts[t] = w - s;
        });
        int total = 0;
        for (int t = 0; t < threads; t++)
        {
            int s = lo + t * chunkLen;
            Array.Copy(_cullScratch, s, outBuf, total, _chunkCounts[t]);
            total += _chunkCounts[t];
        }
        return total;
    }

    private bool Visible(int i)
    {
        Vector3 c = _instCenter[i]; float r = _instRadius[i];
        for (int p = 0; p < 6; p++)
        {
            Vector4 pl = _planes[p];
            if (pl.X * c.X + pl.Y * c.Y + pl.Z * c.Z + pl.W < -r) return false;
        }
        return true;
    }

    public void Resize(GraphicsDevice gd, uint width, uint height)
    {
        width = Math.Max(width, 1u); height = Math.Max(height, 1u);
        if (width == _width && height == _height) return;
        _api.vkDeviceWaitIdle();
        DestroyTargets();
        _width = width; _height = height;
        CreateTargets(gd);
        // Next Frame() re-records against the new targets.
    }

    private (VkBuffer, VkDeviceMemory) CreateBuffer(ulong size, VkBufferUsageFlags usage, VkMemoryPropertyFlags props)
    {
        var info = new VkBufferCreateInfo { size = size, usage = usage, sharingMode = VkSharingMode.Exclusive };
        VkBuffer buffer; Check(_api.vkCreateBuffer(&info, &buffer), "vkCreateBuffer");
        VkMemoryRequirements reqs; _api.vkGetBufferMemoryRequirements(buffer, &reqs);
        VkDeviceMemory memory = Allocate(reqs, props);
        Check(_api.vkBindBufferMemory(buffer, memory, 0), "vkBindBufferMemory");
        return (buffer, memory);
    }

    private VkDeviceMemory Allocate(VkMemoryRequirements reqs, VkMemoryPropertyFlags required)
    {
        VkPhysicalDeviceMemoryProperties memProps;
        _ctx.InstanceApi.vkGetPhysicalDeviceMemoryProperties(_ctx.PhysicalDevice, &memProps);
        uint typeIndex = uint.MaxValue;
        for (uint i = 0; i < memProps.memoryTypeCount; i++)
            if ((reqs.memoryTypeBits & (1u << (int)i)) != 0 && (memProps.memoryTypes[(int)i].propertyFlags & required) == required) { typeIndex = i; break; }
        if (typeIndex == uint.MaxValue) throw new InvalidOperationException($"[VkRenderer] No memory type for {required}.");
        var allocInfo = new VkMemoryAllocateInfo { allocationSize = reqs.size, memoryTypeIndex = typeIndex };
        VkDeviceMemory memory; Check(_api.vkAllocateMemory(&allocInfo, &memory), "vkAllocateMemory");
        return memory;
    }

    private static void Check(VkResult result, string what)
    {
        if (result != VkResult.Success) throw new InvalidOperationException($"[VkRenderer] {what} -> {result}");
    }

    public void Dispose()
    {
        _api.vkDeviceWaitIdle();
        DestroyTargets();
        _api.vkDestroyImageView(_envCubeView);
        foreach (var v in _texViews) _api.vkDestroyImageView(v);
        _api.vkDestroySampler(_sampler);
        _api.vkDestroyPipeline(_pipelineOpaque);
        _api.vkDestroyPipeline(_pipelineAccum);
        _api.vkDestroyPipeline(_pipelineResolve);
        _api.vkDestroyPipeline(_volumePipeline);
        _api.vkDestroyPipelineLayout(_layout);
        _api.vkDestroyPipelineLayout(_resolveLayout);
        _api.vkDestroyPipelineLayout(_volumeLayout);
        _api.vkDestroyShaderModule(_vs);
        _api.vkDestroyShaderModule(_fsOpaque);
        _api.vkDestroyShaderModule(_fsAccum);
        _api.vkDestroyShaderModule(_resolveVs);
        _api.vkDestroyShaderModule(_resolveFs);
        _api.vkDestroyShaderModule(_volumeVs);
        _api.vkDestroyShaderModule(_volumeFs);
        _api.vkDestroyBuffer(_edgeVertexBuffer); _api.vkFreeMemory(_edgeVbMemory);
        _api.vkDestroyBuffer(_edgeIndexBuffer); _api.vkFreeMemory(_edgeIbMemory);
        _api.vkDestroyRenderPass(_rpOpaque);
        _api.vkDestroyRenderPass(_rpAccum);
        _api.vkDestroyRenderPass(_rpResolve);
        _api.vkDestroyDescriptorPool(_descPool);
        _api.vkDestroyDescriptorSetLayout(_matSetLayout);
        _api.vkDestroyDescriptorSetLayout(_descLayout);
        _api.vkDestroyDescriptorSetLayout(_resolveSetLayout);
        _api.vkDestroyBuffer(_transformBuffer); _api.vkFreeMemory(_tbMemory);
        _api.vkDestroyBuffer(_lightBuffer); _api.vkFreeMemory(_lightMemory);
        _api.vkDestroyBuffer(_uniformBuffer); _api.vkFreeMemory(_ubMemory);
        _api.vkDestroyBuffer(_indexBuffer); _api.vkFreeMemory(_ibMemory);
        _api.vkDestroyBuffer(_vertexBuffer); _api.vkFreeMemory(_vbMemory);
        _api.vkDestroyFence(_fence);
        _api.vkDestroyCommandPool(_pool);
    }
}
