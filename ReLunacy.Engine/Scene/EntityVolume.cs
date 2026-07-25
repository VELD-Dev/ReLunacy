using System.Numerics;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Transformations;
using ReLunacy.Engine.Assets.LevelElements;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public class EntityVolume : Entity
{
    public readonly Volume BaseVolume;

    public override Vector4 BoundingSphere { get; set; } = Vector4.Zero;
    public Vector3 scale;

    public override string Name { get; protected set; }

    public EntityVolume(Volume volume)
    {
        BaseVolume = volume;
        Name = !string.IsNullOrEmpty(volume.Name) ? volume.Name : $"Volume_{ID}";

        Matrix4x4.Decompose(volume.transform, out scale, out var rotation, out var position);

        Transform = new Transform { Translation = position, Rotation = rotation, Scale = Vector3.One };

        // BoundingSphere is LOCAL space per Entity's convention (offset from Transform.Translation)
        // — center coincides with the volume's own position (zero local offset), and the radius is
        // the distance from that center to the cube's furthest corner: the half-extents vector's
        // length, since one corner sits at exactly (sx/2, sy/2, sz/2) from center.
        BoundingSphere = new Vector4(Vector3.Zero, (scale / 2f).Length());
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

        immediateRenderer.DrawCubeWires(Transform, scale, selected ? Color.White : Color.DarkYellow);
        EntitiesRenderedThisFrame++;
    }
}
