using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Animations;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Diagnostics;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public class EntityMoby : Entity
{
    public readonly IMoby BaseMoby;
    public override string Name { get; protected set; }
    public RenderModel[]? Models { get; private set; }

    /// <summary>Lazily created on first use - most mobys never play an animation in the 3D View, so
    /// there is no reason for every instance to carry one. See EntityManager.PlayingMobyAnimations
    /// for the per-frame update/GPU-push loop that drives this once playback starts.</summary>
    public AnimationPlayer AnimationPlayer => _animationPlayer ??= new AnimationPlayer();
    private AnimationPlayer? _animationPlayer;
    public bool IsPlayingAnimation => _animationPlayer?.IsPlaying ?? false;

    public override Vector4 BoundingSphere { get; set; }

    /// <summary>In-game display distance for this instance (units), &lt; 0 = unlimited. Read straight from the level's own gameplay data - see MobyInstanceOld/New.</summary>
    public float DisplayDistance { get; set; }

    /// <summary>In-game update distance for this instance (units), &lt; 0 = unlimited - gates the
    /// game's own logic updates, not rendering. Not used by ReLunacy's own rendering/culling; kept
    /// for the Property Inspector.</summary>
    public float UpdateDistance { get; set; }

    public EntityMoby(IPlacedInstance<IMoby> mobyInstance, AssetManager assetManager)
    {
        BaseMoby = mobyInstance.Asset;
        // Rotation is ZYX Euler angles in radians, composed as Qz * Qy * Qx (rotate X first, then Y,
        // then Z) - not the same composition as CreateFromYawPitchRoll.
        var rotationQuat = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, mobyInstance.Rotation.Z)
                          * Quaternion.CreateFromAxisAngle(Vector3.UnitY, mobyInstance.Rotation.Y)
                          * Quaternion.CreateFromAxisAngle(Vector3.UnitX, mobyInstance.Rotation.X);

        Transform = new Transform
        {
            Translation = mobyInstance.Position,
            Rotation = rotationQuat,
            Scale = new Vector3(mobyInstance.Scale)
        };

        var (center, radius) = BaseMoby.GetBoundingSphere();
        BoundingSphere = new Vector4(center, radius);

        DisplayDistance = mobyInstance.DisplayDistance;
        UpdateDistance = mobyInstance.UpdateDistance;

        Name = !string.IsNullOrEmpty(mobyInstance.Name) ? mobyInstance.Name.Split('/')[^1] : $"Moby_{BaseMoby.Id:X}_{mobyInstance.Group}";

        assetManager.Mobys.TryGetValue(BaseMoby.Id, out var models);
        Models = models;
    }

    protected override void EnsureRenderables()
    {
        if (!IsDirty || Models is null) return;
        cachedRenderables.Clear();
        foreach (var model in Models)
            foreach (var mesh in model.Meshes)
                cachedRenderables.Add(new Renderable(mesh, Transform));
        IsDirty = false;
    }

}
