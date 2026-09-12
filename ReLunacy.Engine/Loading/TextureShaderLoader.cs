using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Engine.Loading.Textures;

namespace ReLunacy.Engine.Loading;

// Loads every texture and shader up front, keyed by TUID (new engine) or flat index (old
// engine) - this is what mesh shaderIndex fields resolve through. Textures must load first:
// shader construction resolves albedo/normal/expensive texture references immediately.
public sealed class TextureShaderLoader
{
    public readonly Dictionary<ulong, Texture> Textures = [];
    public readonly Dictionary<ulong, Shader> Shaders = [];

    /// <summary>Old-engine textures in physical order of the 0x5200 section - element N is the
    /// descriptor at sectionOffset + N * 0x20. This is the addressing direct texture-index fields
    /// use (e.g. <see cref="Objects.FoliageMetadata.TextureIndex"/>). Same instances as
    /// <see cref="Textures"/>, just also held in order. Empty on the new engine.</summary>
    public readonly List<Texture> OldTexturesByIndex = [];

    private readonly FileManager _fileManager;

    public TextureShaderLoader(FileManager fileManager)
    {
        _fileManager = fileManager;
    }

    public void LoadAll(Action<string, float>? progressCallback = null)
    {
        progressCallback?.Invoke("Loading textures...", 0.0f);
        LoadTextures();
        progressCallback?.Invoke("Loading shaders...", 0.5f);
        LoadShaders();
    }

    public void LoadTextures()
    {
        if (_fileManager.isOld) LoadTexturesOld();
        else LoadTexturesNew();
    }

    public void LoadShaders()
    {
        if (_fileManager.isOld) LoadShadersOld();
        else LoadShadersNew();
    }

