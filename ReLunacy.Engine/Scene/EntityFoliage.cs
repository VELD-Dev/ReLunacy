using System.Numerics;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry.Meshes;
using Bliss.CSharp.Geometry.Meshes.Data;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Graphics.VertexTypes;
using Bliss.CSharp.Transformations;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using Veldrith;

namespace ReLunacy.Engine.Scene;

/// <summary>One placement of a foliage asset: a batch of camera-facing sprite cards.
///
/// The geometry is built ONCE, not per frame. Every vertex stores the card's ANCHOR as its
/// position and its own 2D corner offset in TexCoords2; BillboardModelShaderSource does the
/// facing by adding that offset after the view transform. So the vertex buffer is static and the
/// cards still turn with the camera - no per-frame rebuild, no CPU billboarding.
///
/// Only ONE sprite LOD is built (the highest-detail one). The LOD chain is a distance-switching
/// mechanism and drawing every level at once stacks 117 cards where the game draws 58; wiring the
/// switch needs the LOD distances in Loading.Objects.FoliageSpriteLodRange, which are read but not
/// yet acted on.</summary>
public class EntityFoliage : Entity
{
    public readonly Assets.Foliage.Foliage BaseFoliage;

    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    private readonly Mesh<Vertex3D>? _mesh;
    private readonly Bliss.CSharp.Materials.Material? _material;

    /// <summary>Which sprite LOD this entity draws. 0 is the densest set.</summary>
    public const int BuiltLod = 0;

    public EntityFoliage(Assets.Foliage.Foliage foliage, in Assets.Foliage.FoliagePlacement placement,
        IMaterial? material, AssetManager assetManager, GraphicsDevice gd)
    {
        BaseFoliage = foliage;

        Matrix4x4.Decompose(placement.Transform, out var scale, out var rotation, out var translation);
        Transform = new Transform { Translation = translation, Rotation = rotation, Scale = scale };

        // BoundingSphere is LOCAL per Entity's convention (an offset from Transform.Translation).
        var centre = new Vector3(placement.BoundingSphere.X, placement.BoundingSphere.Y, placement.BoundingSphere.Z);
        float radius = placement.BoundingSphere.W;
        BoundingSphere = radius > 0f
            ? new Vector4(centre - translation, radius)
            : ComputeLocalBounds(foliage);

        Name = $"{foliage.Name}_{ID}";

        var cards = foliage.SpritesForLod(BuiltLod).ToList();
        if (cards.Count == 0) return;

        // material carries the atlas resolved from FoliageMetadata.TextureIndex — a DIRECT index
        // into the 0x5200 texture table, proven by the game's own A200 loader (see that field). It
        // is null only when the asset's index is the 0xFFFFFFFF sentinel or the level is new-engine;
        // GetOrBuildBillboardMaterial then falls back to the default white texture, which still
        // shows the billboarding and card geometry while making an unresolved case obvious on screen.
        _material = assetManager.GetOrBuildBillboardMaterial(material);
        _mesh = BuildMesh(gd, cards, _material);
    }

    /// <summary>Two triangles per card, sharing the anchor as every corner's position. The corner
    /// order in the file is already a consistent winding around the quad (0,1,2,3), so the two
    /// triangles are 0-1-2 and 0-2-3.</summary>
    private static Mesh<Vertex3D> BuildMesh(GraphicsDevice gd, List<Assets.Foliage.FoliageSpriteCard> cards, Bliss.CSharp.Materials.Material material)
    {
        var vertices = new Vertex3D[cards.Count * 4];
        var indices = new uint[cards.Count * 6];

        for (int c = 0; c < cards.Count; c++)
        {
            var card = cards[c];
            for (int k = 0; k < 4; k++)
            {
                vertices[c * 4 + k] = new Vertex3D(
                    card.Anchor,
                    card.Uvs[k],
                    card.CornerOffsets[k],
                    Vector3.UnitY,
                    new Vector4(1f, 0f, 0f, 1f),
                    Vector4.One);
            }

            int v = c * 4;
            int i = c * 6;
            indices[i + 0] = (uint)(v + 0);
            indices[i + 1] = (uint)(v + 1);
            indices[i + 2] = (uint)(v + 2);
            indices[i + 3] = (uint)(v + 0);
            indices[i + 4] = (uint)(v + 2);
            indices[i + 5] = (uint)(v + 3);
        }

        return new Mesh<Vertex3D>(gd, material, new BasicMeshData(vertices, indices));
    }

    /// <summary>Fallback bounds from the cards themselves, used when the instance record's radius
    /// is zero. Card offsets are added in view space so they can point any direction in world
    /// space - the anchor spread is padded by the largest corner offset rather than assuming the
    /// cards lie in some plane.</summary>
    private static Vector4 ComputeLocalBounds(Assets.Foliage.Foliage foliage)
    {
        if (foliage.Sprites.Count == 0) return Vector4.Zero;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        float pad = 0f;
        foreach (var card in foliage.Sprites)
        {
            min = Vector3.Min(min, card.Anchor);
            max = Vector3.Max(max, card.Anchor);
            foreach (var o in card.CornerOffsets) pad = MathF.Max(pad, o.Length());
        }

        var centre = (min + max) * 0.5f;
        return new Vector4(centre, (max - centre).Length() + pad);
    }

    public override void Draw(IRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender || !EntityManager.Singleton.renderFoliage) return;

        var sphere = WorldBoundingSphere;
        var sphereCenter = new Vector3(sphere.X, sphere.Y, sphere.Z);
        if (EntityManager.Singleton.FrustumCullingEnabled && !camera.GetFrustum().ContainsSphere(sphereCenter, sphere.W)) return;

        if (EntityManager.Singleton.renderBoundingSpheres)
            DrawBoundingSphere(outputDescription, commandList, immediateRenderer);

        if (_mesh == null) return;

        if (IsDirty)
        {
            cachedRenderables.Clear();
            cachedRenderables.Add(new Renderable(_mesh, Transform));
            IsDirty = false;
        }

        foreach (var renderable in cachedRenderables)
            renderer.DrawRenderable(renderable);

        Diagnostics.FrameProfiler.AddCounter("Foliage draws", cachedRenderables.Count);
        EntitiesRenderedThisFrame++;
    }
}
