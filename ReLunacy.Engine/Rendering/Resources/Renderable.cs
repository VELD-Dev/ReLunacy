namespace ReLunacy.Engine.Rendering.Resources;

/// <summary>One mesh placed in the world, with an optional per-placement material override.
///
/// The override is what lit ties need: one tie model is shared across many placements, but each
/// placement has its own baked lightmap textures, so the material cannot live on the shared mesh.
///
/// Like <see cref="RenderMesh"/>, this is pure data now. The Bliss Renderable it replaced allocated a
/// transform uniform buffer, an instance vertex buffer, a bone buffer and a material uniform buffer per
/// placement, all of which existed to feed a renderer that no longer runs.</summary>
public sealed class Renderable
{
    public RenderMesh Mesh { get; }

    /// <summary>The per-placement override if there is one, otherwise the mesh's own material.</summary>
    public RenderMaterial Material { get; set; }

    private Transform[] _transforms;

    public Renderable(RenderMesh mesh, Transform transform, RenderMaterial? material = null)
    {
        Mesh = mesh;
        Material = material ?? mesh.Material;
        _transforms = [transform];
    }

    public Renderable(RenderMesh mesh, Transform[] transforms, RenderMaterial? material = null)
    {
        Mesh = mesh;
        Material = material ?? mesh.Material;
        _transforms = transforms.Length > 0 ? transforms : [new Transform()];
    }

    public int InstanceCount => _transforms.Length;

    public ReadOnlySpan<Transform> GetTransforms() => _transforms;

    public void SetTransforms(Transform[] transforms) => _transforms = transforms;
}
