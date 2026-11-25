// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Numerics;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>One sprite the preview draws: a billboard, a quad laid in a plane, or a beam.</summary>
public struct EffectBillboard
{
    public Vector3 Position;
    /// <summary>Half the side of the square, world units.</summary>
    public float HalfSize;
    /// <summary>Half the width when it differs from <see cref="HalfSize"/> (a quad's width track); 0 = square.</summary>
    public float HalfWidth;
    public float Rotation;
    public Vector4 Color;
    public int TextureIndex;
    public EffectSpriteBlend Blend;
    public bool Distortion;
    /// <summary>Beams and streaks: from the head (<see cref="Position"/>) to the tail, same space as the
    /// position. Zero for a camera-facing sprite.</summary>
    public Vector3 Stretch;
    /// <summary>
    /// The quad lies in the plane of <see cref="AxisX"/> / <see cref="AxisY"/> (unit vectors, same space as the position)
    /// instead of facing the camera - shader 10/13 emitters and quads with FxDefQuad +0x15E != 0.
    /// </summary>
    public bool Oriented;
    /// <summary>Face the camera around the fixed up direction in AxisY.</summary>
    public bool Axial;
    public Vector3 AxisX, AxisY;
    /// <summary>Which piece produced it, for "solo" and highlighting.</summary>
    public EffectPiece Source;
}

/// <summary>A light piece, for the preview's glow/radius gizmo.</summary>
public struct EffectLightMarker
{
    public Vector3 Position;
    public Vector4 Color;
    public float Radius;
    public EffectLight Source;
}

/// <summary>Knobs the user can turn in the preview. None of them is read from the file.</summary>
public sealed class EffectSimulationSettings
{
    /// <summary>Extra downward (world -Y) acceleration on top of the file's own motion block, for experiments.
    /// 0 reproduces the game.</summary>
    public float Gravity;
    public float TimeScale = 1f;
    /// <summary>
    /// Rotation of the matrix the effect is spawned with (the root frame its pieces orient from). The simulator
    /// works in world axes (Y-up) around the effect's origin, like FXVis; the caller only adds the translation.
    /// FxController hands its own rotation (request +0x90..+0xB0 at 0x00353228); the Asset Viewer uses identity,
    /// or <see cref="EffectSpace.ZUpToYUp"/> to preview a spawner whose +Z points up. Takes effect at the next
    /// (re)start: <see cref="EffectSimulator.Reset"/>.
    /// </summary>
    public Matrix4x4 SpawnFrame = Matrix4x4.Identity;
    /// <summary>Game frames per second the turbulence step (+0x344, applied once per FXVis run, not scaled by dt)
    /// is replayed at. ASSUMED: QForce renders at 30 Hz.</summary>
    public float GameFrameRate = 30f;
    /// <summary>When set, only this piece (and the package chain above it) plays.</summary>
    public EffectPiece? Solo;
    public bool ShowEmitters = true;
    public bool ShowQuads = true;
    public bool ShowLights = true;
    /// <summary>Emitters keyed to the shader classes the preview cannot reproduce (distortion) are drawn
    /// as faint alpha sprites instead of being hidden.</summary>
    public bool ShowDistortion = true;
    public int MaxParticles = 20000;
    /// <summary>Period, seconds, over which a continuous emitter (duration 0, no component cycle) runs its
    /// emitter-life curves in the legacy preview. ACIT uses the individual track's timing field.</summary>
    public float ContinuousCurvePeriod = 1f;
}

/// <summary>
/// Frames of the new-engine effect system. The game simulates in world axes, and the world is Y-up (gravity,
/// motion mode 9, is world -Y; FxController rotations turn about Y). A piece's frame comes from its parent's by its
/// orientation mode (FxDefFX +0x26): mode 8 turns local +Z to world +Y, which is why pieces "authored Z-up" rise.
/// Frames are row matrices, rows = the local X/Y/Z axes in world coordinates (as the SPU keeps them, 0x2B0).
/// </summary>
public static class EffectSpace
{
    /// <summary>Local +Z to world +Y: what orientation mode 8 gives on an unrotated parent (rows X, -Z, +Y).
    /// Kept for the preview's "spawner Z up" option.</summary>
    public static readonly Matrix4x4 ZUpToYUp = Matrix4x4.CreateRotationX(-MathF.PI / 2f);

    /// <summary>Raw mode byte through the table at 0x00F2EBC8 (0..5 -> 0..5, 6..11 -> 16..21); -1 past 11.</summary>
    public static int MapMode(int raw) => raw is >= 0 and <= 5 ? raw : raw is >= 6 and <= 11 ? raw + 10 : -1;

    /// <summary>
    /// FXVis 0x17068 (run in spuemu for all 12 values): the frame a mode builds from <paramref name="parent"/>
    /// (modes 0..5) or <paramref name="world"/> (6..11). Low bits: 0 (X, Y, Z), 1 (-X, Y, -Z), 2 (X, -Z, Y),
    /// 3 (X, Z, -Y), 4 (Z, Y, -X), 5 (-Z, Y, X). The translation of the result is the parent's.
    /// </summary>
    public static Matrix4x4 Orient(int rawMode, Matrix4x4 parent, Matrix4x4 world)
    {
        int m = MapMode(rawMode);
        if (m < 0) return parent;
        Matrix4x4 f = m > 5 ? world : parent;
        Vector3 x = new(f.M11, f.M12, f.M13), y = new(f.M21, f.M22, f.M23), z = new(f.M31, f.M32, f.M33);
        (Vector3 a, Vector3 b, Vector3 c) = (m & 7) switch
        {
            1 => (-x, y, -z),
            2 => (x, -z, y),
            3 => (x, z, -y),
            4 => (z, y, -x),
            5 => (-z, y, x),
            _ => (x, y, z),
        };
        return new Matrix4x4(a.X, a.Y, a.Z, 0, b.X, b.Y, b.Z, 0, c.X, c.Y, c.Z, 0, parent.M41, parent.M42, parent.M43, 1);
    }

    /// <summary>
    /// Tools of Destruction's orient (fxvis 0x1D850, run in spuemu for all modes): one source matrix, a jump table
    /// of 12. Modes 0..5 and 9 permute the rows of <paramref name="source"/>, 6, 7, 8, 10, 11 the world axes, 12+ =
    /// world. Patterns: 0 (X, Y, Z), 1 (-X, Y, -Z), 2 (X, -Z, Y), 3 (X, Z, -Y), 4 (-Z, Y, X), 5 (Z, Y, -X) - 4 and 5
    /// (and so 10 and 11) are swapped against QForce's 0x17068, and the table sends 9 to mode 3's code (rows of the
    /// source, not the world). The translation of the result is the source's.
    /// </summary>
    public static Matrix4x4 OrientOldEngine(int mode, Matrix4x4 source)
    {
        Matrix4x4 f = mode is >= 0 and <= 5 or 9 ? source : Matrix4x4.Identity;
        Vector3 x = new(f.M11, f.M12, f.M13), y = new(f.M21, f.M22, f.M23), z = new(f.M31, f.M32, f.M33);
        (Vector3 a, Vector3 b, Vector3 c) = (mode is >= 0 and <= 11 ? mode % 6 : 0) switch
        {
            1 => (-x, y, -z),
            2 => (x, -z, y),
            3 => (x, z, -y),
            4 => (-z, y, x),
            5 => (z, y, -x),
            _ => (x, y, z),
        };
        return new Matrix4x4(a.X, a.Y, a.Z, 0, b.X, b.Y, b.Z, 0, c.X, c.Y, c.Z, 0, source.M41, source.M42, source.M43, 1);
    }

