using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Sky;

/// <summary>Old-engine sky definition from main.dat section 0x8150.</summary>
public sealed class SkyDefs
{
    public const uint SectionId = 0x8150;
    public const int Size = 0x100;
    public byte[] Raw { get; }

    public SkyDefs(byte[] raw)
    {
        if (raw.Length != Size) throw new ArgumentException($"SkyDefs must be 0x{Size:X} bytes.", nameof(raw));
        Raw = raw;
    }

    private float ReadSingle(int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(Raw.AsSpan(offset, 4)));
    private uint ReadUInt32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(Raw.AsSpan(offset, 4));
    private Vector4 ReadVector4(int offset) => new(
        ReadSingle(offset), ReadSingle(offset + 4), ReadSingle(offset + 8), ReadSingle(offset + 12));

    // Dome producer 0x580574. The extra aliases preserve the newer gradient reverse without losing
    // the older names used by diagnostics.
    public float DomeVerticalProfileScale => ReadSingle(0x04);
    public float DomeVerticalProfileOffset => ReadSingle(0x08);
    public float DomeRadialScale => DomeVerticalProfileScale;
    public float DomeVerticalScale => DomeVerticalProfileOffset;
    public float DomeProfileScalar => ReadSingle(0x0C);
    public float DomeColourTransitionParameter => ReadSingle(0x10);
    public Vector4 DomeColourVector14 => ReadVector4(0x14);
    public Vector4 DomeColourVector24 => ReadVector4(0x24);
    public Vector4 DomeColourVector34 => ReadVector4(0x34);
    public Vector4 DomeRuntimeColourSource44 => ReadVector4(0x44);
    public float DomeGradientSource54 => ReadSingle(0x54);
    public float DomeGradientSource58 => ReadSingle(0x58);
    public float DomeGradientStartY => DomeGradientSource54 * DomeVerticalProfileScale - DomeVerticalProfileOffset;
    public float DomeGradientEndY => DomeGradientSource58 * DomeVerticalProfileScale - DomeVerticalProfileOffset;
    public float DomeGradientInverseRange => 1.0f / (DomeGradientEndY - DomeGradientStartY);
    public Vector4 DomeVertexConstant42 => new(DomeGradientStartY, DomeGradientInverseRange, 0f, 0f);

    /// <summary>SkyDefs+0x5C. Retail multiplies frame delta by this before shell/cloud phase updates.</summary>
    public float SkyTimeScale => ReadSingle(0x5C);

    // Pass 17 Clouds. 0x580024 consumes two six-float parameter blocks at +0x60 and +0x7C.
    // Neutral names are intentional: the arithmetic is proven, but artistic LF/HF naming is not.
    public float Cloud0Period => ReadSingle(0x60);
    public float Cloud0Angle => ReadSingle(0x64);
    public float Cloud0PrimaryScroll => ReadSingle(0x68);
    public float Cloud0Bias0 => ReadSingle(0x6C);
    public float Cloud0SecondaryScroll => ReadSingle(0x70);
    public float Cloud0Bias1 => ReadSingle(0x74);
    public float CloudTexture4PerturbScale => ReadSingle(0x78);

    public float Cloud1Period => ReadSingle(0x7C);
    public float Cloud1Angle => ReadSingle(0x80);
    public float Cloud1PrimaryScroll => ReadSingle(0x84);
    public float Cloud1Bias0 => ReadSingle(0x88);
    public float Cloud1SecondaryScroll => ReadSingle(0x8C);
    public float Cloud1Bias1 => ReadSingle(0x90);
    public float CloudTexture5PerturbScale => ReadSingle(0x94);

    /// <summary>Pass-17 fragment multiplier alpha, SkyDefs+0x98.</summary>
    public float CloudColourAlpha => ReadSingle(0x98);
    /// <summary>Blend between the TEX4 and TEX5 cloud layers, SkyDefs+0x9C.</summary>
    public float CloudLayerBlend => ReadSingle(0x9C);
    /// <summary>Pass-17 fragment multiplier RGB from SkyDefs+0xA8/+0xAC/+0xB0.</summary>
    public Vector4 CloudColourMultiplier => new(
        ReadSingle(0xA8), ReadSingle(0xAC), ReadSingle(0xB0), CloudColourAlpha);

