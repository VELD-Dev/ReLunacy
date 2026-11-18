// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Numerics;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>
/// The kind of an effect piece: the index of the 0x300 list that holds it, and the type byte an
/// FxDefFX entry uses to point at it. The EBOOT's load-time binder (QForce 0x007DCBF8) dispatches on
/// 16 values; QForce, All 4 One, Into the Nexus and A Crack in Time only ever ship four of them
/// (PROVEN on 55 levels). The mapping of 1 and 2 to Quad and Lite is STRONG: the record sizes and key
/// headers match FxDefQuad/FxQuadKeys (80 bytes of keys) and FxDefLite/FxLiteKeys (84 bytes).
/// Tools of Destruction keeps one list per emitter class instead (<see cref="OldEngineEffectTypes"/>); its
/// emitters are all read as <see cref="Emitter"/> and keep their list in <see cref="EffectEmitter.OldEngineType"/>.
/// </summary>
public enum EffectPieceType : byte
{
    Package = 0,
    Quad = 1,
    Light = 2,
    Emitter = 13,
}

/// <summary>Blend byte of an FxTexture (+0x16). INFERRED from 12 QForce levels: 1 goes with DXT1 (no alpha)
/// 9 581 times against 2 309, 0 with DXT5 3 032 against 506 - additive glows versus alpha-blended smoke.
/// The runtime compares it with 3 (0x007E4EA4); what 3 selects is OPEN.</summary>
public enum EffectBlend : byte
{
    Alpha = 0,
    Additive = 1,
    Mode2 = 2,
    Mode3 = 3,
}

/// <summary>
/// How a sprite is blended into the frame, in the order of Tools of Destruction's table: the ToD FxTexture byte
/// +0x0C indexes the render-state words at fxvis LS 0x22150 (0x000D2300, 0x000D0100, 0x00050500, 0x00010000,
/// 0x00050200, 0x00050400; emitter create 0x0FDCC), whose bits 8..12 select an entry of the EBOOT blend table
/// 0x100EBAE4 that the state setter 0x00611E18 sends to the RSX (0x310 / 0x314 / 0x320). PROVEN for ToD; QForce maps
/// its own blend byte to Alpha / Additive only (<see cref="EffectBlend"/>, INFERRED).
/// </summary>
public enum EffectSpriteBlend : byte
{
    /// <summary>SRC_ALPHA, 1 - SRC_ALPHA.</summary>
    Alpha = 0,
    /// <summary>SRC_ALPHA, ONE.</summary>
    Additive = 1,
    /// <summary>Reverse subtract: destination - colour x alpha.</summary>
    Subtractive = 2,
    /// <summary>ONE, ZERO (blending off).</summary>
    Opaque = 3,
    /// <summary>ZERO, SRC_COLOR.</summary>
    Multiply = 4,
    /// <summary>ONE, 1 - SRC_COLOR.</summary>
    Screen = 5,
}

/// <summary>
/// One FxTexture (28 bytes; 8 of them + a count make an FxTexs). Structure PROVEN: the EBOOT walks
/// <c>count</c> of them at stride 0x1C (0x007DCD58) and the class size is 28. The first u16 of a handle
/// is <c>0x8000 | index</c> into vfx_system_header.dat's 0x5200 table (STRONG: on all 15 QForce levels the
/// largest index is exactly the table's count - 1 and none is out of range); 0xFFFF = empty slot.
///
/// Tools of Destruction's FxTexture is 16 bytes (<see cref="OldEngine"/>): four u16 handles, a float at +0x08 and
/// four flag bytes at +0x0C. A handle is <c>0x8000 | index</c> into the level's <c>system.tph</c>/<c>system.tp</c>
/// (on every one of the 23 readable levels the indices are exactly 0..count-1 - metropolis: 48 handles, 48
/// textures), or, without the bit, an index into the game's own <c>processed/texpacks/effect.tph</c> (69
/// textures; the same 14-16 ids 10..73 recur in every level). <see cref="GlobalBase"/> is where that second pack
/// starts in the combined <see cref="VfxTextureSet"/>. STRONG (counts), the packs are read by the new-engine reader
/// unchanged (same 0x5200 / 0x5300 / 0x5A00 sections).
/// </summary>
public sealed class EffectTexture
{
    public required int FieldOffset { get; init; }
    /// <summary>The four u32 handle words at +0x00..+0x0C, raw (new engine); the four u16 handles (old engine).</summary>
    public required uint[] Handles { get; init; }
    /// <summary>+0x10, 1.0 on almost every emitter (compared by the render setup at 0x007E4E88). OPEN.
    /// Old engine: +0x08 (0 or 1).</summary>
    public float Word10 { get; init; }
    /// <summary>+0x14..+0x17 raw; +0x16 is the blend byte. Old engine: +0x0C..+0x0F; +0x0C is the blend (0, 1, 2, 5 in
    /// retail: <see cref="EffectSpriteBlend"/>), +0x0D / +0x0E go into the emitter state word +0x88 as bits 0-1 and 2-3
    /// (0x0FD4C: equal on nearly every texture - U/V addressing, INFERRED). PROVEN (ToD fxvis 0x0FC8C..0x0FDEC).</summary>
    public uint Flags14 { get; init; }
    public uint Word18 { get; init; }
    /// <summary>A Tools of Destruction FxTexture: 16-bit handles, two texture packs.</summary>
    public bool OldEngine { get; init; }
    /// <summary>Old engine: index of the global effect pack's first texture in the level's combined set (-1 = not loaded).</summary>
    public int GlobalBase { get; set; } = -1;