    /// <summary>FXVis 0x17000: the single axis a mode names - 0 +Z, 1 -Z, 2 +Y, 3 -Y, 4 +X, 5 -X, of the frame for
    /// 0..5 and of the world for 6..11 (<paramref name="world"/>). False past 11.</summary>
    public static bool ModeAxis(int rawMode, out Vector3 axis, out bool world)
    {
        int m = MapMode(rawMode);
        axis = Vector3.Zero;
        world = m > 5;
        if (m < 0) return false;
        axis = (m & 4) != 0 ? Vector3.UnitX : (m & 2) != 0 ? Vector3.UnitY : Vector3.UnitZ;
        if ((m & 1) != 0) axis = -axis;
        return true;
    }

    /// <summary>
    /// FXVis 0x179C0 (run in spuemu): tilts a frame by <paramref name="tiltDegrees"/> about its local axis
    /// (sin az, cos az, 0), so +Z leans towards (cos az, -sin az, 0) - tilt 30 at azimuth 0 leans +Z to +X, at
    /// azimuth 90 to -Y. Row convention: new row i = sum_j R_ij x old row j.
    /// </summary>
    public static Matrix4x4 Tilt(Matrix4x4 frame, float tiltDegrees, float azimuthDegrees)
    {
        if (tiltDegrees == 0f) return frame;
        float az = azimuthDegrees * MathF.PI / 180f;
        var r = Matrix4x4.CreateFromAxisAngle(new Vector3(MathF.Sin(az), MathF.Cos(az), 0f), tiltDegrees * MathF.PI / 180f);
        Matrix4x4 rotation = new(frame.M11, frame.M12, frame.M13, 0, frame.M21, frame.M22, frame.M23, 0,
            frame.M31, frame.M32, frame.M33, 0, 0, 0, 0, 1);
        Matrix4x4 result = r * rotation;
        result.M41 = frame.M41; result.M42 = frame.M42; result.M43 = frame.M43;
        return result;
    }

    /// <summary>A sprite moved by <paramref name="world"/>: its position, and its stretch and plane as directions.</summary>
    public static EffectBillboard Transform(EffectBillboard sprite, Matrix4x4 world)
    {
        sprite.Position = Vector3.Transform(sprite.Position, world);
        sprite.Stretch = Vector3.TransformNormal(sprite.Stretch, world);
        if (sprite.Oriented || sprite.Axial)
        {
            sprite.AxisX = Unit(Vector3.TransformNormal(sprite.AxisX, world));
            sprite.AxisY = Unit(Vector3.TransformNormal(sprite.AxisY, world));
        }
        return sprite;
    }

    private static Vector3 Unit(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : v;

    /// <summary>The X and Y rows of a frame: the plane a frame-oriented quad is laid in (its normal is the frame's +Z,
    /// the cone and ring axis).</summary>
    public static (Vector3 X, Vector3 Y) PlaneOf(Matrix4x4 frame) =>
        (Unit(new Vector3(frame.M11, frame.M12, frame.M13)), Unit(new Vector3(frame.M21, frame.M22, frame.M23)));
}

/// <summary>
/// CPU playback of one effect package, for the editor preview. The game simulates particles in the SPU job
/// FXVis (QForce EBOOT 0x00D7EB00) and the per-type overlays it loads; this class reproduces what those were
/// shown to do (Docs/reverse/NEW_ENGINE_FX_SPU_2026-09-26.md): the 61 spawn generators
/// (<see cref="EffectSpawnStyles"/>, measured by running the retail overlays), the motion block (acceleration
/// along a frame or world axis, thrust, exp drag, speed clamp, per-frame turbulence - PROVEN), the pieces'
/// frames (orientation mode, random tilt, offset - FXVis 0xA3E8, <see cref="Place"/>), plus the decoded curves,
/// textures, blend byte, component delays and emitter duration. Everything is in world axes (Y-up) around the
/// effect's origin. Still approximate: key random modes 1..6 are treated as a uniform draw between min and max,
/// and streaks are stretched along the velocity (the Streak overlay is OPEN); beams follow the Beam overlay.
/// </summary>
public sealed class EffectSimulator
{
    private sealed class Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Age;
        public float Life;
        public float Rotation;
        public bool ReverseSpin;
        public float R0, R1, R2, R3, R4;
        /// <summary>A beam's tail minus its head (class 2; zero when born).</summary>
        public Vector3 Beam;
    }

    /// <summary>One placed piece: its FxDefFX, the placement it hangs under, and this play's frames.</summary>
    private sealed class Placement
    {
        public required EffectComponent Component;
        public required int Parent;
        /// <summary>The piece's frame: rows = its axes in world coordinates, translation = its position.</summary>
        public Matrix4x4 Frame = Matrix4x4.Identity;
        /// <summary>The slot a package hands its children (FXVis 0xC02C..0xC08C).</summary>
        public Matrix4x4 ChildSlot = Matrix4x4.Identity;
        /// <summary>Not created this play: its spawn chance failed, or its parent's did.</summary>
        public bool Skipped;
        public float Delay, Tilt, Azimuth;
        public float Spin, SpinRandom, PackageSpin, PackageSpinRandom;
        public Vector3 Offset;
    }

    private sealed class EmitterInstance
    {
        public required EffectEmitter Emitter;
        public required int Placement;
        public EffectScalarTrack? ParticleSpin;
        /// <summary>World-axis frame of this play (rows = local axes, translation = position).</summary>
        public Matrix4x4 Frame = Matrix4x4.Identity;
        public float Delay;
        /// <summary>The component's restart cycle (+0xC0 max, +0xC4 min), 0 = none.</summary>
        public float CycleMin, CycleMax;
        /// <summary>This run's period, drawn in [CycleMin, CycleMax] at each (re)start.</summary>
        public float Cycle;
        public float Age = -1;
        public float SpawnDebt;
        /// <summary>ToD: the FxTexture this instance drew at creation (fxvis 0x0FC8C: U x count), -1 = none.</summary>
        public int TextureChoice = -1;
        public readonly List<Particle> Particles = [];
        public int Spawned;
    }

    private sealed class QuadInstance
    {
        public required EffectQuad Quad;
        public required int Placement;
        public Vector3 Position;
        public Matrix4x4 Frame = Matrix4x4.Identity;
        public float Delay;
        public float Age = -1;
        public float R0, R1, R2;
    }

    private sealed class LightInstance
    {
        public required EffectLight Light;
        public required int Placement;
        public Vector3 Position;
        public float Delay;
        public float Age = -1;
        public float ColourRandom, RadiusRandom;
    }

