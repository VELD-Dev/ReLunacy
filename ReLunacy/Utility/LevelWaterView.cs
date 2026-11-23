using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Utility;

/// <summary>What the 3D view hands the renderer for the level's water bodies (zone 0x6641) and its
/// VerticalFogControllers.</summary>
public static class LevelWaterView
{
    /// <summary>VerticalFogController: the same class number in A Crack in Time, All 4 One and Into the Nexus.</summary>
    public const uint VerticalFogControllerClass = 0x472;

    private static object? _watersLevel;
    private static int _watersVisibility = -1, _watersRegions = -1;
    private static object? _watersFirstRegion;
    private static WaterDraw[] _waters = [];
    private static readonly List<VerticalFogDraw> _fogs = [];

    /// <summary>Whether the level has any water body, shown or not.</summary>
    public static bool HasWater(LevelData? level) => level?.Zones.Values.Any(z => z.Waters.Count > 0) == true;

    /// <summary>The water bodies to draw, each record once (a zone two regions share has an EntityWater in each), with
    /// the hidden ones left out and each carrying its entity's id for picking. Rebuilt when the level or the hierarchy's
    /// visibility changes.</summary>
    public static IReadOnlyList<WaterDraw> Waters(LevelData? level)
    {
        var em = EntityManager.Singleton;
        // A region switch replaces the regions (and so the entities and their ids) without changing the level.
        object? firstRegion = em.Regions.Count > 0 ? em.Regions[0] : null;
        if (ReferenceEquals(level, _watersLevel) && _watersVisibility == em.VisibilityVersion && _watersRegions == em.Regions.Count &&
            ReferenceEquals(firstRegion, _watersFirstRegion))
            return _waters;
        _watersFirstRegion = firstRegion;
        _watersLevel = level;
        _watersVisibility = em.VisibilityVersion;
        _watersRegions = em.Regions.Count;
        var list = new List<WaterDraw>();
        var seen = new HashSet<Engine.Assets.Water.WaterSurface>(ReferenceEqualityComparer.Instance);
        if (level != null)
            foreach (var region in em.Regions)
            {
                if (!region.allowRender) continue;
                var regionCubes = level.CubemapsByRegion.GetValueOrDefault(region.RegionName) ?? level.Cubemaps;
                ulong defaultCube = regionCubes.FirstOrDefault()?.ResourceTuid ?? 0;
                ulong waterCube = region.DefaultWaterCubeTuid;
                // ACIT uses table index zero if the water default is absent (0x5899D8).
                // The region load orders the table by TUID (0x587B84).
                if (waterCube == 0 || !regionCubes.Any(c => c.ResourceTuid == waterCube))
                    waterCube = region.Game == Engine.Games.GameId.ACrackInTime && regionCubes.Count > 0
                        ? regionCubes.Min(c => c.ResourceTuid) : defaultCube;
                foreach (var zone in region.Zones)
                {
                    if (!zone.allowRender) continue;
                    foreach (var water in zone.Waters)
                    {
                        if (!water.allowRender || !seen.Add(water.Surface)) continue;
                        var w = water.Surface;
                        ulong reflection = w.CubeSet == 0 ? waterCube
                            : w.ReflectionCubeTuid != 0 ? w.ReflectionCubeTuid : defaultCube;
                        list.Add(new WaterDraw(w.Corner, w.Size, w.Colour, w.Absorption, w.TessellationLevel,
                            (uint)water.ID, reflection, w.ClipMask?.Id ?? 0));
                    }
                }
            }
        _waters = [.. list];
        return _waters;
    }

    /// <summary>The fogs as they stand now: each controller's volume (its first Volume reference, settings +0x14 on
    /// A Crack in Time) with the colour bytes after it. Rebuilt each frame, so a moved volume moves its fog.</summary>
    public static IReadOnlyList<VerticalFogDraw> VerticalFogs(IEnumerable<EntityRegion> regions)
    {
        _fogs.Clear();
        foreach (var region in regions)
        {
            Dictionary<int, EntityVolume>? volumes = null;
            foreach (var controller in region.Controllers)
            {
                if (controller.Class != VerticalFogControllerClass) continue;
                var reference = controller.References.FirstOrDefault(r => r.Kind == "Volume");
                if (reference == null || reference.Index < 0) continue;
                volumes ??= region.Volumes.Entities.OfType<EntityVolume>()
                    .Where(v => v.BaseVolume.Index >= 0)
                    .GroupBy(v => v.BaseVolume.Index).ToDictionary(g => g.Key, g => g.First());
                if (!volumes.TryGetValue(reference.Index, out var volume)) continue;
                int c = reference.Offset + 4;
                byte[] s = controller.Settings;
                var colour = c + 3 <= s.Length ? new Vector3(s[c], s[c + 1], s[c + 2]) / 255f : Vector3.One;
                _fogs.Add(new VerticalFogDraw(volume.FillMatrix, colour));
            }
        }
        return _fogs;
    }
}
