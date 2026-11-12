using System.Numerics;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Scene;

/// <summary>How a spline key's two handles follow each other when one is dragged.</summary>
public enum PathHandleMode
{
    /// <summary>The other handle mirrors it: opposite direction, same length.</summary>
    Smooth,
    /// <summary>The other handle turns to stay opposite, keeping its own length.</summary>
    Aligned,
    /// <summary>The handles move independently (a corner).</summary>
    Free,
}

/// <summary>
/// A path (spline), drawn as the line through its points (VulkanRenderer.PathLines). The game keeps only
/// points, joined by straight segments, each carrying its distance along the path - the sum of the segments
/// before it (exact on every path of krell_canyon, creature_collector and haunted_city).
///
/// Most paths are cubic splines as exported: 10 samples per span, evenly spaced in the curve's parameter, so
/// a path of 10 x n + 1 points whose every 10-segment block is one cubic - to float precision (median 1e-6
/// of the block's size on all three games; a block shifted by 5 points misses by 1e-3) - is a spline with a
/// key at every 10th point. Its keys and Bezier handles are recovered here (each span's two inner control
/// points fitted to its 9 inner samples, the keys kept exactly), edits move keys and handles, and the spans
/// they touch are sampled again exactly as the export did. Any other path (hand-placed points, 1-2 point
/// paths, other counts) stays a polyline whose every point is a key.
///
/// Everything is kept relative to <see cref="Entity.Transform"/>, which starts at the points' centre: the
/// gizmo moves, turns or scales the whole path, and <see cref="EntityPathPoint"/> moves one key, handle or point.
/// </summary>
public class EntityPath : Entity
{
    public const int SamplesPerSpan = 10;
    /// <summary>How far a block may miss its cubic, relative to its size, and still count as one.</summary>
    private const float SplineTolerance = 1e-4f;

    public readonly GameplayPath Path;

    /// <summary>The colour EntityManager.PathColorMode gives it: its role's, its layer's, or the single path colour.</summary>
    public Vector4 Colour => EntityManager.Singleton.PathColorMode switch
    {
        PathColorMode.Role => new Vector4(PathRoles.Colour(Path.Role), 0.9f),
        PathColorMode.Layer when Path.Layer is { } layer => new Vector4(ClueClasses.ColourOf(layer), 0.9f),
        _ => new Vector4(PathRoles.Colour(PathRoles.Unassigned), 0.9f),
    };
    private readonly List<Vector3> _local;
    // Spline mode: keys, and per key its incoming (P2 of the span before) and outgoing (P1 of the span after) handle.
    private readonly List<Vector3> _keys = [], _in = [], _out = [];
    private readonly List<PathHandleMode> _modes = [];
    private Matrix4x4 _lastMatrix;

    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    /// <summary>Bumped by every change to the points' world positions, so the line buffer is rebuilt only then.</summary>
    public int Version { get; private set; }

    /// <summary>The points are a cubic spline's samples, edited through its keys and handles.</summary>
    public bool IsSpline { get; private set; }

    public EntityPath(GameplayPath path)
    {
        Path = path;
        Name = path.Name;
        var points = path.Points.Select(p => p.Position).ToList();
        Vector3 centre = points.Count > 0 ? points.Aggregate(Vector3.Zero, (a, b) => a + b) / points.Count : Vector3.Zero;
        _local = [.. points.Select(p => p - centre)];
        Transform = new Transform { Translation = centre, Rotation = Quaternion.Identity, Scale = Vector3.One };
        _lastMatrix = Transform.GetMatrix();
        BoundingSphere = new Vector4(Vector3.Zero, _local.Count > 0 ? MathF.Max(_local.Max(p => p.Length()), 0.5f) : 0.5f);
        IsSpline = TryRecoverSpline();
    }

    public int PointCount => _local.Count;
    public int KeyCount => IsSpline ? _keys.Count : _local.Count;
    public bool Closed => Path.Closed;

    public Vector3 PointAt(int index) => ToWorld(_local[index]);
    public Vector3 KeyAt(int key) => IsSpline ? ToWorld(_keys[key]) : PointAt(key);
    /// <summary>A key's incoming handle; null on the first key (and on a polyline).</summary>
    public Vector3? InHandleAt(int key) => IsSpline && key > 0 ? ToWorld(_in[key]) : null;
    /// <summary>A key's outgoing handle; null on the last key (and on a polyline).</summary>
    public Vector3? OutHandleAt(int key) => IsSpline && key < _keys.Count - 1 ? ToWorld(_out[key]) : null;
    public PathHandleMode ModeAt(int key) => IsSpline ? _modes[key] : PathHandleMode.Free;
    public void SetMode(int key, PathHandleMode mode) { if (IsSpline) _modes[key] = mode; }