    public EffectBlend Blend => OldEngine ? (EffectBlend)(Flags14 >> 24) : (EffectBlend)((Flags14 >> 8) & 0xFF);

    /// <summary>The blend the sprite is drawn with: ToD's +0x0C through its state table (6+ index zero words: opaque),
    /// QForce's blend byte as additive or alpha.</summary>
    public EffectSpriteBlend SpriteBlend => OldEngine
        ? (Flags14 >> 24) is var b && b <= 5 ? (EffectSpriteBlend)b : EffectSpriteBlend.Opaque
        : Blend == EffectBlend.Additive ? EffectSpriteBlend.Additive : EffectSpriteBlend.Alpha;

    /// <summary>Index of the primary texture in the level's VFX texture table, or -1.</summary>
    public int PrimaryTextureIndex => TextureIndex(0);

    /// <summary>Old engine: the composite of this texture's layers in the level's set (Smoke, Flame), -1 = none.</summary>
    public int CompositeIndex { get; set; } = -1;

    /// <summary>What a sprite is drawn with: the composite when there is one, else the primary texture.</summary>
    public int DrawTextureIndex => CompositeIndex >= 0 ? CompositeIndex : PrimaryTextureIndex;

    public int TextureIndex(int handle)
    {
        if (OldEngine)
        {
            uint h = Handles[handle];
            if (h == 0xFFFF) return -1;
            if ((h & 0x8000) != 0) return (int)(h & 0x7FFF);
            return GlobalBase >= 0 ? GlobalBase + (int)h : -1;
        }
        uint word = Handles[handle];
        ushort hi = (ushort)(word >> 16);
        if (hi == 0xFFFF || (hi & 0x8000) == 0) return -1;
        return hi & 0x7FFF;
    }
}

/// <summary>
/// Tools of Destruction's effect piece lists. The EBOOT names them itself (0x005FCDC0, a switch over the list type that
/// returns these strings): PROVEN. Its package binder (0x005FE8F4) copies each piece with the size below and skips
/// Decal, Function and MoveFacing (none ships in the 23 readable levels). The class number is the new engine's for the
/// same name (1 Cam, 2 Beam, 3 Streak, 4 Smoke, 5 Flame, 6 Distort, 7 Trail, 8 Swipe), so the simulator and renderer
/// treat a ToD Beam like a QForce one.
/// </summary>
public static class OldEngineEffectTypes
{
    public const int Package = 0, Quads = 1, Lites = 2, Decal = 3, Function = 4, CamFacing = 5, Beam = 6, Streak = 7,
        MoveFacing = 8, Smoke = 9, Flame = 10, Distort = 11, Trail = 12, Swipe = 13;

    private static readonly string[] Names =
    [
        "Package", "Quads", "Lites", "Decal", "Function", "Emt-CamFacing-TurbAccel", "Emt-Beam-TurbAccel",
        "Emt-Streak-TurbAccel", "Emt-MoveFacing-TurbAccel", "Emt-Smoke-TurbAccel", "Emt-Flame-TurbAccel",
        "Emt-Distort-TurbAccel", "Emt-Trail-TurbAccel", "Emt-Swipe-TurbAccel",
    ];

