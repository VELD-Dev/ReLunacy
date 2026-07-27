using System.Numerics;
using System.Runtime.InteropServices;

namespace ReLunacy.Engine.Rendering;

/// <summary>GPU-side layout for LightModelShaderSource's LightBuffer uniform — must match its
/// std140 layout exactly. Vector3 (12 bytes) followed by a scalar float packs into a 16-byte slot
/// under std140's own alignment rules (a vec3's base alignment is 16 bytes, and a directly
/// following 4-byte scalar fills the leftover space), which is also exactly how this sequential
/// C# struct lays out — Direction+Ambient, Color+padding and CameraPosition+padding each occupy
/// one 16-byte block, 48 bytes total. CameraPosition isn't really "light" data, but it's needed
/// for specular's view-direction term and this is already the one scene-wide per-frame buffer
/// every lit material binds — see DecalAwareForwardRenderer.Draw. SpecularPower is scene-wide
/// (not per-material) because this game's texture format has no per-pixel specular-power channel
/// to sample — see LitModelShaderSource's header comment.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct LightData
{
    public Vector3 Direction;
    public float Ambient;
    public Vector3 Color;
    public float SpecularPower;
    public Vector3 CameraPosition;
    private readonly float _padding;
}
