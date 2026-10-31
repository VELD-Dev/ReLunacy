using System.Buffers.Binary;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Shaders;

/// <summary>
/// New-engine shader-resource 0x33001/0x33002/0x33003 bundle.
/// 0x33001 is not a full Sony CgBinaryProgram container: each 0x33002 slice starts with a compact
/// Insomniac wrapper and points to raw NV47 fragment microcode plus inline-constant patch lists.
/// </summary>
public sealed class NewEngineShaderBundle
{
    public const uint ShaderDataSectionId = 0x00033001;
    public const uint ShaderTableSectionId = 0x00033002;
    public const uint SelectorSectionId = 0x00033003;

    public ulong MaterialBaseTuid { get; }
    /// <summary>0x33003 as stored: one 4-byte record per 0x33002 shader (the last one unpadded),
    /// each {u8 n, n x (u8 a, u8 b)}; retail n is 0 or 1 and b is always 0x13 (a in 1, 2, 3, 0x0A,
    /// 0x0B). PROVEN (QForce EBOOT 0x00724F60): the loader copies the first record's n (only when
    /// n &lt;= 2) and pairs into a 16-byte slot keyed by the bundle's first shader TUID, and every
    /// registered shader keeps a pointer to that slot (+0x0C, 0x00724EF8). At draw time 0x0072526C
    /// (called by the Tie, UFrag, Moby and class-0x13 paths) asks the game's callback 0x00677240 for
    /// the vec4 of SOURCE a, given the drawn object, and writes it into fragment-constant patch GROUP
    /// b - so retail's b = 0x13 is group 19, fed from gameplay object state (a = 1, 2, 3, 0x0A, 0x0B
    /// are callback cases). PROVEN.</summary>
    public byte[] SelectorData { get; }
    public IReadOnlyList<NewEngineShaderVariant> Variants { get; }
    public bool BaseSequenceMatches { get; }

    private NewEngineShaderBundle(
        ulong materialBaseTuid,
        byte[] selectorData,
        IReadOnlyList<NewEngineShaderVariant> variants,
        bool baseSequenceMatches)
    {
        MaterialBaseTuid = materialBaseTuid;
        SelectorData = selectorData;
        Variants = variants;
        BaseSequenceMatches = baseSequenceMatches;
    }

    public static NewEngineShaderBundle? TryRead(
        IGFile file,
        StreamHelper sh,
        ulong materialBaseTuid,
        string shaderName)
    {
        IGFile.SectionHeader dataSection = file.QuerySection(ShaderDataSectionId);
        IGFile.SectionHeader tableSection = file.QuerySection(ShaderTableSectionId);
        IGFile.SectionHeader selectorSection = file.QuerySection(SelectorSectionId);
        if (dataSection.id == 0 && tableSection.id == 0 && selectorSection.id == 0)
            return null;

        if (dataSection.id != ShaderDataSectionId ||
            tableSection.id != ShaderTableSectionId ||
            selectorSection.id != SelectorSectionId)
        {
            Console.WriteLine(
                $"New-engine shader '{shaderName}': incomplete 0x33001/0x33002/0x33003 bundle; ignored.");
            return null;
        }

        if (tableSection.length == 0 || tableSection.length % 0x10 != 0)
        {
            Console.WriteLine(
                $"New-engine shader '{shaderName}': 0x33002 byte length 0x{tableSection.length:X} " +
                "is not a multiple of 0x10; ignored.");
            return null;
        }

        sh.Seek(dataSection.offset);
        byte[] shaderData = sh.ReadBytes(checked((int)dataSection.length));
        sh.Seek(selectorSection.offset);
        byte[] selectorData = sh.ReadBytes(checked((int)selectorSection.length));

        int variantCount = checked((int)(tableSection.length / 0x10));
        var variants = new List<NewEngineShaderVariant>(variantCount);
        for (int i = 0; i < variantCount; i++)
        {
            sh.Seek(tableSection.offset + (uint)(i * 0x10));
            ulong tuid = sh.ReadUInt64();
            uint size = sh.ReadUInt32();
            uint offset = sh.ReadUInt32();

            ulong end = (ulong)offset + size;
            if (size < 0x18 || end > (ulong)shaderData.Length)
            {
                Console.WriteLine(
                    $"New-engine shader '{shaderName}' shader 0x{tuid:X16}: " +
                    $"slice +0x{offset:X}/0x{size:X} exceeds 0x33001 length 0x{shaderData.Length:X}; skipped.");
                continue;
            }

            ReadOnlySpan<byte> slice = shaderData.AsSpan(checked((int)offset), checked((int)size));
            var variant = ParseShader(slice, tuid, size, offset, registerCountInSize: false, out string? error);
            if (variant == null)
            {
                Console.WriteLine($"New-engine shader '{shaderName}' shader 0x{tuid:X16}: {error}; skipped.");
                continue;
            }
            variants.Add(variant);
        }

        bool baseMatches = MatchesBaseSequence(materialBaseTuid, variants);
        return new NewEngineShaderBundle(materialBaseTuid, selectorData, variants, baseMatches);
    }

