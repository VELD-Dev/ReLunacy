using OldTextureSourceState = ReLunacy.Engine.Assets.Textures.OldTextureSourceState;
using OldTextureStreamRecord = ReLunacy.Engine.Assets.Textures.OldTextureStreamRecord;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Engine.Loading.Textures;

namespace ReLunacy.Engine.Loading;

public sealed class TextureShaderLoader
{
    public readonly Dictionary<ulong, Texture> Textures = [];
    public readonly Dictionary<ulong, Shader> Shaders = [];
    public readonly List<Texture> OldTexturesByIndex = [];

    /// <summary>New engine: TUIDs of <see cref="Textures"/> / <see cref="Shaders"/> taken from the
    /// patches' <c>built/patch/</c> layer (it wins over the level's copy, like on console).</summary>
    public readonly HashSet<ulong> PatchLayerTextures = [];
    public readonly HashSet<ulong> PatchLayerShaders = [];

    private readonly FileManager _fileManager;

    public TextureShaderLoader(FileManager fileManager) { _fileManager = fileManager; }

    public void LoadAll(Action<string, float>? progressCallback = null)
    {
        progressCallback?.Invoke("Loading textures...", 0.0f);
        LoadTextures();
        progressCallback?.Invoke("Loading shaders...", 0.5f);
        LoadShaders();
    }

    public void LoadTextures() { if (_fileManager.isOld) LoadTexturesOld(); else LoadTexturesNew(); }
    public void LoadShaders() { if (_fileManager.isOld) LoadShadersOld(); else LoadShadersNew(); }

