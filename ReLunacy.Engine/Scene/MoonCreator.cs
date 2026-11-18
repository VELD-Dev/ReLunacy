using System.Buffers.Binary;
using System.Numerics;
using System.Text.RegularExpressions;
using ReLunacy.Engine.Assets.LevelElements;

namespace ReLunacy.Engine.Scene;

/// <summary>A moon that can serve as the model of a new one: a SpaceMoon Clue with its surface Tie, area volume and
/// landing pad beam, all of this level.</summary>
public sealed record MoonSurface(EntityRegion Region, EntityVolume Clue, EntityTie Tie, EntityVolume Area, EntityMoby Beam,
    string Name, float Radius);

/// <summary>A Pod or Area to make for the moon, in its gameplay layer. <paramref name="WithObjects"/> puts the moon's own
/// instances in it: its landing pad beam in a Pod, its area volume in an Area.</summary>
public sealed record MoonGroup(string Name, bool WithObjects);

/// <summary>
/// What the dialog asks for: the id (the names of its layers, zone and objects), the model moon, the radius, where the
/// centre goes (X and Z; Y follows the model's beam) and the optional extras. Left alone, a moon gets the layers art_&lt;id&gt;
/// and gameplay_&lt;id&gt;, a zone of the art layer's name, and no segment.
/// </summary>
/// <param name="SegmentPod">An existing segment Pod (an index of the region's Pods) that takes the landing pad beam; -1 for none.</param>
public sealed record MoonRequest(string Id, MoonSurface Source, float Radius, Vector2 CentreXZ, int SegmentPod = -1)
{
    /// <summary>A new segment Pod of this name, in the gameplay layer, holding the beam (instead of <see cref="SegmentPod"/>).</summary>
    public string? NewSegment { get; init; }
    /// <summary>The art layer: an existing layer's name or a new one's; null for art_&lt;id&gt;.</summary>
    public string? ArtLayer { get; init; }
    /// <summary>The gameplay layer: an existing layer's name (one with no moon in it) or a new one's; null for gameplay_&lt;id&gt;.</summary>
    public string? GameplayLayer { get; init; }
    /// <summary>The zone the surface goes in, or null for a new zone.</summary>
    public EntityZone? Zone { get; init; }
    /// <summary>The new zone's name; null for the art layer's name when that layer is new, else art_&lt;id&gt;.</summary>
    public string? ZoneName { get; init; }
    public IReadOnlyList<MoonGroup> Pods { get; init; } = [];
    public IReadOnlyList<MoonGroup> Areas { get; init; } = [];
    /// <summary>With a new segment: enter it in level.csv (a segment is live in game only there). See <see cref="Config.SegmentRegistry"/>.</summary>
    public bool RegisterSegment { get; init; } = true;
}

/// <summary>A moon made in the editor: what was added, and the undo and redo of the whole step.</summary>
public sealed class MoonCreated
{
    public required EntityRegion Region { get; init; }
    public required string ArtLayer { get; init; }
    public required string GameplayLayer { get; init; }
    public required EntityZone Zone { get; init; }
    public required EntityTie Tie { get; init; }
    public required EntityVolume Area { get; init; }
    public required EntityMoby Beam { get; init; }
    public required EntityVolume Clue { get; init; }
    /// <summary>The existing segment Pod the beam went into; -1 for none.</summary>
    public int SegmentPod { get; init; } = -1;
    /// <summary>The layers this step added, in the order it added them.</summary>
    public IReadOnlyList<string> NewLayers { get; init; } = [];
    /// <summary>The zone is this step's (an existing one was not made here).</summary>
    public bool ZoneIsNew { get; init; } = true;
    /// <summary>The Pods this step added, the new segment first, in the order it added them.</summary>
    public IReadOnlyList<EntityPod> NewPods { get; init; } = [];
    public EntityPod? NewSegment => NewPods.FirstOrDefault();
    /// <summary>The Areas this step added.</summary>
    public IReadOnlyList<NewArea> NewAreas { get; init; } = [];
    /// <summary>The level.csv entry this step made pending, if any.</summary>
    public Config.SegmentRegistration? Registration { get; init; }