    /// <summary>Bloom shells' factor, SkyDefs+0xE0: the Sky update (0x0057FFAC, 0x00581204) copies it to
    /// the constant 0x1068F330 that the Bloom shell handler 0x00583BD4 patches into its program
    /// (alpha = texture alpha x this x vertex alpha). Cache values 0.45..2.6.</summary>
    public float BloomShellScale => ReadSingle(0xE0);

    /// <summary>Physical 0x5200 texture indices bound by Clouds to RSX TEX4/TEX5/TEX6.</summary>
    public uint TextureIndexE8 => ReadUInt32(0xE8);
    public uint TextureIndexEC => ReadUInt32(0xEC);
    public uint TextureIndexF0 => ReadUInt32(0xF0);

    /// <summary>
    /// +0xF8 is the byte offset relative to vertices.dat 0x9000 of the shared native SkyShell
    /// vertex stream. The audited Apogee fixture closes this arithmetically: the resulting tail is
    /// exactly 17,246 native 16-byte vertices and shell indices address 0..17,245.
    /// </summary>
    public uint ShellVertexStreamOffset => ReadUInt32(0xF8);

    /// <summary>
    /// +0xFC is the byte offset relative to vertices.dat 0x9100 of the shared native SkyShell index
    /// stream. Apogee serializes zero and the shell record windows consume the resulting u16 stream.
    /// </summary>
    public uint ShellIndexStreamOffset => ReadUInt32(0xFC);
}

/// <summary>One 0x20-byte record from native sky family 0x8160 or 0x8170.</summary>
public sealed record SkyFamilyRecord(
    uint SectionId,
    int Index,
    uint Raw00,
    uint Raw04,
    uint TextureIndex,
    uint Raw0C,
    uint Raw10,
    uint Raw14,
    uint Raw18,
    uint Raw1C,
    ITexture? Texture)
{
    private static float AsSingle(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));

    /// <summary>record+0x00: first u16 index number in the shared 0x9100 SkyShell stream.</summary>
    public uint FirstIndex => Raw00;

    /// <summary>record+0x04 high half: number of indices drawn by this shell.</summary>
    public ushort IndexCount => unchecked((ushort)(Raw04 >> 16));

    /// <summary>record+0x06: authored shell order/queue value.</summary>
    public ushort DrawOrder => unchecked((ushort)(Raw04 & 0xFFFF));

    /// <summary>
    /// record+0x10 fixed Z rotation. Native 0x580260 closes the local transform order as
    /// Ry(Phase) * Rz(Rotation10) * Ry(Rotation14). This supersedes the older tentative packed-colour
    /// interpretation; shell colour is carried per vertex in the shared native mesh.
    /// </summary>
    public float Rotation10 => AsSingle(Raw10);

    /// <summary>record+0x14 fixed Y rotation in the same native local transform chain.</summary>
    public float Rotation14 => AsSingle(Raw14);

    /// <summary>record+0x18 native angular velocity.</summary>
    public float SpinRate => AsSingle(Raw18);

    /// <summary>record+0x1C native accumulated phase.</summary>
    public float Phase => AsSingle(Raw1C);

    /// <summary>record+0x0C bit0: Color=0, Bloom/destination-alpha contribution=1.</summary>
    public bool IsBloomContribution => (Raw0C & 1u) != 0;
    public bool IsColorContribution => !IsBloomContribution;

    public int RetailPassId => SectionId switch
    {
        SkyData.Family8160SectionId => IsBloomContribution ? SkyData.BloomPreShellPassId : SkyData.ColorPreShellPassId,
        SkyData.Family8170SectionId => IsBloomContribution ? SkyData.BloomPostShellPassId : SkyData.ColorPostShellPassId,
        _ => -1,
    };
}

