using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.LevelElements;

/// <summary>A trigger volume's shape: the top byte of the last word of its new-engine record (gp_prius
/// 0x2505C +0x3C, the matrix's M44 slot). Each is the unit shape the matrix scales, like the box.</summary>
public enum VolumeShape : byte
{
    Box = 0,
    /// <summary>Along local Y: the elevator shafts and towers, X and Z equal on nearly all of them.</summary>
    Cylinder = 1,
    Sphere = 2,
    /// <summary>8 in all four games. Taken along local Y like the cylinder; the axis is not confirmed.</summary>
    Capsule = 3,
}

public class Volume : IVolumeData
{
    public ulong Id { get; init; }
    public string? Name { get; set; }
    public Matrix4x4 transform;
    public bool IsLoaded { get; set; }
    public ushort group { get; set; }
    public VolumeShape Shape { get; init; }

    /// <summary>The volume's place in the region's volume table, which is what a reference names it by (names
    /// repeat: axiom_city has six "volume30"); -1 on the old engine.</summary>
    public int Index { get; init; } = -1;

    /// <summary>The editor layer the volume was placed in (gp_prius 0x25008, indexed by <see cref="group"/>),
    /// e.g. "gameplay", "cameras", "audio_reverb". Null on the old engine.</summary>
    public string? Layer { get; set; }

    /// <summary>What a plain volume is for, when a Clue names it in a settings field whose meaning is known
    /// (<see cref="ClueClasses.FieldName"/>): "Moon area" for a moon's. Null when nothing known names it.</summary>
    public string? Role { get; set; }
    /// <summary>The Clue that gave <see cref="Role"/>, e.g. "moon1a_moonclue (SpaceMoon +0xD0)".</summary>
    public string? RoleSource { get; set; }

    /// <summary>The volume is where a gravity Clue acts (<see cref="ClueClasses.AssignVolumeRoles"/>): a moon's
    /// SpaceMoon Clue sphere, inside which a jump curves back to the moon. Drawn as a globe, with faces.</summary>
    public bool GravityRegion { get; set; }

    /// <summary>The Clue this volume is the shape of, when it is one: a Clue is a volume with a behaviour
    /// class (fall to death, ledge grab, camera field...). Null for a plain volume.</summary>
    public VolumeClue? Clue { get; set; }

    /// <summary>The Areas listing this volume (an Area groups paths and volumes).</summary>
    public List<string> Areas { get; } = [];

    /// <summary>The Scents listing this volume's Clue (a Scent groups Clues).</summary>
    public List<string> Scents { get; } = [];

    public Volume(ulong id, Matrix4x4 transform, string? name = null, ushort group = 0, VolumeShape shape = VolumeShape.Box)
    {
        Shape = shape;
        Id = id;
        Name = name;
        this.transform = transform;
        IsLoaded = true;
        this.group = group;
    }
}

/// <summary>A Clue: its own instance name and its class (gp_prius 0x25064 +0x0C), with the class's name as
/// <see cref="ClueClasses"/> gives it, and its settings.</summary>
public sealed record VolumeClue(uint Class, string Name, string ClassName)
{
    /// <summary>The Clue instance's TUID (its gp_prius metadata), which a save finds its record by.</summary>
    public ulong Tuid { get; init; }

    /// <summary>The Clue's place in the region's Clue table (see <see cref="Volume.Index"/>).</summary>
    public int Index { get; init; } = -1;

    /// <summary>The class's struct as the level stores it, big-endian, starting with the class number. Its
    /// fields are not named yet beyond <see cref="References"/>.</summary>
    public byte[] Settings { get; init; } = [];

    /// <summary>The fields of <see cref="Settings"/> that name another instance (a rail's path, a camera's
    /// controller, a respawn's Area).</summary>
    public IReadOnlyList<ClueReference> References { get; init; } = [];
}

/// <summary>A field of a Clue's settings, at <paramref name="Offset"/>, naming an instance.</summary>
/// <param name="Kind">Moby, Path, Volume, Clue, Controller, Scent, Area or Pod.</param>
/// <param name="Name">The instance's name, for display: several instances of a kind can share one.</param>
/// <param name="Index">The instance's place in its kind's table, which is what tells it from its namesakes.</param>
public sealed record ClueReference(int Offset, string Kind, string Name, int Index);
