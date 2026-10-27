namespace ReLunacy.Engine.Assets.Textures;

/// <summary>
/// Proven Tools of Destruction old-engine 0x9800 texstream allocation attached to a 0x5200 texture.
/// The streamed payload is the extra top mip. Its allocation is serialized as 128-byte blocks in a
/// ushort, so a single record can represent at most 0xFFFF * 0x80 = 0x7FFF80 bytes.
/// </summary>
public readonly record struct OldTextureStreamingState(
    uint TexstreamOffset,
    ushort AllocationBlocks128,
    ushort TextureIndex)
{
    public const uint AllocationUnit = 0x80;
    public const uint MaximumAllocationBytes = ushort.MaxValue * AllocationUnit;

    public uint AllocationBytes => (uint)AllocationBlocks128 * AllocationUnit;
}