    /// <summary>One compact shader wrapper: +0x00 microcode size, +0x04/+0x08/+0x0C control words,
    /// +0x10 microcode offset, +0x14 patch-group count, +0x18 + 4g the offset of group g = {u16 n,
    /// n x u16 slot}. The EBOOT's own shaders (<see cref="NewEngineStandardShaders"/>) keep the
    /// register count in the size word's high half (<paramref name="registerCountInSize"/>).</summary>
    internal static NewEngineShaderVariant? ParseShader(
        ReadOnlySpan<byte> slice, ulong tuid, uint size, uint offset, bool registerCountInSize, out string? error)
    {
        error = null;
        if (slice.Length < 0x18)
        {
            error = "wrapper shorter than its header";
            return null;
        }
        uint microcodeSize = ReadU32(slice, 0x00);
        if (registerCountInSize)
            microcodeSize &= 0xFFFF;
        uint config04 = ReadU32(slice, 0x04);
        uint config08 = ReadU32(slice, 0x08);
        uint config0C = ReadU32(slice, 0x0C);
        uint microcodeOffset = ReadU32(slice, 0x10);
        uint patchGroupCount = ReadU32(slice, 0x14);

        if (microcodeOffset < 0x18 ||
            (ulong)microcodeOffset + microcodeSize > (ulong)slice.Length ||
            patchGroupCount > 256 ||
            0x18UL + (ulong)patchGroupCount * sizeof(uint) > (ulong)microcodeOffset)
        {
            error = $"invalid compact header ucode=0x{microcodeOffset:X}+0x{microcodeSize:X}, patches={patchGroupCount}";
            return null;
        }

        var patchGroups = new List<IReadOnlyList<ushort>>(checked((int)patchGroupCount));
        for (int group = 0; group < patchGroupCount; group++)
        {
            uint pointer = ReadU32(slice, 0x18 + group * 4);
            if ((ulong)pointer + 2 > (ulong)microcodeOffset)
            {
                error = "invalid constant patch list";
                return null;
            }

            ushort count = ReadU16(slice, checked((int)pointer));
            if ((ulong)pointer + 2UL + (ulong)count * 2UL > (ulong)microcodeOffset)
            {
                error = "invalid constant patch list";
                return null;
            }

            var sites = new ushort[count];
            for (int site = 0; site < count; site++)
            {
                ushort slot = ReadU16(slice, checked((int)pointer + 2 + site * 2));
                if ((ulong)slot * 16UL + 16UL > microcodeSize)
                {
                    error = "invalid constant patch list";
                    return null;
                }
                sites[site] = slot;
            }
            patchGroups.Add(sites);
        }

        ReadOnlySpan<byte> microcode = slice.Slice(
            checked((int)microcodeOffset), checked((int)microcodeSize));
        RsxShaderScan scan = ScanFragmentShader(microcode);

        return new NewEngineShaderVariant(
            tuid,
            size,
            offset,
            microcodeSize,
            microcodeOffset,
            config04,
            config08,
            config0C,
            scan.InstructionCount,
            scan.HasEnd,
            scan.TextureUnits,
            patchGroups)
        {
            SkeletonKey = scan.SkeletonKey,
            Microcode = microcode.ToArray(),
        };
    }

    private static bool MatchesBaseSequence(
        ulong baseTuid,
        IReadOnlyList<NewEngineShaderVariant> variants)
    {
        if (baseTuid == 0 || variants.Count == 0)
            return false;

        // Retail corpus has two forms: the common 3-way table starts at base+1, while the four-way
        // form starts at base. Accept either exact consecutive sequence.
        ulong start = variants.Count == 4 ? baseTuid : baseTuid + 1;
        for (int i = 0; i < variants.Count; i++)
            if (variants[i].Tuid != start + (ulong)i)
                return false;
        return true;
    }

