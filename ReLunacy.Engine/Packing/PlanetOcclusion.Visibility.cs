using System.Numerics;

namespace ReLunacy.Engine.Packing;

/// <summary>How <see cref="PlanetOcclusion.Context.ComputeHidden"/> samples: <see cref="CameraSamples"/> points per axis
/// of a cell's box (corners included), <see cref="SphereSamples"/> points on the object's sphere grown by
/// <see cref="Grow"/> world units, plus its centre; cells farther than <see cref="MaxDistance"/> from the sphere are left
/// shown; a cell stays hidden only when every cell within <see cref="Margin"/> cells of it is hidden too; past
/// <see cref="TimeoutSeconds"/> the whole answer is dropped. The defaults are what validated best against retail sets
/// (krell_canyon, axiom_city: see dev/tools/planetcolprobe --validate): more samples barely change the result, the margin
/// and the growth take out most of the cells retail shows that collision hides.</summary>
public sealed record OcclusionSettings(int CameraSamples = 2, int SphereSamples = 24, float MaxDistance = 2047, double TimeoutSeconds = 20,
    int Margin = 2, float Grow = 4)
{
    public static readonly OcclusionSettings Default = new();
}

public static partial class PlanetOcclusion
{
    /// <summary>
    /// What hides what in a planet region, from its own collision: the 0x8300 grid and the collision (0x2900) as an
    /// <see cref="OcclusionScene"/>. Built once per region (under half a second on krell_canyon and axiom_city), then
    /// asked per object (0.4 to 0.6 s there, parallel over cells). Immutable once built: safe to ask from several threads.
    ///
    /// An object is hidden from a cell when every segment from a point of the cell's box (a grid of
    /// <see cref="OcclusionSettings.CameraSamples"/>³, corners included) to a point of its sphere (the centre and
    /// <see cref="OcclusionSettings.SphereSamples"/> points over the surface, grown by <see cref="OcclusionSettings.Grow"/>)
    /// crosses a collision triangle that is not the object's own, and the same holds for every cell around it (within
    /// <see cref="OcclusionSettings.Margin"/>). A cell the sphere comes within half a cell of is never hidden.
    ///
    /// Against retail sets (200 Ties each, every cell): this hides 90% (krell_canyon) and 86% (axiom_city) of what
    /// retail hides, and hides 0.09% / 0.26% of all (cell, Tie) pairs that retail shows. Those are not sampling misses
    /// (a 6³ x 400-point test still finds every line blocked) but collision that draws nothing, such as invisible walls,
    /// railings and glass, through which retail sees: the price of collision as the occluder.
    /// </summary>
    public sealed class Context
    {
        public Grid Grid { get; }
        public OcclusionScene Scene { get; }
        /// <summary>Each cell record's cell (x, y, z in cells from the origin); (-1, -1, -1) for a record no cell names.</summary>
        public (int X, int Y, int Z)[] CellOf { get; }
        private readonly Dictionary<(int, int, int), int> _recordAt = new();

        public Context(Grid grid, OcclusionScene scene)
        {
            Grid = grid;
            Scene = scene;
            CellOf = new (int, int, int)[grid.Records];
            Array.Fill(CellOf, (-1, -1, -1));
            foreach (var (i, c) in grid.Cells()) { CellOf[i] = c; _recordAt[c] = i; }
        }

        /// <summary>The context of a planet region's collision.dat, or null when it has no occlusion grid or collision.</summary>
        public static Context? FromCollisionFile(byte[] file, Func<Assets.Collision.CollisionInstance, bool>? occluder = null)
        {
            var occlusion = Read(file, out _);
            if (occlusion == null) return null;
            var scene = OcclusionScene.FromCollisionFile(file, occluder);
            return scene == null ? null : new Context(occlusion.Value.Grid, scene);
        }

        /// <summary>The collision instances of the Tie placed by <paramref name="placement"/> (its class
        /// <paramref name="classTuid"/>, 0: any): those a visibility test leaves out.</summary>
        public HashSet<int> TieInstances(Matrix4x4 placement, ulong classTuid = 0)
        {
            var set = new HashSet<int>();
            var collision = Scene.Collision;
            for (int i = 0; i < collision.Instances.Count; i++)
            {
                var instance = collision.Instances[i];
                if (!instance.IsTie || Vector3.DistanceSquared(instance.Transform.Translation, placement.Translation) > 1e-4f) continue;
                if (classTuid != 0 && instance.Id < collision.TieClassTuids.Count && collision.TieClassTuids[(int)instance.Id] != classTuid) continue;
                set.Add(i);
            }
            return set;
        }

        /// <summary>The cell's box in world space.</summary>
        public (Vector3 Min, Vector3 Max) CellBox(int record)
        {
            var (x, y, z) = CellOf[record];
            var min = new Vector3(x + Grid.Origin[0], y + Grid.Origin[1], z + Grid.Origin[2]) * Grid.CellSize;
            return (min, min + new Vector3(Grid.CellSize));
        }

