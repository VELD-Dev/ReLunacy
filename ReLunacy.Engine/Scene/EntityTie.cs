using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public class EntityTie : Entity
{
    public readonly ITie BaseTie;

    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    public RenderModel? Model { get; private set; }

    public EntityTie(IPlacedInstance<ITie> tieInstance, AssetManager assetManager)
    {
        BaseTie = tieInstance.Asset;

        // Ties are placed via a raw affine matrix; decomposed directly rather than through
        // IPlacedInstance's Euler-angle properties, which are lossy and collapse anisotropic scale
        // into a single averaged float.
        Matrix4x4.Decompose(tieInstance.GetTransformMatrix(), out var scale, out var rotation, out var translation);

        Transform = new Transform
        {
            Translation = translation,
            Rotation = rotation,
            Scale = scale
        };

        var (center, radius) = BaseTie.GetBoundingSphere();
        BoundingSphere = new Vector4(center, radius);

        Name = !string.IsNullOrEmpty(tieInstance.Name) ? tieInstance.Name.Split('/')[^1] : $"Tie_{BaseTie.Id:X}_{ID}";

        assetManager.Ties.TryGetValue(BaseTie.Id, out var model);
        Model = model;

        _assetManager = assetManager;

        OldBakedLightScale = tieInstance.OldBakedLightScale;
        NewEngineBakedLight = tieInstance.NewEngineBakedLight;
        NewEngineCubeTuid = tieInstance.NewEngineCubeTuid;
        NewEnginePrelight = tieInstance.NewEnginePrelight;
        OldEnginePrelight = tieInstance.OldEnginePrelight;
        NewEnginePrelightBaked = tieInstance.NewEnginePrelightBaked;

        // The game reads this as a BE ushort at instance+0x5A, then sanitizes values outside the
        // available 0x5400/0x5410 lists to 0xFFFF. Mirror that where both decoded lists are known.
        ushort candidateLightmap = tieInstance.LightmapIndex;
        LightmapIndex = candidateLightmap != Loading.Objects.Instances.TieInstance.NoLightmap
            && candidateLightmap < assetManager.ZoneLightmaps.Count
            && candidateLightmap < assetManager.ZoneDirectionals.Count
            && assetManager.ZoneLightmaps[candidateLightmap] != null
            && assetManager.ZoneDirectionals[candidateLightmap] != null
                ? candidateLightmap
                : Loading.Objects.Instances.TieInstance.NoLightmap;
    }

    /// <summary>
    /// Whether a mesh carries a lightmap UV for every one of its vertices.
    ///
    /// The same test <see cref="EntityUFrag"/> applies to UFrags, for the same reason: a short or
    /// absent array is not a bake this mesh can sample, and binding the atlas anyway reads a texel
    /// nothing authored.
    /// </summary>
    private static bool HasLightmapUVs(Assets.Interfaces.IMesh mesh)
    {
        var uvs = mesh.Geometry.GetLightmapUVs();
        if (uvs is null) return false;
        int vertexCount = mesh.Geometry.GetVertexPositions().Length / 3;
        return vertexCount > 0 && uvs.Length == vertexCount * 2;
    }

    private readonly AssetManager _assetManager;

    /// <summary>This placement's sanitized baked lighting entry, or 0xFFFF for none - see
    /// IPlacedInstance.LightmapIndex.</summary>
    public ushort LightmapIndex { get; }

    /// <summary>Old engine: per-instance baked light scale (TieInstance +0x60), or null.</summary>
    public float? OldBakedLightScale { get; }

    /// <summary>New engine: this placement's baked-light vertex constant c[4], or null.</summary>
    public Vector4? NewEngineBakedLight { get; }

    /// <summary>New engine: the cubemaps.dat resource this placement reflects, 0 for the region's default.</summary>
    public ulong NewEngineCubeTuid { get; }

    /// <summary>New engine: this placement's prelight words, or null (see IPlacedInstance).</summary>
    public byte[]? NewEnginePrelight { get; }

    /// <summary>Old engine: this placement's prelight words, or null (see IPlacedInstance).</summary>
    public byte[]? OldEnginePrelight { get; }

    /// <summary>New engine: whether the placement record carries a bake (+0x5A != 0xFFFF).</summary>
    public bool NewEnginePrelightBaked { get; }

    protected override void EnsureRenderables()
    {
        if (!IsDirty || Model is null) return;
        cachedRenderables.Clear();
        // Baked lighting is per-placement while the model is shared across instances, so lit
        // instances get their own material via Renderable's override constructor; unlit instances
        // share the mesh's own material.
        bool lit = LightmapIndex != Loading.Objects.Instances.TieInstance.NoLightmap;
        for (int i = 0; i < Model.Meshes.Length; i++)
        {
            var mesh = Model.Meshes[i];
            // The index says this PLACEMENT has a bake; the UVs say this MESH can sample it. Both
            // are needed. A mesh whose lightmap UV slice is missing, which TieReader leaves null
            // per mesh, otherwise binds the real atlas and samples it at (0,0) - a black texel,
            // drawn as though it were the light reaching that surface.
            if (lit && i < BaseTie.Meshes.Count && HasLightmapUVs(BaseTie.Meshes[i]))
            {
                var perInstance = _assetManager.GetOrBuildMaterial(BaseTie.Meshes[i].Material, LightmapIndex);
                cachedRenderables.Add(new Renderable(mesh, Transform, perInstance));
            }
            else
            {
                cachedRenderables.Add(new Renderable(mesh, Transform));
            }
        }
        IsDirty = false;
    }

}
