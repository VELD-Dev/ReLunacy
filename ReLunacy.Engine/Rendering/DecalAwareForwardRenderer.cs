using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Effects;
using Bliss.CSharp.Graphics;
using Bliss.CSharp.Graphics.Pipelines;
using Bliss.CSharp.Graphics.Pipelines.Buffers;
using Bliss.CSharp.Graphics.Pipelines.Textures;
using Bliss.CSharp.Graphics.Rendering;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Materials;
using ReLunacy.Engine.Diagnostics;
using Veldrith;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Drop-in replacement for Bliss's BasicForwardRenderer that disables depth *writes* (but keeps
/// depth testing) for translucent renderables. BasicForwardRenderer hardcodes
/// DepthStencilStateDescription.DEPTH_ONLY_LESS_EQUAL (depth write ON) for every renderable
/// regardless of RenderMode, and never exposes a way to change that per-material/per-draw.
/// That's fine for opaque geometry, but any AlphaBlend "decal" mesh that sits flush against
/// (or very close to) the opaque surface it's meant to fade into — e.g. UFrag vine/moss decals
/// painted directly onto terrain chunks — z-fights against that surface once it writes its own
/// depth, since both surfaces occupy nearly the same depth value. The result: instead of a soft
/// alpha fade, large parts of the decal randomly fail the depth test and are never drawn at all,
/// which reads as a hard rectangular clip rather than a gradient. Verified against Bliss 1.6.15's
/// real source (BasicForwardRenderer.cs on GitHub, matches the shipped DLL) — this isn't
/// configurable there, and BasicForwardRenderer's methods aren't virtual, so it can't be patched
/// via inheritance; hence this parallel implementation via the small IRenderer interface.
/// </summary>
public class DecalAwareForwardRenderer : IRenderer
{
    private readonly List<Renderable> _opaqueRenderables = [];
    private readonly List<Renderable> _translucentRenderables = [];
    private SimplePipelineDescription _pipelineDescription;

    /// <summary>Master switch for the instanced draw path. On, the renderer uploads every visible
    /// instance's transform into one storage buffer and collapses runs of identical (mesh, material)
    /// into a single instanced draw — killing both the per-draw transform descriptor bind and most of
    /// the draw-call count, which is what makes 20k+ visible meshes affordable. Off falls back to the
    /// per-renderable path (kept as an escape hatch since this touches the lit shader). Only effects
    /// whose vertex shader declares the InstanceTransforms storage buffer are instanced; the rest use
    /// the per-renderable path regardless.</summary>
    public bool UseInstancing = true;

    // One storage buffer holding every instanced renderable's transform for the frame, uploaded once.
    // gl_InstanceIndex (with a per-batch firstInstance offset) indexes it in the vertex shader. Grown
    // as needed; the resource set is rebuilt when the buffer is (re)created — see EnsureInstanceCapacity.
    private DeviceBuffer? _instanceTransformBuffer;
    private uint _instanceCapacity;
    private Matrix4x4[] _instanceScratch = new Matrix4x4[4096];
    private int _instanceCount;
    // Resource set binding _instanceTransformBuffer, keyed by the effect layout it was built against.
    private ResourceSet? _instanceSet;
    private SimpleBufferLayout? _instanceSetLayout;

    // Last texture resource set bound per slot, so consecutive materials sharing a texture (the lit
    // effect's normal/properties/detail/lightmap slots are usually the same shared default across
    // most materials) skip re-binding it. Indexed by texture slot; only a handful of slots exist.
    // Reset at the start of each pass and after any pipeline change (which can invalidate bindings).
    private readonly ResourceSet?[] _lastTexSet = new ResourceSet?[32];

    // Temp sub-timers for Draw Record, to localize the per-batch cost (ticks). See Draw's report.
    private long _tsPipeline, _tsBind, _tsDraw;

    // Reusable scratch for the opaque sort (primitive key + parallel items), grown as needed so the
    // per-frame sort allocates nothing.
    private long[] _sortKeys = new long[8192];
    private Renderable[] _sortItems = new Renderable[8192];

    // Our own pipeline cache, keyed by reference identity of (material, vertex format, pass). Bliss's
    // Effect.GetPipeline already caches the Pipeline objects, but its key is the whole
    // SimplePipelineDescription, so every lookup rebuilds a ShaderSetDescription and deep-hashes the
    // description — ~15us each, ~4.5k times a frame. This collapses that to one cheap identity lookup;
    // Bliss's GetPipeline is only hit on the first sight of a given combo. Cleared when the render
    // target's OutputDescription changes (resize), which invalidates every pipeline built against it.
    private readonly Dictionary<(Bliss.CSharp.Materials.Material Material, VertexFormat Format, int Pass), SimplePipeline> _pipelineCache = new();
    private OutputDescription _pipelineCacheOutput;