    private void LoadTexturesNew()
    {
        if (!_fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
            throw new FileNotFoundException("Assetlookup is absent", "assetlookup.dat");
        if (!_fileManager.rawfiles.TryGetValue("textures.dat", out Stream? texturestream) || texturestream is null
            || !_fileManager.rawfiles.TryGetValue("highmips.dat", out Stream? highmipstream) || highmipstream is null)
            throw new FileNotFoundException("Textures files are missing.", "textures.dat (or) highmips.dat");

        LoadTextureTable(assetlookup, texturestream, highmipstream, fromPatchLayer: false);

    }

    private void LoadTextureTable(IGFile assetlookup, Stream texturestream, Stream highmipstream, bool fromPatchLayer)
    {
        var alstream = assetlookup.sh;
        var hmstream = new StreamHelper(highmipstream, StreamHelper.Endianness.Big);
        var texstream = new StreamHelper(texturestream, StreamHelper.Endianness.Big);
        var highmipsPtrSec = assetlookup.QuerySection(Texture.HighmipsPointerID);
        var textureMetaSec = assetlookup.QuerySection(TextureMetadataNew.ID);
        var textureRefSec = assetlookup.QuerySection(0x1D180);
        alstream.Seek(highmipsPtrSec.offset);
        var highmipsPtrs = AssetPointer.ReadArray(alstream, highmipsPtrSec.length / 0x10);
        uint textureCount = textureMetaSec.length / TextureMetadataNew.Size;
        for (uint i = 0; i < textureCount; i++)
        {
            alstream.Seek(textureMetaSec.offset + TextureMetadataNew.Size * i);
            var tex = new Texture(alstream) { highmipsRef = highmipsPtrs[i], id = highmipsPtrs[i].TUID };
            if (fromPatchLayer)
            {
                Textures[tex.id] = tex;
                PatchLayerTextures.Add(tex.id);
            }
            else
                Textures.Add(tex.id, tex);
            AssetPointer? lowresRef = null;
            if (textureRefSec.length >= (i + 1) * 0x10)
            {
                alstream.Seek(textureRefSec.offset + i * 0x10);
                lowresRef = new AssetPointer(alstream);
            }
            tex.ReadTexture(hmstream, texstream, lowresRef);
        }
    }

    public const uint ResidentTextureDataSectionId = 0x9650;

    private void LoadTexturesOld()
    {
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
            throw new FileNotFoundException("main.dat is absent");
        if (!_fileManager.rawfiles.TryGetValue("textures.dat", out Stream? textureStream) || textureStream is null)
            throw new FileNotFoundException("textures.dat is absent");

        StreamHelper? texstream = null;
        if (_fileManager.rawfiles.TryGetValue("texstream.dat", out Stream? texstreamStream) && texstreamStream is not null)
            texstream = new StreamHelper(texstreamStream, StreamHelper.Endianness.Big);

        var mainStream = main.sh;
        var textures = new StreamHelper(textureStream, StreamHelper.Endianness.Big);
        var residentSection = main.QuerySection(ResidentTextureDataSectionId);
        StreamHelper? residentTextures = null;
        if (residentSection.id == ResidentTextureDataSectionId && residentSection.count != 0 && residentSection.length > 0)
        {
            var residentBytes = mainStream.ReadFromOffset((int)residentSection.length, residentSection.offset);
            residentTextures = new StreamHelper(new MemoryStream(residentBytes, false), StreamHelper.Endianness.Big);
            Console.WriteLine($"[RENDER-FIX R7] Native RSX texture memory routing active - MAIN => main.dat:0x{ResidentTextureDataSectionId:X4} (file+0x{residentSection.offset:X}, 0x{residentSection.length:X} bytes), LOCAL => textures.dat");
        }

        var textureMetadataSection = main.QuerySection(TextureMetadataOld.ID);
        var texstreamRefSection = main.QuerySection(TexstreamReference.ID);
        if (textureMetadataSection.length != TextureMetadataOld.Size)
            throw new InvalidDataException($"Old 0x5200 texture descriptor stride is 0x{textureMetadataSection.length:X}, expected 0x{TextureMetadataOld.Size:X}.");

        uint oldTextureCount = textureMetadataSection.count;
        for (uint i = 0; i < oldTextureCount; i++)
        {
            mainStream.Seek(textureMetadataSection.offset + TextureMetadataOld.Size * i);
            var texture = new Texture(mainStream, true)
            {
                ReadAuthoredMipChain = true
            };
            Textures.Add(texture.id, texture);
            OldTexturesByIndex.Add(texture);
            if (texstream is not null) texture.highmipsMetadatasOld = [];
        }

        var streamRecords = new Dictionary<uint, List<OldTextureStreamRecord>>();
        bool streamCoverageComplete = true;
        // Inspect the table even when texstream.dat is missing. Missing payload is not resident-only.
        if (texstreamRefSection.id == TexstreamReference.ID)
        {
            if (texstreamRefSection.length != TexstreamReference.Size)
                throw new InvalidDataException($"Old 0x9800 texstream-reference stride is 0x{texstreamRefSection.length:X}, expected 0x{TexstreamReference.Size:X}.");
            uint texstreamReferenceCount = texstreamRefSection.count;
            for (uint i = 0; i < texstreamReferenceCount; i++)
            {
                mainStream.Seek(texstreamRefSection.offset + TexstreamReference.Size * i);
                var texstreamref = TexstreamReference.Read(mainStream);
                if (texstreamref.index < OldTexturesByIndex.Count)
                {
                    if (!streamRecords.TryGetValue(texstreamref.index, out var records))
                        streamRecords.Add(texstreamref.index, records = []);
                    records.Add(new OldTextureStreamRecord(
                        i, checked(texstreamRefSection.offset + TexstreamReference.Size * i),
                        texstreamref.offset, texstreamref.allocationBlocks128,
                        texstreamref.index, texstreamref.Unk2));
                    OldTexturesByIndex[texstreamref.index].highmipsMetadatasOld?.Add(texstreamref);
                }
                else
                {
                    streamCoverageComplete = false;
                    Console.WriteLine($"Warning: texstream 0x9800[{i}] targets texture index {texstreamref.index}, but 0x5200 contains only {OldTexturesByIndex.Count} descriptors.");
                }
            }
        }

        for (int textureIndex = 0; textureIndex < OldTexturesByIndex.Count; textureIndex++)
        {
            var tex = OldTexturesByIndex[textureIndex];
            var meta = (TextureMetadataOld)tex.textureMetadata;
            streamRecords.TryGetValue((uint)textureIndex, out var records);
            tex.SourceState = new OldTextureSourceState(
                TextureMetadataOld.ID, (uint)textureIndex,
                checked(textureMetadataSection.offset + TextureMetadataOld.Size * (uint)textureIndex),
                meta.IsMainMemory ? "main.dat" : "textures.dat",
                meta.IsMainMemory ? ResidentTextureDataSectionId : null,
                meta.offset, records is null ? Array.Empty<OldTextureStreamRecord>() : records.ToArray(),
                streamCoverageComplete, texstream is not null);
            StreamHelper residentSource;
            if (meta.IsMainMemory)
            {
                if (residentTextures is null)
                    throw new InvalidDataException($"Texture {tex.id:X} requests CELL_GCM_LOCATION_MAIN but main.dat has no 0x{ResidentTextureDataSectionId:X4} resident texture section.");
                residentSource = residentTextures;
            }
            else if (meta.IsLocalMemory)
            {
                residentSource = textures;
            }
            else
            {
                throw new InvalidDataException($"Texture {tex.id:X} has invalid encoded RSX texture location {meta.EncodedLocation} in formatBitfield 0x{meta.formatBitfield:X4}.");
            }

            bool hasOverride = (tex.highmipsMetadatasOld?.Count ?? 0) > 0;
            if (hasOverride && texstream is not null)
                tex.ReadTexture(texstream, residentSource);
            else
                tex.ReadTexture(residentSource);
        }

        LoadZoneLightingSection(main, textures, residentTextures, ZoneLightmapSectionId, ZoneLightmaps);
        LoadZoneLightingSection(main, textures, residentTextures, ZoneDirectionalSectionId, ZoneDirectionals);
        LoadZoneLightingSection(main, textures, residentTextures, AuxiliaryTextureSectionId, AuxiliaryTextures);
        ValidateEnvironmentCubemapStorage(main, textures, residentTextures);
    }

    public Texture? ResolveOldTextureIndex(uint index)
    {
        if (index == 0xFFFFFFFF || index >= (uint)OldTexturesByIndex.Count) return null;
        return OldTexturesByIndex[(int)index];
    }

    public const uint CubemapSectionId = 0x5920;

    /// <summary>
    /// Kept for renderer API compatibility. Deliberately null on the old engine until the retail
    /// cubemap RGB/exponent decode and its fragment constants are closed. Older Texture Lab code
    /// used hand-picked exponent constants here; that was an approximation, not native proof.
    /// </summary>
    public System.Numerics.Vector3? EnvironmentAverage { get; private set; }

    private void ValidateEnvironmentCubemapStorage(IGFile main, StreamHelper textures, StreamHelper? residentTextures)
    {
        var section = main.QuerySection(CubemapSectionId);
        if (section.id != CubemapSectionId || section.count == 0 || section.length != TextureMetadataOld.Size) return;

        main.sh.Seek(section.offset);
        var meta = TextureMetadataOld.Read(main.sh);
        int width = checked((int)meta.Width);
        int height = checked((int)meta.Height);
        if (width <= 0 || height <= 0) return;

        StreamHelper source;
        if (meta.IsMainMemory)
        {
            if (residentTextures is null) return;
            source = residentTextures;
        }
        else if (meta.IsLocalMemory)
        {
            source = textures;
        }
        else
        {
            return;
        }

        int faceStride = OldCubemapFaceStride(width, height, checked((int)meta.MipmapCount));
        long payloadEnd = checked((long)meta.offset + 6L * faceStride);
        if (payloadEnd > source.BaseStream.Length)
            throw new InvalidDataException(
                $"Environment cubemap 0x5920 needs [0x{meta.offset:X},0x{payloadEnd:X}) but resident pool length is 0x{source.BaseStream.Length:X}.");

        EnvironmentAverage = null;
        Console.WriteLine(
            $"Environment cubemap 0x5920: {meta.Width}x{meta.Height}, {meta.MipmapCount} mips, " +
            $"{(meta.IsMainMemory ? "MAIN" : "LOCAL")}, faceStride=0x{faceStride:X}; HDR decode remains OPEN.");
    }

    private static int OldCubemapFaceStride(int width, int height, int mipCount)
    {
        int total = 0;
        for (int m = 0; m < Math.Max(1, mipCount); m++)
        {
            int w = Math.Max(1, width >> m), h = Math.Max(1, height >> m);
            total = checked(total + w * h * 4);
        }
        return checked((total + 0x7F) & ~0x7F);
    }

    // NAMING TRAP: these two are called "Zone" for historical reasons only. Both sections live in
    // main.dat, and LoadZoneLightingSection is called with `main` below. It is the per-instance
    // INDEX into them that the zone carries. Do not go looking for the descriptors in a zone file.
    public const uint ZoneLightmapSectionId = 0x5400;
    public const uint ZoneDirectionalSectionId = 0x5410;

    /// <summary>
    /// main.dat 0x5700. NATIVE PROOF: the retail texture-subsystem loader at 0x004E1918 walks the
    /// section directory and files five descriptor pools into one registry struct — 0x5000 at +0x00,
    /// 0x5200 at +0x0C, 0x5400 at +0x18, 0x5410 at +0x1C (sharing the count at +0x20/+0x24), 0x5700
    /// at +0x28/+0x2C/+0x30 (handler 0x004E2018) and 0x5920 at +0x34. 0x5700 is therefore a
    /// first-class peer of the other pools, not an oddity, and it uses the same 0x20-byte RSX
    /// descriptor. It is present in 13 of the 24 retail levels with 1..29 entries.
    ///
    /// What these textures are FOR is still OPEN; loading them is not conditional on that.
    /// </summary>
    public const uint AuxiliaryTextureSectionId = 0x5700;

    public readonly List<Texture> ZoneLightmaps = [];
    public readonly List<Texture> ZoneDirectionals = [];
    public readonly List<Texture> AuxiliaryTextures = [];

    private static void LoadZoneLightingSection(IGFile main, StreamHelper textures, StreamHelper? residentTextures, uint sectionId, List<Texture> into)
    {
        var section = main.QuerySection(sectionId);
        if (section.id != sectionId || section.count == 0) return;
        if (section.length != TextureMetadataOld.Size)
            throw new InvalidDataException($"Old 0x{sectionId:X4} lighting descriptor stride is 0x{section.length:X}, expected 0x{TextureMetadataOld.Size:X}.");
        uint recordCount = section.count;
        for (uint i = 0; i < recordCount; i++)
        {
            main.sh.Seek(section.offset + TextureMetadataOld.Size * i);
            var tex = new Texture(main.sh, true)
            {
                ReadAuthoredMipChain = true
            };
            var meta = (TextureMetadataOld)tex.textureMetadata;
            tex.SourceState = new OldTextureSourceState(
                sectionId, i, checked(section.offset + TextureMetadataOld.Size * i),
                meta.IsMainMemory ? "main.dat" : "textures.dat",
                meta.IsMainMemory ? ResidentTextureDataSectionId : null,
                meta.offset, Array.Empty<OldTextureStreamRecord>(),
                streamTableInspected: true, streamFileAvailable: false);
            try
            {
                if (meta.IsMainMemory)
                {
                    if (residentTextures is null)
                        throw new InvalidDataException($"0x{sectionId:X4}[{i}] requests CELL_GCM_LOCATION_MAIN but main.dat has no 0x{ResidentTextureDataSectionId:X4} section.");
                    tex.ReadTexture(residentTextures);
                }
                else if (meta.IsLocalMemory) tex.ReadTexture(textures);
                else throw new InvalidDataException($"invalid encoded RSX location {meta.EncodedLocation} in formatBitfield 0x{meta.formatBitfield:X4}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: zone lighting texture 0x{sectionId:X4}[{i}] failed to read: {ex.Message}");
            }
            into.Add(tex);
        }
    }

    private void LoadShadersNew()
    {
        if (!_fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
            throw new FileNotFoundException("Assetlookup is missing.", "assetlookup.dat");
        if (!_fileManager.rawfiles.TryGetValue("shaders.dat", out Stream? shadersStream) || shadersStream is null)
            throw new FileNotFoundException("Shaders file not found.", "shaders.dat");

        var alstream = assetlookup.sh;
        var shaderPtrSec = assetlookup.QuerySection(Shader.PointerID);
        uint shaderCount = shaderPtrSec.length / AssetPointer.Size;
        var shaderPointers = new List<(AssetPointer Pointer, Stream Source, bool FromPatchLayer)>((int)shaderCount);
        for (uint i = 0; i < shaderCount; i++)
        {
            alstream.Seek(shaderPtrSec.offset + AssetPointer.Size * i);
            shaderPointers.Add((new AssetPointer(alstream), shadersStream, false));
        }


        if (Textures.Count < 1)
            throw new InvalidOperationException("Textures must be loaded before shaders.");

        var texturesByLow32 = new Dictionary<uint, Texture>();
        foreach (var tex in Textures.Values)
            texturesByLow32[(uint)tex.id] = tex;

        // The game's second lookup key: a texture TUID's high 32 bits are the CRC of its source path.
        var texturesByPathCrc = new Dictionary<uint, Texture>();
        foreach (var tex in Textures.Values)
            texturesByPathCrc.TryAdd((uint)(tex.id >> 32), tex);
        var unresolved = new List<(Shader Shader, int Slot, string Name)>();

        foreach (var (ptr, source, fromPatchLayer) in shaderPointers)
        {
            var shaderBuffer = new byte[ptr.length];
            source.Seek(ptr.offset, SeekOrigin.Begin);
            source.ReadExactly(shaderBuffer);

            using var memstream = new MemoryStream(shaderBuffer, writable: false);
            var shadstream = new StreamHelper(memstream, StreamHelper.Endianness.Big);
            var igshader = new IGFile(memstream);
            var metadataSection = igshader.QuerySection(ShaderMetadataNew.ID);
            if (metadataSection.id != ShaderMetadataNew.ID || metadataSection.length != ShaderMetadataNew.Size)
            {
                Console.WriteLine(
                    $"New-engine shader 0x{ptr.TUID:X16}: invalid 0x5000 record " +
                    $"(stride 0x{metadataSection.length:X}); skipped.");
                continue;
            }

            shadstream.Seek(metadataSection.offset);
            var shader = new Shader(shadstream, tuidOverride: ptr.TUID);
            ShaderMetadataNew meta = shader.metadataNew!.Value;
            ShaderReference sref = shader.reference!.Value;

            var localBindings = ReadNewEngineTextureBindings(igshader, shadstream);
            ReadOnlySpan<uint> namePointers =
            [
                sref.albedoNamePointer,
                sref.normalNamePointer,
                sref.expensiveNamePointer,
                sref.detailNamePointer,
                sref.texture4NamePointer,
                sref.texture5NamePointer,
            ];

            for (int slot = 0; slot < Shader.NewTextureSlotCount; slot++)
            {
                string slotName = namePointers[slot] != 0
                    ? shadstream.ReadString(namePointers[slot])
                    : string.Empty;
                shader.NewTextureSourceNames[slot] = slotName;

                int localIndex = meta.GetTextureLocalIndex(slot);
                if (localIndex < 0)
                    continue;
                if (localIndex >= localBindings.Length)
                {
                    Console.WriteLine(
                        $"New-engine shader 0x{ptr.TUID:X16} '{shader.name}': 0x5000 slot {slot} " +
                        $"indexes local texture {localIndex}, but 0x5A00/0x5200 has {localBindings.Length}; ignored.");
                    continue;
                }

                var binding = localBindings[localIndex];
                shader.NewTextureSourceIds[slot] = binding.Id;
                shader.NewTextureDescriptors[slot] = binding.Descriptor;

                if (texturesByLow32.TryGetValue(binding.Id, out var texture))
                {
                    SetSlot(shader, slot, texture);
                    // Keep one descriptor on the source texture for generic asset inspection only.
                    // MaterialReader uses the per-slot descriptor above and creates a binding-specific
                    // wrapper when native state differs, so rendering no longer depends on first-wins.
                    texture.NewEngineDescriptor ??= binding.Descriptor;
                    if (string.IsNullOrEmpty(texture.name)) texture.name = slotName;
                }
                else if (slotName.Length > 0)
                {
                    unresolved.Add((shader, slot, slotName));
                }
            }

            shader.name = shadstream.ReadString(sref.namePointer);
            shader.ProgramBundle = NewEngineShaderProgramBundle.TryRead(
                igshader, shadstream, meta.fragmentProgramBaseTuid, shader.name);
            if (fromPatchLayer)
            {
                Shaders[ptr.TUID] = shader;
                PatchLayerShaders.Add(ptr.TUID);
            }
            else
                Shaders.Add(ptr.TUID, shader);
        }

        // The game's own lookup (QForce EBOOT 0x00726FA4, keys built at 0x006ACE84): a slot's key is
        // CRC32(0x5D00 source path) << 32 | 0x5D00 id; the texture table is searched by the id first,
        // then - id 0 or absent - by the path CRC against the TUIDs' high 32 bits. On the 15 QForce
        // levels that resolves every one of 17,256 slots: 16,512 by id, 744 by path.
        int byPath = 0;
        foreach (var (shader, slot, name) in unresolved)
        {
            if (!texturesByPathCrc.TryGetValue(InsomniacCrc32.Compute(name), out var texture)) continue;
            SetSlot(shader, slot, texture);
            byPath++;
        }
        if (unresolved.Count > 0)
            Console.WriteLine(
                $"New-engine shaders: {byPath}/{unresolved.Count} local texture binding(s) " +
                "missing from the global texture table resolved by source name.");
    }

    private readonly record struct NewEngineTextureBinding(
        uint Id,
        ReLunacy.Engine.Assets.Textures.OldTextureDescriptorState Descriptor);

    private static NewEngineTextureBinding[] ReadNewEngineTextureBindings(IGFile shaderFile, StreamHelper sh)
    {
        var keys = shaderFile.QuerySection(0x5A00);
        var descriptors = shaderFile.QuerySection(0x5200);
        int count = checked((int)Math.Min(keys.count, descriptors.count));
        if (count == 0)
            return [];

        if (keys.length != sizeof(ulong) || descriptors.length != 0x20)
            throw new InvalidDataException(
                $"New-engine shader texture tables have unexpected strides " +
                $"0x5A00=0x{keys.length:X}, 0x5200=0x{descriptors.length:X}.");

        var result = new NewEngineTextureBinding[count];
        for (int i = 0; i < count; i++)
        {
            sh.Seek(keys.offset + (uint)(i * sizeof(ulong)));
            uint id = (uint)(sh.ReadUInt64() >> 32);

            sh.Seek(descriptors.offset + (uint)(i * 0x20));
            uint offset = sh.ReadUInt32();
            ushort mips = sh.ReadUInt16();
            ushort format = sh.ReadUInt16();
            uint address = sh.ReadUInt32();
            uint control0 = sh.ReadUInt32();
            uint control1 = sh.ReadUInt32();
            uint filter = sh.ReadUInt32();
            ushort width = sh.ReadUInt16();
            ushort height = sh.ReadUInt16();
            uint control3 = sh.ReadUInt32();

            result[i] = new NewEngineTextureBinding(
                id,
                new ReLunacy.Engine.Assets.Textures.OldTextureDescriptorState(
                    offset, mips, format, address, control0, control1, filter, width, height, control3));
        }

        return result;
    }

    private static void SetSlot(Shader shader, int slot, Texture texture)
    {
        switch (slot)
        {
            case 0: shader.Albedo = texture; break;
            case 1: shader.Normal = texture; break;
            case 2: shader.Expensive = texture; break;
            case 3: shader.DetailMap = texture; break;
            case 4: shader.Auxiliary4 = texture; break;
            case 5: shader.Auxiliary5 = texture; break;
        }
    }

    private void LoadShadersOld()
    {
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
            throw new FileNotFoundException("Main file is missing.", "main.dat");
        var mainstream = main.sh;
        var shaderMetadataSec = main.QuerySection(ShaderMetadataOld.ID);
        if (shaderMetadataSec.length != ShaderMetadataOld.Size)
            throw new InvalidDataException($"Old shader metadata stride is 0x{shaderMetadataSec.length:X}, expected 0x{ShaderMetadataOld.Size:X}.");
        uint shaderCount = shaderMetadataSec.count;
        for (uint i = 0; i < shaderCount; i++)
        {
            mainstream.Seek(shaderMetadataSec.offset + ShaderMetadataOld.Size * i);
            var shader = new Shader(mainstream, true, i);
            if (Textures.Count < 1) throw new InvalidOperationException("Textures must be loaded before shaders.");
            var meta = shader.metadataOld!.Value;
            if (meta.albedo != 0 && Textures.TryGetValue(meta.albedo, out var albedo)) shader.Albedo = albedo;
            if (meta.normal != 0 && Textures.TryGetValue(meta.normal, out var normal)) shader.Normal = normal;
            if (meta.expensive != 0 && Textures.TryGetValue(meta.expensive, out var expensive)) shader.Expensive = expensive;
            if (meta.detailMap != 0 && Textures.TryGetValue(meta.detailMap, out var detail)) shader.DetailMap = detail;
            Shaders.Add(shader.TUID, shader);
        }
    }
}
