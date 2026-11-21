namespace ReLunacy.Engine.Importing;

/// <summary>One shader class of A Crack in Time: the byte at shader 0x5000 +0x22, which picks the class's draw handler (QForce
/// table 0x00E7DB18; 0x13 carries embedded programs). Counts are distinct shader TUIDs over the 22 ACiT levels.</summary>
/// <param name="Id">0x5000 +0x22.</param>
/// <param name="Name">What the class draws.</param>
/// <param name="Count">Distinct shaders of the class.</param>
/// <param name="Slots">Slots its shaders use (0 albedo, 1 normal, 2 expensive, 3 detail, 4 / 5 program inputs).</param>
/// <param name="UsedBy">Families that reference its shaders.</param>
/// <param name="Creatable">The editor can make new shaders of the class.</param>
public sealed record ShaderClassInfo(byte Id, string Name, int Count, string Slots, string UsedBy, bool Creatable, string Notes);

/// <summary>One render mode (shader 0x5000 +0x21; QForce Tie builder 0x0073E280, the same set on ACiT).</summary>
public sealed record ShaderRenderModeInfo(byte Id, string Name, ImportAlphaMode? Alpha, int Count);

/// <summary>
/// The shader classes, render modes and texture slots of A Crack in Time, from the 22-level corpus (3056 distinct shaders; script
/// corpus.py / agg.py of the 2026-11-18 research). The class is 0x5000 +0x22, PROVEN as a field: every shader of a class shares
/// its section set and its slot pattern, and only class 0x13 has the embedded program sections 0x33001 / 0x33002 (85 / 85).
/// Lightmapped and overlay (two-layer) are not classes: they are Tie primitive properties (+0x0E bit 0; +0x0A mask, +0x22 second
/// slot), and both layers of an overlay are ordinary class 0x00 shaders (27 of 27 retail overlay shaders, render mode 0).
/// Emissive is not a class either: it is the expensive texture's blue channel.
/// </summary>
public static class ShaderClasses
{
    public const byte Standard = 0x00;

    public static readonly IReadOnlyList<ShaderClassInfo> All =
    [
        new(0x00, "Standard", 2945, "albedo; normal, expensive, detail optional", "Ties 1650, Mobys 1847, UFrags 377",
            true, "All Tie materials, lit or unlit, overlay layers, glows. Sections 5A00 5000 5D00 11300 5200."),
        new(0x04, "Render target", 6, "none or albedo", "Mobys 6", false, "Off-screen and holovision screens; +0x23 0x20."),
        new(0x05, "Fur fins", 5, "albedo", "Mobys 5", false, "Fur fin cards, render mode 6."),
        new(0x0C, "Fur shells", 7, "albedo, normal", "Mobys 7", false, "Shell fur; +0x38 is the shell count."),
        new(0x0E, "Foliage", 6, "albedo, normal, expensive, detail", "none of Ties, Mobys, UFrags", false, "Tree leaves; float +0x40."),
        new(0x0F, "Anisotropic BRDF", 2, "albedo, normal, expensive, detail", "Mobys 2", false, "Zoni brdf shaders; float +0x40."),
        new(0x13, "Embedded program", 85, "albedo; slots 4 and 5 are program inputs", "Ties 19, Mobys 70, UFrags 5", false,
            "Own fragment programs in 0x33001 / 0x33002, selectors 0x33003; slot roles are per program."),
    ];

    /// <summary>Render modes of the standard class (counts over its 2945 shaders).</summary>
    public static readonly IReadOnlyList<ShaderRenderModeInfo> RenderModes =
    [
        new(0, "Opaque", ImportAlphaMode.Opaque, 2353),
        new(1, "Blend, unsorted", null, 119),
        new(2, "Additive, sorted", null, 76),
        new(3, "Blend, pass 0x40", null, 7),
        new(4, "Cut-out", ImportAlphaMode.Cutout, 165),
        new(5, "Opaque (5)", null, 14),
        new(6, "Blend, sorted", ImportAlphaMode.Blend, 211),
    ];

    public static IEnumerable<ShaderClassInfo> Creatable => All.Where(c => c.Creatable);

    public static ShaderClassInfo? Find(byte id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>Retail sampler words per slot of a standard shader (address, CONTROL1, filter), the majority over the corpus:
    /// albedo 0x730101 (gamma on RGB) / 0xA9E4 opaque or 0xAAE4 with alpha; normal 0x31121 / 0x6AE0 (DXT5 X in A, Y in G);
    /// expensive 0x130101 / 0x29E4 (B zero) or 0xA9E4 (B kept); detail 0x31101 (biased) / 0xAAE4, A8R8G8B8.</summary>
    public static (uint Address, uint Control1, uint Filter) DefaultSampler(int slot) => slot switch
    {
        0 => (0x00730101, 0xA9E4, 0x02042000),
        1 => (0x00031121, 0x6AE0, 0x02062000),
        2 => (0x00130101, 0x29E4, 0x02042000),
        3 => (0x00031101, 0xAAE4, 0x02042000),
        _ => (0x00030101, 0xAAE4, 0x02042000),
    };

    /// <summary>The expensive texture's CONTROL1: R and G always; B (emissive) kept or zero; A (detail mask) kept or one. Retail:
    /// 0x29E4 x1501, 0xA9E4 x842, 0x2AE4 x120, 0xAAE4 x16 (the A-kept ones are DXT5, 111 of 136 with a detail map).</summary>
    public static uint ExpensiveControl1(bool emissive, bool detailMask) => (emissive ? 0x8000u : 0) | 0x2800u | (detailMask ? 0x0200u : 0x0100u) | 0xE4u;
}