    private readonly EffectLibrary _library;
    private readonly List<Placement> _placements = [];
    private readonly List<EmitterInstance> _emitters = [];
    private readonly List<QuadInstance> _quads = [];
    private readonly List<LightInstance> _lights = [];
    private readonly List<Placement> _spinningPlacements = [];
    private readonly EffectScalarTrack? _rootSpinTrack;
    private float _rootSpin, _rootSpinRandom;
    private Random _random;
    private readonly int _seed;
    private bool UsesAcitTiming => ReferenceEquals(_library.Layout, EffectLayout.CrackInTime);
    private const double SimulationStep = 1.0 / 60.0;
    private double _pendingTime;
    private int _simulationFrame;
    /// <summary>Fraction of a game frame not yet handed to the turbulence step.</summary>
    private float _frameClock;

    public EffectPackage Package { get; }
    public EffectSimulationSettings Settings { get; }
    public float Time { get; private set; }
    public int ParticleCount { get; private set; }

    /// <summary>Longest bounded thing in the effect: last burst end + longest particle life, or 0 when
    /// the effect runs forever (a continuous emitter or an immortal quad).</summary>
    public float PlayLength { get; private set; }
    public bool Continuous { get; private set; }

    /// <summary>How long a continuous effect takes to fill up: the latest start of a continuous emitter plus its
    /// longest particle life. 0 when nothing runs forever.</summary>
    public float SteadyStateTime { get; private set; }

    public EffectSimulator(EffectLibrary library, EffectPackage package, EffectSimulationSettings? settings = null, int seed = 1234)
    {
        _library = library;
        Package = package;
        Settings = settings ?? new EffectSimulationSettings();
        _seed = seed;
        _random = new Random(seed);
        _rootSpinTrack = UsesAcitTiming ? SpinTrack(package.Scalar("Spin")) : null;
        Instantiate(package, -1, 0);
        Place(reroll: true);
        ComputePlayLength();
    }

    private void Instantiate(EffectPackage package, int parent, int depth)
    {
        if (depth > 6) return;
        foreach (EffectComponent component in package.Components)
        {
            int placement = _placements.Count;
            var instance = new Placement { Component = component, Parent = parent };
            _placements.Add(instance);
            if (UsesAcitTiming &&
                (SpinTrack(component.Spin) != null || SpinTrack((component.Piece as EffectPackage)?.Scalar("Spin")) != null))
                _spinningPlacements.Add(instance);
            switch (component.Piece)
            {
                case EffectEmitter { Disabled: true }:
                    break;
                case EffectEmitter emitter:
                    _emitters.Add(new EmitterInstance
                    {
                        Emitter = emitter, Placement = placement, ParticleSpin = SpinTrack(emitter.Scalar("Particle spin")),
                        CycleMin = MathF.Max(0f, MathF.Min(component.CycleMin, component.CycleMax)),
                        CycleMax = MathF.Max(0f, component.CycleMax),
                    });
                    break;
                case EffectQuad quad:
                    _quads.Add(new QuadInstance { Quad = quad, Placement = placement });
                    break;
                case EffectLight light:
                    _lights.Add(new LightInstance { Light = light, Placement = placement });
                    break;
                case EffectPackage child when !ReferenceEquals(child, package):
                    Instantiate(child, placement, depth + 1);
                    break;
            }
        }
    }

    /// <summary>
    /// This play's frame of every piece, as FXVis 0xA3E8 builds an instance and 0x52C8 its spawn frame (both run
    /// in spuemu under a rotated parent slot S, translation T):
    /// <list type="bullet">
    /// <item>emitters, and pieces with +0x24 == 1: M = tilt(orient(+0x26, I, Sᵀ)), offset frame = orient(+0x2E, M, I)
    /// - the offset turns with the piece;</item>
    /// <item>other pieces (quads, lights, packages with +0x24 == 0): M = tilt(orient(+0x26, S, I)), offset frame =
    /// orient(+0x2E, S, I) - the offset is in the parent's frame;</item>
    /// </list>
    /// then frame = M x S at T + (offset x (1 + spread x U(-1,1))) x offset frame x S. 0xA3E8 picks the branch on
    /// flag 32 (+0x24 == 1) OR SPU kind 5, the byte the PPU writes into every emitter's record (0x007E4DB4). The
    /// random tilt is drawn in +0x27..+0x28 towards an azimuth in +0x2A..+0x2C. A package hands its children M
    /// (+0x24 == 0) or M x S (+0x24 == 1) at its position. Parents come before children in <see cref="_placements"/>.
    /// </summary>
    private void Place(bool reroll)
    {
        if (reroll)
        {
            _rootSpin = 0f;
            if (_rootSpinTrack != null) _rootSpinRandom = _random.NextSingle();
        }
        Matrix4x4 spawn = RootRotation();
        foreach (Placement p in _placements)
        {
            Matrix4x4 slot = p.Parent < 0 ? spawn : _placements[p.Parent].ChildSlot;
            Vector3 origin = slot.Translation;
            Matrix4x4 s = Rotation(slot);
            EffectComponent c = p.Component;
            if (reroll)
            {
                Placement? parent = p.Parent < 0 ? null : _placements[p.Parent];
                p.Skipped = parent?.Skipped == true || c.SpawnChance < 1f && _random.NextSingle() >= c.SpawnChance;
                float delay = c.StartDelay;
                if (c.StartDelaySpread != 0f) delay *= 1f + c.StartDelaySpread * Signed();
                float limit = (parent?.Component.Piece as EffectPackage ?? Package).TimelineDuration;
                if (UsesAcitTiming && limit > 0f)
                    delay = MathF.Min(delay, limit);
                p.Delay = (parent?.Delay ?? 0f) + MathF.Max(0f, delay);
                p.Tilt = Lerp(c.TiltMinDegrees, c.TiltMaxDegrees, _random.NextSingle());
                p.Azimuth = Lerp(c.AzimuthMinDegrees, c.AzimuthMaxDegrees, _random.NextSingle());
                Vector3 spread = c.OldEngine ? Vector3.Clamp(c.OffsetSpread, Vector3.Zero, Vector3.One) : c.OffsetSpread;
                p.Offset = c.Offset * (Vector3.One + spread * new Vector3(Signed(), Signed(), Signed()));
                p.Spin = p.PackageSpin = 0f;
                if (UsesAcitTiming)
                {
                    if (SpinTrack(c.Spin) != null) p.SpinRandom = _random.NextSingle();
                    if (SpinTrack((c.Piece as EffectPackage)?.Scalar("Spin")) != null)
                        p.PackageSpinRandom = _random.NextSingle();
                }
            }
            bool ownFrame = c.Attached || c.Type == EffectPieceType.Emitter;
            if (c.OldEngine)
            {
                PlaceOldEngine(p, c, ownFrame, origin, s);
                continue;
            }
            Matrix4x4 m = ownFrame
                ? EffectSpace.Orient(c.OrientationMode, Matrix4x4.Identity, Matrix4x4.Transpose(s))
                : EffectSpace.Orient(c.OrientationMode, s, Matrix4x4.Identity);
            m = EffectSpace.Tilt(m, p.Tilt, p.Azimuth);
            Matrix4x4 offsetFrame = ownFrame
                ? EffectSpace.Orient(c.OffsetMode, m, Matrix4x4.Identity)
                : EffectSpace.Orient(c.OffsetMode, s, Matrix4x4.Identity);
            Vector3 position = origin + Vector3.TransformNormal(Vector3.TransformNormal(p.Offset, offsetFrame), s);
            if (p.PackageSpin != 0f && c.Piece is EffectPackage package)
                m = PackageRotation(package, p.PackageSpin) * m;
            if (p.Spin != 0f) m = Matrix4x4.CreateRotationZ(p.Spin) * m;
            Matrix4x4 frame = m * s;
            frame.Translation = position;
            p.Frame = frame;
            Matrix4x4 child = c.Attached ? frame : m;
            child.Translation = position;
            p.ChildSlot = child;
        }
        foreach (EmitterInstance e in _emitters)
        {
            e.Frame = _placements[e.Placement].Frame;
            e.Delay = _placements[e.Placement].Delay;
            int textures = Math.Min(e.Emitter.TextureCount, e.Emitter.Textures.Count);
            if (reroll)
                e.TextureChoice = e.Emitter.OldEngineType > 0 && textures > 0 ? _random.Next(textures) : -1;
        }
        foreach (QuadInstance q in _quads)
        {
            q.Frame = _placements[q.Placement].Frame;
            q.Position = q.Frame.Translation;
            q.Delay = _placements[q.Placement].Delay;
        }
        foreach (LightInstance l in _lights)
        {
            l.Position = _placements[l.Placement].Frame.Translation;
            l.Delay = _placements[l.Placement].Delay;
        }
    }