    /// <summary>The EBOOT's own name of a list type ("UNKNOWN" past 13).</summary>
    public static string Name(int type) => (uint)type < (uint)Names.Length ? Names[type] : "UNKNOWN";

    public static bool IsEmitter(int type) => type is >= CamFacing and <= Swipe and not MoveFacing;

    /// <summary>Record size the binder copies (0x005FE8F4 jump table), -1 for a type it skips.</summary>
    public static int RecordSize(int type) => type switch
    {
        Package => 0xA8, Quads => 0xE0, Lites => 0xDC,
        CamFacing or Smoke or Trail => 0x174, Beam => 0x16C, Streak => 0x190, Flame => 0x178, Distort => 0x17C, Swipe => 0x180,
        _ => -1,
    };

    /// <summary>The new engine's emitter class for the same name (FxDefEmt*Turb declaration order).</summary>
    public static int EmitterClass(int type) => type switch
    {
        CamFacing => 1, Beam => 2, Streak => 3, Smoke => 4, Flame => 5, Distort => 6, Trail => 7, Swipe => 8, _ => 0,
    };

    /// <summary>Beam and Streak have no particle spin track (9 key headers, no +0x80), so every block after the keys
    /// sits 0x0C earlier. PROVEN: 0x005FE1EC copies the tenth header for the other six types only, and the per-type
    /// preparation (0x006013B8) hands 0x00600254 +0x10C/+0x110 for these two and +0x118/+0x11C for the rest.</summary>
    public static bool HasSpin(int type) => type is not (Beam or Streak);
}

/// <summary>Common part of every piece: where it lives in the file and its raw record.</summary>
public abstract class EffectPiece
{
    public required EffectPieceType Type { get; init; }
    /// <summary>Index inside its type's 0x300 list - what an FxDefFX entry names.</summary>
    public required int Index { get; init; }
    public required int FileOffset { get; init; }
    public required byte[] Raw { get; init; }

    public string Label => $"{Type} {Index}";
}

/// <summary>
/// FxDefEmitter (932 bytes on QForce/All 4 One/Into the Nexus, 896 on A Crack in Time): a particle
/// emitter. Layout, see Docs/reverse/NEW_ENGINE_EFFECTS_2026-09-25.md:
/// <code>
///   +0x000  FxEmtKeys (212): spawn rate/life/speed/radius, emitter-time colour/alpha/alpha-test/scale,
///           particle-life colour/alpha/alpha-test/scale, rotation - 13 key headers, pointer positions PROVEN
///   +0x0D4  FxEmtShaders (332): FxTexs (8 x FxTexture + count at +0x1B4) and four shader selector
///           bytes at +0x1CC/+0x1CE/+0x1D0/+0x1D2 (only +0x1D2 is non-zero in retail)
///   +0x1D8  distortion keys, bound only for shaders 7 and 13 (EBOOT table 0x00F2EFA4)
///   +0x220  controls - mostly OPEN, see the doc; +0x328 FxEmtSpawnStyleControl (spawn generator, cone) and
///           +0x338 FxEmtTurbAccelDirControl (motion) PROVEN from the SPU code (NEW_ENGINE_FX_SPU_2026-09-26.md)
///   +0x36C  runtime scratch (zero in every file; the EBOOT writes it at load)
/// </code>
/// </summary>
public sealed class EffectEmitter : EffectPiece
{
    public required IReadOnlyList<EffectScalarTrack> ScalarTracks { get; init; }
    public required IReadOnlyList<EffectColorTrack> ColorTracks { get; init; }
    public required IReadOnlyList<EffectTexture> Textures { get; init; }
    /// <summary>FxTexs count byte: how many of the 8 FxTextures are live (1 on 16 131 of 16 423 QForce emitters).</summary>
    public int TextureCount { get; init; }
    /// <summary>The shader selector byte (+0x1D2 on QForce). 0, 3, 5, 7, 9, 10 dominate.</summary>
    public int ShaderId { get; init; }
    /// <summary>
    /// The emitter class the EBOOT derives from the shader selectors at load (0x007DB360): 1..8, written to
    /// +0x314, and the switch at 0x007DE9E0 sizes each class differently. 9 means the selectors disagree
    /// and the emitter is disabled. PROVEN table (0x00F2EF6C): shader 0,1,2,7,10,13 -> 1; 3 -> 2; 4 -> 3;
    /// 5 -> 4; 6 -> 5; 8,11 -> 6; 9 -> 7; 12 -> 8.
    /// </summary>
    public int EmitterClass { get; init; }
    /// <summary>A Crack in Time maps shader 12 to class 0, which its binder never runs (table 0x00DB49B0). PROVEN.</summary>
    public bool Disabled { get; init; }
    /// <summary>Shaders 7 and 13 set the distortion flag (+0x1E8) and bind the keys at +0x1D8. PROVEN.</summary>
    public bool IsDistortion { get; init; }