    // Scene-wide, not per-renderable — only ever bound for materials whose Effect declares a
    // "LightBuffer" layout (currently just LitModelShaderSource, via AssetManager's lit effect),
    // checked by name in DrawPreparedRenderable rather than assumed, since GetBufferLayoutSlot
    // throws KeyNotFoundException for any effect that doesn't declare it (every other effect in
    // this engine, at least for now). Values default to a plain downward light — View3D pushes
    // the real EditorSettings.LightDirection/LightColor/Ambient in every frame, same pattern as
    // Camera.FarPlane/VolumeWireThickness.
    private readonly SimpleUniformBuffer<LightData> _lightBuffer;
    public Vector3 LightDirection = new(-0.4f, -0.8f, 0.3f);
    public Vector3 LightColor = Vector3.One;
    public float Ambient = 0.15f;
    public float SpecularPower = 32f;
    // Averaged from the level's own cubemap at load; see LightData.EnvironmentColour. Intensity
    // defaults to 0 so nothing changes until a level actually supplies one.
    public Vector3 EnvironmentColour = Vector3.One;
    public float EnvironmentIntensity;
    // Debug: draw the raw cubemap reflection on everything (see LightData.ReflectionDebugView).
    public bool ReflectionDebugView;
    // Fresnel F0 for the cubemap reflection (see LightData.ReflectionBase). 0 = specular-map-gated.
    public float ReflectionBase;
    // The level's analytic lighting environment (section 0x8b00), pushed by View3D from
    // LevelData.LightingEnvironment. Lights undecoded (non-baked) surfaces with the game's own
    // sun/ambient. HasLightingEnvironment stays false for levels without one (flat fallback).
    public bool HasLightingEnvironment;
    public Vector3 EnvDirection0 = Vector3.UnitY;
    public Vector3 EnvDirection1 = Vector3.UnitY;
    public Vector3 EnvAmbient;
    public Vector3 EnvLight0Colour;
    public Vector3 EnvLight1Colour;
    // The level's environment cubemap (AssetManager.EnvironmentCubemapView), sampled by the lit
    // effect for reflections. Scene-wide like LightBuffer — bound below for any effect that declares
    // the "fEnvCube" texture layout. View3D pushes this each frame, same pattern as EnvironmentColour.
    public TextureView? EnvironmentCubemap;
    private ResourceSet? _envCubeSet;
    private TextureView? _envCubeSetView;
    // Live lightmap research controls — see LightData for what each one stands in for.
    public Vector2 LightmapUVScale = Vector2.One;
    public Vector2 LightmapUVOffset = Vector2.Zero;
    public float BakedLightScale = 4f;
    public float BakedBumpFade = 1f;
    public bool BakedDebugView;
    public Vector2 LightmapUVPivot = new(0.5f, 0.5f);
    public float LightmapUVRotation;

    public GraphicsDevice GraphicsDevice { get; }

    public DecalAwareForwardRenderer(GraphicsDevice graphicsDevice)
    {
        GraphicsDevice = graphicsDevice;
        _pipelineDescription = new SimplePipelineDescription
        {
            PrimitiveTopology = PrimitiveTopology.TriangleList
        };
        _lightBuffer = new SimpleUniformBuffer<LightData>(graphicsDevice, 1u, ShaderStages.Fragment);
    }

    /// <summary>Builds the scene-wide LightData from the current public lighting fields (set per frame
    /// by View3D). Public so the from-scratch raw-Vulkan renderer can upload the SAME lighting into its
    /// own uniform without duplicating the field-to-struct mapping. Direction is normalized here rather
    /// than trusting the caller; SpecularPower is clamped away from 0 (pow(x,0) would highlight the
    /// whole scene).</summary>
    public LightData BuildLightData(Vector3 cameraPosition) => new()
    {
        Direction = LightDirection.LengthSquared() > 0f ? Vector3.Normalize(LightDirection) : Vector3.UnitY,
        Ambient = Ambient,
        Color = LightColor,
        SpecularPower = MathF.Max(SpecularPower, 1f),
        CameraPosition = cameraPosition,
        ReflectionDebugView = ReflectionDebugView ? 1f : 0f,
        ReflectionBase = ReflectionBase,
        EnvironmentColour = EnvironmentColour,
        EnvironmentIntensity = EnvironmentIntensity,
        LightmapUVScale = LightmapUVScale,
        LightmapUVOffset = LightmapUVOffset,
        BakedLightScale = BakedLightScale,
        BakedBumpFade = BakedBumpFade,
        BakedDebugView = BakedDebugView ? 1f : 0f,
        LightmapUVPivot = LightmapUVPivot,
        LightmapUVRotation = LightmapUVRotation,
        EnvHasLighting = HasLightingEnvironment ? 1f : 0f,
        EnvDirection0 = EnvDirection0,
        EnvDirection1 = EnvDirection1,
        EnvAmbient = EnvAmbient,
        EnvLight0Colour = EnvLight0Colour,
        EnvLight1Colour = EnvLight1Colour,
    };