    /// <summary>
    /// Tools of Destruction's placement (fxvis 0x194B8, read; orient 0x1D850 and incline 0x20A80 run in spuemu),
    /// under the parent slot S at <paramref name="origin"/>. Same two branches as QForce, on FxDefFX +0x1C (attached)
    /// OR emitter (byte table LS 0x22920):
    /// <list type="bullet">
    /// <item>own frame: M = tilt(orient(+0x3D, I)), times Sᵀ when the mode is above 5; offset frame = orient(+0x1E, M);
    /// the emitter's world frame is then M x S (0x0D550 multiplies the instance by the same slot) - modes 0..5 follow
    /// the parent, the others stay world-aligned;</item>
    /// <item>other: M = tilt(orient(+0x3D, S)), offset frame = orient(+0x1E, S). A package's children get M at
    /// origin + offset x offset frame (0x1B5F0..0x1B62C: the slot is the instance's rows and offset plus the parent's
    /// translation); a quad's or light's frame keeps QForce's second turn by S (INFERRED: their ToD handlers 0x11C90 /
    /// 0x10778 are not read).</item>
    /// </list>
    /// </summary>
    private void PlaceOldEngine(Placement p, EffectComponent c, bool ownFrame, Vector3 origin, Matrix4x4 s)
    {
        Matrix4x4 m;
        Vector3 position;
        if (ownFrame)
        {
            m = EffectSpace.Tilt(EffectSpace.OrientOldEngine(c.OrientationMode, Matrix4x4.Identity), p.Tilt, p.Azimuth);
            if (c.OrientationMode > 5) m *= Matrix4x4.Transpose(s);
            Matrix4x4 offsetFrame = EffectSpace.OrientOldEngine(c.OffsetMode, m);
            position = origin + Vector3.TransformNormal(Vector3.TransformNormal(p.Offset, offsetFrame), s);
        }
        else
        {
            m = EffectSpace.Tilt(EffectSpace.OrientOldEngine(c.OrientationMode, s), p.Tilt, p.Azimuth);
            Vector3 offset = Vector3.TransformNormal(p.Offset, EffectSpace.OrientOldEngine(c.OffsetMode, s));
            position = origin + (c.Type == EffectPieceType.Package ? offset : Vector3.TransformNormal(offset, s));
        }
        m.Translation = Vector3.Zero;
        Matrix4x4 frame = m * s;
        frame.Translation = position;
        p.Frame = frame;
        Matrix4x4 child = c.Attached ? frame : m;
        child.Translation = position;
        p.ChildSlot = child;
    }

    /// <summary>This play's frame of the first placement of <paramref name="piece"/>: rows = its axes in world
    /// coordinates, translation = its position around the effect's origin. False when the piece is not placed.</summary>
    public bool TryGetFrame(EffectPiece piece, out Matrix4x4 frame)
    {
        foreach (Placement p in _placements)
        {
            if (!ReferenceEquals(p.Component.Piece, piece)) continue;
            frame = p.Frame;
            return true;
        }
        frame = Matrix4x4.Identity;
        return false;
    }

    private static Matrix4x4 Rotation(Matrix4x4 m)
    {
        m.M14 = m.M24 = m.M34 = 0f;
        m.M41 = m.M42 = m.M43 = 0f;
        m.M44 = 1f;
        return m;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>The rotation of the slot a placement hangs under (the spawn frame for a top-level piece).</summary>
    private Matrix4x4 ParentRotation(int placement)
    {
        int parent = _placements[placement].Parent;
        return parent < 0 ? RootRotation() : Rotation(_placements[parent].ChildSlot);
    }

    private Matrix4x4 RootRotation() => PackageRotation(Package, _rootSpin) * Rotation(Settings.SpawnFrame);

    private static Matrix4x4 PackageRotation(EffectPackage package, float angle) =>
        angle != 0f && package.SpinAxis.LengthSquared() > 1e-12f
            ? Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(package.SpinAxis), angle)
            : Matrix4x4.Identity;
    private float Signed() => 2f * _random.NextSingle() - 1f;

    private void ComputePlayLength()
    {
        float end = 0f, steady = 0f;
        bool forever = false;
        foreach (EmitterInstance e in _emitters)
        {
            if (_placements[e.Placement].Skipped) continue;
            float life = e.Emitter.Scalar("Spawn life") is { Keys.Length: > 0 } track && UsesAcitTiming
                ? track.Keys.Max(k => MathF.Max(k.Min, k.IsRandom ? k.Max : k.Min) * (1f + MathF.Abs(k.Extra)))
                : MaxOf(e.Emitter.Scalar("Spawn life"), 1f);
            if (e.Emitter.Duration <= 1e-4f || e.CycleMax > 1e-4f)
            {
                forever = true;
                steady = MathF.Max(steady, e.Delay + life);
            }
            else end = MathF.Max(end, e.Delay + e.Emitter.Duration + life);
        }
        foreach (QuadInstance q in _quads)
        {
            if (_placements[q.Placement].Skipped) continue;
            if (q.Quad.Lifetime <= 0f) forever = true;
            else end = MathF.Max(end, q.Delay + q.Quad.Lifetime);
        }
        if (UsesAcitTiming)
        {
            foreach (LightInstance l in _lights)
                if (!_placements[l.Placement].Skipped && l.Light.Lifetime > 0f)
                    end = MathF.Max(end, l.Delay + l.Light.Lifetime);
            if (end == 0f && _lights.Any(l => !_placements[l.Placement].Skipped)) forever = true;
        }
        Continuous = forever;
        PlayLength = MathF.Max(end, Package.TimelineDuration);
        SteadyStateTime = forever ? steady : 0f;
    }