    /// <summary>Tools of Destruction: the piece list the emitter comes from (<see cref="OldEngineEffectTypes"/>: 5 CamFacing,
    /// 6 Beam, 7 Streak, 9 Smoke, 10 Flame, 11 Distort, 12 Trail, 13 Swipe), which is its class there. 0 on the new engine.</summary>
    public int OldEngineType { get; init; }
    /// <summary>Tools of Destruction FxEmtLifeControl (28 bytes, +0x118 / +0x10C on Beam and Streak): +0x00 the emitter's
    /// period, defaulted at load from +0x04 when 0, and five per-track periods that become the emitter-time tracks' header
    /// word when theirs is 0 (0x00600254, PROVEN). That +0x00 is how long the emitter emits is INFERRED.</summary>
    public float OldPeriod { get; init; }
    public float OldPeriodDefault { get; init; }

    /// <summary>+0x324 (QForce layout): emission duration in seconds, 0 = continuous. INFERRED: 0.1 on 8 430 of
    /// 16 423 emitters (impact bursts), 0 on 6 082; the load-time binder tests it against 1e-4.</summary>
    public float Duration { get; init; }
    /// <summary>+0x25C..+0x264: a unit vector, (0,0,1) on 16 402 emitters, normalized at load for class 6. NOT the
    /// emission axis: the spawn overlays tilt around the emitter frame's own +Z (FXVis 0x52C8 builds that frame
    /// from the instance matrix; none of the 61 overlays reads this field). OPEN (render packet).</summary>
    public Vector3 Direction { get; init; }
    /// <summary>+0x290/+0x291 bytes: 180/180 on 16 422 of 16 423 emitters, 120/120 on one. Once read as the
    /// spread; the spread is <see cref="ConeMinDegrees"/>/<see cref="ConeMaxDegrees"/>. OPEN.</summary>
    public float SpreadDegrees { get; init; }

    /// <summary>
    /// +0x328 (FxEmtSpawnStyleControl, 8 bytes): the spawn generator, an INDEX into 61 SPU overlays (0..0x3C), not a
    /// set of flags. PROVEN: the PPU copies it to the SPU record byte 4 (0x007E3D60 via the +0x328 pointer
    /// 0x00B59D50 passes), FXVis loads overlay [byte] from the FX globals +0x1640 table and runs it for every batch
    /// of 8 new particles. What each one draws: <see cref="EffectSpawnStyles"/> (measured by running the retail
    /// overlays). The cone bytes follow at +0x329/+0x32A.
    /// </summary>
    public int SpawnStyle { get; init; }
    /// <summary>+0x329 / +0x32A: cone tilt range in degrees from the emitter's +Z, min / max (0, 5, 10, 20, 45, 90,
    /// 120, 180). PROVEN: SPU record bytes 5/6, unpacked to float at FXVis 0x35B0; the cone overlays draw the tilt
    /// uniformly in [min, max] (0x24 TiltSigned uses [-min, max]).</summary>
    public int ConeMinDegrees { get; init; }
    public int ConeMaxDegrees { get; init; }
    public EffectSpawnStyleInfo Spawn => EffectSpawnStyles.Describe(SpawnStyle, OldEngineType > 0);
    /// <summary>+0x32C: float, 0 on 16 408 emitters. Scales the instance velocity added to every new particle's
    /// velocity (FXVis 0x6D68: instance +208 x emitter state +160 x 0x1C210). Use PROVEN, "inherit emitter
    /// velocity" meaning INFERRED.</summary>
    public float SpawnInheritVelocity { get; init; }

