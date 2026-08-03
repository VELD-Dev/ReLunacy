using System.Numerics;

namespace ReLunacy.Engine.Assets.Lighting;

/// <summary>One directional light of a <see cref="LightingEnvironment"/>: a colour (carrying its own
/// HDR magnitude — there is no separate intensity field) and a unit direction, taken as pointing
/// TOWARD the light.</summary>
public sealed class DirectionalLight
{
    public Vector3 Colour { get; set; }
    public Vector3 Direction { get; set; }
}

/// <summary>The level's analytic lighting environment — old-engine main.dat section 0x8b00, one
/// record per level. Reverse-engineered by matching its floats to a RenderDoc capture of the game's
/// fragment constant bank. An ambient colour plus a list of directional lights.
///
/// The record has room for two directions (0x50/0x60) and three colours (0x20 ambient, 0x30/0x40
/// per light), and its header's first word is a count (2 on both levels seen). We DON'T assume a
/// level always has exactly two: the reader builds <see cref="Lights"/> from whichever direction
/// slots are actually populated, so a level with one — or none — is handled. If a future level's
/// record is larger than 0x80 it carries more than two and the reader would need extending.</summary>
public sealed class LightingEnvironment
{
    // Mutable so the Level Data frame can live-tune them (View3D re-reads these into the renderer
    // every frame). Reloading the level restores the file values.
    public Vector3 Ambient { get; set; }
    public List<DirectionalLight> Lights { get; } = [];
}
