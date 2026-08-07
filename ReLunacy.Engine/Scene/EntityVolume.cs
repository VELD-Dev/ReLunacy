using System.Numerics;
using Bliss.CSharp;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Geometry.Meshes;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Graphics.VertexTypes;
using Bliss.CSharp.Images;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Textures;
using Bliss.CSharp.Transformations;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Rendering;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public class EntityVolume : Entity
{
    public readonly Volume BaseVolume;

    public override Vector4 BoundingSphere { get; set; } = Vector4.Zero;
    /// <summary>The box's real (possibly non-uniform) half-extents source — kept separate from
    /// Transform.Scale (always 1,1,1 for a Volume) because each edge instance takes this as an
    /// explicit length rather than folding it into the placement transform. Only settable via
    /// <see cref="SetScale"/>, which keeps BoundingSphere and the edge instances in sync with it —
    /// never assign this field directly.</summary>
    public Vector3 scale { get; private set; }

    public override string Name { get; protected set; }

    // Both entirely flat-color materials, tinted by the map's own Color — swapped on
    // Renderable.Material directly when selection changes, no mesh rebuild needed. Static/shared
    // since neither carries any per-instance state; built lazily since GlobalResource isn't
    // guaranteed initialized before the first Volume loads otherwise. Colors are live-configurable
    // (Editor Settings), so EnsureMaterialsCurrent() rebuilds these in place — and bumps
    // _materialsVersion so every EntityVolume's Draw() knows to re-fetch its Renderable's Material
    // reference, even one that isn't changing selection state this frame — whenever
    // EntityManager's color fields drift from what's currently baked in.
    private static Material? _unselectedMaterial;
    private static Material? _selectedMaterial;
    private static Vector4 _appliedVolumeColor = new(float.NaN);
    private static Vector4 _appliedVolumeSelectedColor = new(float.NaN);
    private static int _materialsVersion;

    private static Material GetUnselectedMaterial(GraphicsDevice gd) { EnsureMaterialsCurrent(gd); return _unselectedMaterial!; }
    private static Material GetSelectedMaterial(GraphicsDevice gd) { EnsureMaterialsCurrent(gd); return _selectedMaterial!; }

    private static void EnsureMaterialsCurrent(GraphicsDevice gd)
    {
        var color = EntityManager.Singleton.VolumeColor;
        var selectedColor = EntityManager.Singleton.VolumeSelectedColor;
        if (_unselectedMaterial != null && color == _appliedVolumeColor && selectedColor == _appliedVolumeSelectedColor)
            return;

        _unselectedMaterial = BuildTintMaterial(gd, ToColor(color));
        _selectedMaterial = BuildTintMaterial(gd, ToColor(selectedColor));
        _appliedVolumeColor = color;
        _appliedVolumeSelectedColor = selectedColor;
        _materialsVersion++;
    }

    private static Color ToColor(Vector4 v) => new(
        (byte)(Math.Clamp(v.X, 0f, 1f) * 255f),
        (byte)(Math.Clamp(v.Y, 0f, 1f) * 255f),
        (byte)(Math.Clamp(v.Z, 0f, 1f) * 255f),
        (byte)(Math.Clamp(v.W, 0f, 1f) * 255f));

    // GlobalResource.DefaultModelTexture — despite its name/every other comment in this file
    // previously assuming it was white — is actually Bliss's own hardcoded 1x1 50% GRAY
    // placeholder (confirmed via decompile: GlobalResource's static constructor builds it as
    // `new Image(1, 1, Color.Gray)`). The fragment shader does texelColor * maps[0].color, so
    // every volume tint was silently getting halved (pure Yellow (1,1,0) * Gray (0.5,0.5,0.5) =
    // a dark olive yellow) — nothing to do with lighting or gamma, just multiplying against the
    // wrong placeholder texture. Own dedicated solid-WHITE 1x1 texture instead, so the tint color
    // is the only thing that reaches the framebuffer.
    private static Texture2D? _whiteTexture;

    private static Texture2D GetWhiteTexture(GraphicsDevice gd) => _whiteTexture ??= new Texture2D(gd, new Image(1, 1, Color.White));

    private static Material BuildTintMaterial(GraphicsDevice gd, Color tint)
    {
        // Material's own default (RasterizerStateDescription.DEFAULT) back-face culls, and is
        // never touched by AssetManager.SetBackfaceCulling (that only tracks materials it built
        // itself) — these are edges, not opaque faces, so they should always draw from both sides
        // regardless of the app-wide backface-culling setting.
        var material = new Material(GlobalResource.DefaultModelEffect, RasterizerStateDescription.CULL_NONE);
        material.AddMaterialMap(new MaterialMapKey(MaterialMapType.Albedo), 0, new MaterialMap(GetWhiteTexture(gd), color: tint));
        return material;
    }

    // One shared unit-length edge mesh (see Primitives.CreateWireEdge), reused across 12 separate
    // Renderables per volume — replaces the old approach of building a whole unique box mesh per
    // volume. NOT GPU-instanced: GlobalResource.DefaultModelEffect (used by BuildTintMaterial
    // below) is compiled by Bliss itself with no macros at all, so its shader's "#if
    // USE_INSTANCING" branch never compiles in and always falls back to a single uTransformation
    // uniform — which Renderable sets to Matrix4x4.Identity whenever UseInstancing is true,
    // trusting the shader to use per-instance attributes instead. Through this effect, an
    // instanced Renderable silently renders every vertex at local-space identity instead of world
    // position (confirmed empirically: volumes stopped rendering entirely the one time this was
    // tried). 12 non-instanced Renderables sharing one mesh is more draw calls but actually works.
    // Only the edge's own thickness lives in this mesh's geometry; each edge's real
    // length/position/orientation comes from its own per-instance Transform (see
    // RecomputeEdgeTransforms), so rebuilding this mesh (thickness changed) never requires
    // recomputing those.
    private static Mesh<Vertex3D>? _sharedEdgeMesh;
    private static float _appliedEdgeThickness = float.NaN;
    private static int _edgeMeshVersion;

    private static Mesh<Vertex3D> SharedEdgeMesh => _sharedEdgeMesh!;

    private static void EnsureEdgeMeshCurrent(GraphicsDevice gd)
    {
        float thickness = EntityManager.Singleton.VolumeWireThickness;
        if (_sharedEdgeMesh != null && thickness == _appliedEdgeThickness)
            return;

        _sharedEdgeMesh?.Dispose();
        // Material passed here is never actually used for drawing — every Renderable built from
        // this mesh uses the explicit-material constructor, which overrides it. Only matters for
        // the vertex layout compatibility check Mesh<T> does at construction.
        _sharedEdgeMesh = Primitives.CreateWireEdge(gd, GetUnselectedMaterial(gd), thickness);
        _appliedEdgeThickness = thickness;
        _edgeMeshVersion++;
    }

    private readonly GraphicsDevice _gd;
    private Transform[] _edgeTransforms = [];
    private bool _drawnSelected;
    private int _appliedMaterialsVersion = -1;
    private int _appliedEdgeMeshVersion = -1;

    public EntityVolume(Volume volume, GraphicsDevice gd)
    {
        BaseVolume = volume;
        _gd = gd;
        Name = !string.IsNullOrEmpty(volume.Name) ? volume.Name : $"Volume_{ID}";

        Matrix4x4.Decompose(volume.transform, out var initialScale, out var rotation, out var position);

        Transform = new Transform { Translation = position, Rotation = rotation, Scale = Vector3.One };

        SetScale(initialScale);
    }

    /// <summary>Sets <see cref="scale"/> and, in the same step, recomputes the local-space bounding
    /// sphere and the 12 edge instances — the three always have to change together, so this is the
    /// only way to change the volume's size (from the Property Inspector or otherwise). Rebuilds
    /// synchronously rather than waiting for Draw()'s IsDirty check: View3D's picking reads
    /// cachedRenderables directly, independent of Draw() (which may not even run this frame if the
    /// volume is culled or Render &gt; Volumes is off), so a stale entry could otherwise report the
    /// pre-resize size for up to a frame.</summary>
    public void SetScale(Vector3 newScale)
    {
        scale = newScale;

        // BoundingSphere is LOCAL space per Entity's convention (offset from Transform.Translation)
        // — center coincides with the volume's own position (zero local offset), and the radius is
        // the distance from that center to the cube's furthest corner: the half-extents vector's
        // length, since one corner sits at exactly (sx/2, sy/2, sz/2) from center.
        BoundingSphere = new Vector4(Vector3.Zero, (scale / 2f).Length());

        RebuildEdgeRenderable();
    }

    /// <summary>The 12 world matrices of this volume's wireframe edges — each places/stretches the
    /// shared unit-length edge mesh (see RecomputeEdgeTransforms / ComposeEdgeTransform). The raw-Vulkan
    /// renderer draws the same thin-box edge geometry at these, so its wireframe matches the pick target
    /// exactly (and honours VolumeWireThickness). Same composition as the Renderables: local * Transform.</summary>
    public IEnumerable<Matrix4x4> GetWorldEdgeTransforms()
    {
        var vol = Transform.GetMatrix();
        foreach (var e in _edgeTransforms)
            yield return e.GetMatrix() * vol;
    }

    /// <summary>Current wireframe colour (RGBA, 0..1): the selected or unselected volume tint.</summary>
    public Vector4 VolumeColour => selected ? EntityManager.Singleton.VolumeSelectedColor : EntityManager.Singleton.VolumeColor;

    /// <summary>Recomputes the 12 per-edge local Transforms (position/orientation/length) from the
    /// current <see cref="scale"/> — each edge is a unit-length instance of
    /// <see cref="SharedEdgeMesh"/> stretched along its own local X (see
    /// <see cref="ComposeEdgeTransform"/>) and placed at one of the box's 4 corners parallel to
    /// that axis, same layout the old single-mesh CreateWireBox used.</summary>
    private void RecomputeEdgeTransforms()
    {
        var edges = new Transform[12];
        int i = 0;
        AddAxisEdges(Vector3.UnitX, scale.X, Vector3.UnitY, scale.Y, Vector3.UnitZ, scale.Z, edges, ref i);
        AddAxisEdges(Vector3.UnitY, scale.Y, Vector3.UnitX, scale.X, Vector3.UnitZ, scale.Z, edges, ref i);
        AddAxisEdges(Vector3.UnitZ, scale.Z, Vector3.UnitX, scale.X, Vector3.UnitY, scale.Y, edges, ref i);
        _edgeTransforms = edges;
    }

    private static readonly float[] Signs = [-1f, 1f];

    private void AddAxisEdges(Vector3 axisLength, float lengthExtent, Vector3 axisB, float extentB, Vector3 axisC, float extentC, Transform[] edges, ref int i)
    {
        foreach (float sb in Signs)
        {
            foreach (float sc in Signs)
            {
                Vector3 localCenter = axisB * (sb * extentB * 0.5f) + axisC * (sc * extentC * 0.5f);
                edges[i++] = ComposeEdgeTransform(axisLength, MathF.Max(lengthExtent, 0.0001f), localCenter);
            }
        }
    }

    /// <summary>Builds one edge's full WORLD Transform by composing its volume-local placement
    /// (length/orientation/offset) with this volume's own Transform, matching the same
    /// Matrix4x4.Decompose-based composition already used to derive the volume's own Transform
    /// from its source data in the constructor. Matrix4x4.Decompose can theoretically fail on a
    /// degenerate input (never expected here — this volume's own Transform.Scale is always
    /// Vector3.One, so there's no shear/reflection to trip it up), in which case the edge falls
    /// back to this volume's own placement with a zero local offset rather than leaving it at a
    /// stale or default Transform.</summary>
    private Transform ComposeEdgeTransform(Vector3 lengthAxis, float length, Vector3 localCenter)
    {
        var local = new Transform
        {
            // Scale is applied in local mesh space BEFORE rotation (see Transform.GetMatrix()'s
            // Scale*Rotation*Translation order), so Scale.X always stretches SharedEdgeMesh's own
            // local length axis regardless of the rotation below — this is what keeps the
            // thickness axes (Y/Z, left at 1) constant no matter how long the edge is.
            Scale = new Vector3(length, 1f, 1f),
            Rotation = AlignUnitXTo(lengthAxis),
            Translation = localCenter,
        };

        var combined = local.GetMatrix() * Transform.GetMatrix();
        if (!Matrix4x4.Decompose(combined, out var decomposedScale, out var decomposedRotation, out var decomposedTranslation))
            return new Transform { Translation = Transform.Translation, Rotation = Transform.Rotation };

        return new Transform { Scale = decomposedScale, Rotation = decomposedRotation, Translation = decomposedTranslation };
    }

    /// <summary>Rotation aligning SharedEdgeMesh's local +X (its length axis) to point along
    /// <paramref name="axis"/> (always UnitX/UnitY/UnitZ). Only the axis LINE matters, not its
    /// polarity — the edge mesh is symmetric about its own center and radially symmetric in
    /// cross-section, so a +90°/-90° sign mismatch here would still produce an identical result.</summary>
    private static Quaternion AlignUnitXTo(Vector3 axis)
    {
        if (axis == Vector3.UnitY) return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        if (axis == Vector3.UnitZ) return Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f);
        return Quaternion.Identity;
    }

    /// <summary>Rebuilds both the edge transforms and the 12 per-edge Renderables from scratch —
    /// needed whenever the volume's own size changes. NOT GPU-instanced (see the class-level
    /// comment on <see cref="_sharedEdgeMesh"/> for why) — 12 small Renderables all pointing at
    /// the one shared mesh, which is still cheap to reconstruct (no unique per-volume mesh data
    /// involved, unlike the old approach).</summary>
    private void RebuildEdgeRenderable()
    {
        EnsureEdgeMeshCurrent(_gd);
        RecomputeEdgeTransforms();

        cachedRenderables.Clear();
        var material = selected ? GetSelectedMaterial(_gd) : GetUnselectedMaterial(_gd);
        foreach (var edgeTransform in _edgeTransforms)
            cachedRenderables.Add(new Renderable(SharedEdgeMesh, edgeTransform, material));
        IsDirty = false;
        _drawnSelected = selected;
        _appliedMaterialsVersion = _materialsVersion;
        _appliedEdgeMeshVersion = _edgeMeshVersion;
    }

    public override void Draw(IRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender || !EntityManager.Singleton.renderVolumes) return;

        // Was ContainsOrientedBox(boundingBox, Transform.Translation, Transform.Rotation) — the
        // only entity type culling against an OBB instead of its BoundingSphere, and it was
        // dropping volumes that were still visibly inside the frustum. Switched to the same
        // ContainsSphere/WorldBoundingSphere check every other entity (Moby/Tie/UFrag) uses; the
        // sphere fully encloses the box (see the constructor's radius derivation), so this can't
        // cull anything the OBB test would have kept.
        var sphere = WorldBoundingSphere;
        var sphereCenter = new Vector3(sphere.X, sphere.Y, sphere.Z);
        if (EntityManager.Singleton.FrustumCullingEnabled && !camera.GetFrustum().ContainsSphere(sphereCenter, sphere.W)) return;

        if (EntityManager.Singleton.renderBoundingSpheres)
            DrawBoundingSphere(outputDescription, commandList, immediateRenderer);

        // Must run before reading the version fields below: these are what actually rebuild the
        // shared static materials/edge mesh in place if their configured values changed, and bump
        // the corresponding version counter. Called unconditionally (not just from the getters) so
        // a volume whose selection state isn't changing this frame still notices a color/thickness
        // change on the very frame it happens, rather than only whenever some other volume's
        // Draw() happens to touch a getter first.
        EnsureMaterialsCurrent(_gd);
        EnsureEdgeMeshCurrent(_gd);
        bool materialsChanged = _appliedMaterialsVersion != _materialsVersion;
        bool edgeMeshChanged = _appliedEdgeMeshVersion != _edgeMeshVersion;

        if (IsDirty)
        {
            // Volume moved/rotated (Transform's setter sets IsDirty) — the 12 edge transforms are
            // derived from Transform, so they need recomputing, not just a material/mesh swap.
            RebuildEdgeRenderable();
        }
        else if (edgeMeshChanged)
        {
            // Only the shared mesh reference changed (thickness setting) — Renderable.Mesh has no
            // setter, so new Renderables are still needed, but the existing edge transforms are
            // still correct (thickness never factors into them) and don't need recomputing.
            cachedRenderables.Clear();
            var material = selected ? GetSelectedMaterial(_gd) : GetUnselectedMaterial(_gd);
            foreach (var edgeTransform in _edgeTransforms)
                cachedRenderables.Add(new Renderable(SharedEdgeMesh, edgeTransform, material));
            _drawnSelected = selected;
            _appliedMaterialsVersion = _materialsVersion;
            _appliedEdgeMeshVersion = _edgeMeshVersion;
        }
        else if (_drawnSelected != selected || materialsChanged)
        {
            // Selection (or the shared tint materials themselves) changed without a geometry
            // rebuild — swap the material reference directly on all 12 (Renderable.Material has a
            // public setter that flags its own GPU buffer dirty), no need to touch the mesh or
            // cachedRenderables list itself. View3D skips its generic selection-outline pass for
            // Volumes entirely (see View3D.Render) in favor of this — the outline technique
            // inflates along vertex normals and expects one closed mesh, which the 12 disjoint
            // edge instances are not.
            var material = selected ? GetSelectedMaterial(_gd) : GetUnselectedMaterial(_gd);
            foreach (var renderable in cachedRenderables)
                renderable.Material = material;
            _drawnSelected = selected;
            _appliedMaterialsVersion = _materialsVersion;
        }

        foreach (var renderable in cachedRenderables)
            renderer.DrawRenderable(renderable);

        EntitiesRenderedThisFrame++;
    }

    public override void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