        /// <summary>
        /// The cell records the object with world sphere <paramref name="sphere"/> (xyz centre, w radius) is hidden from,
        /// ascending; the collision instances <paramref name="ignore"/> holds (the object's own) hide nothing. Null when
        /// the time limit ran out or <paramref name="cancel"/> fired (then the object must be left never hidden).
        /// </summary>
        public int[]? ComputeHidden(Vector4 sphere, IReadOnlySet<int>? ignore = null, OcclusionSettings? settings = null,
            CancellationToken cancel = default)
        {
            settings ??= OcclusionSettings.Default;
            var centre = new Vector3(sphere.X, sphere.Y, sphere.Z);
            float radius = MathF.Max(sphere.W, 0);
            if (!float.IsFinite(radius) || !float.IsFinite(centre.X + centre.Y + centre.Z)) return null;
            var sampler = new Sampler(this, centre, radius, ignore, settings);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            stop.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var hidden = new bool[Grid.Records];
            try
            {
                Parallel.For(0, Grid.Records, new ParallelOptions { CancellationToken = stop.Token }, (record, state) =>
                {
                    if (!stop.IsCancellationRequested) hidden[record] = sampler.Hidden(record);
                });
            }
            catch (OperationCanceledException) { return null; }
            if (stop.IsCancellationRequested) return null;
            // Lines between samples can miss a gap: what is hidden from a cell must be hidden from its neighbours too.
            var list = new List<int>();
            int m = Math.Max(0, settings.Margin);
            for (int i = 0; i < hidden.Length; i++)
            {
                if (!hidden[i]) continue;
                var (x, y, z) = CellOf[i];
                bool keep = true;
                for (int dx = -m; dx <= m && keep; dx++)
                for (int dy = -m; dy <= m && keep; dy++)
                for (int dz = -m; dz <= m && keep; dz++)
                    if (_recordAt.TryGetValue((x + dx, y + dy, z + dz), out int o) && !hidden[o]) keep = false;
                if (keep) list.Add(i);
            }
            return list.ToArray();
        }

        /// <summary>Whether the object is hidden from the one cell record <paramref name="record"/> by the lines alone
        /// (no <see cref="OcclusionSettings.Margin"/>).</summary>
        public bool HiddenFrom(int record, Vector4 sphere, IReadOnlySet<int>? ignore = null, OcclusionSettings? settings = null) =>
            new Sampler(this, new Vector3(sphere.X, sphere.Y, sphere.Z), MathF.Max(sphere.W, 0), ignore, settings ?? OcclusionSettings.Default).Hidden(record);

        /// <summary>The lines of one object: its sphere's points, the points of a cell (centre first: most visible cells
        /// end on the first line).</summary>
        private sealed class Sampler
        {
            private readonly Context _context;
            private readonly Vector3 _centre;
            private readonly float _radius;
            private readonly Vector3[] _targets;
            private readonly Vector3[] _eyes;
            private readonly IReadOnlySet<int>? _ignore;
            private readonly OcclusionSettings _settings;

            public Sampler(Context context, Vector3 centre, float radius, IReadOnlySet<int>? ignore, OcclusionSettings settings)
            {
                (_context, _centre, _radius, _ignore, _settings) = (context, centre, radius, ignore, settings);
                _targets = SpherePoints(centre, radius + settings.Grow, settings.SphereSamples);
                int k = Math.Max(1, settings.CameraSamples);
                var f = new float[k];
                for (int i = 0; i < k; i++) f[i] = k == 1 ? 0.5f : 0.001f + 0.998f * i / (k - 1);
                _eyes = Enumerable.Range(0, k * k * k).Select(i => new Vector3(f[i % k], f[i / k % k], f[i / (k * k)]))
                    .OrderBy(v => MathF.Abs(v.X - 0.5f) + MathF.Abs(v.Y - 0.5f) + MathF.Abs(v.Z - 0.5f)).ToArray();
            }

            public bool Hidden(int record)
            {
                if (_context.CellOf[record].X < 0) return false;
                var (min, max) = _context.CellBox(record);
                float gap = Vector3.Distance(Vector3.Clamp(_centre, min, max), _centre) - _radius;
                if (gap <= _context.Grid.CellSize * 0.5f || gap > _settings.MaxDistance) return false;
                var size = max - min;
                foreach (var e in _eyes)
                {
                    var eye = min + size * e;
                    foreach (var target in _targets)
                        if (!_context.Scene.Blocked(eye, target, _ignore)) return false;
                }
                return true;
            }
        }

        /// <summary>The centre, then <paramref name="count"/> points spread over the sphere (a Fibonacci spiral).</summary>
        private static Vector3[] SpherePoints(Vector3 centre, float radius, int count)
        {
            var points = new Vector3[count + 1];
            points[0] = centre;
            float golden = MathF.PI * (3 - MathF.Sqrt(5));
            for (int i = 0; i < count; i++)
            {
                float y = 1 - 2 * (i + 0.5f) / count, r = MathF.Sqrt(1 - y * y), a = golden * i;
                points[i + 1] = centre + radius * new Vector3(r * MathF.Cos(a), y, r * MathF.Sin(a));
            }
            return points;
        }
    }

    /// <summary>The cell records of <paramref name="collisionFile"/>'s grid the object with world sphere
    /// <paramref name="sphere"/> is hidden from (see <see cref="Context.ComputeHidden"/>), for a Tie placed by
    /// <paramref name="placement"/> of class <paramref name="classTuid"/> (its own collision hides nothing). Null when
    /// the region has no occlusion or the time limit ran out. Builds a <see cref="Context"/>: keep one to ask often.</summary>
    public static int[]? ComputeHidden(byte[] collisionFile, Vector4 sphere, Matrix4x4? placement = null, ulong classTuid = 0,
        OcclusionSettings? settings = null, CancellationToken cancel = default)
    {
        var context = Context.FromCollisionFile(collisionFile);
        if (context == null) return null;
        var ignore = placement is { } m ? context.TieInstances(m, classTuid) : null;
        return context.ComputeHidden(sphere, ignore, settings, cancel);
    }
}
