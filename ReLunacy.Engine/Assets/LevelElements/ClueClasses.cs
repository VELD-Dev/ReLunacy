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

    /// <summary>The class's name: the known one, else the one most of <paramref name="instanceNames"/> share,
    /// else its number.</summary>
    public static string Name(uint cls, IEnumerable<string> instanceNames, Games.GameId? game = null)
    {
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