    public void DrawRenderable(Renderable renderable)
    {
        if (renderable.Material.RenderMode == RenderMode.Translucent)
            _translucentRenderables.Add(renderable);
        else
            _opaqueRenderables.Add(renderable);
    }

    public void Draw(CommandList commandList, OutputDescription output)
    {
        var cam3D = Cam3D.ActiveCamera;
        if (cam3D == null)
            return;

        // Draw-call / renderable counts: for a CPU-bound forward renderer the frame cost tracks the
        // draw count almost linearly, so this is the number to watch when optimising. Reported even
        // though most of Draw's cost is below, because it is what makes the timings interpretable.
        FrameProfiler.SetCounter("Pipeline binds", 0);
        FrameProfiler.SetCounter("Opaque renderables", _opaqueRenderables.Count);
        FrameProfiler.SetCounter("Translucent renderables", _translucentRenderables.Count);
        FrameProfiler.SetCounter("Draw calls", _opaqueRenderables.Count + _translucentRenderables.Count);

        using (FrameProfiler.Sample("Sort"))
        {
            // Opaque: sort by effect then material IDENTITY so the record loop below binds each
            // pipeline / material / texture set once per run instead of once per draw. Front-to-back
            // ordering (for early-Z) is deliberately dropped — the frame is CPU-submission bound with
            // the GPU sitting idle, so state coherence is worth far more than reduced overdraw. Opaque
            // draws are order-independent (depth test resolves them), so this is visually identical.
            // Precompute one primitive sort key per renderable (O(n)), then sort keys+items with the
            // primitive Array.Sort — a delegate comparator that dereferences Material.Effect/Mesh and
            // hashes them on every one of ~700k comparisons is what made this 70ms. The key packs the
            // effect identity hash in the high 32 bits and a combined (material, mesh) hash in the low
            // 32, so identical (effect, material, mesh) triples land adjacent (the batching the record
            // loop needs); a rare hash collision only splits a batch, never mis-renders.
            int n = _opaqueRenderables.Count;
            if (_sortKeys.Length < n)
            {
                _sortKeys = new long[n];
                _sortItems = new Renderable[n];
            }
            for (int i = 0; i < n; i++)
            {
                var r = _opaqueRenderables[i];
                _sortItems[i] = r;
                long eff = (uint)RuntimeHelpers.GetHashCode(r.Material.Effect);
                long matMesh = (uint)HashCode.Combine(RuntimeHelpers.GetHashCode(r.Material), RuntimeHelpers.GetHashCode(r.Mesh));
                _sortKeys[i] = (eff << 32) | matMesh;
            }
            Array.Sort(_sortKeys, _sortItems, 0, n);
            for (int i = 0; i < n; i++)
                _opaqueRenderables[i] = _sortItems[i];
            // Translucent MUST stay back-to-front for correct alpha blending — it can't be reordered
            // for coherence. It is normally a small fraction of the scene, so the per-comparison
            // distance recompute here is not the bottleneck.
            _translucentRenderables.Sort((a, b) => Vector3.DistanceSquared(b.GetTransforms()[0].Translation, cam3D.Position).CompareTo(Vector3.DistanceSquared(a.GetTransforms()[0].Translation, cam3D.Position)));
        }

        // Characterize the opaque set so the right draw-count fix is unambiguous (only walked when
        // profiling). The list is sorted by (effect, material, mesh):
        //  - "Opaque batches (mat+mesh)" = distinct consecutive (material, mesh) runs = the draw count
        //    GPU instancing could collapse the opaque pass into (its ceiling).
        //  - "Distinct opaque materials": if ~= renderable count, materials are per-instance unique
        //    (lit ties each with their own lightmap) and cross-instance instancing is blocked; if
        //    small, materials are shared and instancing is viable.
        //  - "Distinct opaque meshes": with ~6.2k instances making ~24k draws, ~4 submeshes/model —
        //    this is how many real mesh buffers exist, i.e. the ceiling for merging a model's
        //    same-material submeshes (a shader-free reduction that helps lit ties too).
        if (FrameProfiler.Enabled)
        {
            int batches = 0, distinctMaterials = 0;
            object? lastMaterial = null, lastMesh = null, lastMaterialOnly = null;
            var meshes = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var r in _opaqueRenderables)
            {
                if (!ReferenceEquals(r.Material, lastMaterial) || !ReferenceEquals(r.Mesh, lastMesh))
                {
                    batches++;
                    lastMaterial = r.Material;
                    lastMesh = r.Mesh;
                }
                if (!ReferenceEquals(r.Material, lastMaterialOnly)) { distinctMaterials++; lastMaterialOnly = r.Material; }
                meshes.Add(r.Mesh);
            }
            FrameProfiler.SetCounter("Opaque batches (mat+mesh)", batches);
            FrameProfiler.SetCounter("Distinct opaque materials", distinctMaterials);
            FrameProfiler.SetCounter("Distinct opaque meshes", meshes.Count);

            // Duplicate test: a renderable drawing the SAME mesh at the SAME world position as another
            // is genuine duplication (a bug); the same mesh at DIFFERENT positions is a legitimate
            // second instance. Position is quantised to 1mm and paired with the mesh identity. If this
            // is ~0, the 24k draws are real distinct geometry; if it's large, the draw list is doubled.
            var placed = new HashSet<(int mesh, int x, int y, int z)>();
            int duplicates = 0;
            foreach (var r in _opaqueRenderables)
            {
                var p = r.GetTransforms()[0].Translation;
                var key = (RuntimeHelpers.GetHashCode(r.Mesh),
                           (int)MathF.Round(p.X * 1000f),
                           (int)MathF.Round(p.Y * 1000f),
                           (int)MathF.Round(p.Z * 1000f));
                if (!placed.Add(key)) duplicates++;
            }
            FrameProfiler.SetCounter("Duplicate opaque draws (same mesh+pos)", duplicates);
        }