/// <summary>Expanded losslessly from HALF4 position + UNORM8x4 colour + HALF2 authored UV.</summary>
public readonly record struct SkyShellVertex(Vector4 Position, Vector4 Color, Vector2 Uv);

/// <summary>
/// Native shared shell geometry extracted from vertices.dat. Indices contains only the contiguous
/// global-index window covered by shell records; index VALUES remain native vertex indices.
/// </summary>
public sealed class SkyShellMesh
{
    public uint VertexStreamOffset { get; }
    public uint IndexStreamOffset { get; }
    public uint GlobalFirstIndex { get; }
    public IReadOnlyList<SkyShellVertex> Vertices { get; }
    public IReadOnlyList<uint> Indices { get; }
    public uint GlobalEndIndex => GlobalFirstIndex + (uint)Indices.Count;

    public SkyShellMesh(uint vertexStreamOffset, uint indexStreamOffset, uint globalFirstIndex,
        IReadOnlyList<SkyShellVertex> vertices, IReadOnlyList<uint> indices)
    {
        VertexStreamOffset = vertexStreamOffset;
        IndexStreamOffset = indexStreamOffset;
        GlobalFirstIndex = globalFirstIndex;
        Vertices = vertices;
        Indices = indices;
    }

    public uint LocalFirstIndex(SkyFamilyRecord record)
    {
        if (record.FirstIndex < GlobalFirstIndex || record.FirstIndex + record.IndexCount > GlobalEndIndex)
            throw new ArgumentOutOfRangeException(nameof(record), "Sky shell draw is outside the extracted native index window.");
        return record.FirstIndex - GlobalFirstIndex;
    }
}

public sealed record SkyDefinitionTexture(int Offset, uint TextureIndex, ITexture? Texture);
public sealed record SkyRenderSource(string Label, uint TextureIndex, ITexture Texture);

/// <summary>
/// Proof-bounded old-engine sky source model. Dome, Clouds, Color shells and Bloom shells remain
/// distinct retail stages. 0x5920 is an environment cubemap and must never substitute for this sky.
/// </summary>
public sealed class SkyData
{
    public const uint Family8160SectionId = 0x8160;
    public const uint Family8170SectionId = 0x8170;
    public const uint NativeVertexSectionId = 0x9000;
    public const uint NativeIndexSectionId = 0x9100;

    public const int DomePassId = 14;
    public const int ColorPreShellPassId = 15;
    public const int BloomPreShellPassId = 16;
    public const int CloudsPassId = 17;
    public const int ColorPostShellPassId = 18;
    public const int BloomPostShellPassId = 19;

    public const int NativeShellVertexStride = 16;
    public const int NativeShellPositionOffset = 0x00;
    public const int NativeShellColorOffset = 0x08;
    public const int NativeShellUvOffset = 0x0C;

    public SkyDefs? Definitions { get; }
    public IReadOnlyList<SkyFamilyRecord> Family8160Records { get; }
    public IReadOnlyList<SkyFamilyRecord> Family8170Records { get; }
    public IReadOnlyList<SkyDefinitionTexture> DefinitionTextures { get; }
    public IReadOnlyList<SkyRenderSource> RenderSources { get; }
    public SkyShellMesh? ShellMesh { get; }

    public bool HasSkyDefs => Definitions != null;
    public bool HasNativeShellMesh => ShellMesh != null;
    public bool HasRenderableShell => ShellMesh != null &&
        Family8160Records.Concat(Family8170Records).Any(x => x.Texture != null && x.IndexCount != 0);
    public int NativeDefinitionTextureCount => DefinitionTextures.Count(x => x.Texture != null);

    public SkyData(SkyDefs? definitions,
        IReadOnlyList<SkyFamilyRecord>? family8160,
        IReadOnlyList<SkyFamilyRecord>? family8170,
        IReadOnlyList<SkyDefinitionTexture>? definitionTextures,
        SkyShellMesh? shellMesh = null)
    {
        Definitions = definitions;
        Family8160Records = family8160 ?? [];
        Family8170Records = family8170 ?? [];
        DefinitionTextures = definitionTextures ?? [];
        ShellMesh = shellMesh;
        RenderSources = BuildRenderSources();
    }

