namespace ReLunacy.Engine.Assets.LevelElements;

/// <summary>What a Clue volume does in game, as far as it matters to how it is shown.</summary>
public enum ClueHazard
{
    None,
    /// <summary>Kills: ClueDeath, ClueFallToDeath (and ClueDirectionalFallToDeath, 0x128), ClueFallFar.</summary>
    Death,
    Lava,
    Water,
}

/// <summary>
/// Names of the new engine's Clue classes. The EBOOTs of All 4 One and Into the Nexus list every class as
/// "&lt;Name&gt;_CLASS" (ClueFallToDeath_CLASS, ClueDirectionalFallToDeath_CLASS...), but not next to its number. The files carry only the class number (gp_prius 0x25064
/// +0x0C); these names are the ones the level designers' own Clue instances are overwhelmingly called,
/// counted over every level of A Crack in Time, All 4 One and Into the Nexus (e.g. 0x127: 958 of 1,349
/// instances are named ClueFallToDeath, and the class is the same in all three games). A class is not
/// always numbered the same in every game - No Ledge Grab is 0x812 on All 4 One and Into the Nexus and 0x85B
/// on A Crack in Time - so a class missing here is named from the level's own instances instead
/// (<see cref="NameFromInstances"/>).
/// </summary>
public static class ClueClasses
{
    private static readonly Dictionary<uint, string> Known = new()
    {
        [0x11D] = "Base", [0x11E] = "Gravity", [0x120] = "GrindRail", [0x121] = "Wind", [0x122] = "LedgeGrab",
        [0x123] = "Water", [0x126] = "Lava", [0x127] = "FallToDeath", [0x128] = "DirectionalFallToDeath", [0x129] = "Death", [0x12E] = "SwingshotEquip",
        [0x130] = "HudMap", [0x131] = "JumpSlot", [0x4C2] = "SpacePlanet", [0x4D3] = "3DNavPoint", [0x4D4] = "3DNavNetwork",
        [0x4D6] = "SuperSoakerFillUp", [0x4D7] = "Cover", [0x4D9] = "FirePoint", [0x59D] = "SwarmBridge",
        [0x5DC] = "GrindvinePatch", [0x608] = "HeroSpawn", [0x622] = "ShipRoute", [0x62E] = "Camera", [0x62F] = "CameraField",
        [0x69F] = "AzimuthShip", [0x6CA] = "AllyJumpSpot", [0x6CB] = "AllyJump", [0x730] = "AmbientTarget",
        [0x773] = "HBSAmbientMovement", [0x774] = "HBSAmbientAction", [0x7EC] = "Enemy", [0x7EF] = "EnemySetup",
        [0x7F9] = "Crate", [0x7FE] = "Current", [0x812] = "NoLedgeGrab", [0x85B] = "NoLedgeGrab",
        [0x860] = "DuncanHoverBootHack", [0x888] = "LedgeUnbalanced", [0x8A3] = "NavAzimuthBackstab",
        [0x8B6] = "NoWater", [0x947] = "FallFar", [0x961] = "TankGrenade", [0x966] = "NoSwingshot", [0x973] = "AIBot",
        [0x983] = "Wait", [0x98A] = "Respawn", [0x998] = "Darkness", [0x999] = "Light", [0xA28] = "CameraEquation",
        [0xA51] = "Rappel", [0xA8D] = "Swingshot", [0xAED] = "Safety", [0xAF1] = "JetpackNoLand", [0xB2E] = "NoJump",
        [0xB39] = "GrindTubeConstraint", [0xB49] = "HoverbootJump", [0xB8E] = "PDC", [0xBAE] = "Jetpack",
        [0xBC3] = "Space", [0xBF8] = "SpaceMagboots", [0xBF9] = "NavVolume", [0xC0E] = "GravTetherExit",
        [0xC2C] = "NoGravAttach", [0xC32] = "CameraForward", [0xC37] = "Rift", [0xC49] = "CameraExtents",
        [0xC52] = "ChunkPlatformAttach", [0xC5B] = "JetpackTunnel", [0xC5D] = "PlantSounds",
    };

    /// <summary>Classes whose number means something else in the other games, named for A Crack in Time only.</summary>
    private static readonly Dictionary<uint, string> KnownCrackInTime = new()
    {
        // A space sector's moon (and the junk trader's station): where the ship lands (the scripts' space_land_ship
        // target), its landing pad beam, its area and its radius. Found by the user, 2026-11-12.
        [0x4BC] = "SpaceMoon",
    };

