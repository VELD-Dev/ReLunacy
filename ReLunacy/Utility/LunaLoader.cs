using LibLunacy.Legacy;
using LibLunacy.Experimental.Loading.Readers;
using LibLunacy.Objects;
using LibLunacy.Shaders;
using LibLunacy.Textures;
using ReLunacy.Core.Frames.Modals;

namespace ReLunacy.Utility;

public class LunaLoader : IDisposable
{
    public struct LoadingSettings
    {
        public LoadingSettings()
        {
            LoadMobys = false;
            LoadRegions = false;
            LoadShaders = false;
            LoadTies = false;
            LoadTies = false;
            LoadTextures = false;
        }

        public readonly static LoadingSettings Default = new()
        {
            LoadRegions = true
        };

        public bool LoadMobys
        {
            get => field;
            set
            {
                field = value;
                if (value)
                {
                    LoadTextures = true;
                    LoadShaders = true;
                }
            }
        } = false;
        public bool LoadTies
        {
            get => field;
            set
            {
                field = value;
                if (value)
                {
                    LoadTextures = true;
                    LoadShaders = true;
                }
            }
        } = false;
        public bool LoadZones
        {
            get => field;
            set
            {
                field = value;
                if (value)
                {
                    LoadTies = true;
                }
            }
        } = false;
        public bool LoadRegions
        {
            get => field;
            set
            {
                field = value;
                if (value)
                {
                    LoadZones = true;
                    LoadMobys = true;
                }
            }
        } = false;
        public bool LoadTextures { get; set; } = false;
        public bool LoadShaders { get; set; } = false;
    }

    private readonly LoadingModal loadingTracker;
    public readonly FileManager fileManager;

    public Dictionary<ulong, Texture> Textures = [];
    public Dictionary<ulong, Shader> Shaders = [];

    // Experimental system
    public LevelData? LevelData { get; private set; }
    private LoadingProgress? currentProgress;

    // Backwards compatibility properties
    public IReadOnlyDictionary<ulong, LibLunacy.Experimental.Assets.Mobys.Moby> Mobys =>
        LevelData?.Mobys ?? new Dictionary<ulong, LibLunacy.Experimental.Assets.Mobys.Moby>();

    public IReadOnlyDictionary<ulong, LibLunacy.Experimental.Assets.Ties.Tie> Ties =>
        LevelData?.Ties ?? new Dictionary<ulong, LibLunacy.Experimental.Assets.Ties.Tie>();

    public LibLunacy.Experimental.Assets.Levels.Region? Region => LevelData?.Region;

    // Note: Regions array is no longer used; use Region property instead
    public LibLunacy.Experimental.Assets.Levels.Region[] Regions => Region != null ? [ Region ] : [];

    public bool Loaded { get; private set; } = false;

    public LunaLoader(LoadingModal loadModal, FileManager fileManager, LoadingSettings loadSettings)
    {
        loadingTracker = loadModal;
        this.fileManager = fileManager;

        var globalLoadingMax = 3; // Textures, Shaders, Level
        var loadingState = new LoadingProgress("Loading level...", (uint)globalLoadingMax);
        loadingTracker.LoadProgresses.Add(loadingState);

        // Load textures and shaders (still using legacy system)
        if(loadSettings.LoadTextures)
            LoadTextures();
        loadingState.current++;

        if(loadSettings.LoadShaders)
            LoadShaders();
        loadingState.current++;

        // Load level data using experimental system

        if(loadSettings.LoadRegions || loadSettings.LoadZones || loadSettings.LoadMobys || loadSettings.LoadTies)
            LoadLevelExperimental(loadSettings);
        loadingState.current++;

        Loaded = true;
    }

    private void LoadLevelExperimental(LoadingSettings loadingSettings)
    {
        var levelReader = new LevelReader(fileManager);

        LevelData = levelReader.LoadLevel((status, progress) =>
        {
            // Update or create progress bar for current operation
            if (currentProgress == null || currentProgress.status != status)
            {
                if (currentProgress != null)
                {
                    loadingTracker.LoadProgresses.Remove(currentProgress);
                }

                currentProgress = new LoadingProgress(status, 100);
                loadingTracker.LoadProgresses.Add(currentProgress);
            }

            currentProgress.SetProgress((uint)(progress * 100));
        }, loadingSettings.LoadMobys, loadingSettings.LoadTies, loadingSettings.LoadZones, loadingSettings.LoadRegions);

        if (currentProgress != null)
        {
            loadingTracker.LoadProgresses.Remove(currentProgress);
            currentProgress = null;
        }
    }

    #region Texture and Shader Loading

    public void LoadTextures()
    {
        if (fileManager.isOld) LoadTexturesOld();
        else LoadTexturesNew();
    }