    /// <summary>Runs a continuous effect ahead to its steady state, at most <paramref name="maxSeconds"/>, so
    /// it shows as it would after playing a while instead of filling up from nothing.</summary>
    public void Prewarm(float maxSeconds = 20f)
    {
        float left = MathF.Min(SteadyStateTime, maxSeconds);
        while (left > 1e-4f)
        {
            float dt = MathF.Min(0.1f, left);
            Step(dt);
            left -= dt;
        }
    }

    private static float MaxOf(EffectScalarTrack? track, float fallback)
    {
        if (track == null || track.Keys.Length == 0) return fallback;
        return MathF.Max(0.01f, track.Range().Max);
    }

    /// <summary>Turns the effect to a new spawn frame without restarting it: the live particles stay where they are,
    /// the pieces and the particles spawned from now on take the new frame.</summary>
    public void SetSpawnFrame(Matrix4x4 frame)
    {
        Settings.SpawnFrame = frame;
        Place(reroll: false);
    }

    public void Reset(bool reseed = true)
    {
        Time = 0f;
        _frameClock = 0f;
        _pendingTime = 0;
        _simulationFrame = 0;
        if (reseed) _random = new Random(_seed);
        foreach (EmitterInstance e in _emitters) { e.Age = -1; e.SpawnDebt = 0; e.Cycle = 0; e.Particles.Clear(); e.Spawned = 0; }
        foreach (QuadInstance q in _quads) q.Age = -1;
        foreach (LightInstance l in _lights) l.Age = -1;
        ParticleCount = 0;
        Place(reroll: true);
        ComputePlayLength();
    }

    /// <summary>True once every bounded piece has finished and no particle is alive.</summary>
    public bool Finished => !Continuous && Time > PlayLength && ParticleCount == 0;

    private bool Plays(EffectPiece piece) => Settings.Solo == null || ReferenceEquals(Settings.Solo, piece);

    public void Step(float dt)
    {
        float elapsed = Math.Clamp(dt, 0f, 0.1f) * Settings.TimeScale;
        if (!float.IsFinite(elapsed) || elapsed <= 0f) return;
        _pendingTime += elapsed;
        while (_pendingTime + 1e-8 >= SimulationStep)
        {
            _pendingTime -= SimulationStep;
            Advance((float)SimulationStep);
        }
    }

    private void Advance(float dt)
    {
        Time = (float)(++_simulationFrame * SimulationStep);
        AdvancePlacementSpin(dt);
        // Turbulence is applied once per game frame (not scaled by dt): count the frames this step spans.
        float rate = Math.Max(1f, Settings.GameFrameRate);
        _frameClock += dt * rate;
        int frames = (int)_frameClock;
        _frameClock -= frames;
        int budget = Settings.MaxParticles - ParticleCount;
        foreach (EmitterInstance e in _emitters)
        {
            if (_placements[e.Placement].Skipped) continue;
            StepEmitter(e, dt, Math.Min(frames, 4), ref budget);
        }
        foreach (QuadInstance q in _quads)
        {
            if (Time < q.Delay || _placements[q.Placement].Skipped) continue;
            if (q.Age < 0) { q.R0 = _random.NextSingle(); q.R1 = _random.NextSingle(); q.R2 = _random.NextSingle(); }
            q.Age = Time - q.Delay;
        }
        foreach (LightInstance l in _lights)
        {
            if (Time < l.Delay || _placements[l.Placement].Skipped) continue;
            if (l.Age < 0)
            {
                l.ColourRandom = _random.NextSingle();
                l.RadiusRandom = _random.NextSingle();
            }
            l.Age = Time - l.Delay;
        }
        ParticleCount = _emitters.Sum(e => e.Particles.Count);
    }

    private static EffectScalarTrack? SpinTrack(EffectScalarTrack? track) =>
        track?.Keys.Any(key => key.Min != 0f || key.IsRandom && key.Max != 0f) == true ? track : null;

    private void AdvancePlacementSpin(float dt)
    {
        bool changed = AdvanceSpin(_rootSpinTrack, Time, dt, Package.TimelineDuration, _rootSpinRandom, ref _rootSpin);
        foreach (Placement p in _spinningPlacements)
        {
            float age = Time - p.Delay;
            if (p.Skipped || age <= 0f) continue;
            float elapsed = MathF.Min(age, dt);
            float lifetime = p.Component.Piece switch
            {
                EffectEmitter emitter => emitter.Duration,
                EffectQuad quad => quad.Lifetime,
                EffectLight light => light.Lifetime,
                EffectPackage package => package.TimelineDuration,
                _ => 0f,
            };
            changed |= AdvanceSpin(p.Component.Spin, age, elapsed, lifetime, p.SpinRandom, ref p.Spin);
            if (p.Component.Piece is EffectPackage child)
                changed |= AdvanceSpin(child.Scalar("Spin"), age, elapsed, lifetime, p.PackageSpinRandom, ref p.PackageSpin);
        }
        if (changed) Place(reroll: false);
    }

    private static bool AdvanceSpin(EffectScalarTrack? track, float age, float dt, float lifetime, float random, ref float angle)
    {
        if (track is not { Keys.Length: > 0 }) return false;
        float rate = track.Evaluate(TrackTime(age - dt * 0.5f, lifetime, track.Word1), random);
        if (rate == 0f) return false;
        angle = IntegrateSpin(angle, rate, dt);
        return true;
    }

    private static float IntegrateSpin(float angle, float degreesPerSecond, float dt) =>
        MathF.IEEERemainder(angle + degreesPerSecond * (MathF.PI / 180f) * dt, MathF.Tau);

    /// <summary>
    /// Where an emitter track is in its cycle or emission window.
    /// </summary>
    private float CurveTime(EmitterInstance e, float age, uint word1)
    {
        float duration = e.Emitter.Duration;
        // 0x69D430 keeps a positive timing word for a multi-key track. With neither
        // a timing word nor an emitter duration the binder keeps only the first key.
        if (UsesAcitTiming) return TrackTime(age, duration, word1);
        if (duration > 1e-4f) return Math.Clamp(age / duration, 0f, 1f);
        if (e.Cycle > 1e-4f) return Math.Clamp(age / e.Cycle, 0f, 1f);
        float period = Settings.ContinuousCurvePeriod;
        return period > 1e-3f ? age / period % 1f : 0f;
    }

    /// <summary>Sample a track's explicit timing window, or use the lifetime of the track's owner.</summary>
    private static float TrackTime(float age, float lifetime, uint word1, bool repeat = true)
    {
        float period = BitConverter.UInt32BitsToSingle(word1);
        if (float.IsNormal(period) && period > 0f)
        {
            float phase = age / period;
            return repeat ? phase % 1f : Math.Clamp(phase, 0f, 1f);
        }
        return lifetime > 0f ? Math.Clamp(age / lifetime, 0f, 1f) : 0f;
    }