    /// <summary>Settings fields with a known meaning, by game, class and offset in the class's struct.</summary>
    private static readonly Dictionary<(Games.GameId Game, uint Class, int Offset), string> KnownFields = new()
    {
        // Read from the decrypted EBOOT (2026-11-12): space_land_ship -> 0x0022BD84 -> 0x00304704(ship, Clue).
        // The ship is put at the beam Moby's position and orientation: it lands exactly on the beam (the user's
        // observation). The beam itself is placed by the level, not by the Clue.
        [(Games.GameId.ACrackInTime, 0x4BC, 0xC0)] = "Landing pad beam (the ship lands on it)",
        // Init (0x002F4BAC) fills these three itself, whatever the file holds: it copies the Mobys of the Clue's own
        // editor layer (gameplay_moon1a...) into a new run-time Pod, leaving out the beam (class 0x72C), and keeps the
        // Zoni (0x5D3) and the gold bolt (0x210) it meets on the way. See docs/reverse/acit_space_sector_classes.md.
        [(Games.GameId.ACrackInTime, 0x4BC, 0xC8)] = "Zoni (run time: found by Init)",
        [(Games.GameId.ACrackInTime, 0x4BC, 0xCC)] = "Gold bolt (run time: found by Init)",
        [(Games.GameId.ACrackInTime, 0x4BC, 0xD8)] = "Pod of the moon's objects (run time: built by Init)",
        // The embedded prompt (+0x28): its text id is one of the EBOOT's SSn_PROMPT_SPACE_LNDONMOONnX, but Init
        // replaces it with GEN_PROMPT_GPLAY_LAND (0x53F); its range is tested by 0x004DEDF0.
        [(Games.GameId.ACrackInTime, 0x4BC, 0x80)] = "Land prompt text id (replaced at Init)",
        [(Games.GameId.ACrackInTime, 0x4BC, 0x8C)] = "Prompt range",
        // SSn_TXT_SPACE_MNxMC in the EBOOT's text table, a different one on each moon.
        [(Games.GameId.ACrackInTime, 0x4BC, 0xD4)] = "Moon name text id",
        // Fixed up with the beam and the area (0x002F30BC); 0 on every retail moon.
        [(Games.GameId.ACrackInTime, 0x4BC, 0xC4)] = "Reference (unused in retail)",
        // 0x00304790: when byte 0 is set, landing sends message 0xD2 to the beam and to the ship (0x0091D990).
        [(Games.GameId.ACrackInTime, 0x4BC, 0xE4)] = "Landing message flags",
        // The moon's area (moon1a_vol...), a sphere of radius 400: the level script (space_sector_1 main.lua) runs a
        // moon's script while Ratchet is inside its table_moons[i].vol. Not the gravity range (the user's test,
        // 2026-11-12). It is also where the moon's name and stats (Zoni...) are shown, the area growing with the volume
        // (user, 2026-11-12). The Clue itself is the ship's landing target: moons_init.lua space_land_ship(aphelion, clue).
        [(Games.GameId.ACrackInTime, 0x4BC, 0xD0)] = "Moon area",
        // The moon's surface radius: 50 on the large moons, 25 on the small ones; every Tie and volume placed on
        // moon1a (bunker, landing pad, spires, the 22 moon_ap*_vol) stands 50 from its centre, its volcanic_moon Tie.
        // The moon map's orbiting camera (R3 on a moon) keeps its distance from it: a larger radius flies the camera
        // higher (user's test, 2026-11-13).
        [(Games.GameId.ACrackInTime, 0x4BC, 0xE0)] = "Moon radius (also the moon map camera's distance)",

        // SpacePlanet, a destination the ship lands on to load another level (Init 0x002F61D4, Update 0x002F5670).
        [(Games.GameId.ACrackInTime, 0x4C2, 0x88)] = "Land prompt text id (replaced at Init)",
        [(Games.GameId.ACrackInTime, 0x4C2, 0x8C)] = "Prompt volume",
        [(Games.GameId.ACrackInTime, 0x4C2, 0x94)] = "Prompt range",
        // The EBOOT's LEVEL enum: 2 Zolar Forest, 4 Vorselon's ship, 7 Molonoth, 8 Axiom City, 11 Krell Canyon,
        // 12 Agorian Arena, 13 Tombli, 16 Valkyrie Fleet, 17 Nefarious Station, 19 Gimlick Valley, 21 the museum.
        [(Games.GameId.ACrackInTime, 0x4C2, 0xC8)] = "Destination level",
        [(Games.GameId.ACrackInTime, 0x4C2, 0xCC)] = "Planet name text id",
        [(Games.GameId.ACrackInTime, 0x4C2, 0xD0)] = "Barrier name text id",
        [(Games.GameId.ACrackInTime, 0x4C2, 0xD4)] = "Location volume (the ship can land inside it)",
        [(Games.GameId.ACrackInTime, 0x4C2, 0xD8)] = "Barrier outer volume",
        [(Games.GameId.ACrackInTime, 0x4C2, 0xDC)] = "Barrier inner volume",
        // A u16: 0 none, 1 Terachnos, 2 Tombli, 3 the Nefarious station.
        [(Games.GameId.ACrackInTime, 0x4C2, 0xF0)] = "Barrier type",

        // The landing pad beam (fix-up 0x002F3004).
        [(Games.GameId.ACrackInTime, 0x72C, 0x80)] = "Take-off volume",
        [(Games.GameId.ACrackInTime, 0x72C, 0x84)] = "Respawn volume (junk trader stations)",
    };

