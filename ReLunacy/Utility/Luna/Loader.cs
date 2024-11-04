using LibLunacy.Meshes;
using LibLunacy.Objects;
using LibLunacy.Textures;
using LibLunacy.Vertices;
using ReLunacy.Frames.ModalFrames;
using System.Buffers;
using System.Security.Cryptography.X509Certificates;
using Texture = LibLunacy.Textures.Texture;

namespace ReLunacy.Utility.Luna;

public class Loader : IDisposable
{
    private readonly LoadingModal loadingTracker;
    public readonly FileManager fileManager;

    public AssetPointer[] MobyPointers;
    public AssetPointer[] TiePointers;
    public AssetPointer[] ZonePointers;

    public Dictionary<ulong, Texture> Textures = [];
    // public Dictionary<ulong, Shader> Shaders = [];
    public Dictionary<ulong, Moby> Mobys = [];
    public Dictionary<ulong, Tie> Ties = [];
    public Dictionary<ulong, Zone> Zones = [];
    public UFragMetadata[][] UFrags = [];

    public bool Loaded { get; private set; } = false;

    /*
    // Common
    readonly KeyValuePair<IGFile, Stream> gameplay;
    readonly KeyValuePair<IGFile, Stream> textures;
    readonly KeyValuePair<IGFile, Stream> effect;
    // New Engine
    readonly KeyValuePair<IGFile, Stream> assetlookup;
    readonly KeyValuePair<IGFile, Stream> highmips;
    readonly KeyValuePair<IGFile, Stream> animsets;
    readonly KeyValuePair<IGFile, Stream> cubemaps;
    readonly KeyValuePair<IGFile, Stream> lightning;
    readonly KeyValuePair<IGFile, Stream> mobys;
    readonly KeyValuePair<IGFile, Stream> ties;
    readonly KeyValuePair<IGFile, Stream> shrubs;
    readonly KeyValuePair<IGFile, Stream> shaders;
    readonly KeyValuePair<IGFile, Stream> zones;
    // Old Engine
    readonly KeyValuePair<IGFile, Stream> main;
    readonly KeyValuePair<IGFile, Stream> texstream;
    readonly KeyValuePair<IGFile, Stream> vertices;
    readonly KeyValuePair<IGFile, Stream> collision;
    */

    public Loader(LoadingModal loadModal, FileManager fileManager, bool loadMobys = true, bool loadTies = true, bool loadUFrags = true, bool loadShrubs = true, bool loadPlants = true, bool loadFoliages = true)
    {
        loadingTracker = loadModal;
        this.fileManager = fileManager;

        /* This will have to be rewritten smh
        bool flags = CheckIGStream("gameplay.dat", out var iggp, out var gpstream)
                  && CheckIGStream("textures.dat", out var igtex, out var texturestm)
                  && CheckIGStream("effect.dat", out var igeff, out var effstream);
        if (fileManager.isOld)
        {
            flags = flags
                && CheckIGStream("main.dat", out var igmain, out var mainstream)
                && CheckIGStream("texstream.dat", out var igtexstream, out var texstreamstream)
                && CheckIGStream("vertices.dat", out var igvertices, out var verticesstream)
                && CheckIGStream("collision.dat", out var igcollision, out var collisionstream);
        }
        else
        {
            flags = flags
                && CheckIGStream("assetlookup.dat", out var igal, out var alstream)
                && CheckIGStream("highmips.dat", out var ighm, out var hmstream)
                && CheckIGStream("animsets.dat", out var igas, out var asstream) // haha very funny
                && CheckIGStream("cubemaps.dat", out var igcm, out var cmstream)
                && CheckIGStream("lightning.dat", out var iglight, out var lightstream)
                && CheckIGStream("mobys.dat", out var igmobys, out var mobysstream)
                && CheckIGStream("ties.dat", out var igties, out var tiesstream)
                && CheckIGStream("shrubs.dat", out var igshrubs, out var shrubsstream)
                && CheckIGStream("shaders.dat", out var igshaders, out var shadersstream)
                && CheckIGStream("zones.dat", out var igzones, out var zonesstream);
        }
        if(!flags)
        {
            var e = new FileNotFoundException("A file is missing ! Which one ? Who knows lol");
            LunaLog.LogFatal(e);
            throw e;
        }
        gameplay = new(iggp, gpstream);
        textures = new(igtex, texturestm);
        */

        static byte B(bool b) => (byte)(b ? 1 : 0);

        var globalLoadingMax = 3 + B(loadMobys) + B(loadTies) + B(loadUFrags) + B(loadShrubs) + B(loadPlants) + B(loadFoliages);
        var loadingState = new LoadingProgress("Loading level...", (uint)globalLoadingMax);
        loadingTracker.LoadProgresses.Add(loadingState);

        LoadZones();
        loadingState.current++;
        LoadTextures();
        loadingState.current++;
        LoadShaders();
        loadingState.current++;

        if(loadMobys)
        {
            LoadMobys();
            loadingState.current++;
        }

        if (loadTies)
        {
            LoadTies();
            loadingState.current++;
        }

        if(loadUFrags)
        {
            LoadUFrags();
            loadingState.current++;
        }

        if(loadShrubs)
        {
            LoadShrubs();
            loadingState.current++;
        }

        if(loadPlants)
        {
            LoadPlants();
            loadingState.current++;
        }

        if(loadFoliages)
        {
            LoadFoliages();
            loadingState.current++;
        }
        Loaded = true;
    }

