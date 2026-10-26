using System.Numerics;
using ReLunacy.Engine.Assets.Geometry;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IPlacedInstance<out TAsset> where TAsset : IAsset
{
    TAsset Asset { get; }
    Vector3 Position { get; }
    /// <summary>ZYX Euler angles. Radians for Mobys (raw from file); unused for Ties, which carry an exact placement matrix instead - see <see cref="GetTransformMatrix"/>.</summary>
    Vector3 Rotation { get; }
    float Scale { get; }
    public ulong ID { get; set; }
    /// <summary>Often found in debug.dat.</summary>
    public string Name { get; set; }
    /// <summary><c>0</c> on old engine.</summary>
    public ushort Group { get; init; }
    /// <summary>Distance (in-game units) beyond which the game itself stops rendering this instance. &lt; 0 means unlimited. Only Mobys carry this from the file; other instance types default to unlimited.</summary>
    public float DisplayDistance { get; init; }

    /// <summary>Distance (in-game units) beyond which the game stops updating this instance's logic
    /// (separate from <see cref="DisplayDistance"/>, which only gates rendering). &lt; 0 means
    /// unlimited. Only Mobys carry this from the file; other instance types default to unlimited.</summary>
    public float UpdateDistance { get; init; }

    /// <summary>This instance's entry in the level's baked lighting lists (LevelData.ZoneLightmaps
    /// / ZoneDirectionals), or 0xFFFF for none. PER-INSTANCE, not per-asset: measured on
    /// metropolis, 1728 of 4848 tie instances carry one and every index is distinct, i.e. one
    /// unique bake per placement. Only ties populate it today - see TieInstance.LightmapIndex.</summary>
    public ushort LightmapIndex { get; init; }

    /// <summary>Old-engine TIE per-instance baked light scale (TieInstance +0x60), or null.</summary>
    float? OldBakedLightScale => null;

    /// <summary>New-engine TIE baked-light vertex constant c[4] (TieInstance.NewEngineBakedLight), or null.</summary>
    Vector4? NewEngineBakedLight => null;

    /// <summary>New-engine TIE: the cubemaps.dat resource the placement reflects (zone 0x72C1 palette entry
    /// at record +0x5D), 0 for the region's default.</summary>
    ulong NewEngineCubeTuid => 0;

    /// <summary>New-engine TIE prelight words (4 bytes per prototype vertex, record +0x54 in the zone's
    /// lighting.dat slice), or null.</summary>
    byte[]? NewEnginePrelight => null;

    /// <summary>
    /// Old engine (ToD): an unbaked placement's prelight, read by its TIE vertex programs as attribute 4
    /// (UB256 x4 per vertex of the TIE, in the TIE's vertex order): textures.dat at record +0x54
    /// (consecutive placements of one TIE sit 4 x its vertex count apart). Null when the placement is
    /// baked or has none. Docs/reverse/TOD_RENDER_POST_2026-10-09.md section 7.
    /// </summary>
    byte[]? OldEnginePrelight => null;

    /// <summary>Record +0x54: where <see cref="NewEnginePrelight"/> starts in the zone's lighting.dat slice.</summary>
    uint NewEnginePrelightOffset => 0xFFFFFFFF;

    /// <summary>Whether the placement's own record carries a bake (+0x5A != 0xFFFF): it changes which
    /// word a primitive's vertex 0 reads (see NewEngineTiePrimitive.PrelightFirstWord).</summary>
    bool NewEnginePrelightBaked => false;


    Matrix4x4 GetTransformMatrix();
}