    /// <summary>
    /// +0x338 (FxEmtTurbAccelDirControl, 32 bytes): the particles' motion. PROVEN from the SPU code (FXVis 0x52C8
    /// builds the per-frame coefficients, the class overlay's update block applies them, e.g. A_14 0xF90):
    /// <code>
    ///   p += v dt  (+ global wind x Wind x dt)
    ///   v += axis(Mode) x Acceleration x dt
    ///   s = |v|, d = v / s
    ///   s = clamp((s + Thrust dt) x exp(-Drag dt), SpeedMin, SpeedMax)     (exp verified by running 0x5298)
    ///   d = normalize(d + random unit x Turbulence)                         (every game frame, not scaled by dt)
    ///   v = d s
    /// </code>
    /// </summary>
    public int MotionMode { get; init; }
    /// <summary>+0x33C: acceleration along <see cref="MotionMode"/>'s axis, units/s² (0, 10, 1, 2.5, 9.8...).</summary>
    public float Acceleration { get; init; }
    /// <summary>+0x340: speed gained per second along the particle's own direction (0 on 15 753).</summary>
    public float Thrust { get; init; }
    /// <summary>+0x344: per-frame direction noise (0.1, 0.05, 1...).</summary>
    public float Turbulence { get; init; }
    /// <summary>+0x348: drag, speed x exp(-Drag dt) (non-zero on every fast emitter: 30, 10, 1, 20, 5...).</summary>
    public float Drag { get; init; }
    /// <summary>+0x34C / +0x350: speed clamp (0 and 1024 on every retail emitter).</summary>
    public float SpeedMin { get; init; }
    public float SpeedMax { get; init; } = 1024f;
    /// <summary>+0x354: share of the FX globals' wind vector (+0x1480) added to the position (0.1/0.2/0.5 on 117).</summary>
    public float Wind { get; init; }

    /// <summary>
    /// The acceleration axis of <see cref="MotionMode"/>. PROVEN (0x007E3D60 maps the byte through the table at
    /// 0x00F2EBC8 = 0,1,2,3,4,5,16..21; FXVis 0x17000, run in spuemu for all 12 values): 0 +Z, 1 -Z, 2 +Y, 3 -Y,
    /// 4 +X, 5 -X of the emitter frame, 6..11 the same of the world (raw 9, 90 % of emitters = world -Y: gravity;
    /// raw 8 = world +Y: buoyancy). The new-engine world is Y-up. False for a byte past 11.
    /// </summary>
    public bool AccelerationAxis(out Vector3 axis, out bool world) => EffectSpace.ModeAxis(MotionMode, out axis, out world);

    /// <summary>+0x1EC, FxEmtBeamControl's first float: the beam's MAXIMUM length, in quarter units (100/20/10/50... on
    /// beam emitters, 1 elsewhere). See <see cref="BeamMaxLength"/>.</summary>
    public float BeamStretch { get; init; }
    /// <summary>+0x1F0, FxEmtBeamControl's second float: the beam's MINIMUM length, quarter units (0 on 5 400 of the 5 623
    /// distinct beam emitters of QForce, All 4 One and Into the Nexus; 16/16, 8/8... = a fixed-length beam).</summary>
    public float BeamMinStretch { get; init; }
    /// <summary>+0x1F4, FxEmtBeamControl's third float: how fast the beam's tail catches up with its head, per second
    /// (0 on nearly every beam: the tail stays where the particle was born).</summary>
    public float BeamFollowRate { get; init; }

    /// <summary>
    /// The beam's length bounds and tail rate as the SPU receives them. PROVEN, both sides read: the PPU packs
    /// +0x1EC and +0x1F0 as u16 x 256 (saturated at 0xFFFF) and +0x1F4 as s16 x 1024 into the render packet +0x98 /
    /// +0x9A / +0x9C (0x007E4F30), and FXVis's type-1 unpacker (LS 0x2400) turns the first two back with /64 - so the
    /// SPU works with FOUR times the file's values, at most 1 024 units - and the third with /1024.
    /// </summary>
    public float BeamMaxLength => OldEngineType > 0 ? Math.Clamp(BeamStretch, 0f, 1024f) : PackU16x256(BeamStretch) / 64f;
    public float BeamMinLength => OldEngineType > 0 ? 0f : PackU16x256(BeamMinStretch) / 64f;
    public float BeamFollow => Math.Clamp(MathF.Truncate(BeamFollowRate * 1024f + 0.5f), -32766f, 32766f) / 1024f;

    private static float PackU16x256(float value) => Math.Clamp(MathF.Truncate(value * 256f + 0.5f), 0f, 65535f);
    /// <summary>+0x1F9, FxEmtStreakControl: points per streak (8/16/24/64) on class 3. INFERRED.</summary>
    public int StreakPoints { get; init; }