    /// <summary>Everything the step added to the region, in the order it was added.</summary>
    public IEnumerable<Entity> Entities => [Tie, Area, Beam, Clue];

    /// <summary>Takes the moon out: its objects, its Pods and Areas, the zone, then the layers.</summary>
    public void Undo()
    {
        if (SegmentPod >= 0 && SegmentPod < Region.Pods.Count) Region.Pods[SegmentPod].Remove(Beam);
        Region.Remove(Clue);
        Region.Remove(Beam);
        Region.Remove(Area);
        Region.Remove(Tie);
        if (Registration != null) Config.SegmentRegistry.Remove(Registration);
        foreach (var pod in NewPods.Reverse()) Region.RemovePod(pod);
        foreach (var area in NewAreas.Reverse()) Region.RemoveArea(area);
        if (ZoneIsNew) Region.RemoveNewZone(Zone);
        foreach (string layer in NewLayers.Reverse()) Region.RemoveNewLayer(layer);
        Region.SyncHandles();
        Region.RefreshVolumeRoles();
    }

    /// <summary>Puts the moon back after an <see cref="Undo"/>.</summary>
    public void Redo()
    {
        foreach (string layer in NewLayers) Region.AddNewLayer(layer);
        if (ZoneIsNew) Region.Zones.Add(Zone);
        foreach (var area in NewAreas) Region.AddArea(area);
        if (Registration != null) Config.SegmentRegistry.Add(Registration);
        foreach (var pod in NewPods) Region.AddPod(pod);
        foreach (var entity in Entities) Region.Add(entity);
        if (SegmentPod >= 0 && SegmentPod < Region.Pods.Count) Region.Pods[SegmentPod].Add(Beam);
        Region.SyncHandles();
        Region.InvalidateLookups();
        Region.RefreshVolumeRoles();
    }
}

/// <summary>
/// Create moon, for A Crack in Time's space sectors. A moon is its SpaceMoon Clue (class 0x4BC) with +0xC0 the landing pad
/// beam Moby, +0xD0 the area volume and +0xE0 the surface radius, its surface Tie in a zone of its own, and the layers
/// art_&lt;id&gt; and gameplay_&lt;id&gt; (retail: moon1a has the layers art_moon1a and gameplay_moon1a, the zone art_moon1a, the
/// objects moon1a_moonclue, moon1a_vol and moon1a_landing_pad_beam). A new moon copies an existing one of the level: the
/// surface Tie scaled by radius over the model's radius, the offsets from the Tie's centre to the Clue, area and beam
/// scaled the same way, the beam's orientation and the volumes' sizes (scaled). The centre's Y keeps the beam at the
/// model's beam height, which the ship (flying at Y 780) lands on.
/// The layers and zone are pending in the region (<see cref="EntityRegion.NewLayers"/>, <see cref="EntityRegion.NewZones"/>); a
/// save adds them (GameplayStructure, GameplayWriter).
/// </summary>
public static class MoonCreator
{
    public const uint SpaceMoonClass = 0x4BC;
    public const int BeamField = 0xC0, AreaField = 0xD0, RadiusField = 0xE0;

