namespace ReLunacy.Engine.Loading.Shaders;

// Byte value -> render mode mapping. Only Opaque and Cutout have a confirmed render treatment;
// the rest (Scunge, SoftEdge, Blended, BakedOnly, LitOnly) are best-effort guesses - see
// MaterialReader.ToRenderMode.
public enum RenderingMode : byte
{
    Opaque = 0x00,
    Overlay = 0x01,
    Additive = 0x02,
    Scunge = 0x03,
    Cutout = 0x04,
    SoftEdge = 0x05,
    Blended = 0x06,
    BakedOnly = 0x07,
    LitOnly = 0x08,
}