    /// <summary>
    /// An emitter (re)starts: its first particle leaves at once (spawn debt 1) and a new cycle is drawn.
    /// STRONG: 3 541 of the 10 341 distinct burst emitters have rate x duration &lt; 1 - (0.1 s, 5/s) alone is
    /// the most common burst, 2 590 emitters - and would otherwise never emit.
    /// </summary>
    private void Begin(EmitterInstance e)
    {
        e.SpawnDebt = 1f;
        e.Cycle = e.CycleMax > 1e-4f ? e.CycleMin + (e.CycleMax - e.CycleMin) * _random.NextSingle() : 0f;
    }

    private void StepEmitter(EmitterInstance e, float dt, int gameFrames, ref int budget)
    {
        EffectEmitter emitter = e.Emitter;
        if (Time < e.Delay) return;
        if (e.Age < 0) { e.Age = 0; dt = Math.Clamp(Time - e.Delay, 0f, dt); Begin(e); }

        // Age and move the live particles: the class overlays' update block (A_14 0xF90), in its order.
        Vector3 accel = AccelerationOf(e) - Vector3.UnitY * Settings.Gravity;
        float thrust = emitter.Thrust * dt;
        float keep = emitter.Drag != 0f ? MathF.Exp(-emitter.Drag * dt) : 1f;
        float turbulence = emitter.Turbulence;
        float speedMin = emitter.SpeedMin, speedMax = MathF.Max(emitter.SpeedMin, emitter.SpeedMax);
        // Beams (class 2), as the retail Beam overlay A_15 runs them (0xE88..0x1040, executed in spuemu): the old tail is
        // head + beam x exp(-follow dt) (S2[0] = 0x5298), the head moves, then the beam = the direction from the new head
        // to that tail (+X when they meet) x the distance clamped to [min, max] - so the tail stays where it was until the
        // beam is at its longest, and a beam with no follow rate is the path from the particle's birth, capped.
        bool beam = emitter.EmitterClass == 2;
        float follow = beam ? MathF.Exp(-emitter.BeamFollow * dt) : 1f;
        float beamMin = emitter.BeamMinLength, beamMax = emitter.BeamMaxLength;
        EffectScalarTrack? spinTrack = e.ParticleSpin;
        for (int i = e.Particles.Count - 1; i >= 0; i--)
        {
            Particle p = e.Particles[i];
            p.Age += dt;
            if (p.Age >= p.Life) { e.Particles.RemoveAt(i); budget++; continue; }
            Vector3 tail = p.Position + p.Beam * follow;
            p.Position += p.Velocity * dt;
            Vector3 v = p.Velocity + accel * dt;
            float speed = v.Length();
            Vector3 dir = speed > 1e-6f ? v / speed : Vector3.UnitX;
            speed = Math.Clamp((speed + thrust) * keep, speedMin, speedMax);
            for (int f = 0; f < gameFrames && turbulence != 0f; f++)
            {
                Vector3 d = dir + EffectSpawnStyles.UnitSphere(_random) * turbulence;
                if (d.LengthSquared() > 1e-12f) dir = Vector3.Normalize(d);
            }
            p.Velocity = dir * speed;
            if (beam)
            {
                Vector3 d = tail - p.Position;
                float d2 = d.LengthSquared();
                float length = MathF.Max(MathF.Min(d2 > 0f ? MathF.Sqrt(d2) : 0f, beamMax), beamMin);
                p.Beam = (d2 > 9.97e-8f ? d / MathF.Sqrt(d2) : Vector3.UnitX) * length;
            }
            if (spinTrack != null)
            {
                float spinTime = UsesAcitTiming
                    ? TrackTime(p.Age, p.Life, spinTrack.Word1, repeat: false)
                    : p.Age / p.Life;
                float spin = spinTrack.Evaluate(spinTime, p.R4);
                // ACIT FXVis 0x3B98 reads the spin direction from a bit in the particle state.
                if (UsesAcitTiming)
                    p.Rotation = IntegrateSpin(p.Rotation, p.ReverseSpin ? -spin : spin, dt);
                else
                    p.Rotation += spin * (MathF.PI / 180f) * dt;
            }
        }

        // Finish the part of the step before each restart and carry the remaining time into the new cycle.
        while (e.Cycle > 1e-4f && e.Age + dt >= e.Cycle)
        {
            float beforeRestart = MathF.Max(0f, e.Cycle - e.Age);
            Emit(e, beforeRestart, ref budget);
            dt -= beforeRestart;
            e.Age = 0f;
            Begin(e);
        }
        Emit(e, dt, ref budget);
        e.Age += dt;
    }

    private void Emit(EmitterInstance e, float dt, ref int budget)
    {
        float duration = e.Emitter.Duration;
        if (duration > 1e-4f)
        {
            if (e.Age >= duration) return;
            dt = MathF.Min(dt, duration - e.Age);
        }
        if (e.Emitter.Scalar("Spawn rate") is not { } rate) return;
        float age = e.Age + dt * 0.5f;
        float perSecond = MathF.Max(0f, rate.Evaluate(CurveTime(e, age, rate.Word1), _random.NextSingle()));
        if (!(perSecond > 0f)) return;
        e.SpawnDebt += perSecond * dt;
        while (e.SpawnDebt >= 1f && budget > 0)
        {
            e.SpawnDebt -= 1f;
            e.Particles.Add(Spawn(e, age));
            e.Spawned++;
            budget--;
        }
        if (budget <= 0) e.SpawnDebt = 0;
    }

    /// <summary>
    /// A new particle by its spawn generator (<see cref="EffectSpawnStyles"/>), in the emitter's frame: +Z of the
    /// frame is the cone/ring axis, as in the game (FXVis 0x52C8 hands the overlays the instance matrix, not the
    /// +0x25C field). Velocity = direction x spawn speed (a uniform draw between the key's min and max - the game
    /// draws mean x (1 + spread x U(-1, 1)) at 0x6C10, the same distribution).
    /// </summary>
    private float SpawnValue(EmitterInstance e, string name, float age, float fallback, bool spread = false)
    {
        if (e.Emitter.Scalar(name) is not { } track) return fallback;
        float time = CurveTime(e, age, track.Word1);
        float value = track.Evaluate(time, _random.NextSingle(), fallback);
        return spread && UsesAcitTiming ? value * (1f + track.EvaluateExtra(time) * Signed()) : value;
    }

    private Particle Spawn(EmitterInstance e, float age)
    {
        EffectEmitter emitter = e.Emitter;
        float life = SpawnValue(e, "Spawn life", age, 1f, spread: true);
        float speed = SpawnValue(e, "Spawn speed", age, 0f, spread: true);
        float radius = SpawnValue(e, "Spawn radius", age, 0f);

        EffectSpawnStyles.Draw(emitter.SpawnStyle, _random, radius, emitter.ConeMinDegrees, emitter.ConeMaxDegrees,
            e.Spawned & 7, out Vector3 local, out Vector3 direction, oldEngine: emitter.OldEngineType > 0);
        return new Particle
        {
            Position = Vector3.Transform(local, e.Frame),
            Velocity = Vector3.TransformNormal(direction * speed, e.Frame),
            Life = life > 1e-3f ? life : 1f,
            // FXVis 0x6F58: the initial sprite angle is U x 2π.
            Rotation = _random.NextSingle() * MathF.Tau,
            R0 = _random.NextSingle(), R1 = _random.NextSingle(), R2 = _random.NextSingle(),
            R3 = _random.NextSingle(), R4 = _random.NextSingle(),
            ReverseSpin = UsesAcitTiming && e.ParticleSpin != null && _random.Next(2) != 0,
        };
    }

