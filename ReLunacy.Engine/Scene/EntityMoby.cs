using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Diagnostics;
using ReLunacy.Engine.Rendering;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public class EntityMoby : Entity
{
    public readonly IMoby BaseMoby;
    public override string Name { get; protected set; }
    public RenderModel[]? Models { get; private set; }

    public override Vector4 BoundingSphere { get; set; }

    /// <summary>In-game display distance for this instance (units), &lt; 0 = unlimited. Read straight from the level's own gameplay data - see MobyInstanceOld/New.</summary>
    public float DisplayDistance { get; set; }

    /// <summary>In-game update distance for this instance (units), &lt; 0 = unlimited - a separate
    /// budget from <see cref="DisplayDistance"/> that gates the game's own logic updates, not
    /// rendering. Read straight from the level's own gameplay data - see MobyInstanceOld/New. Not
    /// used by anything in ReLunacy's own rendering/culling; carried purely so it's visible and
    /// editable in the Property Inspector, matching ReLunacy-Ymir.</summary>
    public float UpdateDistance { get; set; }

    public EntityMoby(IPlacedInstance<IMoby> mobyInstance, AssetManager assetManager)
    {
        BaseMoby = mobyInstance.Asset;
        // Moby instance rotation is read straight from the file as ZYX Euler angles in radians
        // (see MobyInstanceOld/New - no unit conversion happens at the read site). The original
        // Lunacy app (Transform.SetRotation) builds this as Qz * Qy * Qx, i.e. rotate around X
        // first, then Y, then Z - NOT the same composition as CreateFromYawPitchRoll(Y,X,Z),
        // which was verified (numerically, against an unambiguous row-vector reference matrix)
        // to produce a different rotation whenever X and Z are both non-zero.
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