    public void LoadShaders()
    {
        if (fileManager.isOld) LoadShadersOld();
        else LoadShadersNew();
    }

    #region Textures
    public void LoadTexturesNew()
    {
        if (!fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            var e = new FileNotFoundException("Assetlookup is absent", "assetlookup.dat");
            LunaLog.LogError(e);
            throw e;
        }
        if (!fileManager.rawfiles.TryGetValue("textures.dat", out Stream? texturestream) || texturestream is null
        || !fileManager.rawfiles.TryGetValue("highmips.dat", out Stream? highmipstream) || highmipstream is null)
        {
            var e = new FileNotFoundException("Textures files are missing.", "textures.dat (or) highmips.dat");
            LunaLog.LogError(e);
            throw e;
        }

        var alstream = assetlookup.sh;
        var hmstream = new StreamHelper(highmipstream, StreamHelper.Endianness.Big);

        var highmipsPtrSec = assetlookup.QuerySection(Texture.HighmipsPointerID);
        var textureMetaSec = assetlookup.QuerySection(TextureMetadataNew.ID);

        var loadState = new LoadingProgress("Loading textures...", textureMetaSec.count);
        loadingTracker.LoadProgresses.Add(loadState);

        alstream.Seek(highmipsPtrSec.offset);
        var highmipsPtrs = FileUtils.ReadStructureArray<AssetPointer>(alstream, highmipsPtrSec.length / 0x10);

        for (uint i = 0; i < textureMetaSec.count; i++)
        {
            alstream.Seek(textureMetaSec.offset + TextureMetadataNew.Size * i);
            var tex = new Texture(alstream)
            {
                highmipsRef = highmipsPtrs[i]
            };
            Textures.Add(tex.id, tex);

            tex.ReadTexture(hmstream);
            loadState.SetProgress(i + 1);
        }
        loadingTracker.LoadProgresses.Remove(loadState);
    }