    /// <summary>The emitter's acceleration: its mode's axis (a world axis as is, a frame axis through the
    /// instance frame) x +0x33C.</summary>
    private static Vector3 AccelerationOf(EmitterInstance e)
    {
        EffectEmitter m = e.Emitter;
        if (m.Acceleration == 0f || !m.AccelerationAxis(out Vector3 axis, out bool world)) return Vector3.Zero;
        return (world ? axis : Vector3.TransformNormal(axis, e.Frame)) * m.Acceleration;
    }

    // Sampling a frame must not consume randomness from the simulation. Repeated draws of a paused
    // or culled effect also need to agree about flicker.
    private float Flicker(float seed)
    {
        uint hash = unchecked(BitConverter.SingleToUInt32Bits(seed) + (uint)_simulationFrame * 0x9E3779B9u);
        hash = unchecked((hash ^ (hash >> 16)) * 0x21F0AAADu);
        return (hash >> 8) * (1f / 16777216f);
    }

    /// <summary>Appends this frame's sprites and light markers.</summary>
    public void Collect(List<EffectBillboard> billboards, List<EffectLightMarker> lights)
    {
        Vector4 tint = Package.Color("Tint")?.Evaluate(0f, 0f) ?? Vector4.One;

        if (Settings.ShowEmitters)
        {
            foreach (EmitterInstance e in _emitters)
            {
                if (!Plays(e.Emitter) || e.Age < 0) continue;
                if (e.Emitter.IsDistortion && !Settings.ShowDistortion) continue;
                EffectEmitter m = e.Emitter;
                var emitterColour = m.Color("Emitter colour");
                var emitterAlpha = m.Scalar("Emitter alpha");
                var emitterScale = m.Scalar("Emitter scale");
                Vector4 emtColor = emitterColour?.Evaluate(CurveTime(e, e.Age, emitterColour.Word1), 0f) ?? Vector4.One;
                float emtAlpha = emitterAlpha?.Evaluate(CurveTime(e, e.Age, emitterAlpha.Word1), 0f, 1f) ?? 1f;
                float emtScale = emitterScale?.Evaluate(CurveTime(e, e.Age, emitterScale.Word1), 0f, 1f) ?? 1f;
                EffectColorTrack? pColor = m.Color("Particle colour");
                EffectScalarTrack? pAlpha = m.Scalar("Particle alpha");
                EffectScalarTrack? pSize = m.Scalar("Particle size");
                bool colorFlicker = pColor?.Keys.Any(k => k.RerollsEveryFrame) == true;
                bool alphaFlicker = pAlpha?.Keys.Any(k => k.RerollsEveryFrame) == true;
                EffectTexture? tex = m.PrimaryTexture;
                int frames = Math.Max(1, Math.Min(m.TextureCount, m.Textures.Count));
                // Shaders 10 and 13 lay each particle in the emitter's frame turned by +0x31F (FXVis's B_19 / B_22 call
                // orient(0x17068) on the emitter's instance x parent-slot matrix, or on identity for an attached piece);
                // every other shader faces the camera (B_9...: no frame read).
                bool oriented = m.IsFrameOriented;
                (Vector3 planeX, Vector3 planeY) = oriented
                    ? EffectSpace.PlaneOf(EffectSpace.Orient(m.FacingMode, Rotation(e.Frame), ParentRotation(e.Placement)))
                    : (Vector3.Zero, Vector3.Zero);

                // A Crack in Time class 4 (shader 5) joins each particle to the next one spawned with a ribbon: 1,056 of its
                // 1,945 emitters carry a texture whose alpha only varies across U, and SCEPTER_STASIS's spinning spawns
                // show as continuous spirals in game. INFERRED; the trail overlay is not read.
                bool ribbon = UsesAcitTiming && m.EmitterClass == 4;
                for (int i = 0; i < e.Particles.Count; i++)
                {
                    Particle p = e.Particles[i];
                    if (ribbon && i + 1 >= e.Particles.Count) break;
                    float tl = p.Age / p.Life;
                    Vector4 c = (pColor?.Evaluate(tl, colorFlicker ? Flicker(p.R0) : p.R0) ?? Vector4.One) * emtColor * tint;
                    float a = (pAlpha?.Evaluate(tl, alphaFlicker ? Flicker(p.R1) : p.R1, 1f) ?? 1f) * emtAlpha;
                    // A timed particle size curve holds its final key through the rest of that particle's life.
                    float size = (pSize?.Evaluate(UsesAcitTiming
                        ? TrackTime(p.Age, p.Life, pSize.Word1, repeat: false)
                        : tl, p.R2, 0.1f) ?? 0.1f) * emtScale;
                    if (size <= 0f || c.W * a <= 0.002f) continue;
                    EffectTexture? frameTex = e.TextureChoice >= 0 ? m.Textures[e.TextureChoice]
                        : frames > 1 ? m.Textures[Math.Min(frames - 1, (int)(tl * frames))] : tex;
                    billboards.Add(new EffectBillboard
                    {
                        Position = p.Position,
                        Stretch = ribbon ? e.Particles[i + 1].Position - p.Position
                            : m.EmitterClass == 2 ? p.Beam : StretchOf(m, p.Velocity),
                        HalfSize = size * 0.5f,
                        Rotation = p.Rotation,
                        Color = new Vector4(c.X, c.Y, c.Z, c.W * a),
                        // A ToD Distort texture is a coloured UV-offset map the game never shows (program 0x2F2 shifts the
                        // scene copy with it): the faint hint takes the soft fallback, not the map's colours.
                        TextureIndex = m.IsDistortion && m.OldEngineType > 0 ? -1 : frameTex?.DrawTextureIndex ?? -1,
                        Blend = frameTex?.SpriteBlend ?? EffectSpriteBlend.Additive,
                        Distortion = m.IsDistortion,
                        Oriented = oriented && m.EmitterClass != 2,
                        AxisX = planeX,
                        AxisY = planeY,
                        Source = m,
                    });
                }
            }
        }

        if (Settings.ShowQuads)
        {
            foreach (QuadInstance q in _quads)
            {
                if (!Plays(q.Quad) || q.Age < 0) continue;
                EffectQuad quad = q.Quad;
                if (quad.Lifetime > 0f && q.Age > quad.Lifetime) continue;
                EffectColorTrack? color = quad.Color("Colour");
                EffectScalarTrack? alpha = quad.Scalar("Alpha");
                EffectScalarTrack? size = quad.Scalar("Size");
                float tq = TrackTime(q.Age, quad.Lifetime, quad.Lifetime > 0f ? 0 : alpha?.Word1 ?? 0);
                bool flicker = alpha?.Keys.Any(k => k.RerollsEveryFrame) == true;
                Vector4 c = (color?.Evaluate(UsesAcitTiming ? TrackTime(q.Age, quad.Lifetime, color.Word1) : tq, q.R0) ?? Vector4.One) * tint;
                float a = alpha?.Evaluate(UsesAcitTiming ? TrackTime(q.Age, quad.Lifetime, alpha.Word1) : tq, flicker ? Flicker(q.R1) : q.R1, 1f) ?? 1f;
                float s = size?.Evaluate(UsesAcitTiming ? TrackTime(q.Age, quad.Lifetime, size.Word1) : tq, q.R2, 1f) ?? 1f;
                EffectScalarTrack? widthTrack = UsesAcitTiming ? quad.Scalar("Width") : null;
                float width = widthTrack?.Evaluate(TrackTime(q.Age, quad.Lifetime, widthTrack.Word1), q.R2, 1f) ?? 1f;
                EffectTexture? tex = quad.PrimaryTexture;
                (Vector3 planeX, Vector3 planeY) = quad.IsFrameOriented ? EffectSpace.PlaneOf(q.Frame) : (Vector3.Zero, Vector3.Zero);
                if (quad.IsAxialBillboard)
                    // The local +Z axis is the upright direction of the axial quad.
                    planeY = Vector3.Normalize(new Vector3(q.Frame.M31, q.Frame.M32, q.Frame.M33));
                billboards.Add(new EffectBillboard
                {
                    Position = q.Position,
                    Oriented = quad.IsFrameOriented,
                    Axial = quad.IsAxialBillboard,
                    AxisX = planeX,
                    AxisY = planeY,
                    HalfSize = s * 0.5f,
                    HalfWidth = width > 0f ? s * width * 0.5f : 0f,
                    Rotation = quad.IsFrameOriented || quad.IsAxialBillboard ? 0f : _placements[q.Placement].Spin,
                    Color = new Vector4(c.X, c.Y, c.Z, c.W * a),
                    TextureIndex = tex?.DrawTextureIndex ?? -1,
                    Blend = tex?.SpriteBlend ?? EffectSpriteBlend.Additive,
                    Source = quad,
                });
            }
        }

        if (Settings.ShowLights && (!UsesAcitTiming || !Finished))
        {
            foreach (LightInstance l in _lights)
            {
                if (!Plays(l.Light) || l.Age < 0) continue;
                if (l.Light.Lifetime > 0f && l.Age > l.Light.Lifetime) continue;
                var colour = l.Light.Color("Colour");
                var size = l.Light.Scalar("Outer radius?");
                Vector4 c = colour?.Evaluate(UsesAcitTiming ? TrackTime(l.Age, l.Light.Lifetime, colour.Word1) : 0f,
                    l.ColourRandom) ?? Vector4.One;
                float radius = size?.Evaluate(UsesAcitTiming ? TrackTime(l.Age, l.Light.Lifetime, size.Word1) : 0f,
                    l.RadiusRandom, 1f) ?? 1f;
                lights.Add(new EffectLightMarker { Position = l.Position, Color = c, Radius = radius, Source = l.Light });
            }
        }
    }