    public IEnumerable<Vector3> WorldPoints()
    {
        var matrix = Transform.GetMatrix();
        foreach (var point in _local) yield return Vector3.Transform(point, matrix);
    }

    private Vector3 ToWorld(Vector3 local) => Vector3.Transform(local, Transform.GetMatrix());
    private Vector3 ToLocal(Vector3 world) =>
        Matrix4x4.Invert(Transform.GetMatrix(), out var inverse) ? Vector3.Transform(world, inverse) : world;

    /// <summary>Moves one point of a polyline path (on a spline, its keys and handles are what move).</summary>
    public void SetPoint(int index, Vector3 world)
    {
        if (IsSpline) return;
        _local[index] = ToLocal(world);
        Version++;
    }

    /// <summary>Moves a key; its handles move with it, and the spans either side are sampled again.</summary>
    public void SetKey(int key, Vector3 world)
    {
        if (!IsSpline) { SetPoint(key, world); return; }
        Vector3 delta = ToLocal(world) - _keys[key];
        _keys[key] += delta;
        _in[key] += delta;
        _out[key] += delta;
        Resample(key - 1);
        Resample(key);
    }

    /// <summary>Moves one of a key's handles; the other follows by the key's <see cref="PathHandleMode"/>.</summary>
    public void SetHandle(int key, bool outgoing, Vector3 world)
    {
        if (!IsSpline) return;
        Vector3 handle = ToLocal(world), k = _keys[key];
        var (moved, other) = outgoing ? (_out, _in) : (_in, _out);
        moved[key] = handle;
        bool hasOther = outgoing ? key > 0 : key < _keys.Count - 1;
        if (hasOther && _modes[key] != PathHandleMode.Free && (handle - k).LengthSquared() > 1e-12f)
        {
            Vector3 direction = Vector3.Normalize(k - handle);
            float length = _modes[key] == PathHandleMode.Smooth ? (handle - k).Length() : (other[key] - k).Length();
            other[key] = k + direction * length;
        }
        Resample(key - 1);
        Resample(key);
    }

    /// <summary>Splits span <paramref name="span"/> (between keys span and span + 1) in two at its middle, without
    /// changing its shape (de Casteljau); the path gains a key and 10 points.</summary>
    public void InsertKey(int span)
    {
        if (!IsSpline || span < 0 || span >= _keys.Count - 1) return;
        Vector3 p0 = _keys[span], p1 = _out[span], p2 = _in[span + 1], p3 = _keys[span + 1];
        Vector3 a = (p0 + p1) / 2, b = (p1 + p2) / 2, c = (p2 + p3) / 2, ab = (a + b) / 2, bc = (b + c) / 2, mid = (ab + bc) / 2;
        _out[span] = a;
        _in[span + 1] = c;
        _keys.Insert(span + 1, mid);
        _in.Insert(span + 1, ab);
        _out.Insert(span + 1, bc);
        _modes.Insert(span + 1, PathHandleMode.Smooth);
        _local.InsertRange(SamplesPerSpan * (span + 1), new Vector3[SamplesPerSpan]);
        Resample(span);
        Resample(span + 1);
    }

    /// <summary>Removes an inner key, its two spans becoming the one cubic that best follows them (their 19 inner
    /// samples, the outer keys kept); the path loses 10 points.</summary>
    public void RemoveKey(int key)
    {
        if (!IsSpline || key <= 0 || key >= _keys.Count - 1) return;
        int first = SamplesPerSpan * (key - 1);
        var samples = _local.GetRange(first, 2 * SamplesPerSpan + 1);
        var (p1, p2) = FitInnerControlPoints(samples);
        _out[key - 1] = p1;
        _in[key + 1] = p2;
        _keys.RemoveAt(key);
        _in.RemoveAt(key);
        _out.RemoveAt(key);
        _modes.RemoveAt(key);
        _local.RemoveRange(first + SamplesPerSpan, SamplesPerSpan);
        Resample(key - 1);
    }

    /// <summary>Adds a key (a point, on a polyline) just after <paramref name="key"/> and returns its index: in the
    /// middle of the span after it, without changing the shape, or past the last key along the path's end
    /// direction, one span length further.</summary>
    public int InsertAfter(int key)
    {
        if (key < KeyCount - 1)
        {
            if (IsSpline) InsertKey(key);
            else { _local.Insert(key + 1, (_local[key] + _local[key + 1]) / 2); Version++; }
            return key + 1;
        }
        return Extend(atEnd: true);
    }

