namespace ReLunacy.Engine.Rendering.OldEngine;

/// <summary>What is PROVEN about a retail render pass. Everything not named here is Unresolved: only the
/// handler address is known, not what the pass draws.</summary>
public enum TodRenderPassRole
{
    Unresolved,
    SkyDome,
    SkyColorPreShell,
    SkyBloomPreShell,
    SkyClouds,
    SkyColorPostShell,
    SkyBloomPostShell,
    TieShell,
    RecordRelay,
}

/// <summary>One retail render pass: its dispatch index, the handler descriptor stored in the manager's
/// slot table, and the handler's code address.</summary>
public sealed record TodRenderPass(
    int PassId,
    uint HandlerDescriptor,
    uint HandlerCode,
    TodRenderPassRole Role,
    string Notes);

/// <summary>
/// The Tools of Destruction render-pass table, measured at runtime from the Resurgence single-player build
/// (byte-identical to the canonical retail renderer in every address involved).
///
/// The retail manager (BSS 0x10746800) holds a bucket-sorted queue of 16-byte records. Entry <c>i</c> of the
/// 103 x 16-byte entry table at 0x10631B20 is the slice of that queue for pass <c>i</c>, and slot <c>i</c> of the
/// handler table at manager+0x1080 is called with (slice start, slice length in 16-byte units). So the callback
/// id carried by a record IS its pass index, and "callback 52" is pass 52.
///
/// Source of truth: Tools/Reverse/evidence/TOD_Compaction_Dispatch_Probe_V1_2026-09-18.md and the raw log
/// TOD_Compaction_Dispatch_V2_TTY_2026-09-18.log next to it. Only passes that had a handler AND a non-empty slice
/// at a sampled tick are listed (56 of 103); an absent pass is UNOBSERVED, not proven unused.
///
/// This catalog says WHICH CODE a pass runs. It does not say what the pass draws unless Role says so.
/// </summary>
public static class TodRenderPassCatalog
{
    /// <summary>Address of the 103 x 16-byte pass entry table (BSS, static).</summary>
    public const uint EntryTableAddress = 0x10631B20;

    /// <summary>The render-pass manager instance (BSS, static).</summary>
    public const uint ManagerAddress = 0x10746800;

    /// <summary>Offset of the 103-slot handler table inside the manager.</summary>
    public const int HandlerSlotTableOffset = 0x1080;

    public const int PassCount = 103;

    /// <summary>Passes that had queued records at a sampled tick but no handler registered in their slot.</summary>
    public static readonly IReadOnlyList<int> QueuedWithoutHandler = [5, 77, 78];