    private void LoadTexturesNew()
    {
        if (!_fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
            throw new FileNotFoundException("Assetlookup is absent", "assetlookup.dat");
        if (!_fileManager.rawfiles.TryGetValue("textures.dat", out Stream? texturestream) || texturestream is null
            || !_fileManager.rawfiles.TryGetValue("highmips.dat", out Stream? highmipstream) || highmipstream is null)
            throw new FileNotFoundException("Textures files are missing.", "textures.dat (or) highmips.dat");

        var alstream = assetlookup.sh;
        var hmstream = new StreamHelper(highmipstream, StreamHelper.Endianness.Big);
        var texstream = new StreamHelper(texturestream, StreamHelper.Endianness.Big);

        var highmipsPtrSec = assetlookup.QuerySection(Texture.HighmipsPointerID);
        var textureMetaSec = assetlookup.QuerySection(TextureMetadataNew.ID);
        // Lower-resolution single-mip fallback copies, embedded directly in textures.dat,
        // index-aligned with the metadata/highmip-pointer tables above. May be absent
        // (QuerySection returns a zero-length default header when the section doesn't exist).
        var textureRefSec = assetlookup.QuerySection(0x1D180);

        alstream.Seek(highmipsPtrSec.offset);
        var highmipsPtrs = AssetPointer.ReadArray(alstream, highmipsPtrSec.length / 0x10);

        // assetlookup.dat's section headers carry an unreliable `count` field for these
        // pointer/metadata-table sections - `length / record size` is the real entry count.
        uint textureCount = textureMetaSec.length / TextureMetadataNew.Size;
        for (uint i = 0; i < textureCount; i++)
        {
            alstream.Seek(textureMetaSec.offset + TextureMetadataNew.Size * i);
            // `id` must be set explicitly here: it's normally ReadHighmipsPtr's job, which this
            // bypasses since highmipsPtrs is already read.
            var tex = new Texture(alstream) { highmipsRef = highmipsPtrs[i], id = highmipsPtrs[i].TUID };
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

    private void LoadTexturesOld()
    {
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
            throw new FileNotFoundException("main.dat is absent");
        if (!_fileManager.rawfiles.TryGetValue("textures.dat", out Stream? textureStream) || textureStream is null)
            throw new FileNotFoundException("textures.dat is absent");

        StreamHelper? texstream = null;
        if (_fileManager.rawfiles.TryGetValue("texstream.dat", out Stream? texstreamStream) && texstreamStream is not null)
        {
            texstream = new StreamHelper(texstreamStream, StreamHelper.Endianness.Big);
        }

        var mainStream = main.sh;
        var textures = new StreamHelper(textureStream, StreamHelper.Endianness.Big);

        var textureMetadataSection = main.QuerySection(TextureMetadataOld.ID);
        var texstreamRefSection = main.QuerySection(TexstreamReference.ID);

        for (uint i = 0; i < textureMetadataSection.count; i++)
        {
            mainStream.Seek(textureMetadataSection.offset + TextureMetadataOld.Size * i);
            var texture = new Texture(mainStream, true);
            Textures.Add(texture.id, texture);
            // Physical-position index - what direct index fields resolve through
            // (see OldTexturesByIndex / ResolveOldTextureIndex).
            OldTexturesByIndex.Add(texture);

            if (texstream is not null) texture.highmipsMetadatasOld = [];
        }

        if (texstream is not null)
        {
            var texstreamReferences = new List<TexstreamReference>();
            for (int i = 0; i < texstreamRefSection.count; i++)
            {
                mainStream.Seek(texstreamRefSection.offset + TexstreamReference.Size * i);
                texstreamReferences.Add(TexstreamReference.Read(mainStream));
            }

            // texstreamReferences.index is the target texture's index, not a position in this
            // list - a texstream override only exists for a subset of textures.
            var textureList = Textures.Values.ToArray();
            foreach (var texstreamref in texstreamReferences)
            {
                if (texstreamref.index < textureList.Length)
                    textureList[texstreamref.index].highmipsMetadatasOld?.Add(texstreamref);
            }
        }

        // Only textures with their own texstream override (highmipsMetadatasOld non-empty) read
        // from texstream.dat; its offsets are meaningless against textures.dat and vice versa.
        foreach (var tex in Textures.Values)
        {
            bool hasOverride = (tex.highmipsMetadatasOld?.Count ?? 0) > 0;
            tex.ReadTexture(hasOverride && texstream is not null ? texstream : textures);
        }

        LoadZoneLightingSection(main, textures, ZoneLightmapSectionId, ZoneLightmaps);
        LoadZoneLightingSection(main, textures, ZoneDirectionalSectionId, ZoneDirectionals);
        LoadEnvironmentCubemapAverage(main);
    }

    /// <summary>Resolves a direct old-engine texture index - a physical position in the 0x5200
    /// table (see <see cref="OldTexturesByIndex"/>) - to its texture. Returns null for the
    /// 0xFFFFFFFF "no texture" sentinel and for any index past the end of the table. Used for
    /// addressing like <see cref="Objects.FoliageMetadata.TextureIndex"/>.</summary>
    public Texture? ResolveOldTextureIndex(uint index)
    {
        // 0xFFFFFFFF is the game's -1 "no resource" sentinel.
        if (index == 0xFFFFFFFF || index >= (uint)OldTexturesByIndex.Count)
            return null;
        return OldTexturesByIndex[(int)index];
    }

    public const uint CubemapSectionId = 0x5920;

    /// <summary>Average colour of the level's environment cubemap, or null when there isn't one.
    /// An approximation: a single colour stands in for the full cubemap rather than decoding the
    /// exact face/mip layout.</summary>
    public System.Numerics.Vector3? EnvironmentAverage { get; private set; }

    /// <summary>Reads the cubemap reference at section 0x5920 and averages it. Its pixel data
    /// lives in main.dat itself, not textures.dat. RGB is a near-white greyscale mantissa with
    /// the real variation carried in alpha as a shared HDR exponent; alpha is folded in here as a
    /// plain 0..1 weight rather than fully decoded.</summary>
    private void LoadEnvironmentCubemapAverage(IGFile main)
    {
        var section = main.QuerySection(CubemapSectionId);
        if (section.id != CubemapSectionId || section.count == 0) return;

        main.sh.Seek(section.offset);
        var meta = TextureMetadataOld.Read(main.sh);
        int face = (int)(meta.Width * meta.Height * 4);
        if (face <= 0 || meta.offset + face * 6 > main.sh.BaseStream.Length) return;

        var pixels = main.sh.ReadFromOffset(face * 6, meta.offset);
        double r = 0, g = 0, b = 0, weight = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            // Stored as A,R,G,B.
            double a = pixels[i] / 255.0;
            r += pixels[i + 1] / 255.0 * a;
            g += pixels[i + 2] / 255.0 * a;
            b += pixels[i + 3] / 255.0 * a;
            weight += 1;
        }
        if (weight == 0) return;

        EnvironmentAverage = new System.Numerics.Vector3((float)(r / weight), (float)(g / weight), (float)(b / weight));
        Console.WriteLine($"Environment cubemap: {meta.Width}x{meta.Height}, average fill {EnvironmentAverage}");
    }

    public const uint ZoneLightmapSectionId = 0x5400;
    public const uint ZoneDirectionalSectionId = 0x5410;

    /// <summary>Baked light colour per lightmapped instance (main.dat section 0x5400). Indexed
    /// positionally by TieInstance.LightmapIndex - entry X of this list and of ZoneDirectionals
    /// belong to the same instance. Empty on the new engine.</summary>
    public readonly List<Texture> ZoneLightmaps = [];

    /// <summary>Baked light direction, tangent space (main.dat section 0x5410), same indexing as
    /// ZoneLightmaps.</summary>
    public readonly List<Texture> ZoneDirectionals = [];

    /// <summary>Reads a zone lighting section - same 0x20-byte descriptor layout as regular
    /// textures (0x5200), pixel data in textures.dat. Old engine only: on the new engine this
    /// data moves to lighting.dat, which isn't wired up here. Entries are added even when a read
    /// fails, to keep this list positionally aligned with the indices that reference it.</summary>
    private static void LoadZoneLightingSection(IGFile main, StreamHelper textures, uint sectionId, List<Texture> into)
    {
        var section = main.QuerySection(sectionId);
        if (section.id != sectionId || section.count == 0) return;

        for (uint i = 0; i < section.count; i++)
        {
            main.sh.Seek(section.offset + TextureMetadataOld.Size * i);
            var tex = new Texture(main.sh, true);
            try
            {
                tex.ReadTexture(textures);
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
        var shaderStream = new StreamHelper(shadersStream, StreamHelper.Endianness.Big);

        var shaderPtrSec = assetlookup.QuerySection(Shader.PointerID);
        // Same count-field-is-unreliable quirk as the texture metadata section above.
        uint shaderCount = shaderPtrSec.length / AssetPointer.Size;
        var shaderPointers = new AssetPointer[shaderCount];

        for (uint i = 0; i < shaderCount; i++)
        {
            alstream.Seek(shaderPtrSec.offset + AssetPointer.Size * i);
            shaderPointers[i] = new AssetPointer(alstream);
        }

        if (Textures.Count < 1)
            throw new InvalidOperationException("Textures must be loaded before shaders.");

        // ShaderReference's albedoID/normalID/expensiveID are only 32-bit - the low half of a
        // texture's full 64-bit TUID - so they can't match Textures' full-TUID keys directly.
        var texturesByLow32 = new Dictionary<uint, Texture>();
        foreach (var tex in Textures.Values)
            texturesByLow32[(uint)tex.id] = tex;

        foreach (var ptr in shaderPointers)
        {
            var shaderBuffer = new byte[ptr.length];
            shaderStream.BaseStream.Seek(ptr.offset, SeekOrigin.Begin);
            shaderStream.BaseStream.Read(shaderBuffer, 0, (int)ptr.length);
            var memstream = new MemoryStream(shaderBuffer);
            var shadstream = new StreamHelper(memstream, StreamHelper.Endianness.Big);
            var igshader = new IGFile(memstream);

            var metadataSection = igshader.QuerySection(ShaderMetadataOld.ID); // Old and new section IDs are the same
            shadstream.Seek(metadataSection.offset);
            var shader = new Shader(shadstream, tuidOverride: ptr.TUID);

            var sref = shader.reference!.Value;

            if (sref.albedoID != 0 && texturesByLow32.TryGetValue(sref.albedoID, out var albedo))
            {
                shader.Albedo = albedo;
                shader.Albedo.name = shadstream.ReadString(sref.albedoNamePointer);
            }
            if (sref.normalID != 0 && texturesByLow32.TryGetValue(sref.normalID, out var normal))
            {
                shader.Normal = normal;
                shader.Normal.name = shadstream.ReadString(sref.normalNamePointer);
            }
            if (sref.expensiveID != 0 && texturesByLow32.TryGetValue(sref.expensiveID, out var expensive))
            {
                shader.Expensive = expensive;
                shader.Expensive.name = shadstream.ReadString(sref.expensiveNamePointer);
            }

            shader.name = shadstream.ReadString(sref.namePointer);

            // ShaderReference's own embedded TUID field (offset 0x00) reads as a genuine 0 for
            // every shader in this format - Legacy never relies on it either, keying its shader
            // dictionary by the assetlookup pointer-table TUID (shaderPtrs[i].tuid) instead, same
            // as this codebase's Moby/Zone/Tie readers already do for their own asset tables. The
            // Shader constructor's tuidOverride above (ptr.TUID, same value) keeps the object's own
            // TUID property in sync with this dictionary key - previously it didn't, which broke
            // every direct TUID comparison downstream (ShaderBrowser.SelectShader jumping here from
            // the Asset Viewer, its used-shader filter, its Find Usages button) for new-engine shaders.
            Shaders.Add(ptr.TUID, shader);
        }
    }

    private void LoadShadersOld()
    {
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
            throw new FileNotFoundException("Main file is missing.", "main.dat");

        var mainstream = main.sh;
        var shaderMetadataSec = main.QuerySection(ShaderMetadataOld.ID);

        for (uint i = 0; i < shaderMetadataSec.count; i++)
        {
            mainstream.Seek(shaderMetadataSec.offset + ShaderMetadataOld.Size * i);
            var shader = new Shader(mainstream, true, i);

            if (Textures.Count < 1)
                throw new InvalidOperationException("Textures must be loaded before shaders.");

            var meta = shader.metadataOld!.Value;
            if (meta.albedo != 0 && Textures.TryGetValue(meta.albedo, out var albedo)) shader.Albedo = albedo;
            if (meta.normal != 0 && Textures.TryGetValue(meta.normal, out var normal)) shader.Normal = normal;
            if (meta.expensive != 0 && Textures.TryGetValue(meta.expensive, out var expensive)) shader.Expensive = expensive;
            if (meta.detailMap != 0 && Textures.TryGetValue(meta.detailMap, out var detail)) shader.DetailMap = detail;

            Shaders.Add(shader.TUID, shader);
        }
    }
}