    /// <summary>As <see cref="InsertAfter"/>, just before <paramref name="key"/>.</summary>
    public int InsertBefore(int key) => key > 0 ? InsertAfter(key - 1) : Extend(atEnd: false);

    /// <summary>Removes a key (a point, on a polyline), the ends included; false when the path would be left with
    /// fewer than two.</summary>
    public bool Remove(int key)
    {
        if (KeyCount <= 2 || key < 0 || key >= KeyCount) return false;
        if (!IsSpline)
        {
            _local.RemoveAt(key);
            Version++;
            return true;
        }
        if (key > 0 && key < _keys.Count - 1) { RemoveKey(key); return true; }
        if (key == 0)
        {
            _local.RemoveRange(0, SamplesPerSpan);
            _keys.RemoveAt(0); _in.RemoveAt(0); _out.RemoveAt(0); _modes.RemoveAt(0);
            _in[0] = _keys[0];
            _modes[0] = PathHandleMode.Free;
        }
        else
        {
            _local.RemoveRange(_local.Count - SamplesPerSpan, SamplesPerSpan);
            _keys.RemoveAt(key); _in.RemoveAt(key); _out.RemoveAt(key); _modes.RemoveAt(key);
            _out[^1] = _keys[^1];
            _modes[^1] = PathHandleMode.Free;
        }
        Version++;
        return true;
    }

    /// <summary>Gives a key Bezier curvature (its handles along the line through its neighbours, a third of the
    /// way: a smooth curve) or none (each handle a third of the way to its neighbour: a corner between straight
    /// spans, where the neighbour has none too).</summary>
    public void SetCurvature(int key, bool bezier)
    {
        if (!IsSpline) return;
        Vector3 k = _keys[key];
        bool hasPrevious = key > 0, hasNext = key < _keys.Count - 1;
        if (bezier)
        {
            Vector3 tangent = hasPrevious && hasNext ? (_keys[key + 1] - _keys[key - 1]) / 2
                : hasNext ? _keys[key + 1] - k : k - _keys[key - 1];
            if (hasPrevious) _in[key] = k - tangent / 3;
            if (hasNext) _out[key] = k + tangent / 3;
            _modes[key] = PathHandleMode.Smooth;
        }
        else
        {
            if (hasPrevious) _in[key] = k + (_keys[key - 1] - k) / 3;
            if (hasNext) _out[key] = k + (_keys[key + 1] - k) / 3;
            _modes[key] = PathHandleMode.Free;
        }
        Resample(key - 1);
        Resample(key);
    }

    /// <summary>Whether a key has no curvature: each of its handles a third of the way to its neighbour.</summary>
    public bool IsCorner(int key)
    {
        if (!IsSpline) return true;
        Vector3 k = _keys[key];
        bool corner = true;
        if (key > 0) corner &= Vector3.Distance(_in[key], k + (_keys[key - 1] - k) / 3) < 1e-3f;
        if (key < _keys.Count - 1) corner &= Vector3.Distance(_out[key], k + (_keys[key + 1] - k) / 3) < 1e-3f;
        return corner;
    }

    /// <summary>Turns a polyline into a spline through its points: each one a key with smooth handles, each
    /// segment a span sampled 10 times, as the game's spline paths are. The path gains 9 points per segment.</summary>
    public void ConvertToSpline()
    {
        if (IsSpline || _local.Count < 2) return;
        var points = _local.ToList();
        _keys.Clear(); _in.Clear(); _out.Clear(); _modes.Clear();
        foreach (var point in points)
        {
            _keys.Add(point); _in.Add(point); _out.Add(point); _modes.Add(PathHandleMode.Smooth);
        }
        _local.Clear();
        _local.AddRange(new Vector3[SamplesPerSpan * (points.Count - 1) + 1]);
        IsSpline = true;
        for (int key = 0; key < _keys.Count; key++) SetCurvature(key, bezier: true);
    }

