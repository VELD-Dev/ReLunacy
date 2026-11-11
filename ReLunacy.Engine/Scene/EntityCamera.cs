using System.Numerics;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// A camera controller (see <see cref="GameplayController.IsCamera"/>), drawn with the editor's camera
/// model at its placement. Like a volume, it has no mesh of its own in the level: the renderer draws the
/// model at <see cref="Entity.Transform"/> (VulkanRenderer.CameraMarkers).
/// </summary>
public class EntityCamera : Entity
{
    public readonly GameplayController Controller;

    /// <summary>The camera model looks down its own -Z; a camera controller looks along its local +Z (CamArgFixed's
    /// init at QForce EBOOT 0x000176F0 copies the controller's matrix to the view, the Tools of Destruction
    /// convention - Fianshoo's ReLunacy Resurgence, Docs/reverse/NEW_ENGINE_CAMERAS). So the model is turned
    /// half a turn about Y.</summary>
    public static readonly Matrix4x4 ModelToController = Matrix4x4.CreateRotationY(MathF.PI);

    // The turned model's body lies behind the lens, along -Z.
    public override Vector4 BoundingSphere { get; set; } = new(0f, 0f, -1.8f, 2.6f);
    public override string Name { get; protected set; }

    public EntityCamera(GameplayController controller)
    {
        Controller = controller;
        Name = controller.Name;
        // As a Moby's rotation (EntityMoby): Z, then Y, then X.
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, controller.Rotation.Z)
                     * Quaternion.CreateFromAxisAngle(Vector3.UnitY, controller.Rotation.Y)
                     * Quaternion.CreateFromAxisAngle(Vector3.UnitX, controller.Rotation.X);
        Transform = new Transform { Translation = controller.Position, Rotation = rotation, Scale = Vector3.One };
    }
}