        _pipelineDescription.Outputs = output;
        // Pipelines are built against this OutputDescription; if it changed (window/viewport resize),
        // every cached pipeline is stale.
        if (!_pipelineCacheOutput.Equals(output))
        {
            _pipelineCache.Clear();
            _pipelineCacheOutput = output;
        }

        // Scene-wide, so this only needs updating once per frame rather than per-renderable like
        // UpdateRenderableBuffer below — direction is normalized here rather than trusting the
        // caller, since EditorSettings.LightDirection is a freely-edited ImGui field with no
        // guarantee of unit length. Not the light-direction convention: a since-reverted attempt
        // at negating it here didn't fix the "inverted everywhere" symptom, which pointed back at
        // normal-map reconstruction instead (see LitModelShaderSource).
        var lightData = BuildLightData(cam3D.Position);
        _lightBuffer.SetValueDeferred(commandList, 0, ref lightData);

        // Bliss's Material.IsDirty is cleared by the FIRST renderable that uploads it
        // (Renderable.UpdateMaterialBuffer sets Material.IsDirty = false), so with this engine's
        // shared cached materials (one Material instance across every mesh using that shader), a
        // live material edit - e.g. AssetManager.SetParallax - would only ever reach one
        // renderable per frame through the flag alone. Snapshot which materials are dirty BEFORE
        // any upload clears the flag, and force the update for every renderable sharing them.
        using (FrameProfiler.Sample("Buffer Update"))
        {
            _dirtyMaterials.Clear();
            foreach (var renderable in _opaqueRenderables)
                if (renderable.Material.IsDirty)
                    _dirtyMaterials.Add(renderable.Material);
            foreach (var renderable in _translucentRenderables)
                if (renderable.Material.IsDirty)
                    _dirtyMaterials.Add(renderable.Material);

            foreach (var renderable in _opaqueRenderables)
                UpdateRenderableBuffer(commandList, renderable, _dirtyMaterials);
            foreach (var renderable in _translucentRenderables)
                UpdateRenderableBuffer(commandList, renderable, _dirtyMaterials);
        }

        // Pack every instanced-effect renderable's transform into one storage buffer, uploaded once.
        // The walk order here (opaque then translucent) MUST match RenderPass's, because the vertex
        // shader indexes this buffer by gl_InstanceIndex and each batch draws with a firstInstance
        // offset taken from a cursor advanced in that same order (see _instanceCursor).
        BuildInstanceBuffer(commandList);
        _instanceCursor = 0;

        // "Draw Record" is the per-renderable state setup + draw call recording — the hot spot.
        // RenderPass tracks what's currently bound and skips every resource-set / pipeline bind that
        // would just re-set the same thing, so a run of same-material renderables (which the coherence
        // sort above groups together) costs one full bind then only a transform bind + draw each.
        _tsPipeline = _tsBind = _tsDraw = 0;
        using (FrameProfiler.Sample("Draw Record"))
        {
            _pipelineDescription.DepthStencilState = DepthStencilStateDescription.DEPTH_ONLY_LESS_EQUAL;
            RenderPass(commandList, cam3D, _opaqueRenderables, pass: 0);

            // No depth WRITE for translucent/decal geometry — depth TEST still applies (so decals
            // still occlude correctly behind opaque geometry in front of them), it just stops
            // polluting the depth buffer against the near-coplanar surface it's blending onto.
            //
            // The other half of matching the game here is POLYGON OFFSET, and it lives on the
            // MATERIAL rather than in this method - see AssetManager.RasterizerStateFor, which biases
            // every Translucent material with the values read off a capture of a real overlay draw.
            // It is per-material because Bliss carries rasterizer state on Material, not on the pass.
            _pipelineDescription.DepthStencilState = DepthStencilStateDescription.DEPTH_ONLY_LESS_EQUAL_READ;
            RenderPass(commandList, cam3D, _translucentRenderables, pass: 1);
        }