    private IReadOnlyList<SkyRenderSource> BuildRenderSources()
    {
        var result = new List<SkyRenderSource>();
        foreach (var record in Family8170Records)
            if (record.Texture is { } texture)
                result.Add(new SkyRenderSource($"0x8170[{record.Index}] tex 0x{record.TextureIndex:X} pass {record.RetailPassId}", record.TextureIndex, texture));
        foreach (var record in Family8160Records)
            if (record.Texture is { } texture)
                result.Add(new SkyRenderSource($"0x8160[{record.Index}] tex 0x{record.TextureIndex:X} pass {record.RetailPassId}", record.TextureIndex, texture));
        foreach (var slot in DefinitionTextures)
            if (slot.Texture is { } texture)
                result.Add(new SkyRenderSource($"SkyDefs +0x{slot.Offset:X2} tex 0x{slot.TextureIndex:X}", slot.TextureIndex, texture));
        return result;
    }

    public static SkyData Empty { get; } = new(null, [], [], [], null);

    /// <summary>
    /// Several skies drawn at once, for the editor's per-sky visibility. Retail draws ONE sky at a time
    /// (set_sky picks it), so the first sky given supplies the one Dome, Clouds and clear colour; only
    /// the authored shells of the others are added, each sky's index window rebased onto one combined
    /// vertex list. One sky comes back as itself.
    /// </summary>
    public static SkyData Combine(IReadOnlyList<SkyData> skies)
    {
        if (skies.Count == 0) return Empty;
        if (skies.Count == 1) return skies[0];

        var vertices = new List<SkyShellVertex>();
        var indices = new List<uint>();
        var family8160 = new List<SkyFamilyRecord>();
        var family8170 = new List<SkyFamilyRecord>();
        foreach (SkyData sky in skies)
        {
            if (sky.ShellMesh is not { } mesh) continue;
            uint vertexBase = (uint)vertices.Count;
            uint indexBase = (uint)indices.Count;
            vertices.AddRange(mesh.Vertices);
            foreach (uint index in mesh.Indices) indices.Add(index + vertexBase);

            bool InWindow(SkyFamilyRecord record) => record.IndexCount != 0 &&
                record.FirstIndex >= mesh.GlobalFirstIndex && record.FirstIndex + record.IndexCount <= mesh.GlobalEndIndex;
            SkyFamilyRecord Rebase(SkyFamilyRecord record) =>
                record with { Raw00 = indexBase + (record.FirstIndex - mesh.GlobalFirstIndex) };
            family8160.AddRange(sky.Family8160Records.Where(InWindow).Select(Rebase));
            family8170.AddRange(sky.Family8170Records.Where(InWindow).Select(Rebase));
        }

        SkyData lead = skies.FirstOrDefault(sky => sky.HasSkyDefs) ?? skies[0];
        SkyShellMesh? combined = vertices.Count == 0 ? null : new SkyShellMesh(0, 0, 0, vertices, indices);
        return new SkyData(lead.Definitions, family8160, family8170, lead.DefinitionTextures, combined);
    }
}

/// <summary>
/// One sky of a level. The new engine lets a region hold several - region.dat 0x81B0, one 0x20-byte
/// record each (All 4 One: seaside 3, polar_sea 2, one elsewhere) - and set_sky() switches between them
/// through the 0x81E0 slot table, whose entries name a sky by its index here. The old engine has one.
/// </summary>
/// <param name="Index">The sky's position in 0x81B0, which is what 0x81E0 and set_sky() refer to.</param>
/// <param name="Tuid">0x81B0 +0x00; 0 on the old engine.</param>
/// <param name="SetSkySlots">The 0x81E0 entries that point at this sky (which SKY_* constant each
/// entry stands for is still OPEN).</param>
public sealed record LevelSky(int Index, ulong Tuid, IReadOnlyList<int> SetSkySlots, SkyData Data)
{
    public string Label => Tuid == 0 ? $"Sky {Index}" : $"Sky {Index}  ·  {Tuid:X16}";
}