    private static readonly IReadOnlyDictionary<int, TodRenderPass> Passes =
        new Dictionary<int, TodRenderPass>
        {
            [0] = new(0, 0x00891210, 0x004ED4E4, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [6] = new(6, 0x00892780, 0x005AF0A4, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [7] = new(7, 0x008927D0, 0x005B57E4, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [8] = new(8, 0x00892688, 0x00592F54, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [9] = new(9, 0x008926E0, 0x0059EB4C, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [10] = new(10, 0x00892010, 0x00568FD0, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [11] = new(11, 0x00891F50, 0x0055D190, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [14] = new(14, 0x008924C0, 0x005816B8, TodRenderPassRole.SkyDome,
                "Documented in TOD_Sky_Dome_Colour_2026-09-12 / TOD_Sky_Shell_Blend_2026-09-13."),
            [15] = new(15, 0x008924D0, 0x00583030, TodRenderPassRole.SkyColorPreShell,
                "Documented in TOD_Sky_Shell_Blend_2026-09-13."),
            [16] = new(16, 0x008924D8, 0x00583BD4, TodRenderPassRole.SkyBloomPreShell,
                "Documented in TOD_Sky_Shell_Blend_2026-09-13."),
            [17] = new(17, 0x008924C8, 0x00582178, TodRenderPassRole.SkyClouds,
                "Documented in TOD_Sky_Shell_Blend_2026-09-13."),
            [18] = new(18, 0x008924D0, 0x00583030, TodRenderPassRole.SkyColorPostShell,
                "Documented in TOD_Sky_Shell_Blend_2026-09-13."),
            [19] = new(19, 0x008924D8, 0x00583BD4, TodRenderPassRole.SkyBloomPostShell,
                "Documented in TOD_Sky_Shell_Blend_2026-09-13."),
            [20] = new(20, 0x008927C0, 0x005B4658, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [21] = new(21, 0x008926D0, 0x0059CB80, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [22] = new(22, 0x00891F78, 0x00560760, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [24] = new(24, 0x00892788, 0x005AF7C0, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [26] = new(26, 0x00892690, 0x005936E4, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [27] = new(27, 0x008926E8, 0x0059F378, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [28] = new(28, 0x00891F58, 0x0055DB0C, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [29] = new(29, 0x008927C0, 0x005B4658, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [31] = new(31, 0x00891F78, 0x00560760, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [35] = new(35, 0x00892428, 0x00577A24, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [36] = new(36, 0x00892438, 0x00579480, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [37] = new(37, 0x00891608, 0x00521F98, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [38] = new(38, 0x008915F8, 0x00520CB0, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [39] = new(39, 0x00891600, 0x005213CC, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [40] = new(40, 0x00891618, 0x005237D0, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [41] = new(41, 0x00892718, 0x005A6694, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [43] = new(43, 0x00891EC0, 0x00554B98, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [46] = new(46, 0x00892790, 0x005B0108, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [47] = new(47, 0x00892698, 0x0059404C, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [48] = new(48, 0x00891F60, 0x0055E59C, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [50] = new(50, 0x00892798, 0x005B0D38, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [52] = new(52, 0x008926A8, 0x00598970, TodRenderPassRole.TieShell,
                "Retail TIE render shell (callback52). Records carry the shellFeatureKey in arg2.low8; its producer is still OPEN."),
            [53] = new(53, 0x008926F0, 0x0059FDE4, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [54] = new(54, 0x00891F68, 0x0055F14C, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [57] = new(57, 0x008927C0, 0x005B4658, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [58] = new(58, 0x008926D0, 0x0059CB80, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [59] = new(59, 0x00891F78, 0x00560760, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [61] = new(61, 0x00892440, 0x0057A030, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [62] = new(62, 0x00892430, 0x005786E0, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [79] = new(79, 0x00891218, 0x004EDB88, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [80] = new(80, 0x00891220, 0x004EE71C, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [81] = new(81, 0x00891238, 0x004F0E70, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [89] = new(89, 0x008926B8, 0x0059A348, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [90] = new(90, 0x008926C0, 0x0059B4D8, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [91] = new(91, 0x00892700, 0x005A1134, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [92] = new(92, 0x008926D8, 0x0059DDC4, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [93] = new(93, 0x00891F80, 0x00560F50, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [94] = new(94, 0x00891F88, 0x00561D34, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [95] = new(95, 0x00891F90, 0x00562730, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [97] = new(97, 0x00891478, 0x0050DDC0, TodRenderPassRole.RecordRelay,
                "Registered by 0x004F5338 with the same handler as passes 96 and 100 (0x0050DDC0)."),
            [98] = new(98, 0x00891480, 0x0050DEDC, TodRenderPassRole.RecordRelay,
                "Compaction/relay pass: 32-byte records grouped by their +0x1C flush descriptor, then re-queued to that descriptor. See TOD_Callback52_Compaction_Function_Identity_2026-09-18."),
            [99] = new(99, 0x00891E28, 0x00553E3C, TodRenderPassRole.Unresolved,
                "Handler address observed at runtime; what it draws is not yet reversed."),
            [100] = new(100, 0x00891478, 0x0050DDC0, TodRenderPassRole.RecordRelay,
                "Registered by 0x004F5338 with the same handler as passes 96 and 97 (0x0050DDC0)."),
        };

    public static IReadOnlyDictionary<int, TodRenderPass> All => Passes;

    public static bool TryGet(int passId, out TodRenderPass pass) => Passes.TryGetValue(passId, out pass!);

    /// <summary>The retail TIE shell pass (callback 52).</summary>
    public const int TieShellPassId = 52;
}
