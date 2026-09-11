using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// Tools of Destruction old-engine D100 Moby prototype record.
/// Unresolved fields stay named by offset so the reader does not assign semantics prematurely.
/// The full 0xC0 record is represented to preserve unknown data for future writing support.
/// </summary>
[FileStructure(0xC0)]
public record struct OldMoby : IMoby
{
    public const uint ID = 0xD100;
    public const uint Size = 0xC0;

    [FileOffset(0x00)] public Vector4 boundingSphere;
    [FileOffset(0x10)] public ushort Unknown10;
    [FileOffset(0x12)] public ushort Unknown12;
    [FileOffset(0x14)] public ushort bonesCount;

    /// <summary>Number of animation clips referenced by this Moby. Native initialization reads this field.</summary>
    [FileOffset(0x16)] public ushort animationCount;
    [FileOffset(0x18)] public ushort bangleCount;
    [FileOffset(0x1A)] public ushort mobyId;
    [FileOffset(0x1C)] public ushort Unknown1C;
    [FileOffset(0x1E)] public byte Unknown1E;
    [FileOffset(0x1F)] public byte Unknown1F;

    [FileOffset(0x20)] public uint skeletonPointer;

    /// <summary>Absolute main.dat offset of a u32[animationCount] list of F000 record pointers.</summary>
    [FileOffset(0x24)] public uint animationListPointer;

    [FileOffset(0x28), Reference(nameof(BangleCount))] public MobyBangle[] mobyBangles;
    [FileOffset(0x2C)] public uint Unknown2C;
    [FileOffset(0x30)] public uint Unknown30;
    [FileOffset(0x34)] public uint indicesOffset;
    [FileOffset(0x38)] public uint verticesOffset;
    [FileOffset(0x3C)] public float scale;

    /// <summary>Zero in audited disk records; native initialization writes the runtime animation-state pointer here.</summary>
    [FileOffset(0x40)] public uint runtimeAnimationStatePointer;

    [FileOffset(0x44)] public uint Unknown44;

    /// <summary>Observed absolute pointer into section D900.</summary>
    [FileOffset(0x48)] public uint d900Pointer;

    /// <summary>Observed optional absolute pointer into section DA00.</summary>
    [FileOffset(0x4C)] public uint da00Pointer;

    [FileOffset(0x50)] public uint Unknown50;
    [FileOffset(0x54)] public uint Unknown54;
    [FileOffset(0x58)] public float Unknown58;
    [FileOffset(0x5C)] public float Unknown5C;
    [FileOffset(0x60)] public float Unknown60;
    [FileOffset(0x64)] public uint Unknown64;
    [FileOffset(0x68)] public uint Unknown68;
    [FileOffset(0x6C)] public uint Unknown6C;
    [FileOffset(0x70)] public uint Unknown70;
    [FileOffset(0x74)] public uint Unknown74;

    /// <summary>Observed absolute pointer into main.dat section 0x10200.</summary>
    [FileOffset(0x78)] public uint section10200Pointer;
    [FileOffset(0x7C)] public uint Unknown7C;

    /// <summary>Per-Moby pointer into F400, parallel to the first F000 clip in +0x24.</summary>
    [FileOffset(0x80)] public uint animationAuxF400Pointer;

    [FileOffset(0x84)] public float Unknown84;
    [FileOffset(0x88)] public uint Unknown88;
    [FileOffset(0x8C)] public uint Unknown8C;
    [FileOffset(0x90)] public uint Unknown90;
    [FileOffset(0x94)] public uint Unknown94;

    /// <summary>Observed optional absolute pointer into section E500.</summary>
    [FileOffset(0x98)] public uint e500Pointer;
    [FileOffset(0x9C)] public uint Unknown9C;
    [FileOffset(0xA0)] public uint UnknownA0;

    /// <summary>Observed absolute pointer into main.dat section 0x10400.</summary>
    [FileOffset(0xA4)] public uint section10400Pointer;

    /// <summary>Observed absolute pointer into section E900.</summary>
    [FileOffset(0xA8)] public uint e900Pointer;

    [FileOffset(0xAC)] public uint UnknownAC;
    [FileOffset(0xB0)] public uint UnknownB0;
    [FileOffset(0xB4)] public uint UnknownB4;
    [FileOffset(0xB8)] public uint UnknownB8;
    [FileOffset(0xBC)] public uint UnknownBC;

    public readonly uint BangleCount => bangleCount;

    private ulong _tuid;
    public ulong TUID { readonly get => _tuid; init => _tuid = value; }

    public MobyBangle[] bangles { readonly get => mobyBangles; set => mobyBangles = value; }

    public static OldMoby Read(StreamHelper sh, int index)
    {
        var moby = FileUtils.ReadStructure<OldMoby>(sh);
        moby._tuid = (ulong)index;
        return moby;
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
