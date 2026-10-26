namespace ReLunacy.Engine.Assets.Textures;

/// <summary>
/// Observed reader path, not a claim that the inverse native writer is proven.
/// Unknown includes failed/partial reads. CompleteStreamChainHeuristic MUST NOT authorize writing.
/// </summary>
public enum OldTextureMipLayout
{
    Unknown,
    Resident,
    StreamTopAndResidentLower,
    CompleteStreamChainHeuristic,
}

/// <summary>An exact 0x9800 record and its address in main.dat; Index is the table ordinal,
/// TextureIndex is the target 0x5200 ordinal. Neither is a payload offset.</summary>
public readonly record struct OldTextureStreamRecord(
    uint Index,
    uint RecordOffset,
    uint PayloadOffset,
    ushort AllocationBlocks128,
    ushort TextureIndex,
    ulong Unknown08)
{
    public uint AllocationBytes => (uint)AllocationBlocks128 * 0x80u;
}

/// <summary>
/// Immutable source identity captured by the loader, before wrapping into an Asset Texture.
/// Descriptor addresses are main.dat file offsets. ResidentOffset is relative to the named pool,
/// NOT necessarily a main.dat file offset. Unknown fields and duplicate streaming refs survive.
/// This is provenance only: it does not grant permission to serialize or relocate a texture.
/// </summary>
public sealed class OldTextureSourceState
{
    public uint DescriptorSectionId { get; }
    public uint DescriptorIndex { get; }
    public uint DescriptorRecordOffset { get; }
    public string ResidentFileName { get; }
    public uint? ResidentSectionId { get; }
    public uint ResidentOffset { get; }
    public IReadOnlyList<OldTextureStreamRecord> StreamRecords { get; }
    public bool StreamTableInspected { get; }
    public bool StreamFileAvailable { get; }
    public OldTextureMipLayout ObservedMipLayout { get; }
    public bool PayloadReadComplete { get; }

    public OldTextureSourceState(
        uint descriptorSectionId,
        uint descriptorIndex,
        uint descriptorRecordOffset,
        string residentFileName,
        uint? residentSectionId,
        uint residentOffset,
        IEnumerable<OldTextureStreamRecord> streamRecords,
        bool streamTableInspected,
        bool streamFileAvailable,
        OldTextureMipLayout observedMipLayout = OldTextureMipLayout.Unknown,
        bool payloadReadComplete = false)
    {
        ArgumentNullException.ThrowIfNull(streamRecords);
        DescriptorSectionId = descriptorSectionId;
        DescriptorIndex = descriptorIndex;
        DescriptorRecordOffset = descriptorRecordOffset;
        ResidentFileName = residentFileName;
        ResidentSectionId = residentSectionId;
        ResidentOffset = residentOffset;
        StreamRecords = Array.AsReadOnly(streamRecords.ToArray());
        StreamTableInspected = streamTableInspected;
        StreamFileAvailable = streamFileAvailable;
        ObservedMipLayout = observedMipLayout;
        PayloadReadComplete = payloadReadComplete;
    }

    public OldTextureSourceState WithReadOutcome(OldTextureMipLayout layout, bool complete) => new(
        DescriptorSectionId, DescriptorIndex, DescriptorRecordOffset,
        ResidentFileName, ResidentSectionId, ResidentOffset, StreamRecords,
        StreamTableInspected, StreamFileAvailable, layout, complete);

    /// <summary>First concrete reason why current provenance cannot authorize a disk write.
    /// Even an unambiguous resident layout still needs the native writer/alias audit and round-trip.</summary>
    public string PersistenceBlocker
    {
        get
        {
            if (!StreamTableInspected)
                return "Streaming reference coverage has not been established.";
            if (StreamRecords.Count > 0 && !StreamFileAvailable)
                return "Referenced texstream.dat is missing; the resident preview is incomplete.";
            if (StreamRecords.Count > 1)
                return "Multiple 0x9800 records target this descriptor; their write semantics are unresolved.";
            if (!PayloadReadComplete || ObservedMipLayout == OldTextureMipLayout.Unknown)
                return "The native texture payload was not read completely.";
            if (ObservedMipLayout == OldTextureMipLayout.CompleteStreamChainHeuristic)
                return "The reader selected a complete texstream mip chain heuristically; its native write layout is unproven.";
            return "Native texture serialization, shared-payload references and reload validation are not yet implemented.";
        }
    }
}