    private readonly record struct RsxShaderScan(
        int InstructionCount,
        bool HasEnd,
        IReadOnlyList<int> TextureUnits,
        string SkeletonKey);

    private static RsxShaderScan ScanFragmentShader(ReadOnlySpan<byte> bytes)
    {
        var units = new SortedSet<int>();
        int offset = 0;
        int count = 0;
        bool hasEnd = false;
        // The shader with its inline constants zeroed, hashed - see NewEngineShaderVariant.SkeletonKey.
        using var skeleton = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA1);
        Span<byte> blankConstant = stackalloc byte[16];
        blankConstant.Clear();

        while (offset + 16 <= bytes.Length && count < 4096)
        {
            uint d0 = RsxWord(bytes, offset);
            uint s0 = RsxWord(bytes, offset + 4);
            uint s1 = RsxWord(bytes, offset + 8);
            uint s2 = RsxWord(bytes, offset + 12);
            int op = Bits(d0, 24, 6) | (Bits(s1, 31, 1) << 6);

            if (IsTextureOpcode(op))
                units.Add(Bits(d0, 17, 4));

            bool inlineConstant =
                Bits(s0, 0, 2) == 2 ||
                Bits(s1, 0, 2) == 2 ||
                Bits(s2, 0, 2) == 2;
            int step = inlineConstant ? 32 : 16;
            if (offset + step > bytes.Length)
                break;

            skeleton.AppendData(bytes.Slice(offset, 16));
            if (inlineConstant)
                skeleton.AppendData(blankConstant);
            count++;
            hasEnd = (d0 & 1u) != 0;
            offset += step;
            if (hasEnd)
                break;
        }

        string key = Convert.ToHexStringLower(skeleton.GetHashAndReset())[..12];
        return new RsxShaderScan(count, hasEnd, units.ToArray(), key);
    }

    private static bool IsTextureOpcode(int op) =>
        op is 0x17 or 0x18 or 0x19 or 0x2F or 0x31 or 0x33 or 0x34;

    // NV47 fragment words are stored in the same halfword-swizzled form used by the existing
    // Tools/Reverse/rsx/rsxfp.py decoder.
    private static uint RsxWord(ReadOnlySpan<byte> bytes, int offset) =>
        ((uint)bytes[offset + 2] << 24) |
        ((uint)bytes[offset + 3] << 16) |
        ((uint)bytes[offset + 0] << 8) |
        bytes[offset + 1];

    private static int Bits(uint value, int low, int count) =>
        (int)((value >> low) & ((1u << count) - 1u));

    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));

    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
}

public sealed record NewEngineShaderVariant(
    ulong Tuid,
    uint ResourceSize,
    uint ResourceOffset,
    uint MicrocodeSize,
    uint MicrocodeOffset,
    uint Config04,
    uint Config08,
    uint Config0C,
    int InstructionCount,
    bool HasEndInstruction,
    IReadOnlyList<int> TextureUnits,
    IReadOnlyList<IReadOnlyList<ushort>> ConstantPatchGroups)
{
    /// <summary>
    /// The shader's family key: SHA-1 of its microcode with every inline constant zeroed, first 12 hex
    /// digits - the key of Tools/Reverse/newengine_shader_geometry_join.py and of the RPCS3-cache family
    /// audits (Tools/Reverse/evidence/NEWENGINE_FP_Families_*.md), so a material's embedded shaders can
    /// be matched to a decoded family.
    /// </summary>
    public string SkeletonKey { get; init; } = "";

    /// <summary>The NV47 microcode as stored (RSX word order, inline constants included; the patch-group
    /// slots hold zeros until the engine fills them per draw).</summary>
    public byte[] Microcode { get; init; } = [];

    /// <summary>Wrapper +0x08: the RSX shader control word. Bit 0x40 = 32-bit exports (the colour is r0,
    /// else h0); every QForce embedded shader leaves it clear.</summary>
    public bool Exports32 => (Config08 & 0x40) != 0;

    /// <summary>Constant slot (byte offset / 16) -> patch group, from <see cref="ConstantPatchGroups"/>.</summary>
    public IReadOnlyDictionary<int, int> SlotGroups()
    {
        var map = new Dictionary<int, int>();
        for (int g = 0; g < ConstantPatchGroups.Count; g++)
            foreach (ushort slot in ConstantPatchGroups[g])
                map.TryAdd(slot, g);
        return map;
    }
}
