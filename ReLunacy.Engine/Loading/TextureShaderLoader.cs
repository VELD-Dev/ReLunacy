using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Engine.Loading.Textures;

namespace ReLunacy.Engine.Loading;

// Loads every texture and shader up front, keyed by TUID (new engine) or flat index (old
// engine) — this is what mesh shaderIndex fields resolve through. Shaders must load after
// textures: shader construction resolves albedo/normal/expensive texture references immediately.
public sealed class TextureShaderLoader
{
    public readonly Dictionary<ulong, Texture> Textures = [];
    public readonly Dictionary<ulong, Shader> Shaders = [];

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

        var highmipsPtrSec = assetlookup.QuerySection(Texture.HighmipsPointerID);
        var textureMetaSec = assetlookup.QuerySection(TextureMetadataNew.ID);

        alstream.Seek(highmipsPtrSec.offset);
        var highmipsPtrs = AssetPointer.ReadArray(alstream, highmipsPtrSec.length / 0x10);

        // assetlookup.dat's section headers carry an unreliable `count` field for these
        // pointer/metadata-table sections (same quirk already worked around for zone/moby/tie
        // pointer tables elsewhere) — `length / record size` is the real entry count. Using
        // `.count` directly here was loading only 1 of 1458 textures for this level.
        uint textureCount = textureMetaSec.length / TextureMetadataNew.Size;
        for (uint i = 0; i < textureCount; i++)
        {
            alstream.Seek(textureMetaSec.offset + TextureMetadataNew.Size * i);
            // Texture's new-engine constructor branch never sets `id` itself (that's normally
            // ReadHighmipsPtr's job, which this bypasses since highmipsPtrs is already read) —
            // every texture was silently getting id=0, which only surfaced once the count fix
            // above made this loop run more than once (id=0 duplicate on the 2nd texture).
            var tex = new Texture(alstream) { highmipsRef = highmipsPtrs[i], id = highmipsPtrs[i].TUID };
            Textures.Add(tex.id, tex);
            tex.ReadTexture(hmstream);
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

            var textureList = Textures.Values.ToArray();
            for (uint i = 0; i < texstreamRefSection.count; i++)
            {
                if (!texstreamReferences.Any(otr => otr.index == i))
                    continue;

                var texstreamref = texstreamReferences.Find(otr => otr.index == i);
                textureList[i].highmipsMetadatasOld?.Add(texstreamref);
            }
        }

        var streamToRead = texstream ?? textures;
        foreach (var tex in Textures.Values)
        {
            tex.ReadTexture(streamToRead);
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
        // Same count-field-is-unreliable quirk as the texture metadata section above — this was
        // loading only 1 of 693 shaders for this level, leaving nearly every mesh's material
        // resolution falling back to the default material.
        uint shaderCount = shaderPtrSec.length / AssetPointer.Size;
        var shaderPointers = new AssetPointer[shaderCount];

        for (uint i = 0; i < shaderCount; i++)
        {
            alstream.Seek(shaderPtrSec.offset + AssetPointer.Size * i);
            shaderPointers[i] = new AssetPointer(alstream);
        }

        if (Textures.Count < 1)
            throw new InvalidOperationException("Textures must be loaded before shaders.");

        // ShaderReference's albedoID/normalID/expensiveID (and Legacy's identical NewReferences
        // struct) are only 32-bit — the low half of a texture's full 64-bit TUID — so they can't
        // match Textures' full-TUID keys directly. Legacy's own texture dictionary is likewise
        // keyed by the truncated 32-bit value; mirror that here for the lookup.
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
            var shader = new Shader(shadstream);

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
            // every shader in this format — Legacy never relies on it either, keying its shader
            // dictionary by the assetlookup pointer-table TUID (shaderPtrs[i].tuid) instead, same
            // as this codebase's Moby/Zone/Tie readers already do for their own asset tables.
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
