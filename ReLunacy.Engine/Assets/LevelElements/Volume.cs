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
    public VolumeShape Shape { get; set; }
    internal bool UsesFallOfManShape { get; init; }

    /// <summary>The volume's place in the region's volume table, which is what a reference names it by (names
    /// repeat: axiom_city has six "volume30"). On the old engine it is its gameplay.dat position too, read only: no old-engine save writes it.</summary>
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

    /// <summary>The same Areas by their place in the region's Area table, which is what a reference names.</summary>
    public List<int> AreaIndexes { get; } = [];

    /// <summary>The Scents listing this volume's Clue (a Scent groups Clues).</summary>
    public List<string> Scents { get; } = [];

    /// <summary>The same Scents by their place in the region's Scent table.</summary>
    public List<int> ScentIndexes { get; } = [];

    public Volume(ulong id, Matrix4x4 transform, string? name = null, ushort group = 0, VolumeShape shape = VolumeShape.Box)
    {
        Shape = shape;
        Id = id;
        Name = name;
        this.transform = transform;
        IsLoaded = true;
        this.group = group;
    }

    /// <summary>A volume made in the editor: <paramref name="index"/> is its place at the end of the region's
    /// volume table (see NewInstance), <paramref name="layer"/> the name of layer <paramref name="group"/>.</summary>
    public Volume(ulong id, Matrix4x4 transform, string name, ushort group, VolumeShape shape, int index, string? layer)
        : this(id, transform, name, group, shape)
    {
        Index = index;
        Layer = layer;
    }
}

/// <summary>A Clue: its own instance name and its class (gp_prius 0x25064 +0x0C), with the class's name as
/// <see cref="ClueClasses"/> gives it, and its settings.</summary>
public sealed record VolumeClue(uint Class, string Name, string ClassName)
{
    /// <summary>The Clue instance's name; a rename changes it in place (the Clue object is compared by identity for changes).</summary>
    public string Name { get; set; } = Name;

    /// <summary>The Clue instance's TUID (its gp_prius metadata), which a save finds its record by.</summary>
    public ulong Tuid { get; init; }

    /// <summary>The Clue's place in the region's Clue table (see <see cref="Volume.Index"/>).</summary>
    public int Index { get; init; } = -1;

    /// <summary>The class's big-endian settings struct. Fall of Man stores the Clue type in the
    /// placement, so its settings start with the type-specific payload.</summary>
    public byte[] Settings { get; init; } = [];

    /// <summary>The Clue's settings block is its class's default one (gp_prius 0x25030, shared), as the file holds it.</summary>
    public bool SharedSettings { get; init; }

    /// <summary>The fields of <see cref="Settings"/> that name another instance (a rail's path, a camera's
    /// controller, a respawn's Area).</summary>
    public IReadOnlyList<ClueReference> References { get; set; } = [];
}

/// <summary>A field of a Clue's settings, at <paramref name="Offset"/>, naming an instance.</summary>
/// <param name="Kind">Moby, Path, Volume, Clue, Controller, Scent, Area or Pod.</param>
/// <param name="Name">The instance's name, for display: several instances of a kind can share one.</param>
/// <param name="Index">The instance's place in its kind's table, which is what tells it from its namesakes.</param>
/// <param name="Item">-1 for a reference in the struct itself; else the entry, from 0, of the list the pointer field
/// at <paramref name="Offset"/> points to (kept outside the struct, so the word is not in the settings bytes).</param>
public sealed record ClueReference(int Offset, string Kind, string Name, int Index, int Item = -1)
{
    /// <summary>The reference is an entry of a list, not a word of the struct.</summary>
    public bool InList => Item >= 0;

    /// <summary>Where it is, for display: +0x74, or +0x28[3] for a list's entry.</summary>
    public string Location => InList ? $"+0x{Offset:X}[{Item}]" : $"+0x{Offset:X}";
}
