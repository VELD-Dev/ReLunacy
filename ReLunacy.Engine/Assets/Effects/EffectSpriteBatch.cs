// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Numerics;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>One run of sprites sharing a blend and a texture. <see cref="TextureIndex"/> -1 = the soft
/// fallback (lights, missing texture).</summary>
public readonly record struct EffectSpriteDraw(EffectSpriteBlend Blend, int TextureIndex, int FirstVertex, int VertexCount);

/// <summary>
/// Camera-facing quads for a frame of effect sprites, back to front, as a flat vertex array
/// (<see cref="FloatsPerVertex"/> floats: position xyz, uv, rgba) and the draw runs over it. Built on the
/// CPU once per frame by whoever plays effects - the Asset Viewer preview and the level view's
/// renderer consume the same thing.
/// </summary>
public sealed class EffectSpriteBatch
{
    public const int FloatsPerVertex = 9;

    private float[] _vertices = new float[FloatsPerVertex * 6 * 256];
    private float[] _depth = new float[256];
    private int[] _order = new int[256];

    public ReadOnlySpan<float> Vertices => _vertices.AsSpan(0, VertexCount * FloatsPerVertex);
    public int VertexCount { get; private set; }
    public List<EffectSpriteDraw> Draws { get; } = [];

    public void Clear()
    {
        VertexCount = 0;
        Draws.Clear();
    }

    /// <summary>Appends <paramref name="sprites"/> (world space), sorted back to front for <paramref name="view"/>.
    /// Distortion sprites are drawn as faint alpha sprites (the heat-haze pass is not reproduced).</summary>
    public void Add(IReadOnlyList<EffectBillboard> sprites, Matrix4x4 view, float distortionAlpha = 0.2f)
    {
        Matrix4x4.Invert(view, out Matrix4x4 camera);
        Vector3 right = Vector3.Normalize(new Vector3(camera.M11, camera.M12, camera.M13));
        Vector3 up = Vector3.Normalize(new Vector3(camera.M21, camera.M22, camera.M23));
        Vector3 eye = camera.Translation;

        // Back to front: keys sorted ascending by distance, walked from the far end.
        ReadOnlySpan<EffectBillboard> list = sprites is List<EffectBillboard> concrete
            ? System.Runtime.InteropServices.CollectionsMarshal.AsSpan(concrete) : sprites.ToArray();
        int count = list.Length;
        if (_depth.Length < count) { _depth = new float[count * 2]; _order = new int[count * 2]; }
        for (int i = 0; i < count; i++)
        {
            _depth[i] = Vector3.DistanceSquared(list[i].Position, eye);
            _order[i] = i;
        }
        Array.Sort(_depth, _order, 0, count);

        for (int k = count - 1; k >= 0; k--)
        {
            int i = _order[k];
            ref readonly EffectBillboard s = ref list[i];
            Orient(s, right, up, eye, out Vector3 centre, out Vector3 ax, out Vector3 ay);
            Vector4 color = s.Color;
            EffectSpriteBlend blend = s.Blend;
            if (s.Distortion) { blend = EffectSpriteBlend.Alpha; color.W *= distortionAlpha; }
            AddQuad(centre, ax, ay, color, blend, s.TextureIndex);
        }
    }

    /// <summary>
    /// The quad of one sprite: camera-facing (or laid in its plane when <see cref="EffectBillboard.Oriented"/>) and turned
    /// by its rotation, or - for a beam or streak
    /// (<see cref="EffectBillboard.Stretch"/>) - laid from its head to its tail and turned about that line
    /// to face the camera, as wide as the sprite. Shared by every effect renderer.
    /// </summary>
    public static void Orient(in EffectBillboard s, Vector3 right, Vector3 up, Vector3 eye,
        out Vector3 centre, out Vector3 ax, out Vector3 ay)
    {
        if (s.Stretch.LengthSquared() > 1e-8f)
        {
            Vector3 along = s.Stretch * 0.5f;
            float half = along.Length();
            if (half < s.HalfSize) along = along / half * s.HalfSize;
            centre = s.Position + s.Stretch * 0.5f;
            Vector3 side = Vector3.Cross(s.Stretch, centre - eye);
            ax = (side.LengthSquared() > 1e-12f ? Vector3.Normalize(side) : right) * s.HalfSize;
            ay = along;
            return;
        }
        float c = MathF.Cos(s.Rotation), sn = MathF.Sin(s.Rotation);
        centre = s.Position;
        // A frame-oriented quad turns within its own plane; any other faces the camera.
        Vector3 x = s.Oriented ? s.AxisX : right, y = s.Oriented ? s.AxisY : up;
        ax = (x * c + y * sn) * s.HalfSize;
        ay = (y * c - x * sn) * s.HalfSize;
    }

    /// <summary>A soft additive glow (light pieces).</summary>
    public void AddGlow(Vector3 position, float halfSize, Vector4 color, Matrix4x4 view)
    {
        Matrix4x4.Invert(view, out Matrix4x4 camera);
        Vector3 right = Vector3.Normalize(new Vector3(camera.M11, camera.M12, camera.M13));
        Vector3 up = Vector3.Normalize(new Vector3(camera.M21, camera.M22, camera.M23));
        AddQuad(position, right * halfSize, up * halfSize, color, EffectSpriteBlend.Additive, -1);
    }

    private void AddQuad(Vector3 centre, Vector3 ax, Vector3 ay, Vector4 color, EffectSpriteBlend blend, int texture)
    {
        int first = VertexCount;
        int need = (VertexCount + 6) * FloatsPerVertex;
        if (need > _vertices.Length) Array.Resize(ref _vertices, Math.Max(need, _vertices.Length * 2));
        Span<float> v = _vertices.AsSpan(VertexCount * FloatsPerVertex, 6 * FloatsPerVertex);
        Write(v, 0, centre - ax - ay, 0, 1, color);
        Write(v, 1, centre + ax - ay, 1, 1, color);
        Write(v, 2, centre + ax + ay, 1, 0, color);
        Write(v, 3, centre - ax - ay, 0, 1, color);
        Write(v, 4, centre + ax + ay, 1, 0, color);
        Write(v, 5, centre - ax + ay, 0, 0, color);
        VertexCount += 6;

        if (Draws.Count > 0)
        {
            EffectSpriteDraw last = Draws[^1];
            if (last.Blend == blend && last.TextureIndex == texture && last.FirstVertex + last.VertexCount == first)
            {
                Draws[^1] = last with { VertexCount = last.VertexCount + 6 };
                return;
            }
        }
        Draws.Add(new EffectSpriteDraw(blend, texture, first, 6));
    }

    private static void Write(Span<float> v, int vertex, Vector3 p, float u, float t, Vector4 c)
    {
        int o = vertex * FloatsPerVertex;
        v[o] = p.X; v[o + 1] = p.Y; v[o + 2] = p.Z;
        v[o + 3] = u; v[o + 4] = t;
        v[o + 5] = c.X; v[o + 6] = c.Y; v[o + 7] = c.Z; v[o + 8] = c.W;
    }
}