    public void LoadTexturesOld()
    {
        if (!fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
        {
            throw new FileNotFoundException("main.dat is absent");
        }
        if (!fileManager.rawfiles.TryGetValue("textures.dat", out Stream? textureStream) || textureStream is null)
        {
            throw new FileNotFoundException("textures.dat is absent");
        }
        StreamHelper? texstream = null;
        if (!fileManager.rawfiles.TryGetValue("texstream.dat", out Stream? texstreamStream) || texstreamStream is null)
        {
            LunaLog.LogWarn("texstream.dat is missing. Low quality textures only.");
        }
        else
        {
            texstream = new StreamHelper(texstreamStream, StreamHelper.Endianness.Big);
        }

        var mainStream = main.sh;
        var textures = new StreamHelper(textureStream, StreamHelper.Endianness.Big);

        var textureMetadataSection = main.QuerySection(TextureMetadataOld.ID);
        var texstreamRefSection = main.QuerySection(TexstreamReference.ID);

        var loadState = new LoadingProgress("Loading textures metadata...", textureMetadataSection.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < textureMetadataSection.count; i++)
        {
            mainStream.Seek(textureMetadataSection.offset + TextureMetadataOld.Size * i);
            var texture = new Texture(mainStream, true);
            Textures.Add(texture.id, texture);

            if (texstream is not null) texture.highmipsMetadatasOld = [];

            loadState.SetProgress(i + 1);
        }

        if (texstream is not null)
        {
            loadState.SetStatus("Loading textures highmips...");
            loadState.SetTotal(texstreamRefSection.count);
            loadState.SetProgress(0);
            var texstreamReferences = new List<TexstreamReference>();
            for(int i = 0; i <  texstreamRefSection.count; i++)
            {
                mainStream.Seek(texstreamRefSection.offset + TexstreamReference.Size * i);
                texstreamReferences.Add(TexstreamReference.Read(mainStream));
            }

            for (uint i = 0; i < texstreamRefSection.count; i++)
            {
                var tex = Textures.Values.ToArray()[i];
                if (!texstreamReferences.Any(otr => otr.index == i))
                    continue;

                var texstreamref = texstreamReferences.Find(otr => otr.index == i);
                tex.highmipsMetadatasOld?.Add(texstreamref);
                loadState.SetProgress(i + 1);
            }
        }

        loadState.SetStatus("Reading textures...");
        loadState.SetTotal((uint)Textures.Count);
        loadState.SetProgress(0);
        var streamToRead = texstream is null ? textures : texstream;
        for (uint i = 0; i < Textures.Count; i++)
        {
            var tex = Textures.Values.ToArray()[i];
            tex.ReadTexture(streamToRead);
            loadState.SetProgress(i + 1);
        }
        loadingTracker.LoadProgresses.Remove(loadState);
    }
    #endregion

    #region Shaders
    public void LoadShadersNew()
    {
        if (!fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            var e = new FileNotFoundException("Assetlookup is missing.", "assetlookup.dat");
            LunaLog.LogError(e);
            throw e;
        }
        if (!fileManager.rawfiles.TryGetValue("shaders.dat", out Stream? shadersStream) || shadersStream is null)
        {
            var e = new FileNotFoundException("Shaders file not found.", "shaders.dat");
            LunaLog.LogError(e);
            throw e;
        }

        var alstream = assetlookup.sh;
        var shaderStream = new StreamHelper(shadersStream, StreamHelper.Endianness.Big);

        var shaderPtrSec = assetlookup.QuerySection(Shader.PointerID);

        var shaderPointers = new AssetPointer[shaderPtrSec.count];

        var loadState = new LoadingProgress("Loading shader pointers...", shaderPtrSec.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < shaderPtrSec.count; i++)
        {
            alstream.Seek(shaderPtrSec.offset + AssetPointer.Size * i);
            shaderPointers[i] = new AssetPointer(alstream);

            loadState.SetProgress(i + 1);
        }


        loadState.SetStatus("Loading shaders...");
        loadState.SetTotal((uint)shaderPointers.Length);
        loadState.SetProgress(0);
        for (uint i = 0; i < shaderPtrSec.count; i++)
        {
            ref var ptr = ref shaderPointers[i];
            var shaderBuffer = new byte[ptr.length];
            shaderStream.BaseStream.Seek(ptr.offset, SeekOrigin.Begin);
            shaderStream.BaseStream.Read(shaderBuffer, 0, (int)ptr.length);
            var memstream = new MemoryStream(shaderBuffer);
            var shadstream = new StreamHelper(memstream, StreamHelper.Endianness.Big);
            var igshader = new IGFile(memstream);

            var metadataSection = igshader.QuerySection(ShaderMetadataOld.ID); // Old and new section IDs are the same
            shadstream.Seek(metadataSection.offset);
            var shader = new Shader(shadstream);

            if (Textures.Count < 1)
            {
                var e = new InvalidOperationException("Race error: Textures must be loaded BEFORE shaders ! (for now)");
                LunaLog.LogError(e);
                throw e;
            }

            var sref = shader.reference.Value;

            if (sref.albedoID != 0 && Textures.ContainsKey(sref.albedoID))
            {
                shader.Albedo = Textures[sref.albedoID];
                shader.Albedo.name = shadstream.ReadString((uint)sref.albedoNamePointer);
            }
            if (sref.normalID != 0 && Textures.ContainsKey(sref.normalID))
            {
                shader.Normal = Textures[sref.normalID];
                shader.Normal.name = shadstream.ReadString((uint)sref.normalNamePointer);
            }
            if (sref.expensiveID != 0 && Textures.ContainsKey(sref.expensiveID))
            {
                shader.Expensive = Textures[sref.expensiveID];
                shader.Expensive.name = shadstream.ReadString((uint)sref.expensiveNamePointer);
            }

            shader.name = shadstream.ReadString((uint)sref.namePointer);

            Shaders.Add(shader.TUID, shader);

            loadState.SetProgress(i + 1);
        }
        loadingTracker.LoadProgresses.Remove(loadState);
    }

    public void LoadShadersOld()
    {
        if (!fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
        {
            var e = new FileNotFoundException("Main file is missing.", "main.dat");
            LunaLog.LogError(e);
            throw e;
        }

        var mainstream = main.sh;

        var shaderMetadataSec = main.QuerySection(ShaderMetadataOld.ID); // Old and new section IDs are the same

        var loadState = new LoadingProgress("Loading shaders...", shaderMetadataSec.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < shaderMetadataSec.count; i++)
        {
            mainstream.Seek(shaderMetadataSec.offset + ShaderMetadataOld.Size * i);  // Shader metadata size is the same for old and new
            var shader = new Shader(mainstream, true, i);

            if (Textures.Count < 1)
            {
                var e = new InvalidOperationException("Race error: Textures must be loaded BEFORE shaders ! (for now)");
                LunaLog.LogError(e);
                throw e;
            }

            if (shader.metadataOld?.albedo != 0)
            {
                shader.Albedo = Textures[(ulong)shader.metadataOld?.albedo];
            }
            if (shader.metadataOld?.normal != 0)
            {
                shader.Normal = Textures[(ulong)shader.metadataOld?.normal];
            }
            if (shader.metadataOld?.expensive != 0)
            {
                shader.Expensive = Textures[(ulong)shader.metadataOld?.expensive];
            }

            Shaders.Add(shader.TUID, shader);
            loadState.SetProgress(i + 1);
        }

        loadingTracker.LoadProgresses.Remove(loadState);
    }
    #endregion

    #endregion

    public void Dispose()
    {
        // The experimental system handles disposal of its own assets
        GC.SuppressFinalize(this);
    }
}