    /// <summary>
    /// Radius that frames the effect, MEASURED: a private copy plays <paramref name="seconds"/> and the
    /// 85th percentile of every sprite's distance from the origin (plus its half-size) is returned. The
    /// analytic <see cref="EstimateExtent"/> over-frames effects with one fast, rare emitter or a big
    /// light, which left the interesting part a few pixels wide.
    /// </summary>
    public float MeasureExtent(float seconds = 1.5f)
    {
        var probe = new EffectSimulator(_library, Package,
            new EffectSimulationSettings { MaxParticles = 4000, SpawnFrame = Settings.SpawnFrame }, _seed);
        var sprites = new List<EffectBillboard>();
        var lights = new List<EffectLightMarker>();
        var reach = new List<float>();
        for (float t = 0f; t < seconds; t += 0.05f)
        {
            probe.Step(0.05f);
            sprites.Clear();
            lights.Clear();
            probe.Collect(sprites, lights);
            foreach (EffectBillboard s in sprites)
                if (s.Color.W > 0.01f) reach.Add(s.Position.Length() + s.HalfSize + s.Stretch.Length() * 0.5f);
            foreach (EffectLightMarker l in lights)
                reach.Add(l.Position.Length() + 0.1f);
        }
        if (reach.Count == 0) return EstimateExtent();
        reach.Sort();
        return Math.Clamp(reach[(int)(reach.Count * 0.85f)], 0.1f, 200f);
    }

    /// <summary>
    /// Radius of the sphere around the origin that holds every sprite and light of a private play of
    /// <paramref name="seconds"/> (from the steady state for a continuous effect), plus their half-sizes. Not
    /// clamped: an effect with pieces offset far away reaches that far.
    /// </summary>
    public float MeasureReach(float seconds = 2f)
    {
        var probe = new EffectSimulator(_library, Package,
            new EffectSimulationSettings { MaxParticles = 2000, SpawnFrame = Settings.SpawnFrame }, _seed);
        probe.Prewarm();
        var sprites = new List<EffectBillboard>();
        var lights = new List<EffectLightMarker>();
        float reach = 0f;
        for (float t = 0f; t < seconds; t += 0.1f)
        {
            probe.Step(0.1f);
            sprites.Clear();
            lights.Clear();
            probe.Collect(sprites, lights);
            foreach (EffectBillboard s in sprites)
                reach = MathF.Max(reach, s.Position.Length() + s.HalfSize + s.Stretch.Length());
            foreach (EffectLightMarker l in lights)
                reach = MathF.Max(reach, l.Position.Length() + l.Radius);
        }
        return reach > 0f && float.IsFinite(reach) ? reach : EstimateExtent();
    }

    /// <summary>
    /// The tail of a streak (class 3): back along the velocity, one 60 Hz frame per streak point. INFERRED (the Streak
    /// overlay A_16 is not traced yet); beams have their own tail (<see cref="Particle.Beam"/>).
    /// </summary>
    private static Vector3 StretchOf(EffectEmitter emitter, Vector3 velocity)
    {
        if (emitter.EmitterClass != 3 || velocity.LengthSquared() < 1e-8f) return Vector3.Zero;
        return -velocity * (Math.Clamp(emitter.StreakPoints, 1, 64) / 60f);
    }

    /// <summary>Radius that frames the effect: offsets, spawn radius + speed x life, sizes.</summary>
    public float EstimateExtent()
    {
        float extent = 0.25f;
        foreach (EmitterInstance e in _emitters)
        {
            EffectEmitter m = e.Emitter;
            float life = MaxOf(m.Scalar("Spawn life"), 1f), speed = MaxOf(m.Scalar("Spawn speed"), 0f);
            // With drag the travel is speed / k x (1 - e^-k life), not speed x life.
            float travel = m.Drag > 1e-4f ? speed / m.Drag * (1f - MathF.Exp(-m.Drag * life)) : speed * life;
            float reach = MaxOf(m.Scalar("Spawn radius"), 0f) + travel + MaxOf(m.Scalar("Particle size"), 0.1f);
            extent = MathF.Max(extent, e.Frame.Translation.Length() + reach);
        }
        foreach (QuadInstance q in _quads)
            extent = MathF.Max(extent, q.Position.Length() + MaxOf(q.Quad.Scalar("Size"), 1f) * 0.5f);
        return Math.Clamp(extent, 0.25f, 200f);
    }
}