    /// <summary>Adds a key past the last one (or before the first) along the path's direction there, one end
    /// span's length away, and returns its index.</summary>
    private int Extend(bool atEnd)
    {
        if (!IsSpline)
        {
            if (_local.Count == 0) return -1;
            Vector3 end = atEnd ? _local[^1] : _local[0];
            Vector3 step = _local.Count > 1 ? end - (atEnd ? _local[^2] : _local[1]) : Vector3.UnitX;
            if (atEnd) _local.Add(end + step); else _local.Insert(0, end + step);
            Version++;
            return atEnd ? _local.Count - 1 : 0;
        }
        int last = _keys.Count - 1, edge = atEnd ? last : 0, neighbour = atEnd ? last - 1 : 1;
        Vector3 k = _keys[edge];
        float chord = MathF.Max(Vector3.Distance(k, _keys[neighbour]), 0.5f);
        Vector3 inward = atEnd ? _in[edge] : _out[edge];
        Vector3 direction = (k - inward).LengthSquared() > 1e-10f ? Vector3.Normalize(k - inward) : Vector3.Normalize(k - _keys[neighbour]);
        Vector3 added = k + direction * chord;
        if (atEnd)
        {
            _out[edge] = k + direction * chord / 3;
            _keys.Add(added); _in.Add(added - direction * chord / 3); _out.Add(added); _modes.Add(PathHandleMode.Free);
            _local.AddRange(new Vector3[SamplesPerSpan]);
            Resample(last);
            return last + 1;
        }
        _in[0] = k + direction * chord / 3;
        _keys.Insert(0, added); _in.Insert(0, added); _out.Insert(0, added - direction * chord / 3); _modes.Insert(0, PathHandleMode.Free);
        _local.InsertRange(0, new Vector3[SamplesPerSpan]);
        Resample(0);
        return 0;
    }

    /// <summary>Everything an edit can change about the path's shape, for undo: its points, its keys, handles and
    /// their modes, whether it is a spline. Its placement is its <see cref="Entity.Transform"/>, kept apart.</summary>
    public sealed record ShapeState(Vector3[] Local, Vector3[] Keys, Vector3[] In, Vector3[] Out, PathHandleMode[] Modes, bool IsSpline)
    {
        public bool SameAs(ShapeState other) => IsSpline == other.IsSpline
            && Local.AsSpan().SequenceEqual(other.Local) && Keys.AsSpan().SequenceEqual(other.Keys)
            && In.AsSpan().SequenceEqual(other.In) && Out.AsSpan().SequenceEqual(other.Out)
            && Modes.AsSpan().SequenceEqual(other.Modes);
    }

    public ShapeState CaptureShape() => new([.. _local], [.. _keys], [.. _in], [.. _out], [.. _modes], IsSpline);

    /// <summary>Puts the path's shape back as captured (undo, redo).</summary>
    public void RestoreShape(ShapeState state)
    {
        _local.Clear(); _local.AddRange(state.Local);
        _keys.Clear(); _keys.AddRange(state.Keys);
        _in.Clear(); _in.AddRange(state.In);
        _out.Clear(); _out.AddRange(state.Out);
        _modes.Clear(); _modes.AddRange(state.Modes);
        IsSpline = state.IsSpline;
        Version++;
    }

    /// <summary>Notices a gizmo edit of the whole path; true when the points moved since the last call.</summary>
    public bool Sync()
    {
        var matrix = Transform.GetMatrix();
        if (matrix == _lastMatrix) return false;
        _lastMatrix = matrix;
        Version++;
        return true;
    }

    /// <summary>Each point's distance along the path as the game computes it: 0 at the first point, then the
    /// sum of the straight segments before it. The last one is the path's length.</summary>
    public float[] Distances()
    {
        var points = WorldPoints().ToArray();
        var distances = new float[points.Length];
        for (int i = 1; i < points.Length; i++)
            distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        return distances;
    }

    public float Length => Distances() is { Length: > 0 } d ? d[^1] : 0f;

    /// <summary>Samples span <paramref name="span"/> again, as the export did: 10 points evenly in its parameter.</summary>
    private void Resample(int span)
    {
        if (span < 0 || span >= _keys.Count - 1) return;
        Vector3 p0 = _keys[span], p1 = _out[span], p2 = _in[span + 1], p3 = _keys[span + 1];
        for (int k = 0; k <= SamplesPerSpan; k++)
        {
            float t = k / (float)SamplesPerSpan, u = 1 - t;
            _local[SamplesPerSpan * span + k] = u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
        }
        Version++;
    }