        // Sub-breakdown of Draw Record so we know which command-list op dominates: building/binding
        // the pipeline, binding resource sets (scene/material/texture), or the draw calls themselves.
        double toMs(long t) => t * 1000.0 / Stopwatch.Frequency;
        FrameProfiler.SetCounter("DR Pipeline us", (long)(toMs(_tsPipeline) * 1000));
        FrameProfiler.SetCounter("DR Binds us", (long)(toMs(_tsBind) * 1000));
        FrameProfiler.SetCounter("DR Draws us", (long)(toMs(_tsDraw) * 1000));

        _opaqueRenderables.Clear();
        _translucentRenderables.Clear();
    }

    // Records one depth-state pass, binding only what changed since the previous renderable. The
    // trackers are pass-local (not fields): each pass sets its own DepthStencilState, so its pipelines
    // differ from the other pass's, and starting a pass with everything "unbound" forces a clean first
    // bind. Correctness rests on three facts about this engine (all verified): Effect.Apply is a
    // no-op in Bliss 1.6.15 (so this method's SetGraphicsResourceSet calls ARE the whole binding);
    // materials are shared, cached instances (so same-material renderables have identical material +
    // texture sets and identical material-buffer CONTENT); and VertexFormat is a shared static per
    // vertex type (so ReferenceEquals keys the pipeline cache correctly). Bindings are ordered
    // pipeline-first because Veldrid can invalidate resource sets when the pipeline LAYOUT changes —
    // which only happens on an effect change, where every set is rebound anyway.
    private void RenderPass(CommandList commandList, Cam3D camera, List<Renderable> renderables, int pass)
    {
        Effect? boundEffect = null;
        EffectBindings? eb = null;
        Bliss.CSharp.Materials.Material? boundMaterial = null;
        Pipeline? boundPipeline = null;

        Bliss.CSharp.Materials.Material? pipeKeyMaterial = null;
        VertexFormat? pipeKeyFormat = null;
        SimplePipeline? pipeKeyResult = null;
        object? boundMesh = null;
        Array.Clear(_lastTexSet);

        for (int i = 0; i < renderables.Count; )
        {
            var renderable = renderables[i];
            var material = renderable.Material;
            var effect = material.Effect;

            if (!ReferenceEquals(effect, boundEffect))
            {
                eb = GetEffectBindings(effect);
                boundEffect = effect;
                boundMaterial = null;
                boundPipeline = null;
                pipeKeyMaterial = null;
            }

            long __t = Stopwatch.GetTimestamp();

            // Pipeline first (see note on Veldrid layout invalidation). Unchanged by instancing —
            // instanced transforms come from a storage buffer, not extra vertex attributes, so the
            // vertex layout (and therefore the pipeline) is identical to the non-instanced path.
            var format = renderable.Mesh.VertexFormat;
            if (!ReferenceEquals(material, pipeKeyMaterial) || !ReferenceEquals(format, pipeKeyFormat))
            {
                pipeKeyResult = GetOrCachePipeline(material, format, effect, pass);
                pipeKeyMaterial = material;
                pipeKeyFormat = format;
            }

            bool pipelineSet = false;
            if (!ReferenceEquals(pipeKeyResult!.Pipeline, boundPipeline))
            {
                commandList.SetPipeline(pipeKeyResult.Pipeline);
                boundPipeline = pipeKeyResult.Pipeline;
                pipelineSet = true;
                FrameProfiler.AddCounter("Pipeline binds", 1);
            }

            _tsPipeline += Stopwatch.GetTimestamp() - __t;
            __t = Stopwatch.GetTimestamp();

            // Scene-wide sets, bound once per effect (re-forced after any SetPipeline for backends that
            // drop bindings on a pipeline switch). For an instanced effect this includes the whole
            // transform storage buffer — bound ONCE here instead of a per-draw transform descriptor,
            // which is the real cost this whole change removes.
            if (pipelineSet)
            {
                commandList.SetGraphicsResourceSet(eb!.MatrixSlot, camera.GetMatrixBuffer().GetResourceSet(eb.MatrixLayout));
                if (eb.Instanced && _instanceSet != null)
                    commandList.SetGraphicsResourceSet(eb.InstanceTransformSlot, _instanceSet);
                if (eb.HasLight)
                    commandList.SetGraphicsResourceSet(eb.LightSlot, _lightBuffer.GetResourceSet(eb.LightLayout));
                if (eb.HasEnvCube && EnvironmentCubemap != null)
                    commandList.SetGraphicsResourceSet(eb.EnvCubeSlot, GetEnvCubeResourceSet(eb.EnvCubeLayout));
                boundMaterial = null;
                // A pipeline switch can drop resource-set bindings, so the per-slot cache is stale.
                Array.Clear(_lastTexSet);
            }

            // Material sets (MaterialBuffer + textures): bind once per material run. The MaterialBuffer
            // differs per material so is always bound; each texture is bound only if it actually
            // changed from what is already in that slot — most of the lit effect's texture slots are
            // the same shared default across materials, so this skips the bulk of the descriptor binds.
            if (!ReferenceEquals(material, boundMaterial))
            {
                commandList.SetGraphicsResourceSet(eb!.MaterialSlot, renderable.GetMaterialBuffer().GetResourceSet(eb.MaterialLayout));

                var mapKeys = material.GetMaterialMapKeys();
                foreach (var tex in eb.Textures)
                {
                    foreach (var materialMapKey in mapKeys)
                    {
                        if (tex.Name != materialMapKey.Name)
                            continue;
                        var materialMap = material.GetMaterialMap(materialMapKey);
                        var textureResourceSet = materialMap!.GetTextureResourceSet(materialMap.Sampler ?? GraphicsHelper.GetSampler(GraphicsDevice, SamplerType.PointWrap), tex.Layout);
                        if (textureResourceSet != null && !ReferenceEquals(textureResourceSet, _lastTexSet[tex.Slot]))
                        {
                            commandList.SetGraphicsResourceSet(tex.Slot, textureResourceSet);
                            _lastTexSet[tex.Slot] = textureResourceSet;
                        }
                        break; // names are unique, so stop scanning once matched
                    }
                }
                boundMaterial = material;
            }

            var mesh = renderable.Mesh;
            bool meshChanged = !ReferenceEquals(mesh, boundMesh);
            if (meshChanged)
            {
                commandList.SetVertexBuffer(0, mesh.VertexBuffer);
                if (mesh.IndexCount != 0)
                    commandList.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
                boundMesh = mesh;
            }

            _tsBind += Stopwatch.GetTimestamp() - __t;
            __t = Stopwatch.GetTimestamp();

            if (eb!.Instanced)
            {
                // Batch: consecutive renderables sharing this material AND mesh are the same geometry
                // at different transforms — exactly one instanced draw, reading transforms
                // [_instanceCursor .. +count) from the storage buffer via gl_InstanceIndex. UseInstancing
                // off degrades this to one draw per renderable (still storage-buffer transforms), to
                // isolate the batching win from the per-draw-bind win.
                int runEnd = i + 1;
                if (UseInstancing)
                    while (runEnd < renderables.Count
                           && ReferenceEquals(renderables[runEnd].Material, material)
                           && ReferenceEquals(renderables[runEnd].Mesh, mesh))
                        runEnd++;
                uint count = (uint)(runEnd - i);

                if (mesh.IndexCount != 0)
                    commandList.DrawIndexed(mesh.IndexCount, count, 0, 0, (uint)_instanceCursor);
                else
                    commandList.Draw(mesh.VertexCount, count, 0, (uint)_instanceCursor);

                _instanceCursor += (int)count;
                i = runEnd;
            }
            else
            {
                // Non-instanced effect (unlit paths): the per-object transform is still a uniform.
                commandList.SetGraphicsResourceSet(eb.TransformSlot, renderable.GetTransformBuffer().GetResourceSet(eb.TransformLayout));
                if (renderable.HasBones && eb.HasBone)
                {
                    var boneBuffer = renderable.GetBoneBuffer();
                    if (boneBuffer != null)
                        commandList.SetGraphicsResourceSet(eb.BoneSlot, boneBuffer.GetResourceSet(eb.BoneLayout));
                }

                if (mesh.IndexCount != 0)
                    commandList.DrawIndexed(mesh.IndexCount);
                else
                    commandList.Draw(mesh.VertexCount);
                i++;
            }

            _tsDraw += Stopwatch.GetTimestamp() - __t;
        }
    }

    private SimplePipeline GetOrCachePipeline(Bliss.CSharp.Materials.Material material, VertexFormat format, Effect effect, int pass)
    {
        var key = (material, format, pass);
        if (_pipelineCache.TryGetValue(key, out var cached))
            return cached;

        // Miss (first time this combo is seen): build the full description and let Bliss create/cache
        // the Pipeline. DepthStencilState and Outputs are already set on _pipelineDescription by Draw
        // for this pass; BlendState/RasterizerState come from the material, layouts/shaders from the
        // effect. The (material, format, pass) key captures every input that varies, so it is safe to
        // reuse the result on every later frame without re-hashing Bliss's whole description.
        _pipelineDescription.BlendState = material.BlendState;
        _pipelineDescription.RasterizerState = material.RasterizerState;
        _pipelineDescription.BufferLayouts = effect.GetBufferLayouts();
        _pipelineDescription.TextureLayouts = effect.GetTextureLayouts();
        _pipelineDescription.ShaderSet = new ShaderSetDescription(format.Layouts, effect.Shaders);
        var pipeline = effect.GetPipeline(_pipelineDescription);
        _pipelineCache[key] = pipeline;
        return pipeline;
    }

    private int _instanceCursor;

    // Packs every instanced-effect renderable's world matrix into _instanceScratch (opaque then
    // translucent order) and uploads it to the storage buffer the lit vertex shader reads. Matrices
    // are Transform.GetMatrix() uploaded as-is — the same value and convention the old per-object
    // TransformBuffer uniform used, so the shading maths is unchanged.
    private void BuildInstanceBuffer(CommandList commandList)
    {
        _instanceCount = 0;
        SimpleBufferLayout? layout = null;
        using (FrameProfiler.Sample("Instance Gather"))
        {
            AppendInstanced(_opaqueRenderables, ref layout);
            AppendInstanced(_translucentRenderables, ref layout);
        }
        if (_instanceCount == 0 || layout == null)
            return;

        EnsureInstanceCapacity(_instanceCount, layout);
        using (FrameProfiler.Sample("Instance Upload"))
            commandList.UpdateBuffer(_instanceTransformBuffer!, 0, _instanceScratch.AsSpan(0, _instanceCount));
    }

    private void AppendInstanced(List<Renderable> renderables, ref SimpleBufferLayout? layout)
    {
        foreach (var renderable in renderables)
        {
            var eb = GetEffectBindings(renderable.Material.Effect);
            if (!eb.Instanced)
                continue;
            if (_instanceCount >= _instanceScratch.Length)
                Array.Resize(ref _instanceScratch, _instanceScratch.Length * 2);
            _instanceScratch[_instanceCount++] = renderable.GetTransforms()[0].GetMatrix();
            layout ??= eb.InstanceTransformLayout;
        }
    }

    private void EnsureInstanceCapacity(int count, SimpleBufferLayout layout)
    {
        if (_instanceTransformBuffer != null && _instanceCapacity >= count && ReferenceEquals(_instanceSetLayout, layout))
            return;

        uint newCapacity = _instanceCapacity == 0 ? 4096u : _instanceCapacity;
        while (newCapacity < count) newCapacity *= 2;

        _instanceSet?.Dispose();
        _instanceTransformBuffer?.Dispose();
        // 64 = sizeof(Matrix4x4); StructureByteStride matches so the std430 mat4[] indexes correctly.
        _instanceTransformBuffer = GraphicsDevice.ResourceFactory.CreateBuffer(
            new BufferDescription(newCapacity * 64u, BufferUsage.StructuredBufferReadOnly, 64u));
        _instanceSet = GraphicsDevice.ResourceFactory.CreateResourceSet(
            new ResourceSetDescription(layout.Layout, _instanceTransformBuffer));
        _instanceCapacity = newCapacity;
        _instanceSetLayout = layout;
    }

    // Per-effect resolved binding slots/layouts, so the hot loop never does a string-keyed layout
    // lookup. There are only a handful of effects (default / lit / vertex-alpha / billboard), each
    // built once, so this dictionary stays tiny and is populated lazily on first sight.
    private sealed class EffectBindings
    {
        public uint MatrixSlot;
        public SimpleBufferLayout MatrixLayout = null!;
        // Non-instanced effects: per-object transform is a uniform. Instanced effects instead declare
        // InstanceTransforms (a storage buffer indexed by gl_InstanceIndex) and set Instanced=true.
        public bool HasTransform;
        public uint TransformSlot;
        public SimpleBufferLayout TransformLayout = null!;
        public bool Instanced;
        public uint InstanceTransformSlot;
        public SimpleBufferLayout InstanceTransformLayout = null!;
        public uint MaterialSlot;
        public SimpleBufferLayout MaterialLayout = null!;
        public bool HasLight;
        public uint LightSlot;
        public SimpleBufferLayout LightLayout = null!;
        public bool HasBone;
        public uint BoneSlot;
        public SimpleBufferLayout BoneLayout = null!;
        public (uint Slot, string Name, SimpleTextureLayout Layout)[] Textures = [];
        public bool HasEnvCube;
        public uint EnvCubeSlot;
        public SimpleTextureLayout EnvCubeLayout = null!;
    }

    private readonly Dictionary<Effect, EffectBindings> _effectBindings = new(4);

    private EffectBindings GetEffectBindings(Effect effect)
    {
        if (_effectBindings.TryGetValue(effect, out var cached))
            return cached;

        var b = new EffectBindings
        {
            // Always present — the renderer binds these for every effect.
            MatrixSlot = effect.GetBufferLayoutSlot("MatrixBuffer"),
            MatrixLayout = effect.GetBufferLayout("MatrixBuffer"),
            MaterialSlot = effect.GetBufferLayoutSlot("MaterialBuffer"),
            MaterialLayout = effect.GetBufferLayout("MaterialBuffer"),
        };

        // TEMP diagnostic: log what Bliss actually reflected for this effect, so we can confirm the
        // instanced storage buffer is named "InstanceTransforms" as the detection below expects.
        Console.WriteLine($"[Renderer] effect buffers: {string.Join(", ", effect.GetBufferLayouts().Select(l => l.Name))}");

        // Optional buffers — detected by scanning declared layouts, since GetBufferLayoutSlot throws
        // for a name an effect never registered. An effect has EITHER a TransformBuffer uniform
        // (per-draw, unlit paths) OR an InstanceTransforms storage buffer (instanced, lit path).
        foreach (var bl in effect.GetBufferLayouts())
        {
            switch (bl.Name)
            {
                case "TransformBuffer": b.HasTransform = true; b.TransformSlot = effect.GetBufferLayoutSlot("TransformBuffer"); b.TransformLayout = bl; break;
                case "InstanceTransforms": b.Instanced = true; b.InstanceTransformSlot = effect.GetBufferLayoutSlot("InstanceTransforms"); b.InstanceTransformLayout = bl; break;
                case "LightBuffer": b.HasLight = true; b.LightSlot = effect.GetBufferLayoutSlot("LightBuffer"); b.LightLayout = bl; break;
                case "BoneBuffer": b.HasBone = true; b.BoneSlot = effect.GetBufferLayoutSlot("BoneBuffer"); b.BoneLayout = bl; break;
            }
        }

        // Textures: fEnvCube is a scene-wide cubemap bound separately; everything else is a
        // per-material map matched by name in the hot loop.
        var textures = new List<(uint, string, SimpleTextureLayout)>();
        foreach (var tl in effect.GetTextureLayouts())
        {
            if (tl.Name == "fEnvCube") { b.HasEnvCube = true; b.EnvCubeSlot = effect.GetTextureLayoutSlot("fEnvCube"); b.EnvCubeLayout = tl; }
            else textures.Add((effect.GetTextureLayoutSlot(tl.Name), tl.Name, tl));
        }
        b.Textures = [.. textures];

        _effectBindings[effect] = b;
        return b;
    }

    // Builds (and caches) the resource set binding the current EnvironmentCubemap into the lit
    // effect's set 10 (textureCube + sampler). Rebuilt only when the view itself changes — i.e. once
    // per level load, not per frame. LinearSampler gives smooth reflections; a cube view clamps at
    // face edges by construction, so the sampler's address mode is irrelevant.
    private ResourceSet GetEnvCubeResourceSet(SimpleTextureLayout layout)
    {
        if (EnvironmentCubemap == null)
            return null;
        
        if (_envCubeSet != null && ReferenceEquals(_envCubeSetView, EnvironmentCubemap))
            return _envCubeSet;

        _envCubeSet?.Dispose();
        _envCubeSet = GraphicsDevice.ResourceFactory.CreateResourceSet(
            new ResourceSetDescription(layout.Layout, EnvironmentCubemap, GraphicsDevice.LinearSampler));
        _envCubeSetView = EnvironmentCubemap;
        return _envCubeSet;
    }

    // Reused across frames to avoid a per-frame allocation; only ever touched inside Draw.
    private readonly HashSet<Bliss.CSharp.Materials.Material> _dirtyMaterials = [];

    private static void UpdateRenderableBuffer(CommandList commandList, Renderable renderable, HashSet<Bliss.CSharp.Materials.Material> dirtyMaterials)
    {
        if (renderable.IsTransformBufferDirty)
            renderable.UpdateTransformBuffer(commandList);
        if (renderable.IsInstanceVertexBufferDirty)
            renderable.UpdateInstanceVertexBuffer(commandList);
        if (renderable.IsBoneBufferDirty)
            renderable.UpdateBoneBuffer(commandList);
        // dirtyMaterials: see Draw - IsMaterialBufferDirty alone misses shared-material
        // renderables once the first upload clears Material.IsDirty.
        if (renderable.IsMaterialBufferDirty || dirtyMaterials.Contains(renderable.Material))
            renderable.UpdateMaterialBuffer(commandList);
    }

    public void Dispose()
    {
        _lightBuffer.Dispose();
        _envCubeSet?.Dispose();
        _instanceSet?.Dispose();
        _instanceTransformBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