    /// <summary>A Crack in Time's ClueGravity, which SpaceMoon derives from: a region with a gravity of its own.</summary>
    private const uint CrackInTimeGravity = 0x11E;
    /// <summary>The role of a plain volume a gravity Clue acts in instead of its own (ClueGravity +0x14, +0x18).</summary>
    public const string GravityRegionRole = "Gravity region";

    /// <summary>Gives the volumes a known Clue field names their role (<see cref="Volume.Role"/>): a moon's
    /// area. Only links that resolve count; nothing is guessed from names. Also marks where each gravity Clue acts
    /// (<see cref="Volume.GravityRegion"/>).</summary>
    public static void AssignVolumeRoles(IReadOnlyList<Volume> volumes, Games.GameId? game)
    {
        // By place in the table: volume names repeat, and a reference means one of the namesakes.
        var byIndex = volumes.Where(v => v.Index >= 0).ToLookup(v => v.Index);
        foreach (var volume in volumes)
            if (volume.Clue is { } clue)
            {
                foreach (var reference in clue.References)
                {
                    if (reference.Kind != "Volume" || game is not { } g
                        || !KnownFields.TryGetValue((g, clue.Class, reference.Offset), out var field)) continue;
                    foreach (var target in byIndex[reference.Index])
                    {
                        target.Role = field;
                        target.RoleSource = $"{clue.Name} ({clue.ClassName} +0x{reference.Offset:X})";
                    }
                }
                if (game == Games.GameId.ACrackInTime && ClassFields.IsA(game, clue.Class, CrackInTimeGravity))
                    MarkGravityRegion(volume, clue, volumes, byIndex);
            }
    }

    /// <summary>
    /// Where a gravity Clue acts, as the game finds it (EBOOT 0x004F076C): the volumes of its Area (+0x18) when one is
    /// set, else its volume reference (+0x14), else the Clue's own volume - a moon's case, every SpaceMoon leaving
    /// both unset. Inside it, in the air, "up" points away from the volume's centre.
    /// </summary>
    private static void MarkGravityRegion(Volume own, VolumeClue clue, IReadOnlyList<Volume> volumes, ILookup<int, Volume> byIndex)
    {
        var targets = new List<Volume>();
        if (clue.References.FirstOrDefault(r => r.Offset == 0x18 && r.Kind == "Area") is { } area)
            targets.AddRange(volumes.Where(v => v.Areas.Contains(area.Name)));
        else if (clue.References.FirstOrDefault(r => r.Offset == 0x14 && r.Kind == "Volume") is { } volume)
            targets.AddRange(byIndex[volume.Index]);
        if (targets.Count == 0) { own.GravityRegion = true; return; }
        foreach (var target in targets)
        {
            target.GravityRegion = true;
            if (target.Clue != null) continue;
            target.Role = GravityRegionRole;
            target.RoleSource = $"{clue.Name} ({clue.ClassName})";
        }
    }

    /// <summary>A volume role's colour (see <see cref="Volume.Role"/>).</summary>
    public static System.Numerics.Vector3 RoleColour(string role) => role switch
    {
        "Moon area" => new(0.62f, 0.4f, 1f),
        GravityRegionRole => new(0.3f, 0.85f, 1f),
        _ => ColourOf(role),
    };

    /// <summary>Settings fields that are a radius around the Clue's volume (a float), with the colour their preview
    /// sphere is drawn in: the editor shows them live, so editing one shows its effect at once.</summary>
    private static readonly Dictionary<(Games.GameId Game, uint Class), (int Offset, System.Numerics.Vector3 Colour)[]> RadiusFields = new()
    {
        [(Games.GameId.ACrackInTime, 0x4BC)] = [(0xE0, new(1f, 0.6f, 0.15f))], // SpaceMoon: the moon's radius
    };

    /// <summary>The radius fields of a class (see <see cref="RadiusFields"/>); empty for most.</summary>
    public static IReadOnlyList<(int Offset, System.Numerics.Vector3 Colour)> RadiiOf(Games.GameId? game, uint cls) =>
        game is { } g && RadiusFields.TryGetValue((g, cls), out var fields) ? fields : [];