    private bool TryRecoverSpline()
    {
        int n = _local.Count;
        if (n < SamplesPerSpan + 1 || (n - 1) % SamplesPerSpan != 0) return false;
        int spans = (n - 1) / SamplesPerSpan;
        var outs = new Vector3[spans];
        var ins = new Vector3[spans];
        for (int s = 0; s < spans; s++)
        {
            var block = _local.GetRange(SamplesPerSpan * s, SamplesPerSpan + 1);
            var (p1, p2) = FitInnerControlPoints(block);
            Vector3 p0 = block[0], p3 = block[^1];
            float size = MathF.Max(Vector3.Distance(p0, p3), block.Max(p => Vector3.Distance(p, p0)));
            float tolerance = SplineTolerance * MathF.Max(size, 1f);
            for (int k = 1; k < SamplesPerSpan; k++)
            {
                float t = k / (float)SamplesPerSpan, u = 1 - t;
                var fitted = u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
                if (Vector3.Distance(fitted, block[k]) > tolerance) return false;
            }
            outs[s] = p1;
            ins[s] = p2;
        }
        for (int key = 0; key <= spans; key++)
        {
            _keys.Add(_local[SamplesPerSpan * key]);
            _out.Add(key < spans ? outs[key] : _local[SamplesPerSpan * key]);
            _in.Add(key > 0 ? ins[key - 1] : _local[0]);
            // Handles in line through the key read as smooth (equal lengths) or aligned; anything else a corner.
            var mode = PathHandleMode.Free;
            if (key > 0 && key < spans)
            {
                Vector3 a = _in[key] - _keys[key], b = outs[key] - _keys[key];
                if (a.LengthSquared() > 1e-12f && b.LengthSquared() > 1e-12f && Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)) < -0.9998f)
                    mode = MathF.Abs(a.Length() - b.Length()) <= 0.01f * MathF.Max(a.Length(), b.Length()) ? PathHandleMode.Smooth : PathHandleMode.Aligned;
            }
            _modes.Add(mode);
        }
        return true;
    }

    /// <summary>The two inner Bezier control points that make the cubic through the samples' ends follow the samples
    /// best (least squares, the samples evenly spaced in the parameter).</summary>
    private static (Vector3 P1, Vector3 P2) FitInnerControlPoints(IReadOnlyList<Vector3> samples)
    {
        int m = samples.Count - 1;
        Vector3 p0 = samples[0], p3 = samples[m];
        float a11 = 0, a12 = 0, a22 = 0;
        Vector3 r1 = Vector3.Zero, r2 = Vector3.Zero;
        for (int k = 1; k < m; k++)
        {
            float t = k / (float)m, u = 1 - t;
            float b1 = 3 * u * u * t, b2 = 3 * u * t * t;
            Vector3 rest = samples[k] - u * u * u * p0 - t * t * t * p3;
            a11 += b1 * b1; a12 += b1 * b2; a22 += b2 * b2;
            r1 += b1 * rest; r2 += b2 * rest;
        }
        float det = a11 * a22 - a12 * a12;
        if (MathF.Abs(det) < 1e-12f) return (p0 + (p3 - p0) / 3, p0 + 2 * (p3 - p0) / 3);
        return ((a22 * r1 - a12 * r2) / det, (a11 * r2 - a12 * r1) / det);
    }
}

/// <summary>What of a path an <see cref="EntityPathPoint"/> stands for.</summary>
public enum PathPart { Point, Key, InHandle, OutHandle }

/// <summary>One key, handle or (on a polyline) point of the selected path, selected on its own so the gizmo can
/// move it. Made when it is clicked; not part of the region's entities.</summary>
public class EntityPathPoint : Entity
{
    public readonly EntityPath Owner;
    public readonly int Index;
    public readonly PathPart Part;

    public override Vector4 BoundingSphere { get; set; } = new(0f, 0f, 0f, 0.5f);
    public override string Name { get; protected set; }

    public EntityPathPoint(EntityPath owner, int index, PathPart part)
    {
        Owner = owner;
        Index = index;
        Part = part;
        Name = $"{owner.Name} [{index}]";
        Transform = new Transform { Translation = Position(), Rotation = Quaternion.Identity, Scale = Vector3.One };
    }

    /// <summary>Where the part is on the path now.</summary>
    public Vector3 Position() => Part switch
    {
        PathPart.Key => Owner.KeyAt(Index),
        PathPart.InHandle => Owner.InHandleAt(Index) ?? Owner.KeyAt(Index),
        PathPart.OutHandle => Owner.OutHandleAt(Index) ?? Owner.KeyAt(Index),
        _ => Owner.PointAt(Index),
    };

    /// <summary>Writes a gizmo move back to the path; true when the part moved.</summary>
    public bool Sync()
    {
        var position = Transform.Translation;
        if (position == Position()) return false;
        switch (Part)
        {
            case PathPart.Key: Owner.SetKey(Index, position); break;
            case PathPart.InHandle: Owner.SetHandle(Index, false, position); break;
            case PathPart.OutHandle: Owner.SetHandle(Index, true, position); break;
            default: Owner.SetPoint(Index, position); break;
        }
        return true;
    }

    /// <summary>Puts the gizmo back on the part after the path changed under it (a key insertion, a mode change).</summary>
    public void Refresh() => Transform = new Transform { Translation = Position(), Rotation = Quaternion.Identity, Scale = Vector3.One };
}
