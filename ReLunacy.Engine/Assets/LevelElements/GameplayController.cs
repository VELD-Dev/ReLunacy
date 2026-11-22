using System.Numerics;

namespace ReLunacy.Engine.Assets.LevelElements;

/// <summary>
/// A controller instance (gp_prius 0x2506C, instances.lua's controller_handle): a placed point with a
/// class - camera arguments, FX and sound emitters, spawners, puzzle and battle controllers.
/// The 0x30-byte record: +0x00 / +0x04 its settings (absolute offset, size; the class number first, as a
/// Clue's), +0x08 position, +0x14 rotation (radians, applied as a Moby's: Z, then Y, then X), +0x20 scale.
/// </summary>
public sealed class GameplayController
{
    public GameplayController() { }

    /// <summary>A controller made in the editor, with the settings of a controller of the same class (see
    /// NewInstance). <paramref name="index"/> is its place at the end of the region's controller table.</summary>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GameplayController(ulong tuid, string name, int index, string? layer, uint cls, string? className, byte[] settings,
        IReadOnlyList<ClueReference> references, Vector3 position, Vector3 rotation, bool isCamera)
    {
        Tuid = tuid;
        Name = name;
        Index = index;
        Layer = layer;
        Class = cls;
        ClassName = className;
        Settings = settings;
        References = references;
        Position = position;
        Rotation = rotation;
        IsCamera = isCamera;
    }

    public required ulong Tuid { get; init; }
    public required string Name { get; set; }
    /// <summary>The controller's place in the region's controller table (see <see cref="Volume.Index"/>).</summary>
    public int Index { get; init; } = -1;
    public string? Layer { get; set; }
    public uint Class { get; set; }
    /// <summary>The class's name where the game's class table gives it (All 4 One, QForce: CamArgBlendable,
    /// FxController...); null elsewhere.</summary>
    public string? ClassName { get; set; }
    /// <summary>The class's struct, as a Clue's (<see cref="VolumeClue.Settings"/>).</summary>
    public byte[] Settings { get; set; } = [];
    /// <summary>The fields of <see cref="Settings"/> naming another instance.</summary>
    public IReadOnlyList<ClueReference> References { get; set; } = [];
    /// <summary>The controller's settings block is its class's default one (gp_prius 0x25030, shared), as the file holds it.</summary>
    public bool SharedSettings { get; init; }
    public Vector3 Position { get; init; }
    public Vector3 Rotation { get; init; }
    public Vector3 Scale { get; init; } = Vector3.One;

    /// <summary>A camera's placement: its class is the one the level's cam... / CamArg... controllers share
    /// (CamArgBlendable, CamArgFixed and the cinematic cameras; see GameplayGroupReader.ReadControllers).</summary>
    public bool IsCamera { get; set; }
}