    /// <summary>Class 2 draws a beam (head to a lagging tail, <see cref="EffectSimulator"/>), class 3 a streak. The class order
    /// (1 Cam, 2 Beam, 3 Streak, 4 Smoke, 5 Flame, 6 Distort, 7 Trail, 8 Swipe) is the EBOOT's FxDefEmt*Turb
    /// declaration order and matches the class switch of 0x007E4AC8 (class 2 reads FxEmtBeamControl +0x1EC,
    /// class 3 FxEmtStreakControl +0x1F8). STRONG.</summary>
    public bool IsStretched => EmitterClass is 2 or 3;

    public EffectScalarTrack? Scalar(string name) => EffectTracks.Find(ScalarTracks, name);
    public EffectColorTrack? Color(string name) => EffectTracks.Find(ColorTracks, name);

    public EffectTexture? PrimaryTexture => Textures.Count > 0 ? Textures[0] : null;

    /// <summary>
    /// +0x31F (the low byte of the word the PPU reads at +0x31C, 0x007E4C00): the orientation mode of the particles of a
    /// frame-oriented shader, through the same table as a piece's +0x26 (0x00F2EBC8) into the SPU state +320. 0 on every
    /// QForce and Into the Nexus emitter; 2, 3, 4, 5, 8 on 29 All 4 One shader-10 emitters. PROVEN (PPU + B_19).
    /// </summary>
    public int FacingMode { get; init; }

    /// <summary>
    /// Shaders 10 and 13 lay their particles in the emitter's frame instead of facing the camera. PROVEN on the SPU side:
    /// their shader overlays (B_19 0x00DDA480, B_22 0x00DDB380) multiply the emitter's instance rows by the parent slot and
    /// call orient (0x17068) with <see cref="FacingMode"/>, and hand the result to the render; the other twelve read no
    /// frame. That the quad lies in the result's XY plane (normal = frame +Z, the cone/ring axis) is STRONG: the RSX
    /// vertex program is not read, but it is what makes the capture node's rings lie flat in its column.
    /// </summary>
    public bool IsFrameOriented => ShaderId is 10 or 13;
}

/// <summary>FxDefQuad (384 bytes; 372 on A Crack in Time): one textured billboard. Keys at +0x00..+0x50
/// (colour, alpha, alpha-test, size, +0x44), FxTexs at +0x50, lifetime float at +0x150 (0 = forever,
/// the EBOOT tests it against 0 at 0x007DC95C).</summary>
public sealed class EffectQuad : EffectPiece
{
    public required IReadOnlyList<EffectScalarTrack> ScalarTracks { get; init; }
    public required IReadOnlyList<EffectColorTrack> ColorTracks { get; init; }
    public required IReadOnlyList<EffectTexture> Textures { get; init; }
    public int TextureCount { get; init; }
    public float Lifetime { get; init; }
    /// <summary>
    /// +0x15E (0 on 3 660 of 4 766 distinct QForce quads, 1 on 986, 2 on 120). Non-zero sets bit 0x10 of the quad's SPU
    /// flags (PPU 0x007E6950), which the quad overlay A_4 turns into bit 30 of the render word it hands the RSX (0x38C).
    /// Read as "laid in the placement's frame, not facing the camera": the quads placed with a non-zero orientation mode
    /// (8, 2, 4, 5) have it set - STRONG (what 1 and 2 change in the vertex program is OPEN).
    /// </summary>
    public int Facing { get; init; }
    public bool IsFrameOriented => Facing != 0;

    public EffectScalarTrack? Scalar(string name) => EffectTracks.Find(ScalarTracks, name);
    public EffectColorTrack? Color(string name) => EffectTracks.Find(ColorTracks, name);
    public EffectTexture? PrimaryTexture => Textures.Count > 0 ? Textures[0] : null;
}

/// <summary>FxDefLite (384 bytes; 376 on A Crack in Time): a dynamic light. Seven key headers (FxLiteKeys,
/// 84 bytes): colour then six scalars. Which scalar is intensity or radius is INFERRED (values 1-12 on
/// +0x24/+0x30, 35-140 on +0x3C/+0x48).</summary>
public sealed class EffectLight : EffectPiece
{
    public required IReadOnlyList<EffectScalarTrack> ScalarTracks { get; init; }
    public required IReadOnlyList<EffectColorTrack> ColorTracks { get; init; }
    /// <summary>A Crack in Time +0x148: seconds the light lives, 0 = as long as its effect. PROVEN (0x69CA7C).</summary>
    public float Lifetime { get; init; }
    /// <summary>A Crack in Time +0x14C: multiplies every colour key's alpha at load (already applied to
    /// <see cref="ColorTracks"/>). 1 elsewhere.</summary>
    public float Intensity { get; init; } = 1f;

