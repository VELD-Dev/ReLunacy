using System.Numerics;
using ReLunacy.Engine.Rendering;

namespace ReLunacy.Core.Moon;

/// <summary>
/// The camera of moon mode: it stands over the moon at a direction and a distance from the centre, and looks along a
/// heading and a pitch in the frame whose up is that direction. Moving turns the direction about the centre; looking
/// turns only the view, so the pivot stays the centre. Takes over the camera's position, target and up while active.
/// </summary>
public sealed class MoonOrbit
{
    private const float MinAltitude = 2f;
    private const float MaxPitch = 89f;

    private Vector3 _direction = Vector3.UnitY;
    private Vector3 _heading = Vector3.UnitZ;
    private float _distance;
    private float _pitch = -45f;
    private (Vector3 Position, Vector3 Target, Vector3 Up) _saved;

    /// <summary>Takes the camera: from where it stands, seen from further out over the moon.</summary>
    public void Enter(EditorCamera camera)
    {
        _saved = (camera.Position, camera.Target, camera.Up);
        var offset = camera.Position - MoonMode.Centre;
        _direction = offset.LengthSquared() > 1e-4f ? Vector3.Normalize(offset) : Vector3.UnitY;
        _distance = MoonMode.Radius * 2.5f;
        _pitch = -45f;
        var forward = camera.GetForward();
        _heading = Tangent(forward);
        Apply(camera);
    }

    /// <summary>Gives the camera back as it was.</summary>
    public void Exit(EditorCamera camera)
    {
        (camera.Position, camera.Target, camera.Up) = _saved;
    }

    /// <summary>Turns the view by mouse movement in degrees, about the camera and not the moon.</summary>
    public void Look(EditorCamera camera, Vector2 degrees)
    {
        _heading = Vector3.Transform(_heading, Quaternion.CreateFromAxisAngle(_direction, float.DegreesToRadians(-degrees.X)));
        _pitch = Math.Clamp(_pitch - degrees.Y, -MaxPitch, MaxPitch);
        Apply(camera);
    }

    /// <summary>Moves over the sphere: <paramref name="forward"/> and <paramref name="right"/> are -1..1, <paramref name="speed"/> in units a second.</summary>
    public void Move(EditorCamera camera, float forward, float right, float speed, float seconds)
    {
        var step = _heading * forward + Vector3.Cross(_heading, _direction) * right;
        if (step.LengthSquared() < 1e-6f) return;
        step = Vector3.Normalize(step);
        float angle = speed * seconds / MathF.Max(_distance, 1f);
        var turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(_direction, step)), angle);
        _direction = Vector3.Normalize(Vector3.Transform(_direction, turn));
        _heading = Tangent(Vector3.Transform(_heading, turn));
        Apply(camera);
    }

    /// <summary>Changes the distance from the centre: <paramref name="up"/> is -1..1, <paramref name="speed"/> in units a second; never
    /// closer than just over the surface.</summary>
    public void Climb(EditorCamera camera, float up, float speed, float seconds)
    {
        if (up == 0f) return;
        float altitude = MathF.Max(_distance - MoonMode.Radius, MinAltitude) + up * speed * seconds;
        _distance = MoonMode.Radius + Math.Clamp(altitude, MinAltitude, MoonMode.AreaRadius);
        Apply(camera);
    }

    private Vector3 Tangent(Vector3 v)
    {
        var t = v - _direction * Vector3.Dot(v, _direction);
        if (t.LengthSquared() < 1e-6f)
            t = Vector3.Cross(_direction, MathF.Abs(_direction.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ);
        return Vector3.Normalize(t);
    }

    private void Apply(EditorCamera camera)
    {
        camera.Position = MoonMode.Centre + _direction * _distance;
        float pitch = float.DegreesToRadians(_pitch);
        camera.Up = _direction;
        camera.Target = camera.Position + _heading * MathF.Cos(pitch) + _direction * MathF.Sin(pitch);
    }
}
