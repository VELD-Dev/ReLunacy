namespace ReLunacy.Engine.Importing;

/// <summary>A collision material word as the importer UI offers it. The meaning of its bytes is not decoded, so the name is the
/// word itself; <see cref="Example"/> says which retail surfaces use it.</summary>
public sealed record CollisionMaterialChoice(uint Word, string Name, int RetailShapes, string Example);

/// <summary>
/// The palette words common in A Crack in Time collision (counted over the unique shapes of space_sector_1 and krell_canyon,
/// colcookprobe; shapes = chunks that list the word in their palette). A chunk's palette holds at most 4 words; the per-triangle
/// material byte indexes it (docs/reverse/acit_collision_format.md section 4).
/// </summary>
public static class CollisionMaterials
{
    /// <summary>The word retail uses for most surfaces (moons, asteroid fields, junk-trader platforms).</summary>
    public const uint Default = 0x00000000;

    public static IReadOnlyList<CollisionMaterialChoice> Common { get; } =
    [
        new(0x00000000, Name(0x00000000), 921, "most moons, asteroid fields, krell platforms"),
        new(0x00102314, Name(0x00102314), 203, "krell canyon rock walls and platforms"),
        new(0x0009120E, Name(0x0009120E), 158, "habitable and volcanic moons"),
        new(0x000B1B0F, Name(0x000B1B0F), 30, "krell main building, door frames"),
        new(0x00102214, Name(0x00102214), 15, "krell rocks, zolar tower"),
        new(0x00020304, Name(0x00020304), 15, "krell low platform, rocky transitions"),
        new(0x000B1A0F, Name(0x000B1A0F), 10, "molonoth barrels and buildings"),
        new(0x000A1C0F, Name(0x000A1C0F), 7, "krell canyon fan, destroyed building"),
        new(0x000A1A0F, Name(0x000A1A0F), 7, "grind rail supports, shanties"),
        new(0x000A190F, Name(0x000A190F), 5, "hoses, mining pipes"),
        new(0x00040406, Name(0x00040406), 5, "webtacle frames and barnacles"),
        new(0x000B160F, Name(0x000B160F), 5, "krell destroyed building"),
        new(0x00060D0A, Name(0x00060D0A), 3, "grassy rocky transitions"),
        new(0x000B190F, Name(0x000B190F), 2, "refinery, door frame front"),
        new(0x000A1B0F, Name(0x000A1B0F), 2, "shanties"),
        new(0x000B180F, Name(0x000B180F), 2, "mining cart tracks"),
        new(0x00180000, Name(0x00180000), 2, "rock pillars"),
        new(0x0000160F, Name(0x0000160F), 2, "giant hose"),
        new(0x00001A0F, Name(0x00001A0F), 2, "hose, junk pile"),
        new(0x0009121B, Name(0x0009121B), 0, "ice moons (space sectors 2 to 5, not in the counted levels)"),
    ];

    public static string Name(uint word) => $"Material 0x{word:X8}";

    /// <summary>The choice for a word, or an unnamed one when it is not in <see cref="Common"/>.</summary>
    public static CollisionMaterialChoice Get(uint word) =>
        Common.FirstOrDefault(c => c.Word == word) ?? new CollisionMaterialChoice(word, Name(word), 0, "");
}