    bool CheckIGStream(string fileName, out IGFile? igfile, out Stream? stream)
    {
        var flag1 = fileManager.igfiles.TryGetValue(fileName, out igfile);
        var flag2 = fileManager.rawfiles.TryGetValue(fileName, out stream);
        return flag1 && flag2; // for some reason, inline everything doesn't work
    }

    #region Basic Loading

    public void LoadTextures()
    {
        if (fileManager.isOld) LoadTexturesOld();
        else LoadTexturesNew();
    }

    public void LoadShaders()
    {
        if(fileManager.isOld) LoadShadersOld();
        else LoadShadersNew();
    }

    public void LoadMobys()
    {
        if (fileManager.isOld) LoadMobysOld();
        else LoadMobysNew();
    }

    public void LoadTies()
    {
        if(fileManager.isOld) LoadTiesOld();
        else LoadTiesNew();
    }

    public void LoadZones()
    {
        if (fileManager.isOld) LoadZonesOld();
        else LoadZonesNew();
    }

    public void LoadUFrags()
    {
        if (fileManager.isOld) LoadUFragsOld();
        else LoadUFragsNew();
    }

    public void LoadShrubs()
    {
        if (fileManager.isOld) LoadShrubsOld();
        else LoadShrubsNew();
    }

    public void LoadPlants()
    {  
        if (fileManager.isOld) LoadPlantsOld();
        else LoadPlantsNew();
    }

    public void LoadFoliages()
    {
        if(fileManager.isOld) LoadFoliagesOld();
        else LoadFoliagesNew();
    }

    #endregion

    #region Specialized Loading

    #region Textures
    public void LoadTexturesNew()
    {

    }

