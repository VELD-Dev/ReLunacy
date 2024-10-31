using LibLunacy.Meshes;
using LibLunacy.Objects;
using LibLunacy.Vertices;
using ReLunacy.Frames.ModalFrames;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Utility.Luna;

public class Loader : IDisposable
{
    private readonly LoadingModal loadingTracker;
    public readonly FileManager fileManager;

    public AssetPointer[] MobyPointers;

    public Dictionary<ulong, Moby> Mobys = [];
    public Dictionary<ulong, Tie> Ties = [];
    public UFragMetadata[][] UFrags = [];

    public Loader(LoadingModal loadModal, FileManager fileManager, bool loadMobys = true, bool loadTies = true, bool loadUFrags = true, bool loadShrubs = true, bool loadPlants = true, bool loadFoliages = true)
    {
        loadingTracker = loadModal;
        this.fileManager = fileManager;

        if(loadMobys)
        {
            LoadMobys();
        }

        if (loadTies)
        {
            LoadTies();
        }

        if(loadUFrags)
        {
            LoadUFrags();
        }

        if(loadShrubs)
        {
            LoadShrubs();
        }

        if(loadPlants)
        {
            LoadPlants();
        }

        if(loadFoliages)
        {
            LoadFoliages();
        }
    }

    #region Basic Loading

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
            LunaLog.LogDebug($"({assetlookupStream.Position:X}) Read Moby Pointer {MobyPointers[i].TUID:X}");
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

            mobyStream.Seek(moby.BanglesPointer);
            var bangleCount = ((NewMoby)moby.MobyObj).bangleCount1;
            var bangleLoading = new LoadingProgress("Loading bangles...", bangleCount);
            loadingTracker.LoadProgresses.Add(bangleLoading);
            for (uint j = 0; j < bangleCount; j++)
            {
                moby.Bangles[j] = new MobyBangle(mobyStream);
                ref var bangle = ref moby.Bangles[j];

                mobyStream.Seek(bangle.meshesPointer);

                var meshesLoading = new LoadingProgress("Loading meshes...", bangle.meshesCount);
                loadingTracker.LoadProgresses.Add(meshesLoading);
                for(uint k = 0; k < bangle.meshesCount; k++)
                {
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
                mobyStream.JumpRead((int)MobyBangle.Size);
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
        if (!fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
        {
            var e = new FileNotFoundException($"Main file have not been found in {fileManager.folderPath}", "main.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }
        if(!fileManager.rawfiles.TryGetValue("main.dat", out Stream? mainBuffer) || mainBuffer is null)
        {
            var e = new FileNotFoundException($"Main raw file stream have not been found. Main file may be absent in {fileManager.folderPath}.", "main.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }
        if (!fileManager.rawfiles.TryGetValue("vertices.dat", out Stream? verticesRaw) || verticesRaw is null)
        {
            var e = new FileNotFoundException($"Vertices buffer file have not been found in {fileManager.folderPath}", "vertices.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }
        if (!fileManager.rawfiles.TryGetValue("textures.dat", out Stream? texturesRaw) || texturesRaw is null)
        {
            var e = new FileNotFoundException($"Vertices buffer file have not been found in {fileManager.folderPath}", "textures.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }

        var mobySection = main.QuerySection(OldMoby.ID);

        var mainStream = new LunaStream(mainBuffer, mainBuffer);
        

        var loadState = new LoadingProgress("Loading mobys...", mobySection.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for(int i = 0; i < mobySection.count; i++)
        {
            mainStream.Seek(mobySection.offset + 0x0C * i);
            var moby = new Moby(mainStream);

            mainStream.Seek(moby.BanglesPointer);
            for(int j = 0; j < moby.BanglesCount; j++)
            {
                moby.Bangles[j] = new MobyBangle(mainStream);
                ref var bangle = ref moby.Bangles[j];

                for (int k = 0; k < bangle.meshesCount; k++)
                {
                    bangle.meshes[k] = new MobyMesh(mainStream);
                }
            }
        }
    }
    #endregion

    #region Ties
    public void LoadTiesNew()
    {

    }

    public void LoadTiesOld()
    {

    }
    #endregion

    #region Zones
    public void LoadZonesNew()
    {

    }

    public void LoadZonesOld()
    {

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

        GC.SuppressFinalize(this);
    }
}
