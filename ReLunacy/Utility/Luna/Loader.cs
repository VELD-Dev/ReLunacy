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

    public Loader(LoadingModal loadModal, FileManager fileManager, bool loadMobys = true, bool loadTies = true, bool loadUFrags = true, bool loadShrugs = true, bool loadPlants = true, bool LoadFoliages = true)
    {
        loadingTracker = loadModal;
        this.fileManager = fileManager;

        if(loadMobys)
        {
            LoadMobys();
        }

        if (loadTies)
        {

        }

        if(loadUFrags)
        {

        }

        if(loadShrugs)
        {

        }

        if(loadPlants)
        {

        }

        if(LoadFoliages)
        {

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
        if( fileManager.isOld) LoadFoliagesOld();
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

        loadState.SetStatus("Reading mobys' metadata...");
        loadState.SetProgress(0);
        loadState.SetTotal(mobyptrSection.count);
        var mobysStream = new LunaStream(mobyRaw, mobyRaw);
        for(uint i = 0; i < MobyPointers.Length; i++)
        {
            var offset = MobyPointers[i].offset;
            var rentedBuffer = ArrayPool<byte>.Shared.Rent((int)MobyPointers[i].length);
            mobysStream.Read(rentedBuffer, (int)offset, rentedBuffer.Length);
            var mobyStream = new MemoryStream(rentedBuffer);

            var moby = new Moby(new LunaStream(mobyStream, mobyStream));
            Mobys.Add(moby.TUID, moby);
            loadState.SetProgress(i + 1);
            LunaLog.LogDebug($"({mobysStream.Position:X}) Read Moby Metadata {moby.TUID:X}");
        }

        for(uint i = 0; i < Mobys.Count; i++)
        {
            var moby = Mobys[i];
            var igFile = new IGFile(moby.mobyStream);
            IGFile.SectionHeader section = igFile.QuerySection(NewMoby.ID);

            moby.mobyStream.Seek(section.offset);
            moby.ReadMoby(false);

        }
    }

    public void LoadMobysOld()
    {

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
