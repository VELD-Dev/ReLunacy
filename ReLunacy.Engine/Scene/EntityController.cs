using System.Numerics;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// A controller (see <see cref="GameplayController"/>): sound environments and emitters, info prompts, FX,
/// spawners, battle and puzzle controllers. It has no mesh: the 3D view draws its class's icon facing the camera
/// (Rendering.ControllerIcons), or else a small wire diamond with the volumes' wireframe; either also picks it.
/// Cameras are <see cref="EntityCamera"/>.
/// </summary>
public class EntityController : Entity
{
    public readonly GameplayController Controller;

    public override string Name { get; protected set; }

    protected override void OnRenamed(string name) => Controller.Name = name;

    /// <summary>The diamond's half-size, in metres, following the camera model's size setting.</summary>
    public static float MarkerRadius => 1.2f * EntityCamera.ModelScale;

    public override Vector4 BoundingSphere
    {
        get => new(0f, 0f, 0f, MarkerRadius);
        set { }
    }

    public EntityController(GameplayController controller)
    {
        Controller = controller;
        Name = controller.Name;
        // As a Moby's rotation (EntityMoby): Z, then Y, then X.
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, controller.Rotation.Z)
                     * Quaternion.CreateFromAxisAngle(Vector3.UnitY, controller.Rotation.Y)
                     * Quaternion.CreateFromAxisAngle(Vector3.UnitX, controller.Rotation.X);
        Transform = new Transform { Translation = controller.Position, Rotation = rotation, Scale = InitialScale(controller) };
    }

    /// <summary>The scale the entity starts with: a camera's is not shown, so it stays one; the others keep the
    /// record's (+0x20), which a save writes back.</summary>
    protected virtual Vector3 InitialScale(GameplayController controller) => controller.Scale;

    /// <summary>The class's name, where the game's class table or the class fields give one, else its number.</summary>
    public string ClassLabel(Games.GameId? game) =>
        Controller.ClassName ?? ClassFields.ClassName(game, Controller.Class) ?? $"0x{Controller.Class:X}";

    /// <summary>The diamond's twelve edges, as world matrices of the volumes' unit edge mesh. Turned with the
    /// controller, not scaled with it.</summary>
    public IEnumerable<Matrix4x4> MarkerEdges()
    {
        float r = MarkerRadius;
        var rotation = Transform.Rotation;
        var centre = Transform.Translation;
        Vector3[] tips =
        [
            new(r, 0, 0), new(-r, 0, 0), new(0, r * 1.5f, 0), new(0, -r * 1.5f, 0), new(0, 0, r), new(0, 0, -r),
        ];
        for (int a = 0; a < tips.Length; a++)
            for (int b = a + 1; b < tips.Length; b++)
            {
                // Opposite tips are not joined.
                if (a / 2 == b / 2) continue;
                var from = Vector3.Transform(tips[a], rotation);
                var to = Vector3.Transform(tips[b], rotation);
                var d = to - from;
                float length = d.Length();
                yield return Matrix4x4.CreateScale(length, 1f, 1f)
                           * Matrix4x4.CreateFromQuaternion(EntityVolume.AlignUnitXTo(d / length))
                           * Matrix4x4.CreateTranslation(centre + (from + to) * 0.5f);
            }
    }
}
