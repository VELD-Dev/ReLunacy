using System.Numerics;

namespace ReLunacy.Engine.Assets.LevelElements;

/// <summary>A point of a <see cref="GameplayPath"/>: where it is and how far along the path from its first point.</summary>
public readonly record struct GameplayPathPoint(Vector3 Position, float Distance);

/// <summary>
/// A path (spline) instance, gp_prius 0x25050 (instances.lua's path_handle): grind and ledge-grab rails,
/// patrols, camera and look-at paths. The 0x10-byte record: +0x00 file offset of its first point,
/// +0x04 0x07041776 on every retail path, +0x08 its total length (the last point's distance), +0x0C byte
/// closed (a loop), +0x0D byte a kind (1 on 92 %, 0 otherwise, 2 on 21 All 4 One paths; role open),
/// +0x0E u16 the point count. The points, in 0x25058, are 0x10 bytes: x, y, z, and the cumulative
/// distance along the path. Ported from Fianshoo's ReLunacy Resurgence (NewGameplayReader, measured on
/// 430 of 430 QForce paths), checked here on A Crack in Time, All 4 One and Into the Nexus.
/// </summary>
public sealed class GameplayPath
{
    public required ulong Tuid { get; init; }
    public required string Name { get; init; }
    /// <summary>The path's place in the region's path table (see <see cref="Volume.Index"/>).</summary>
    public int Index { get; init; } = -1;
    public string? Layer { get; init; }
    public float Length { get; init; }
    public bool Closed { get; init; }
    public byte Kind { get; init; }
    /// <summary>What the path is for (see <see cref="PathRoles"/>): the class of the Clue or controller that uses
    /// it, else what its name says, else <see cref="PathRoles.Unassigned"/>.</summary>
    public string Role { get; set; } = PathRoles.Unassigned;
    /// <summary>What gave the role: the instance that references the path, or null when it came from the name.</summary>
    public string? RoleSource { get; set; }
    public IReadOnlyList<GameplayPathPoint> Points { get; init; } = [];
}

/// <summary>
/// A named group of other instances: a Pod (gp_prius 0x25074, its Mobys) or a Squadron (0x250A0 / 0x250A8,
/// the Tie placements it lists by TUID; the 0x250A8 member records are 0x10 bytes, the TUID first). Squadrons
/// are not in the reference table's types and only some levels ship any (krell_canyon none, creature_collector
/// 11 squadron_ring_*); found by Fianshoo's ReLunacy Resurgence on QForce.
/// </summary>
public sealed class GameplayGroup
{
    public required ulong Tuid { get; init; }
    public required string Name { get; init; }
    public string? Layer { get; init; }
    /// <summary>The members' names, where they resolve (Pods); empty for Squadrons, whose members are Tie placements.</summary>
    public IReadOnlyList<string> MemberNames { get; init; } = [];
    public IReadOnlyList<ulong> MemberTuids { get; init; } = [];
}