    public static float RadiusOf(VolumeClue clue) =>
        clue.Settings.Length >= RadiusField + 4
            ? BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32BigEndian(clue.Settings.AsSpan(RadiusField))) : 0f;

    /// <summary>The moon's own surface Tie: in a zone named for the moon, a bounding sphere of about the surface radius centred
    /// on its placement (checked on space_sector_1 and 2). Null when there is none.</summary>
    public static EntityTie? MainTie(IEnumerable<EntityRegion> regions, string key, float radius)
    {
        if (key.Length == 0) return null;
        EntityTie? best = null;
        float bestError = radius * 0.1f;
        foreach (var region in regions)
            foreach (var zone in region.Zones.Where(z => z.ZoneName.Contains(key, StringComparison.OrdinalIgnoreCase)))
                foreach (var tie in zone.TieInstances.Entities.OfType<EntityTie>())
                {
                    var sphere = tie.WorldBoundingSphere;
                    // The sphere carries a 2% margin.
                    float error = MathF.Abs(sphere.W / 1.02f - radius);
                    if (error >= bestError || Vector3.Distance(new Vector3(sphere.X, sphere.Y, sphere.Z), tie.Transform.Translation) > radius * 0.1f) continue;
                    bestError = error;
                    best = tie;
                }
        return best;
    }

    /// <summary>The moon's id from its Clue's name: "moon1a_moonclue" is moon1a, "moon1_2_moonclue" moon1_2 (an id may hold
    /// underscores); a Clue not named that way keeps what precedes its first underscore.</summary>
    public static string KeyOf(string clueName) =>
        clueName.EndsWith("_moonclue", StringComparison.OrdinalIgnoreCase) ? clueName[..^"_moonclue".Length] : clueName.Split('_')[0];

    /// <summary>What a moon is made of, found from its Clue: the surface the objects stand on, the area and the beam.</summary>
    public sealed record Frame(Vector3 Centre, float Radius, EntityVolume? Area, EntityTie? Tie, EntityMoby? Beam);

    /// <summary>Finds the moon's parts without trusting names: the area and beam by the Clue's references (a fresh lookup), the
    /// surface Tie as the Tie in any zone whose bounding sphere is about the radius, centred on its placement, nearest the
    /// area (the Clue when there is none), those in a zone named for the moon first. The centre is the Tie's placement, else
    /// the area's, else the Clue's.</summary>
    public static Frame Resolve(EntityRegion region, EntityVolume clueVolume, IEnumerable<EntityRegion> regions)
    {
        var clue = clueVolume.BaseVolume.Clue!;
        float radius = RadiusOf(clue);
        if (!float.IsFinite(radius) || radius <= 1f) radius = 50f;
        var areaRef = clue.References.FirstOrDefault(r => r.Offset == AreaField && !r.InList);
        var beamRef = clue.References.FirstOrDefault(r => r.Offset == BeamField && !r.InList);
        var area = areaRef == null ? null : region.Find(areaRef.Kind, areaRef.Index) as EntityVolume;
        var beam = beamRef == null ? null : region.Find(beamRef.Kind, beamRef.Index) as EntityMoby;
        var near = (area ?? clueVolume).Transform.Translation;
        string key = KeyOf(clue.Name);

        EntityTie? best = null;
        float bestScore = float.MaxValue;
        foreach (var r in regions)
            foreach (var zone in r.Zones)
            {
                bool named = key.Length > 0 && zone.ZoneName.Contains(key, StringComparison.OrdinalIgnoreCase);
                foreach (var tie in zone.TieInstances.Entities.OfType<EntityTie>())
                {
                    var sphere = tie.WorldBoundingSphere;
                    var place = tie.Transform.Translation;
                    // The sphere carries a 2% margin.
                    if (MathF.Abs(sphere.W / 1.02f - radius) > radius * 0.1f
                        || Vector3.Distance(new Vector3(sphere.X, sphere.Y, sphere.Z), place) > radius * 0.1f) continue;
                    float distance = Vector3.Distance(place, near);
                    if (distance > radius * 1.5f) continue;
                    float score = distance + (named ? 0f : radius * 2f);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    best = tie;
                }
            }
        var centre = best?.Transform.Translation ?? near;
        return new Frame(centre, radius, area, best, beam);
    }

    /// <summary>The moons of the level a new one can be made from: ACiT only, with their surface Tie, area and beam found.</summary>
    public static List<MoonSurface> Surfaces(IReadOnlyList<EntityRegion> regions)
    {
        var list = new List<MoonSurface>();
        foreach (var region in regions)
        {
            if (region.Game != Games.GameId.ACrackInTime || !region.References.Editable) continue;
            foreach (var volume in region.Volumes.Entities.OfType<EntityVolume>())
            {
                if (volume.BaseVolume.Clue is not { Class: SpaceMoonClass } clue || volume.IsNew) continue;
                float radius = RadiusOf(clue);
                var beamRef = clue.References.FirstOrDefault(r => r.Offset == BeamField && !r.InList);
                var areaRef = clue.References.FirstOrDefault(r => r.Offset == AreaField && !r.InList);
                if (!float.IsFinite(radius) || radius < 1f || beamRef == null || areaRef == null) continue;
                if (region.Find(beamRef.Kind, beamRef.Index) is not EntityMoby beam || region.Find(areaRef.Kind, areaRef.Index) is not EntityVolume area) continue;
                string name = KeyOf(clue.Name);
                if (Resolve(region, volume, regions).Tie is not { } tie) continue;
                list.Add(new MoonSurface(region, volume, tie, area, beam, name, radius));
            }
        }
        return [.. list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The next free id in retail's style: moon, the sector's digit, the first letter no layer or zone uses.</summary>
    public static string NextFreeId(EntityRegion region, MoonSurface? like = null)
    {
        string digit = like != null && Regex.Match(like.Name, @"^moon(\d+)") is { Success: true } m ? m.Groups[1].Value
            : region.Layers.Select(l => Regex.Match(l, @"^gameplay_moon(\d+)")).FirstOrDefault(x => x.Success)?.Groups[1].Value ?? "1";
        for (char letter = 'a'; letter <= 'z'; letter++)
        {
            string id = $"moon{digit}{letter}";
            if (IdProblem(region, id) == null) return id;
        }
        for (int n = 1; ; n++)
            if (IdProblem(region, $"moon{digit}_{n}") == null) return $"moon{digit}_{n}";
    }

    /// <summary>Why <paramref name="id"/> cannot be a new moon's with the default layers, or null.</summary>
    public static string? IdProblem(EntityRegion region, string id)
    {
        if (IdFormatProblem(id) is { } bad) return bad;
        return region.NameProblem($"art_{id}") ?? region.NameProblem($"gameplay_{id}")
            ?? (region.Zones.Count >= 64 ? "64 zones" : region.Layers.Count + 2 > 64 ? "64 layers" : null);
    }

    /// <summary>Why <paramref name="id"/> is not a usable id whatever the layers are, or null.</summary>
    public static string? IdFormatProblem(string id)
    {
        if (id.Length == 0) return "empty";
        return id.Length > 30 || !id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_') ? "a-z 0-9 _" : null;
    }

    public static string ArtLayerOf(MoonRequest r) => r.ArtLayer is { Length: > 0 } name ? name : $"art_{r.Id}";
    public static string GameplayLayerOf(MoonRequest r) => r.GameplayLayer is { Length: > 0 } name ? name : $"gameplay_{r.Id}";

    /// <summary>The retail name of a moon's segment Pod: SEGMENT_SS&lt;sector&gt;_&lt;ID&gt;, as SEGMENT_SS1_MOON1A is moon1a's. The sector
    /// is the id's digit (moon1d), else the region's.</summary>
    public static string SegmentName(EntityRegion region, string id)
    {
        string digit = Regex.Match(id, @"^moon(\d+)") is { Success: true } m ? m.Groups[1].Value
            : Regex.Match(region.RegionName, @"(\d+)$") is { Success: true } r ? r.Groups[1].Value : "1";
        string name = $"SEGMENT_SS{digit}_{id.ToUpperInvariant()}";
        if (region.GroupNameProblem(false, name) == null) return name;
        for (int n = 2; ; n++)
            if (region.GroupNameProblem(false, $"{name}_{n}") == null) return $"{name}_{n}";
    }

    /// <summary>Why the request cannot be made, or null: everything <see cref="Create"/> needs, checked before it changes
    /// anything.</summary>
    public static string? Problem(MoonRequest request)
    {
        var region = request.Source.Region;
        if (EntityManager.Singleton.Regions.Count != 1) return "one region only";
        if (IdFormatProblem(request.Id) is { } bad) return bad;
        if (!float.IsFinite(request.Radius) || request.Radius < 1f) return "radius";
        if (request.SegmentPod >= region.Pods.Count) return "segment";

        string art = ArtLayerOf(request), gameplay = GameplayLayerOf(request);
        bool artNew = region.LayerByName(art) < 0, gameplayNew = region.LayerByName(gameplay) < 0;
        if (art == gameplay) return "layers differ";
        if (artNew && region.NameProblem(art) is { } artProblem) return artProblem;
        if (gameplayNew && region.NameProblem(gameplay) is { } gameplayProblem) return gameplayProblem;
        if (!gameplayNew && region.Volumes.Entities.OfType<EntityVolume>().Any(v =>
                v.BaseVolume.Clue is { Class: SpaceMoonClass } && region.LayerOf(v) == gameplay)) return "layer has a moon";
        if (region.Layers.Count + (artNew ? 1 : 0) + (gameplayNew ? 1 : 0) > 64) return "64 layers";

        if (request.Zone != null)
        {
            if (!region.Zones.Contains(request.Zone)) return "zone";
        }
        else
        {
            string zone = request.ZoneName is { Length: > 0 } z ? z : artNew ? art : $"art_{request.Id}";
            if (region.Zones.Count >= 64) return "64 zones";
            if (region.ZoneNameProblem(zone) is { } zoneProblem) return zoneProblem;
        }

        // The Pods and Areas go in the gameplay layer: a layer that is new is last, an existing one must be too.
        var names = new HashSet<string>();
        bool pods = request.NewSegment != null || request.Pods.Count > 0;
        if (!gameplayNew && pods && region.GroupLayerProblem(false, region.LayerByName(gameplay)) is { } podLayer) return podLayer;
        if (!gameplayNew && request.Areas.Count > 0 && region.GroupLayerProblem(true, region.LayerByName(gameplay)) is { } areaLayer) return areaLayer;
        foreach (string name in new[] { request.NewSegment }.Concat(request.Pods.Select(p => p.Name)).OfType<string>())
        {
            string? podName = region.GroupNameProblem(false, name);
            if (podName != null || !names.Add(name)) return $"Pod {name}: {podName ?? "twice"}";
        }
        names.Clear();
        foreach (var area in request.Areas)
        {
            string? areaName = region.GroupNameProblem(true, area.Name);
            if (areaName != null || !names.Add(area.Name)) return $"Area {area.Name}: {areaName ?? "twice"}";
        }
        return null;
    }

    /// <summary>Where the centre's Y goes so the beam stands at the model's beam height.</summary>
    public static float CentreY(MoonSurface source, float radius) =>
        source.Beam.Transform.Translation.Y - (source.Beam.Transform.Translation.Y - source.Tie.Transform.Translation.Y) * (radius / source.Radius);

    /// <summary>Makes the moon (see the class summary) and returns it, or null with <paramref name="problem"/> set. One undo
    /// step: <see cref="MoonCreated.Undo"/>.</summary>
    public static MoonCreated? Create(MoonRequest request, out string? problem)
    {
        var source = request.Source;
        var region = source.Region;
        problem = Problem(request);
        if (problem != null) return null;

        float ratio = request.Radius / source.Radius;
        var origin = source.Tie.Transform.Translation;
        var centre = new Vector3(request.CentreXZ.X, CentreY(source, request.Radius), request.CentreXZ.Y);
        Vector3 At(Entity model) => centre + (model.Transform.Translation - origin) * ratio;

        var newLayers = new List<string>();
        string artLayer = ArtLayerOf(request), gameplayLayer = GameplayLayerOf(request);
        if (region.LayerByName(artLayer) < 0) { region.AddNewLayer(artLayer); newLayers.Add(artLayer); }
        if (region.LayerByName(gameplayLayer) < 0) { region.AddNewLayer(gameplayLayer); newLayers.Add(gameplayLayer); }
        ushort layer = (ushort)region.LayerByName(gameplayLayer);
        bool zoneIsNew = request.Zone == null;
        var zone = request.Zone ?? region.CreateNewZone(request.ZoneName is { Length: > 0 } zoneName ? zoneName
            : newLayers.Contains(artLayer) ? artLayer : $"art_{request.Id}");

        // The surface: the model's Tie, scaled.
        string tieName = source.Tie.Name.Contains(source.Name, StringComparison.OrdinalIgnoreCase)
            ? source.Tie.Name.Replace(source.Name, request.Id, StringComparison.OrdinalIgnoreCase) : request.Id;
        var tie = region.CreateTie(source.Tie.BaseTie, centre, source.Tie.Transform.Rotation, zone, tieName)!;
        var tieTransform = tie.Transform;
        tieTransform.Scale = source.Tie.Transform.Scale * ratio;
        tie.Transform = tieTransform;
        region.Add(tie);

        // Gameplay: the area, the beam, then the Clue that names both.
        var area = region.CreateVolume(source.Area.BaseVolume.Shape, At(source.Area), source.Area.Transform.Rotation, layer, 2f, $"{request.Id}_vol");
        area.SetScale(source.Area.scale * ratio);
        region.Add(area);

        var beam = region.CreateMoby(source.Beam.BaseMoby, At(source.Beam), source.Beam.Transform.Rotation, layer, $"{request.Id}_landing_pad_beam")!;
        var beamTransform = beam.Transform;
        beamTransform.Scale = source.Beam.Transform.Scale;
        beam.Transform = beamTransform;
        region.Add(beam);
        if (request.NewSegment == null && request.SegmentPod >= 0) region.Pods[request.SegmentPod].Add(beam);

        var clueVolume = region.CreateVolume(source.Clue.BaseVolume.Shape, At(source.Clue), source.Clue.Transform.Rotation, layer, 2f, $"{request.Id}_moonclue");
        region.Add(clueVolume);
        region.SetClueClass(clueVolume, new ClassOption(SpaceMoonClass, source.Clue.BaseVolume.Clue!.ClassName, false, source.Clue));
        clueVolume.SetScale(source.Clue.scale * ratio);
        var clue = clueVolume.BaseVolume.Clue!;
        BinaryPrimitives.WriteUInt32BigEndian(clue.Settings.AsSpan(RadiusField), BitConverter.SingleToUInt32Bits(request.Radius));
        clue.References =
        [
            new ClueReference(BeamField, "Moby", beam.Name, beam.GameplayIndex),
            new ClueReference(AreaField, "Volume", area.Name, area.BaseVolume.Index),
        ];

        // The Pods: the segment first (retail's SEGMENT_SS<sector>_<ID>, which also lists a moon's own Mobys), then the others.
        var pods = new List<EntityPod>();
        EntityPod MakePod(string name, bool withBeam)
        {
            var pod = region.CreatePod(name, layer, out var podProblem) ?? throw new InvalidOperationException($"Pod {name}: {podProblem}");
            region.AddPod(pod);
            if (withBeam) pod.Add(beam);
            pods.Add(pod);
            return pod;
        }
        if (request.NewSegment is { } segment) MakePod(segment, true);
        foreach (var pod in request.Pods) MakePod(pod.Name, pod.WithObjects);

        // The Areas; the moon's volume is listed by those that ask for it.
        var areas = new List<NewArea>();
        var areaHandles = new List<int>();
        foreach (var requested in request.Areas)
        {
            var made = region.CreateArea(requested.Name, layer, out var areaProblem) ?? throw new InvalidOperationException($"Area {requested.Name}: {areaProblem}");
            region.AddArea(made);
            areas.Add(made);
            if (requested.WithObjects) areaHandles.Add(made.Handle);
        }
        if (areaHandles.Count > 0) region.SetAreas(area, areaHandles);

        Config.SegmentRegistration? registration = null;
        if (request.NewSegment is { } newSegment && request.RegisterSegment)
        {
            string modelSegment = region.Pods.Select(p => p.Name).FirstOrDefault(n =>
                n.StartsWith("SEGMENT", StringComparison.OrdinalIgnoreCase) && n.EndsWith("_" + source.Name, StringComparison.OrdinalIgnoreCase)) ?? "";
            registration = new Config.SegmentRegistration(newSegment, modelSegment.Length > 0 ? modelSegment : null);
            Config.SegmentRegistry.Add(registration);
        }

        region.InvalidateLookups();
        region.SyncHandles();
        region.RefreshVolumeRoles();

        return new MoonCreated
        {
            Region = region, ArtLayer = artLayer, GameplayLayer = gameplayLayer, Zone = zone, Tie = tie, Area = area, Beam = beam,
            Clue = clueVolume, SegmentPod = request.NewSegment == null ? request.SegmentPod : -1,
            NewLayers = newLayers, ZoneIsNew = zoneIsNew, NewPods = pods, NewAreas = areas, Registration = registration,
        };
    }
}