    public void LoadTexturesOld()
    {
        if(!fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null
        || !fileManager.rawfiles.TryGetValue("main.dat", out Stream? mainDatStream) || mainDatStream is null)
        {
            throw new FileNotFoundException("main.dat is absent");
        }
        if(!fileManager.rawfiles.TryGetValue("textures.dat", out Stream? textureStream) || textureStream is null)
        {
            throw new FileNotFoundException("textures.dat is absent");
        }
        LunaStream? texstream = null;
        if(!fileManager.rawfiles.TryGetValue("texstream.dat", out Stream? texstreamStream) || texstreamStream is null)
        {
            LunaLog.LogWarn("texstream.dat is missing. Low quality textures only.");
        }
        else
        {
            textureStream = new LunaStream(texstreamStream, textureStream);
        }

        var mainStream = new LunaStream(mainDatStream, mainDatStream);
        var textures = new LunaStream(textureStream, textureStream);

        var textureMetadataSection = main.QuerySection(TextureMetadataOld.ID);
        var texstreamRefSection = main.QuerySection(TexstreamReference.ID);

        var loadState = new LoadingProgress("Loading textures metadata...", textureMetadataSection.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for(uint i = 0; i < textureMetadataSection.count; i++)
        {
            mainStream.Seek(textureMetadataSection.offset + TextureMetadataOld.Size * i);
            var texture = new Texture(mainStream, true, i);
            Textures.Add(texture.id, texture);

            if (texstream is not null) texture.highmipsMetadatasOld = [];

            loadState.SetProgress(i + 1);
        }

        if(texstream is not null)
        {
            loadState.SetStatus("Loading textures highmips...");
            loadState.SetTotal(texstreamRefSection.count);
            loadState.SetProgress(0);
            for(uint i = 0; i < texstreamRefSection.count; i++)
            {
                mainStream.Seek(texstreamRefSection.offset + TexstreamReference.Size * i);
                var texref = new TexstreamReference(mainStream);
                Textures[texref.index].highmipsMetadatasOld?.Add(texref);
                loadState.SetProgress(i + 1);
            }
        }

        loadState.SetStatus("Reading textures...");
        loadState.SetTotal((uint)Textures.Count);
        loadState.SetProgress(0);
        var streamToRead = texstream is null ? textures : texstream;
        for(uint i = 0; i < Textures.Count; i++)
        {
            Textures[i].ReadTexture(streamToRead);
            loadState.SetProgress(i + 1);
        }
        loadingTracker.LoadProgresses.Remove(loadState);
    }
    #endregion

    #region Shaders
    public void LoadShadersNew()
    {

    }

    public void LoadShadersOld()
    {

    }
    #endregion

    #region Mobys
    public void LoadMobysNew()
    {
        if(!fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            var e = new FileNotFoundException($"Assetlookup file have not been found in {fileManager.folderPath}", "assetlookup.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }
        if(!fileManager.rawfiles.TryGetValue("mobys.dat", out Stream? mobyRaw) || mobyRaw is null)
        {
            var e = new FileNotFoundException($"Mobys data file have not been found in {fileManager.folderPath}", "mobys.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }

        // Read Mobys Pointers

        IGFile.SectionHeader mobyptrSection = assetlookup.QuerySection(NewMoby.PointerID);
        var assetlookupStream = new LunaStream(assetlookup.sh.BaseStream, assetlookup.sh.BaseStream);
        assetlookupStream.Seek(mobyptrSection.offset);
        MobyPointers = ArrayPool<AssetPointer>.Shared.Rent((int)mobyptrSection.count);
        var loadState = new LoadingProgress("Reading moby pointers...", mobyptrSection.count, 0);
        loadingTracker.LoadProgresses.Add(loadState);
        for(uint i = 0; i < mobyptrSection.count; i++)
        {
            MobyPointers[i] = new AssetPointer(assetlookupStream);
            loadState.SetProgress(i + 1);
            assetlookupStream.JumpRead((int)AssetPointer.Size);
        }



        // Read Mobys Metadata

        loadState.SetStatus("Reading mobys...");
        loadState.SetProgress(0);
        loadState.SetTotal(mobyptrSection.count);
        var mobysDatStream = new LunaStream(mobyRaw, mobyRaw);
        for(uint i = 0; i < MobyPointers.Length; i++)
        {
            var offset = MobyPointers[i].offset;
            var rentedBuffer = ArrayPool<byte>.Shared.Rent((int)MobyPointers[i].length);
            mobysDatStream.Read(rentedBuffer, (int)offset, rentedBuffer.Length);
            var memstream = new MemoryStream(rentedBuffer);
            var mobyStream = new LunaStream(memstream, memstream);

            var moby = new Moby(mobyStream);
            var igMoby = new IGFile(mobyStream);

            var indxSection = igMoby.QuerySection(0xE100);
            var vertSection = igMoby.QuerySection(0xE200);

            var bangleCount = ((NewMoby)moby.MobyObj).bangleCount1;
            var bangleLoading = new LoadingProgress("Loading bangles...", bangleCount);
            loadingTracker.LoadProgresses.Add(bangleLoading);
            for (uint j = 0; j < bangleCount; j++)
            {
                mobyStream.Seek(moby.BanglesPointer + MobyBangle.Size * j);
                moby.Bangles[j] = new MobyBangle(mobyStream);
                ref var bangle = ref moby.Bangles[j];

                var meshesLoading = new LoadingProgress("Loading meshes...", bangle.meshesCount);
                loadingTracker.LoadProgresses.Add(meshesLoading);
                for(uint k = 0; k < bangle.meshesCount; k++)
                {
                    mobyStream.Seek(bangle.meshesPointer + MobyMesh.Size * k);
                    bangle.meshes[k] = new MobyMesh(mobyStream);
                    ref var mesh = ref bangle.meshes[k];


                    var vertSize = mesh.verticesType == 0 ? VertexFormat0.Size : VertexFormat1.Size;
                    mobyStream.Seek(vertSection.offset + vertSize * mesh.verticesOffset);
                    mesh.ReadVerticesBuffer(mobyStream);

                    mobyStream.Seek(indxSection.offset + sizeof(uint) * mesh.indicesOffset);
                    mesh.ReadIndicesBuffer(mobyStream);

                    meshesLoading.SetProgress(k + 1);
                }
                loadingTracker.LoadProgresses.Remove(meshesLoading);

                bangleLoading.SetProgress(j + 1);
            }
            loadingTracker.LoadProgresses.Remove(bangleLoading);

            Mobys.Add(moby.TUID, moby);
            loadState.SetProgress(i + 1);
            LunaLog.LogDebug($"({mobysDatStream.Position:X}) Read Moby data {moby.TUID:X}");
        }
        loadingTracker.LoadProgresses.Remove(loadState);
    }

    public void LoadMobysOld()
    {
        if(!fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null
        || !fileManager.rawfiles.TryGetValue("main.dat", out Stream? mainBuffer) || mainBuffer is null)
        {
            var e = new FileNotFoundException($"Main file have not been found in {fileManager.folderPath}", "main.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }
        if(!fileManager.igfiles.TryGetValue("vertices.dat", out IGFile? vertIGFile) || vertIGFile is null
        || !fileManager.rawfiles.TryGetValue("vertices.dat", out Stream? verticesRaw) || verticesRaw is null)
        {
            var e = new FileNotFoundException($"Vertices buffer file have not been found in {fileManager.folderPath}", "vertices.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }
        if(!fileManager.rawfiles.TryGetValue("textures.dat", out Stream? texturesRaw) || texturesRaw is null
        || !fileManager.igfiles.TryGetValue("textures.dat", out IGFile? textIGFile) || textIGFile is null)
        {
            var e = new FileNotFoundException($"Vertices buffer file have not been found in {fileManager.folderPath}", "textures.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }

        var mobySection = main.QuerySection(OldMoby.ID);

        var mainStream = new LunaStream(mainBuffer, mainBuffer);
        

        var loadState = new LoadingProgress("Loading mobys...", mobySection.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for(uint i = 0; i < mobySection.count; i++)
        {
            mainStream.Seek(mobySection.offset + 0x0C * i);
            var moby = new Moby(mainStream);

            var bangleLoading = new LoadingProgress("Loading bangles...", moby.BanglesCount);
            loadingTracker.LoadProgresses.Add(bangleLoading);

            LunaStream vertFile;
            LunaStream indFile;
            if((moby.VerticesOffset & 0x80000000) != 0)
            {
                vertFile = new LunaStream(verticesRaw, verticesRaw);
                vertFile.Seek(vertIGFile.QuerySection(VertexFormat0.OldID).offset);
            }
            else
            {
                vertFile = new LunaStream(texturesRaw, texturesRaw);
                vertFile.Seek(0);
            }
            vertFile.Seek(moby.VerticesOffset & ~0x80000000, SeekOrigin.Current);

            if((moby.IndicesOffset & 0x80000000) != 0)
            {
                indFile = new LunaStream(verticesRaw, verticesRaw);
                indFile.Seek(vertIGFile.QuerySection(0x9100).offset);
            }
            else
            {
                indFile = new LunaStream(texturesRaw, texturesRaw);
                indFile.Seek(0);
            }
            indFile.Seek(moby.IndicesOffset & ~0x80000000, SeekOrigin.Current);

            for(uint j = 0; j < moby.BanglesCount; j++)
            {
                mainStream.Seek(moby.BanglesPointer + MobyBangle.Size * j);
                moby.Bangles[j] = new MobyBangle(mainStream);
                ref var bangle = ref moby.Bangles[j];

                var meshLoading = new LoadingProgress("Loading meshes...", bangle.meshesCount);
                loadingTracker.LoadProgresses.Add(meshLoading);

                for (uint k = 0; k < bangle.meshesCount; k++)
                {
                    mainStream.Seek(bangle.meshesPointer + MobyMesh.Size * k);
                    bangle.meshes[k] = new MobyMesh(mainStream);

                    meshLoading.SetProgress(k + 1);
                }
                loadingTracker.LoadProgresses.Remove(meshLoading);

                bangleLoading.SetProgress(j + 1);
            }

            var lastMesh = moby.Bangles.Last(b => b.meshesCount > 0).meshes[^1];
            var vertBufferSize = lastMesh.verticesOffset + lastMesh.verticesCount * (lastMesh.verticesType == 0 ? VertexFormat0.Size : VertexFormat1.Size);
            var vertBuffer = new byte[vertBufferSize];
            vertFile.Read(vertBuffer);
            var vertMemStream = new MemoryStream(vertBuffer);
            var vertexStream = new LunaStream(vertMemStream, vertMemStream);
            var indBufferSize = (lastMesh.indicesOffset + lastMesh.indicesCount) * sizeof(ushort);
            var indBuffer = new byte[indBufferSize];
            indFile.Read(indBuffer);
            var indMemStream = new MemoryStream(indBuffer);
            var indexStream = new LunaStream(indMemStream, indMemStream);

            for(int j = 0; j < moby.BanglesCount; j++)
            {
                ref var bangle = ref moby.Bangles[j];
                for(int k = 0; k < bangle.meshesCount; k++)
                {
                    ref var mesh = ref bangle.meshes[k];

                    vertexStream.Seek(mesh.verticesOffset);
                    mesh.ReadVerticesBuffer(vertexStream);
                    indexStream.Seek(mesh.indicesOffset);
                    mesh.ReadIndicesBuffer(indexStream);
                }
            }

            loadingTracker.LoadProgresses.Remove(bangleLoading);

            Mobys.Add(moby.TUID, moby);

            loadState.SetProgress(i + 1);
            LunaLog.LogDebug($"(o:{mainStream.Position:X}) Read moby {moby.TUID:X}");
        }

        loadingTracker.LoadProgresses.Remove(loadState);
    }
    #endregion

    #region Ties
    public void LoadTiesNew()
    {
        if(!fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null
        || !fileManager.rawfiles.TryGetValue("assetlookup.dat", out Stream? assetlookupStream) ||  assetlookupStream is null)
        {
            var e = new FileNotFoundException($"Assetlookup file is missing in {fileManager.folderPath}.", "assetlookup.dat");
            LunaLog.LogError(e);
            throw e;
        }
        if(!fileManager.rawfiles.TryGetValue("ties.dat", out Stream? tieFileStream) || tieFileStream is null)
        {
            var e = new FileNotFoundException($"Ties file is missing in {fileManager.folderPath}.", "ties.dat");
            LunaLog.LogError(e);
            throw e;
        }

        // Read pointers

        var tiePtrSection = assetlookup.QuerySection(TieMetadata.PointerID);
        var alStream = new LunaStream(assetlookupStream, assetlookupStream);
        TiePointers = ArrayPool<AssetPointer>.Shared.Rent((int)tiePtrSection.count);
        var loadState = new LoadingProgress("Loading ties pointers...", tiePtrSection.count);
        loadingTracker.LoadProgresses.Add(loadState);
        alStream.Seek(tiePtrSection.offset);
        for(uint i = 0; i < tiePtrSection.count; i++)
        {
            TiePointers[i] = new AssetPointer(alStream);
            alStream.JumpRead((int)AssetPointer.Size);
            loadState.SetProgress(i + 1);
        }

        // Read ties

        loadState.SetProgress(0);
        loadState.SetStatus("Loading ties...");
        loadState.SetTotal((uint)TiePointers.Length);
        for(uint i = 0; i < TiePointers.Length; i++)
        {
            ref var tiePtr = ref TiePointers[i];
            var buffer = ArrayPool<byte>.Shared.Rent((int)tiePtr.length);
            tieFileStream.Read(buffer, (int)tiePtr.offset, buffer.Length);
            var memstream = new MemoryStream(buffer);
            var tieStream = new LunaStream(memstream, memstream);

            var tie = new Tie(tieStream);
            var igTie = new IGFile(tieStream);

            Ties.Add(tie.TUID, tie);

            var vertSection = igTie.QuerySection(0x3000);
            var indxSection = igTie.QuerySection(TieVertIndex.ID);

            var meshLoading = new LoadingProgress("Loading ties meshes", tie.MeshesCount);
            loadingTracker.LoadProgresses.Add(meshLoading);
            for(uint j = 0; j < tie.MeshesCount; j++)
            {
                tieStream.Seek(tie.MeshesOffset + TieMesh.Size * j);
                tie.Meshes[j] = new TieMesh(tieStream, false);
                ref var mesh = ref tie.Meshes[j];

                tieStream.Seek(vertSection.offset + VertexFormat0.Size * j);
                mesh.ReadVerticesBuffer(tieStream);

                tieStream.Seek(indxSection.offset + sizeof(ushort) * j);
                mesh.ReadIndicesBuffer(tieStream);
            }
            loadingTracker.LoadProgresses.Remove(meshLoading);

            loadState.SetProgress(i + 1);
        }
        loadingTracker.LoadProgresses.Remove(loadState);
    }

    public void LoadTiesOld()
    {
        if(!fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) ||           main is null
        || !fileManager.rawfiles.TryGetValue("main.dat", out Stream? mainRawStream) || mainRawStream is null)
        {
            var e = new FileNotFoundException($"Main file is missing in {fileManager.folderPath}.", "main.dat");
            LunaLog.LogError(e);
            throw e;
        }
        if(!fileManager.igfiles.TryGetValue("vertices.dat", out IGFile? vertIGFile) ||   vertIGFile is null
        || !fileManager.rawfiles.TryGetValue("vertices.dat", out Stream? verticesRaw) || verticesRaw is null)
        {
            var e = new FileNotFoundException($"Vertices file is missing {fileManager.folderPath}.", "vertices.dat");
            LunaLog.LogError(e);
            throw e;
        }

        var tieSection = main.QuerySection(TieMetadata.ID);
        var mainStream = new LunaStream(mainRawStream, mainRawStream);

        var vertStream = new LunaStream(verticesRaw, verticesRaw);
        var vertSection = vertIGFile.QuerySection(0x9000);
        var indxSection = vertIGFile.QuerySection(TieVertIndex.OldID);

        var loadState = new LoadingProgress("Loading ties...", tieSection.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for(uint i = 0; i < tieSection.count; i++)
        {
            mainStream.Seek(tieSection.offset + TieMetadata.Size);
            var tie = new Tie(mainStream, true, i);
            Ties.Add(tie.TUID, tie);

            var meshesLoading = new LoadingProgress("Loading tie meshes...", tie.MeshesCount);
            loadingTracker.LoadProgresses.Add(meshesLoading);
            for(uint j = 0; j < tie.MeshesCount; j++)
            {
                mainStream.Seek(tie.MeshesOffset + TieMesh.Size * j);
                tie.Meshes[j] = new TieMesh(mainStream, true);
                ref var mesh = ref tie.Meshes[j];

                vertStream.Seek(vertSection.offset + mesh.verticesIndex * VertexFormat0.Size);
                mesh.ReadVerticesBuffer(vertStream);

                meshesLoading.SetProgress(j + 1);
            }
            loadingTracker.LoadProgresses.Remove(meshesLoading);

            loadState.SetProgress(i + 1);
        }
        loadingTracker.LoadProgresses.Remove(loadState);
    }
    #endregion;

    #region Zones
    public void LoadZonesNew()
    {
        if(!fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null
        || !fileManager.rawfiles.TryGetValue("assetlookup.dat", out Stream? assetlookupStream) || assetlookupStream is null)
        {
            var e = new FileNotFoundException($"Assetlookup is missing in {fileManager.folderPath}", "assetlookup.dat");
            LunaLog.LogError(e);
            throw e;
        }
        if(!fileManager.rawfiles.TryGetValue("zones.dat", out Stream? zonesFileStream) || zonesFileStream is null)
        {
            var e = new FileNotFoundException($"Zones file is missing in {fileManager.folderPath}", "zones.dat");
            LunaLog.LogError(e);
            throw e;
        }

        // Read Zones pointers

        var alStream = new LunaStream(assetlookupStream, assetlookupStream);
        var zoneSection = assetlookup.QuerySection(Zone.PointerID);
        ZonePointers = ArrayPool<AssetPointer>.Shared.Rent((int)zoneSection.length / 0x10);
        var loadState = new LoadingProgress("Loading Zone pointers...", (uint)ZonePointers.Length);
        loadingTracker.LoadProgresses.Add(loadState);
        for(uint i = 0; i < ZonePointers.Length; i++)
        {
            alStream.Seek(zoneSection.offset + AssetPointer.Size * i);
            ZonePointers[i] = new AssetPointer(alStream);

            loadState.SetProgress(i + 1);
        }

        // Read Zones

        loadState.SetStatus("Loading zones...");
        loadState.SetProgress(0);
        loadState.SetTotal((uint)ZonePointers.Length);
        for(uint i = 0; i < ZonePointers.Length; i++)
        {
            ref var pointer = ref ZonePointers[i];
            var buffer = new byte[pointer.length];
            zonesFileStream.Read(buffer, (int)pointer.offset, (int)pointer.length);
            var memStream = new MemoryStream(buffer);
            var zoneStream = new LunaStream(memStream, memStream);

            var zone = new Zone(zoneStream);
            Zones.Add(zone.TUID, zone);

            var tieInstLoading = new LoadingProgress("Loading tie instances...", zone.tieInstanceSection.count);
            loadingTracker.LoadProgresses.Add(tieInstLoading);
            for(uint j = 0; j < zone.tieInstanceSection.count; j++)
            {
                zone.zoneStream.Seek(zone.tieInstanceSection.offset + TieInstance.Size * j);
                zone.tieInstances[j] = new TieInstance(zone.zoneStream);

                tieInstLoading.SetProgress(j + 1);
            }
            loadingTracker.LoadProgresses.Remove(tieInstLoading);

            loadState.SetProgress(i + 1);
        }
        loadingTracker.LoadProgresses.Remove(loadState);
    }

    public void LoadZonesOld()
    {
        if (!fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null
        || !fileManager.rawfiles.TryGetValue("main.dat", out Stream? mainDatStream) || mainDatStream is null)
        {
            var e = new FileNotFoundException($"Assetlookup is missing in {fileManager.folderPath}", "assetlookup.dat");
            LunaLog.LogError(e);
            throw e;
        }

        var mstream = new LunaStream(mainDatStream, mainDatStream);
        var zoneSection = main.QuerySection(Zone.OldID);
        var zone = new Zone(mstream);
        Zones.Add(0, zone);
        var loadState = new LoadingProgress("Loading tie instances...", zone.tieInstanceSection.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < zone.tieInstanceSection.count; i++)
        {
            zone.zoneStream.Seek(zone.tieInstanceSection.offset + TieInstance.Size * i);
            zone.tieInstances[i] = new TieInstance(zone.zoneStream);
            loadState.SetProgress(i + 1);
        }
        loadingTracker.LoadProgresses.Remove(loadState);
    }
    #endregion

    #region UFrags
    public void LoadUFragsNew()
    {

    }

    public void LoadUFragsOld()
    {

    }
    #endregion

    #region Shrubs
    public void LoadShrubsNew()
    {

    }

    public void LoadShrubsOld()
    {

    }
    #endregion

    #region Plants
    public void LoadPlantsNew()
    {

    }

    public void LoadPlantsOld()
    {

    }
    #endregion

    #region Foliages
    public void LoadFoliagesNew()
    {

    }

    public void LoadFoliagesOld()
    {

    }
    #endregion
    #endregion

    public void Dispose()
    {
        foreach(var ufragArray in UFrags)
        {
            ArrayPool<UFragMetadata>.Shared.Return(ufragArray);
        }
        ArrayPool<UFragMetadata[]>.Shared.Return(UFrags);

        if(MobyPointers != null) ArrayPool<AssetPointer>.Shared.Return(MobyPointers);
        if (TiePointers != null) ArrayPool<AssetPointer>.Shared.Return(TiePointers);

        foreach(var tie in Ties) tie.Value.Dispose();
        foreach(var moby in Mobys) moby.Value.Dispose();

        GC.SuppressFinalize(this);
    }
}