    /// <summary>
    /// Instances known to break the game when edited, by game and name: the editor warns on them and a save lists
    /// them. Each one is a finding, with what it did.
    /// </summary>
    private static readonly (Games.GameId Game, string Name, string Effect)[] KnownBreaking =
    [
        // None known. (moon_ap00_vol1 was listed on 2026-11-12: moving it seemed to hang space_sector_1's load; the cause
        // was the game's stale disk cache - see Packing.GameCaches - and saves now clear it.)
    ];

    /// <summary>What editing this instance is known to do to the game, or null.</summary>
    public static string? KnownBreakingEffect(Games.GameId? game, string? name) =>
        game is { } g && name != null && Array.Find(KnownBreaking, k => k.Game == g && k.Name == name) is { Effect: { } effect } ? effect : null;

    /// <summary>The name of the settings field at <paramref name="offset"/> of a Clue (or controller) class, when known.</summary>
    public static string? FieldName(Games.GameId? game, uint cls, int offset) =>
        game is { } g && KnownFields.TryGetValue((g, cls, offset), out var name) ? name
        : ClassFields.At(game, cls, offset) is { Name.Length: > 0 } field ? field.Name : null;

    /// <summary>The class's name: the known one, else the one most of <paramref name="instanceNames"/> share,
    /// else its number.</summary>
    public static string Name(uint cls, IEnumerable<string> instanceNames, Games.GameId? game = null)
    {
        if (game == Games.GameId.ACrackInTime && KnownCrackInTime.TryGetValue(cls, out var crackInTime)) return crackInTime;
        // The game's own class table first, when it has one (All 4 One, QForce), without the "Clue" prefix.
        if (GameClassNames.Name(game, cls) is { } official)
            return official.StartsWith("Clue", StringComparison.Ordinal) && official.Length > 4 ? official[4..] : official;
        if (Known.TryGetValue(cls, out var known)) return known;
        var names = instanceNames.ToList();
        string? name = NameFromInstances(names);
        // Only a "Clue..." name is a class name; anything else is one designer's instance, a hint at best.
        bool clueNamed = names.Any(n => n.StartsWith("Clue", StringComparison.OrdinalIgnoreCase));
        return name == null ? $"0x{cls:X}" : clueNamed ? name : $"0x{cls:X} ({name})";
    }

    /// <summary>The name most of a class's instances share once their numbering is dropped, without its
    /// "Clue" prefix (ClueFallToDeath8 -> FallToDeath); null when there are none.</summary>
    public static string? NameFromInstances(IEnumerable<string> instanceNames)
    {
        var best = instanceNames
            .Select(n => n.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '_'))
            .Where(n => n.Length > 0)
            .GroupBy(n => n)
            .OrderByDescending(g => g.Key.StartsWith("Clue", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(g => g.Count())
            .FirstOrDefault()?.Key;
        if (best == null) return null;
        return best.StartsWith("Clue", StringComparison.OrdinalIgnoreCase) && best.Length > 4 ? best[4..].TrimStart('_') : best;
    }

    /// <summary>The colour a Clue class is drawn in: red for what kills, orange lava, blue water, and a
    /// colour of its own, from its name, for every other class.</summary>
    public static System.Numerics.Vector3 Colour(string className) => Hazard(className) switch
    {
        ClueHazard.Death => new(1f, 0.12f, 0.08f),
        ClueHazard.Lava => new(1f, 0.55f, 0f),
        ClueHazard.Water => new(0.2f, 0.55f, 1f),
        _ => ColourOf(className),
    };

    /// <summary>A stable, saturated colour for a name (layers, unclassified Clue classes).</summary>
    public static System.Numerics.Vector3 ColourOf(string name)
    {
        uint hash = 2166136261;
        foreach (char c in name) hash = (hash ^ c) * 16777619;
        float h = (hash % 360) / 60f, sat = 0.55f + (hash >> 9 & 31) / 155f, v = 0.95f;
        float c1 = v * sat, x = c1 * (1 - MathF.Abs(h % 2 - 1)), m = v - c1;
        var (r, g, b) = (int)h switch { 0 => (c1, x, 0f), 1 => (x, c1, 0f), 2 => (0f, c1, x), 3 => (0f, x, c1), 4 => (x, 0f, c1), _ => (c1, 0f, x) };
        return new(r + m, g + m, b + m);
    }

    public static ClueHazard Hazard(string className) =>
        className is "Death" or "FallToDeath" or "DirectionalFallToDeath" or "FallFar" ? ClueHazard.Death
        : className == "Lava" ? ClueHazard.Lava
        : className == "Water" ? ClueHazard.Water
        : ClueHazard.None;
}