    public EffectScalarTrack? Scalar(string name) => EffectTracks.Find(ScalarTracks, name);
    public EffectColorTrack? Color(string name) => EffectTracks.Find(ColorTracks, name);
}

/// <summary>
/// One FxDefFX (216 bytes; 212 on A Crack in Time): a package's reference to a piece, placed. +0x00 is
/// <c>(index &lt;&lt; 16) | (type &lt;&lt; 8)</c> (PROVEN: every such word resolves into its type's list on 55
/// levels). Position +0x30/+0x38/+0x40 STRONG (offsets like (0,0,-1), (0,-2,0), (0.5,0,0) on 3 438 entries).
/// </summary>
public sealed class EffectComponent
{
    public required int Slot { get; init; }
    public required int FileOffset { get; init; }
    public required EffectPieceType Type { get; init; }
    public required int PieceIndex { get; init; }
    /// <summary>+0x04: start delay in seconds (0, 0.1, 0.2, 0.3...). INFERRED.</summary>
    public float StartDelay { get; init; }
    /// <summary>
    /// The piece's offset: X = +0x38, Y = +0x40, Z = +0x30. PROVEN: the PPU packs +0x30/+0x38/+0x40 x 128 to the
    /// SPU component record +0x64/+0x66/+0x68 (0x007E34B8) and FXVis reads them back as (+0x66, +0x68, +0x64) / 128
    /// (0x2058, mask 0x18850). An emitter's offset is expressed in its own frame (<see cref="OffsetMode"/> applied to
    /// it), a quad's or light's in its parent's unless <see cref="Attached"/> (0xA3E8, run in spuemu): effect 3795's
    /// mode-8 rings at +0x30 = 24 and its mode-0 light at +0x40 = 24 both sit 24 units up.
    /// </summary>
    public Vector3 Offset { get; init; }
    /// <summary>
    /// +0x24 == 1 (bit 32 of the SPU record's flags, 0x007E34B8). PROVEN by running 0xA3E8/0x52C8 under a rotated
    /// parent: the piece is placed like an emitter always is (orientation from identity, world modes from the
    /// parent's inverse, offset in its own frame), an emitter's particles stay in its frame (0x52C8: 0x1D090 = the
    /// emitter's matrix, spawn frame identity), and a package hands its children its full frame. Without it a quad,
    /// light or package orients from the parent and gets the parent's rotation again when its frame is built
    /// (0x7B0) - the same result under an unrotated spawn - and an emitter's particles live in world space.
    /// </summary>
    public bool Attached { get; init; }
    /// <summary>Relative random spread of <see cref="Offset"/> per axis (X = +0x3C, Y = +0x44, Z = +0x34; x 16384 at
    /// 0x007E34B8). 0 on every retail component.</summary>
    public Vector3 OffsetSpread { get; init; }
    /// <summary>+0x26: how the piece's frame is oriented from its parent's (FXVis 0x17068 via the table
    /// 0x00F2EBC8, see <see cref="EffectSpace.Orient"/>): 0 = the parent frame as is, 8 = world-aligned with local
    /// +Z = world up (the rotation the old preview applied to everything). PROVEN.</summary>
    public int OrientationMode { get; init; }
    /// <summary>+0x2E: the orientation the offset is expressed in (0 on nearly every component), from the piece's own
    /// frame for an emitter or an <see cref="Attached"/> piece, else from the parent's. PROVEN.</summary>
    public int OffsetMode { get; init; }
    /// <summary>+0x27 / +0x28: random tilt range of the piece's frame in degrees, drawn per play (0 / 0 on most),
    /// towards an azimuth drawn in [<see cref="AzimuthMinDegrees"/>, <see cref="AzimuthMaxDegrees"/>] (s16 +0x2A /
    /// +0x2C, -180 / 180 on most). With no tilt the azimuth changes nothing. PROVEN (0x179C0 run in spuemu).</summary>
    public int TiltMinDegrees { get; init; }
    public int TiltMaxDegrees { get; init; }
    public int AzimuthMinDegrees { get; init; }
    public int AzimuthMaxDegrees { get; init; }
    /// <summary>
    /// +0xC0 / +0xC4: restart cycle of the piece, seconds, max then min (min &lt;= max on all 16 423 distinct
    /// emitter components). INFERRED: non-zero on 55 of them only - continuous emitters whose emitter-life curves
    /// (alpha 0 -> 1 -> 0 over [0, 1]) need a period, and 21 bursts of 3.5 s with (3, 3). The preview restarts
    /// the piece every random(min, max) seconds and runs its piece-life curves over that period.
    /// </summary>
    public float CycleMax { get; init; }
    public float CycleMin { get; init; }
    /// <summary>A Tools of Destruction FxDefFX (128 bytes): placed by the ToD fxvis rules (<see cref="EffectSpace.OrientOldEngine"/>).</summary>
    public bool OldEngine { get; init; }
    /// <summary>
    /// ToD FxDefFX +0x08: chance the piece is created when its package plays. PROVEN (ToD fxvis 0x1BBF8: the piece
    /// and its subtree are skipped when a draw U[0,1) exceeds it; the PPU drops pieces under 0.0001, 0x00602500).
    /// 1 on 11 421 of 12 141 distinct components. QForce: 1 (no such field known).
    /// </summary>
    public float SpawnChance { get; init; } = 1f;
    /// <summary>ToD FxDefFX +0x60 / +0x5C: the piece is created only when a context value (camera distance, STRONG) lies
    /// in [min, max] (0x1BBC0). 0 / 1024 on nearly all; the preview ignores it.</summary>
    public float VisibleMin { get; init; }
    public float VisibleMax { get; init; } = float.MaxValue;
    /// <summary>+0x78: optional spin keys (degrees: 360, 120, 180...). Present on 2 194 of 23 605 entries.</summary>
    public EffectScalarTrack? Spin { get; init; }
    public required byte[] Raw { get; init; }
    public EffectPiece? Piece { get; set; }
}

