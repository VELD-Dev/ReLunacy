// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Numerics;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>One sprite the preview draws: camera-facing, laid in a plane (<see cref="Oriented"/>), or a beam.</summary>
public struct EffectBillboard
{
    public Vector3 Position;
    /// <summary>Half the side of the square, world units.</summary>
    public float HalfSize;
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
    /// emitter-life curves. ASSUMED: 410 such emitters carry multi-key curves (on/off alpha patterns over
    /// [0, 1]) and nothing in effect.dat names their period - the game's igParticle SPU job decides.</summary>
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
        if (sprite.Oriented)
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
    }

    private sealed class EmitterInstance
    {
        public required EffectEmitter Emitter;
        public required int Placement;
        /// <summary>World-axis frame of this play (rows = local axes, translation = position).</summary>
        public Matrix4x4 Frame = Matrix4x4.Identity;
        public required float Delay;
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
        public required float Delay;
        public float Age = -1;
        public float R0, R1, R2;
    }

    private sealed class LightInstance
    {
        public required EffectLight Light;
        public required int Placement;
        public Vector3 Position;
        public required float Delay;
        public float Age = -1;
    }

    private readonly EffectLibrary _library;
    private readonly List<Placement> _placements = [];
    private readonly List<EmitterInstance> _emitters = [];
    private readonly List<QuadInstance> _quads = [];
    private readonly List<LightInstance> _lights = [];
    private Random _random;
    private readonly int _seed;
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
        Instantiate(package, -1, 0f, 0);
        Place();
        ComputePlayLength();
    }

    private void Instantiate(EffectPackage package, int parent, float delay, int depth)
    {
        if (depth > 6) return;
        foreach (EffectComponent component in package.Components)
        {
            int placement = _placements.Count;
            _placements.Add(new Placement { Component = component, Parent = parent });
            float start = delay + MathF.Max(0f, component.StartDelay);
            switch (component.Piece)
            {
                case EffectEmitter { Disabled: true }:
                    break;
                case EffectEmitter emitter:
                    _emitters.Add(new EmitterInstance
                    {
                        Emitter = emitter, Placement = placement, Delay = start,
                        CycleMin = MathF.Max(0f, MathF.Min(component.CycleMin, component.CycleMax)),
                        CycleMax = MathF.Max(0f, component.CycleMax),
                    });
                    break;
                case EffectQuad quad:
                    _quads.Add(new QuadInstance { Quad = quad, Placement = placement, Delay = start });
                    break;
                case EffectLight light:
                    _lights.Add(new LightInstance { Light = light, Placement = placement, Delay = start });
                    break;
                case EffectPackage child when !ReferenceEquals(child, package):
                    Instantiate(child, placement, start, depth + 1);
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
    private void Place()
    {
        Matrix4x4 spawn = Rotation(Settings.SpawnFrame);
        foreach (Placement p in _placements)
        {
            Matrix4x4 slot = p.Parent < 0 ? spawn : _placements[p.Parent].ChildSlot;
            Vector3 origin = slot.Translation;
            Matrix4x4 s = Rotation(slot);
            EffectComponent c = p.Component;
            p.Skipped = p.Parent >= 0 && _placements[p.Parent].Skipped
                || c.SpawnChance < 1f && _random.NextSingle() >= c.SpawnChance;
            bool ownFrame = c.Attached || c.Type == EffectPieceType.Emitter;
            if (c.OldEngine)
            {
                PlaceOldEngine(p, c, ownFrame, origin, s);
                continue;
            }
            Matrix4x4 m = ownFrame
                ? EffectSpace.Orient(c.OrientationMode, Matrix4x4.Identity, Matrix4x4.Transpose(s))
                : EffectSpace.Orient(c.OrientationMode, s, Matrix4x4.Identity);
            float tilt = Lerp(c.TiltMinDegrees, c.TiltMaxDegrees, _random.NextSingle());
            float azimuth = Lerp(c.AzimuthMinDegrees, c.AzimuthMaxDegrees, _random.NextSingle());
            m = EffectSpace.Tilt(m, tilt, azimuth);
            Matrix4x4 offsetFrame = ownFrame
                ? EffectSpace.Orient(c.OffsetMode, m, Matrix4x4.Identity)
                : EffectSpace.Orient(c.OffsetMode, s, Matrix4x4.Identity);
            Vector3 v = c.Offset * (Vector3.One + c.OffsetSpread * new Vector3(Signed(), Signed(), Signed()));
            Vector3 position = origin + Vector3.TransformNormal(Vector3.TransformNormal(v, offsetFrame), s);
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
            int textures = Math.Min(e.Emitter.TextureCount, e.Emitter.Textures.Count);
            e.TextureChoice = e.Emitter.OldEngineType > 0 && textures > 0 ? Math.Min(textures - 1, (int)(_random.NextSingle() * textures)) : -1;
        }
        foreach (QuadInstance q in _quads)
        {
            q.Frame = _placements[q.Placement].Frame;
            q.Position = q.Frame.Translation;
        }
        foreach (LightInstance l in _lights) l.Position = _placements[l.Placement].Frame.Translation;
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
        float tilt = Lerp(c.TiltMinDegrees, c.TiltMaxDegrees, _random.NextSingle());
        float azimuth = Lerp(c.AzimuthMinDegrees, c.AzimuthMaxDegrees, _random.NextSingle());
        Vector3 v = c.Offset * (Vector3.One + Vector3.Clamp(c.OffsetSpread, Vector3.Zero, Vector3.One) * new Vector3(Signed(), Signed(), Signed()));
        Matrix4x4 m;
        Vector3 position;
        if (ownFrame)
        {
            m = EffectSpace.Tilt(EffectSpace.OrientOldEngine(c.OrientationMode, Matrix4x4.Identity), tilt, azimuth);
            if (c.OrientationMode > 5) m *= Matrix4x4.Transpose(s);
            Matrix4x4 offsetFrame = EffectSpace.OrientOldEngine(c.OffsetMode, m);
            position = origin + Vector3.TransformNormal(Vector3.TransformNormal(v, offsetFrame), s);
        }
        else
        {
            m = EffectSpace.Tilt(EffectSpace.OrientOldEngine(c.OrientationMode, s), tilt, azimuth);
            Vector3 offset = Vector3.TransformNormal(v, EffectSpace.OrientOldEngine(c.OffsetMode, s));
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
        return Rotation(parent < 0 ? Settings.SpawnFrame : _placements[parent].ChildSlot);
    }
    private float Signed() => 2f * _random.NextSingle() - 1f;

    private void ComputePlayLength()
    {
        float end = 0f, steady = 0f;
        bool forever = false;
        foreach (EmitterInstance e in _emitters)
        {
            float life = MaxOf(e.Emitter.Scalar("Spawn life"), 1f);
            if (e.Emitter.Duration <= 1e-4f || e.CycleMax > 1e-4f)
            {
                forever = true;
                steady = MathF.Max(steady, e.Delay + life);
            }
            else end = MathF.Max(end, e.Delay + e.Emitter.Duration + life);
        }
        foreach (QuadInstance q in _quads)
        {
            if (q.Quad.Lifetime <= 0f) forever = true;
            else end = MathF.Max(end, q.Delay + q.Quad.Lifetime);
        }
        Continuous = forever;
        PlayLength = end;
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
        Place();
    }

    public void Reset()
    {
        Time = 0f;
        _frameClock = 0f;
        _random = new Random(_seed);
        foreach (EmitterInstance e in _emitters) { e.Age = -1; e.SpawnDebt = 0; e.Cycle = 0; e.Particles.Clear(); e.Spawned = 0; }
        foreach (QuadInstance q in _quads) q.Age = -1;
        foreach (LightInstance l in _lights) l.Age = -1;
        ParticleCount = 0;
        Place();
    }

    /// <summary>True once every bounded piece has finished and no particle is alive.</summary>
    public bool Finished => !Continuous && Time > PlayLength && ParticleCount == 0;

    private bool Plays(EffectPiece piece) => Settings.Solo == null || ReferenceEquals(Settings.Solo, piece);

    public void Step(float dt)
    {
        dt = Math.Clamp(dt * Settings.TimeScale, 0f, 0.1f);
        Time += dt;
        // Turbulence is applied once per game frame (not scaled by dt): count the frames this step spans.
        float rate = Math.Max(1f, Settings.GameFrameRate);
        _frameClock += dt * rate;
        int frames = (int)_frameClock;
        _frameClock -= frames;
        int total = 0;
        foreach (EmitterInstance e in _emitters)
        {
            if (_placements[e.Placement].Skipped) continue;
            StepEmitter(e, dt, Math.Min(frames, 4));
            total += e.Particles.Count;
        }
        foreach (QuadInstance q in _quads)
        {
            if (Time < q.Delay || _placements[q.Placement].Skipped) continue;
            if (q.Age < 0) { q.Age = 0; q.R0 = _random.NextSingle(); q.R1 = _random.NextSingle(); q.R2 = _random.NextSingle(); }
            else q.Age += dt;
        }
        foreach (LightInstance l in _lights)
        {
            if (Time < l.Delay || _placements[l.Placement].Skipped) continue;
            l.Age = l.Age < 0 ? 0 : l.Age + dt;
        }
        ParticleCount = total;
    }

    /// <summary>
    /// Where an emitter is on its emitter-life curves, in [0, 1]: over its duration for a burst, over its
    /// component cycle for a cycled continuous emitter, else looping over
    /// <see cref="EffectSimulationSettings.ContinuousCurvePeriod"/>. (The spawn-rate track's two words after
    /// the key pointer, once read as a period here, are 0 on every emitter of the corpus.)
    /// </summary>
    private float CurveTime(EmitterInstance e)
    {
        float duration = e.Emitter.Duration;
        if (duration > 1e-4f) return Math.Clamp(e.Age / duration, 0f, 1f);
        if (e.Cycle > 1e-4f) return Math.Clamp(e.Age / e.Cycle, 0f, 1f);
        float period = Settings.ContinuousCurvePeriod;
        return period > 1e-3f ? e.Age / period % 1f : 0f;
    }

    /// <summary>A quad's place on its curves: over its lifetime, else over the period its alpha track's second
    /// word holds when that is a float, else frozen at 0.</summary>
    private static float QuadTime(float age, float lifetime, uint word1)
    {
        if (lifetime > 1e-4f) return Math.Clamp(age / lifetime, 0f, 1f);
        float period = BitConverter.UInt32BitsToSingle(word1);
        if (float.IsNormal(period) && period > 1e-3f) return age / period % 1f;
        return 0f;
    }

    /// <summary>
    /// An emitter (re)starts: its first particle leaves at once (spawn debt 1) and a new cycle is drawn.
    /// STRONG: 3 541 of the 10 341 distinct burst emitters have rate x duration &lt; 1 - (0.1 s, 5/s) alone is
    /// the most common burst, 2 590 emitters - and would otherwise never emit.
    /// </summary>
    private void Begin(EmitterInstance e)
    {
        e.SpawnDebt = MathF.Max(e.SpawnDebt, 1f);
        e.Cycle = e.CycleMax > 1e-4f ? e.CycleMin + (e.CycleMax - e.CycleMin) * _random.NextSingle() : 0f;
    }

    private void StepEmitter(EmitterInstance e, float dt, int gameFrames)
    {
        EffectEmitter emitter = e.Emitter;
        if (Time < e.Delay) return;
        if (e.Age < 0) { e.Age = 0; Begin(e); }
        else e.Age += dt;
        if (e.Cycle > 1e-4f && e.Age >= e.Cycle) { e.Age -= e.Cycle; Begin(e); }

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
        EffectScalarTrack? spinTrack = emitter.Scalar("Particle spin");
        for (int i = e.Particles.Count - 1; i >= 0; i--)
        {
            Particle p = e.Particles[i];
            p.Age += dt;
            if (p.Age >= p.Life) { e.Particles.RemoveAt(i); continue; }
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
            float spin = spinTrack?.Evaluate(p.Age / p.Life, p.R4) ?? 0f;
            p.Rotation += spin * MathF.PI / 180f * dt;
        }

        bool emitting = emitter.Duration <= 1e-4f || e.Age <= emitter.Duration;
        if (!emitting) return;

        EffectScalarTrack? rate = emitter.Scalar("Spawn rate");
        float te = CurveTime(e);
        float perSecond = MathF.Max(0f, rate?.Evaluate(te, _random.NextSingle()) ?? 0f);
        e.SpawnDebt += perSecond * dt;
        int budget = Settings.MaxParticles - ParticleCount;
        while (e.SpawnDebt >= 1f && budget-- > 0)
        {
            e.SpawnDebt -= 1f;
            e.Particles.Add(Spawn(e, te));
            e.Spawned++;
        }
        if (budget <= 0) e.SpawnDebt = 0;
    }

    /// <summary>
    /// A new particle by its spawn generator (<see cref="EffectSpawnStyles"/>), in the emitter's frame: +Z of the
    /// frame is the cone/ring axis, as in the game (FXVis 0x52C8 hands the overlays the instance matrix, not the
    /// +0x25C field). Velocity = direction x spawn speed (a uniform draw between the key's min and max - the game
    /// draws mean x (1 + spread x U(-1, 1)) at 0x6C10, the same distribution).
    /// </summary>
    private Particle Spawn(EmitterInstance e, float te)
    {
        EffectEmitter emitter = e.Emitter;
        float life = emitter.Scalar("Spawn life")?.Evaluate(te, _random.NextSingle(), 1f) ?? 1f;
        float speed = emitter.Scalar("Spawn speed")?.Evaluate(te, _random.NextSingle()) ?? 0f;
        float radius = emitter.Scalar("Spawn radius")?.Evaluate(te, _random.NextSingle()) ?? 0f;

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
                float te = CurveTime(e);
                Vector4 emtColor = m.Color("Emitter colour")?.Evaluate(te, 0f) ?? Vector4.One;
                float emtAlpha = m.Scalar("Emitter alpha")?.Evaluate(te, 0f, 1f) ?? 1f;
                float emtScale = m.Scalar("Emitter scale")?.Evaluate(te, 0f, 1f) ?? 1f;
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

                foreach (Particle p in e.Particles)
                {
                    float tl = p.Age / p.Life;
                    Vector4 c = (pColor?.Evaluate(tl, colorFlicker ? _random.NextSingle() : p.R0) ?? Vector4.One) * emtColor * tint;
                    float a = (pAlpha?.Evaluate(tl, alphaFlicker ? _random.NextSingle() : p.R1, 1f) ?? 1f) * emtAlpha;
                    float size = (pSize?.Evaluate(tl, p.R2, 0.1f) ?? 0.1f) * emtScale;
                    if (size <= 0f || c.W * a <= 0.002f) continue;
                    EffectTexture? frameTex = e.TextureChoice >= 0 ? m.Textures[e.TextureChoice]
                        : frames > 1 ? m.Textures[Math.Min(frames - 1, (int)(tl * frames))] : tex;
                    billboards.Add(new EffectBillboard
                    {
                        Position = p.Position,
                        Stretch = m.EmitterClass == 2 ? p.Beam : StretchOf(m, p.Velocity),
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
                float tq = QuadTime(q.Age, quad.Lifetime, alpha?.Word1 ?? 0);
                bool flicker = alpha?.Keys.Any(k => k.RerollsEveryFrame) == true;
                Vector4 c = (color?.Evaluate(tq, q.R0) ?? Vector4.One) * tint;
                float a = alpha?.Evaluate(tq, flicker ? _random.NextSingle() : q.R1, 1f) ?? 1f;
                float s = size?.Evaluate(tq, q.R2, 1f) ?? 1f;
                EffectTexture? tex = quad.PrimaryTexture;
                (Vector3 planeX, Vector3 planeY) = quad.IsFrameOriented ? EffectSpace.PlaneOf(q.Frame) : (Vector3.Zero, Vector3.Zero);
                billboards.Add(new EffectBillboard
                {
                    Position = q.Position,
                    Oriented = quad.IsFrameOriented,
                    AxisX = planeX,
                    AxisY = planeY,
                    HalfSize = s * 0.5f,
                    Color = new Vector4(c.X, c.Y, c.Z, c.W * a),
                    TextureIndex = tex?.DrawTextureIndex ?? -1,
                    Blend = tex?.SpriteBlend ?? EffectSpriteBlend.Additive,
                    Source = quad,
                });
            }
        }

        if (Settings.ShowLights)
        {
            foreach (LightInstance l in _lights)
            {
                if (!Plays(l.Light) || l.Age < 0) continue;
                if (l.Light.Lifetime > 0f && l.Age > l.Light.Lifetime) continue;
                Vector4 c = l.Light.Color("Colour")?.Evaluate(0f, _random.NextSingle()) ?? Vector4.One;
                float radius = l.Light.Scalar("Outer radius?")?.Evaluate(0f, 0f, 1f) ?? 1f;
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
