using System.Numerics;

namespace ReLunacy.Engine.Assets.Lighting;

/// <summary>One directional light: a colour (its own HDR magnitude, no separate intensity) and a
/// unit direction pointing toward the light.</summary>
public sealed class DirectionalLight
{
    public Vector3 Colour { get; set; }
    public Vector3 Direction { get; set; }
}

/// <summary>The level's analytic lighting environment (old-engine main.dat section 0x8b00): an
/// ambient colour plus a list of directional lights. The reader builds <see cref="Lights"/> from
/// whichever direction slots are populated, so levels with fewer than two lights are handled.</summary>
public sealed class LightingEnvironment
{
    // Mutable to allow live-tuning from the Level Data frame; reloading the level restores file values.
    public Vector3 Ambient { get; set; }
    public List<DirectionalLight> Lights { get; } = [];
}