/// <summary>
/// FxDefPkg (424 bytes; 336 on A Crack in Time): an EFFECT as the game plays it - a list of placed
/// pieces plus package-level tint/scale tracks (FxPkgKeys at +0x14C). The level's 0x500 table maps a
/// game-wide effect id to a package index, which is how code and data name an effect.
/// </summary>
public sealed class EffectPackage : EffectPiece
{
    public required IReadOnlyList<EffectComponent> Components { get; init; }
    public required IReadOnlyList<EffectScalarTrack> ScalarTracks { get; init; }
    public required IReadOnlyList<EffectColorTrack> ColorTracks { get; init; }
    /// <summary>Game-wide effect ids (0x500 table) that resolve to this package. Usually exactly one.</summary>
    public List<int> GlobalIds { get; } = [];

    public EffectScalarTrack? Scalar(string name) => EffectTracks.Find(ScalarTracks, name);
    public EffectColorTrack? Color(string name) => EffectTracks.Find(ColorTracks, name);

    public string DisplayName => GlobalIds.Count > 0 ? $"FX {GlobalIds[0]:D4}" : $"FX pkg {Index}";

    public string Summary
    {
        get
        {
            int e = Components.Count(c => c.Type == EffectPieceType.Emitter);
            int q = Components.Count(c => c.Type == EffectPieceType.Quad);
            int l = Components.Count(c => c.Type == EffectPieceType.Light);
            int p = Components.Count(c => c.Type == EffectPieceType.Package);
            var parts = new List<string>();
            if (e > 0) parts.Add($"{e} emitter{(e > 1 ? "s" : "")}");
            if (q > 0) parts.Add($"{q} quad{(q > 1 ? "s" : "")}");
            if (l > 0) parts.Add($"{l} light{(l > 1 ? "s" : "")}");
            if (p > 0) parts.Add($"{p} sub-effect{(p > 1 ? "s" : "")}");
            return parts.Count == 0 ? "empty" : string.Join(", ", parts);
        }
    }
}

/// <summary>Track lookup by name without LINQ's per-call closure and enumerator: the simulator asks per particle.</summary>
internal static class EffectTracks
{
    public static EffectScalarTrack? Find(IReadOnlyList<EffectScalarTrack> tracks, string name)
    {
        for (int i = 0; i < tracks.Count; i++)
            if (tracks[i].Name == name) return tracks[i];
        return null;
    }

    public static EffectColorTrack? Find(IReadOnlyList<EffectColorTrack> tracks, string name)
    {
        for (int i = 0; i < tracks.Count; i++)
            if (tracks[i].Name == name) return tracks[i];
        return null;
    }
}
