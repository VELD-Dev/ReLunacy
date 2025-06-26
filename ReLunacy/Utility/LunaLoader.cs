using LibLunacy;
using LibLunacy.Meshes;
using LibLunacy.Objects;
using LibLunacy.Objects.Instances;
using LibLunacy.Shaders;
using LibLunacy.Textures;
using LibLunacy.Vertices;
using ReLunacy.Core.Frames.Modals;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Utility;

public class LunaLoader : IDisposable
{
    private readonly LoadingModal loadingTracker;
    public readonly FileManager fileManager;

    public AssetPointer[] ShaderPointers;
    public AssetPointer[] MobyPointers;
    public AssetPointer[] TiePointers;
    public AssetPointer[] ZonePointers;

    public Dictionary<ulong, Texture> Textures = [];
    public Dictionary<ulong, Shader> Shaders = [];
    public Dictionary<ulong, Moby> Mobys = [];
    public Dictionary<ulong, Tie> Ties = [];
    public Region[] Regions;
    private Dictionary<ulong, Zone> TempZones = [];

    public bool Loaded { get; private set; } = false;

    public LunaLoader(LoadingModal loadModal, FileManager fileManager, bool loadMobys = true, bool loadTies = true, bool loadUFrags = true, bool loadShrubs = true, bool loadPlants = true, bool loadFoliages = true)
    {
        loadingTracker = loadModal;
        this.fileManager = fileManager;

        static byte B(bool b) => (byte)(b ? 1 : 0);

        var globalLoadingMax = 4 + B(loadMobys) + B(loadTies) + B(loadUFrags) + B(loadShrubs) + B(loadPlants) + B(loadFoliages);
        var loadingState = new LoadingProgress("Loading level...", (uint)globalLoadingMax);
        loadingTracker.LoadProgresses.Add(loadingState);

        LoadZones();
        loadingState.current++;
        LoadTextures();
        loadingState.current++;
        LoadShaders();
        loadingState.current++;

        if (loadMobys)
        {
            LoadMobys();
            loadingState.current++;
        }

        if (loadTies)
        {
            LoadTies();
            loadingState.current++;
        }

        if (loadUFrags)
        {
            LoadUFrags();
            loadingState.current++;
        }

        if (loadShrubs)
        {
            LoadShrubs();
            loadingState.current++;
        }

        if (loadPlants)
        {
            LoadPlants();
            loadingState.current++;
        }

        if (loadFoliages)
        {
            LoadFoliages();
            loadingState.current++;
        }

        LoadRegions();
        loadingState.current++;

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
        if (fileManager.isOld) LoadShadersOld();
        else LoadShadersNew();
    }

    public void LoadMobys()
    {
        if (fileManager.isOld) LoadMobysOld();
        else LoadMobysNew();
    }

    public void LoadTies()
    {
        if (fileManager.isOld) LoadTiesOld();
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
        if (fileManager.isOld) LoadFoliagesOld();
        else LoadFoliagesNew();
    }

    public void LoadRegions()
    {
        if (fileManager.isOld) LoadRegionsOld();
        else LoadRegionsNew();
    }

    #endregion

    #region Specialized Loading

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

        var alstream = new LunaStream(assetlookup.sh.BaseStream, assetlookup.sh.BaseStream);
        var hmstream = new LunaStream(highmipstream, highmipstream);

        var highmipsPtrSec = assetlookup.QuerySection(Texture.HighmipsPointerID);
        var textureMetaSec = assetlookup.QuerySection(TextureMetadataNew.ID);

        var loadState = new LoadingProgress("Loading textures...", textureMetaSec.count);
        loadingTracker.LoadProgresses.Add(loadState);

        for (uint i = 0; i < textureMetaSec.count; i++)
        {
            alstream.Seek(textureMetaSec.offset + TextureMetadataNew.Size * i);
            var tex = new Texture(alstream);

            alstream.Seek(highmipsPtrSec.offset + AssetPointer.Size * i);
            tex.ReadHighmipsPtr(alstream);
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
        LunaStream? texstream = null;
        if (!fileManager.rawfiles.TryGetValue("texstream.dat", out Stream? texstreamStream) || texstreamStream is null)
        {
            LunaLog.LogWarn("texstream.dat is missing. Low quality textures only.");
        }
        else
        {
            texstream = new LunaStream(texstreamStream, texstreamStream);
        }

        var mainStream = new LunaStream(main.sh.BaseStream, main.sh.BaseStream);
        var textures = new LunaStream(textureStream, textureStream);

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
                texstreamReferences.Add(new TexstreamReference(mainStream));
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

        var alstream = new LunaStream(assetlookup.sh.BaseStream, assetlookup.sh.BaseStream);
        var shaderStream = new LunaStream(shadersStream, shadersStream);

        var shaderPtrSec = assetlookup.QuerySection(Shader.PointerID);

        ShaderPointers = new AssetPointer[shaderPtrSec.count];

        var loadState = new LoadingProgress("Loading shader pointers...", shaderPtrSec.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < shaderPtrSec.count; i++)
        {
            alstream.Seek(shaderPtrSec.offset);
            ShaderPointers[i] = new AssetPointer(alstream);

            loadState.SetProgress(i + 1);
        }


        loadState.SetStatus("Loading shaders...");
        loadState.SetTotal((uint)ShaderPointers.Length);
        loadState.SetProgress(0);
        for (uint i = 0; i < shaderPtrSec.count; i++)
        {
            ref var ptr = ref ShaderPointers[i];
            var shaderBuffer = new byte[ptr.length];
            shaderStream.ReadExactly(shaderBuffer, (int)ptr.offset, (int)ptr.length);
            var memstream = new MemoryStream(shaderBuffer);
            var igshader = new IGFile(memstream);
            var shadstream = new LunaStream(memstream, memstream);

            var metadataSection = igshader.QuerySection(ShaderMetadata.ID);
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
                shader.Albedo.name = shadstream.ReadString((int)sref.albedoNamePointer, false);
            }
            if (sref.normalID != 0 && Textures.ContainsKey(sref.normalID))
            {
                shader.Normal = Textures[sref.normalID];
                shader.Normal.name = shadstream.ReadString((int)sref.normalNamePointer, false);
            }
            if (sref.expensiveID != 0 && Textures.ContainsKey(sref.expensiveID))
            {
                shader.Expensive = Textures[sref.expensiveID];
                shader.Expensive.name = shadstream.ReadString((int)sref.expensiveNamePointer, false);
            }

            shader.name = shadstream.ReadString((int)sref.namePointer, false);

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

        var mainstream = new LunaStream(main.sh.BaseStream, main.sh.BaseStream);

        var shaderMetadataSec = main.QuerySection(ShaderMetadata.ID);

        var loadState = new LoadingProgress("Loading shaders...", shaderMetadataSec.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < shaderMetadataSec.count; i++)
        {
            mainstream.Seek(shaderMetadataSec.offset + ShaderMetadata.Size * i);
            var shader = new Shader(mainstream, true, i);

            if (Textures.Count < 1)
            {
                var e = new InvalidOperationException("Race error: Textures must be loaded BEFORE shaders ! (for now)");
                LunaLog.LogError(e);
                throw e;
            }

            if (shader.metadata.albedo != 0)
            {
                shader.Albedo = Textures[shader.metadata.albedo];
            }
            if (shader.metadata.normal != 0)
            {
                shader.Normal = Textures[shader.metadata.normal];
            }
            if (shader.metadata.expensive != 0)
            {
                shader.Expensive = Textures[shader.metadata.expensive];
            }

            Shaders.Add(shader.TUID, shader);
            loadState.SetProgress(i + 1);
        }

        loadingTracker.LoadProgresses.Remove(loadState);
    }
    #endregion

    #region Mobys
    public void LoadMobysNew()
    {
        if (!fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            var e = new FileNotFoundException($"Assetlookup file have not been found in {fileManager.folderPath}", "assetlookup.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }
        if (!fileManager.rawfiles.TryGetValue("mobys.dat", out Stream? mobyRaw) || mobyRaw is null)
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
        for (uint i = 0; i < mobyptrSection.count; i++)
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
        for (uint i = 0; i < MobyPointers.Length; i++)
        {
            var offset = MobyPointers[i].offset;
            var rentedBuffer = ArrayPool<byte>.Shared.Rent((int)MobyPointers[i].length);
            mobysDatStream.ReadExactly(rentedBuffer, (int)offset, rentedBuffer.Length);
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
                for (uint k = 0; k < bangle.meshesCount; k++)
                {
                    mobyStream.Seek(bangle.meshesPointer + MobyMesh.Size * k);
                    bangle.meshes[k] = new MobyMesh(mobyStream);
                    ref var mesh = ref bangle.meshes[k];


                    var vertSize = mesh.verticesType == 0 ? VertexFormat0.Size : VertexFormat1.Size;
                    mobyStream.Seek(vertSection.offset + vertSize * mesh.verticesOffset);
                    mesh.ReadVerticesBuffer(mobyStream);

                    for (int l = 0; l < 100 || l < mesh.verticesCount; l++)
                    {
                        if (mesh.verticesType == 0)
                            LunaLog.LogDebug($"Vertex {l}: {mesh.vertices0[l]}");
                        else if (mesh.verticesType == 1)
                            LunaLog.LogDebug($"Vertex {l}: {mesh.vertices1[l]}");
                    }

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
        if (!fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
        {
            var e = new FileNotFoundException($"Main file have not been found in {fileManager.folderPath}", "main.dat");
            LunaLog.LogError(e.Message);
            throw e;
        }
        if (!fileManager.igfiles.TryGetValue("vertices.dat", out IGFile? vertIGFile) || vertIGFile is null)
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

        var mainStream = new LunaStream(main.sh.BaseStream, main.sh.BaseStream);


        var loadState = new LoadingProgress("Loading mobys...", mobySection.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < mobySection.count; i++)
        {
            mainStream.Seek(mobySection.offset + OldMoby.Size * i);
            var moby = new Moby(mainStream, (int)i);

            var bangleLoading = new LoadingProgress("Loading bangles...", moby.BanglesCount);
            loadingTracker.LoadProgresses.Add(bangleLoading);

            LunaStream vertFile;
            LunaStream indFile;
            if ((moby.VerticesOffset & 0x80000000) != 0)
            {
                vertFile = new LunaStream(vertIGFile.sh.BaseStream, vertIGFile.sh.BaseStream);
                vertFile.Seek(vertIGFile.QuerySection(VertexFormat0.OldID).offset);
            }
            else
            {
                vertFile = new LunaStream(texturesRaw, texturesRaw);
                vertFile.Seek(0);
            }
            vertFile.Seek(moby.VerticesOffset & ~0x80000000, SeekOrigin.Current);

            if ((moby.IndicesOffset & 0x80000000) != 0)
            {
                indFile = new LunaStream(vertIGFile.sh.BaseStream, vertIGFile.sh.BaseStream);
                indFile.Seek(vertIGFile.QuerySection(0x9100).offset);
            }
            else
            {
                indFile = new LunaStream(texturesRaw, texturesRaw);
                indFile.Seek(0);
            }
            indFile.Seek(moby.IndicesOffset & ~0x80000000, SeekOrigin.Current);

            for (uint j = 0; j < moby.BanglesCount; j++)
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
            vertFile.ReadExactly(vertBuffer);
            var vertMemStream = new MemoryStream(vertBuffer);
            var vertexStream = new LunaStream(vertMemStream, vertMemStream);

            var indBufferSize = lastMesh.indicesOffset + lastMesh.indicesCount * sizeof(ushort);
            var indBuffer = new byte[indBufferSize];
            indFile.ReadExactly(indBuffer);
            var indMemStream = new MemoryStream(indBuffer);
            var indexStream = new LunaStream(indMemStream, indMemStream);

            for (int j = 0; j < moby.BanglesCount; j++)
            {
                ref var bangle = ref moby.Bangles[j];
                for (int k = 0; k < bangle.meshesCount; k++)
                {
                    ref var mesh = ref bangle.meshes[k];

                    vertexStream.Seek(mesh.verticesOffset);
                    mesh.ReadVerticesBuffer(vertexStream);

                    /*
                    for (int l = 0; l < 100 && l < mesh.verticesCount; l++)
                    {
                        if (mesh.verticesType == 0)
                            LunaLog.LogDebug($"Vertex {l}: {mesh.vertices0[l]}");
                        else if (mesh.verticesType == 1)
                            LunaLog.LogDebug($"Vertex {l}: {mesh.vertices1[l]}");
                    }
                    */

                    indexStream.Seek(mesh.indicesOffset);
                    mesh.ReadIndicesBuffer(indexStream);
                    LunaLog.LogDebug($"[ {mesh.indices.Stringify(",")} ]");
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
        if (!fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            var e = new FileNotFoundException($"Assetlookup file is missing in {fileManager.folderPath}.", "assetlookup.dat");
            LunaLog.LogError(e);
            throw e;
        }
        if (!fileManager.rawfiles.TryGetValue("ties.dat", out Stream? tieFileStream) || tieFileStream is null)
        {
            var e = new FileNotFoundException($"Ties file is missing in {fileManager.folderPath}.", "ties.dat");
            LunaLog.LogError(e);
            throw e;
        }

        // Read pointers

        var tiePtrSection = assetlookup.QuerySection(TieMetadata.PointerID);
        var alStream = new LunaStream(assetlookup.sh.BaseStream, assetlookup.sh.BaseStream);
        TiePointers = ArrayPool<AssetPointer>.Shared.Rent((int)tiePtrSection.count);
        var loadState = new LoadingProgress("Loading ties pointers...", tiePtrSection.count);
        loadingTracker.LoadProgresses.Add(loadState);
        alStream.Seek(tiePtrSection.offset);
        for (uint i = 0; i < tiePtrSection.count; i++)
        {
            TiePointers[i] = new AssetPointer(alStream);
            alStream.JumpRead((int)AssetPointer.Size);
            loadState.SetProgress(i + 1);
        }

        // Read ties

        loadState.SetProgress(0);
        loadState.SetStatus("Loading ties...");
        loadState.SetTotal((uint)TiePointers.Length);
        for (uint i = 0; i < TiePointers.Length; i++)
        {
            ref var tiePtr = ref TiePointers[i];
            var buffer = ArrayPool<byte>.Shared.Rent((int)tiePtr.length);
            tieFileStream.ReadExactly(buffer, (int)tiePtr.offset, buffer.Length);
            var memstream = new MemoryStream(buffer);
            var tieStream = new LunaStream(memstream, memstream);

            var tie = new Tie(tieStream);
            var igTie = new IGFile(tieStream);

            Ties.Add(tie.TUID, tie);

            var vertSection = igTie.QuerySection(0x3000);
            var indxSection = igTie.QuerySection(TieVertIndex.ID);

            var meshLoading = new LoadingProgress("Loading ties meshes", tie.MeshesCount);
            loadingTracker.LoadProgresses.Add(meshLoading);
            for (uint j = 0; j < tie.MeshesCount; j++)
            {
                tieStream.Seek(tie.MeshesOffset + TieMesh.Size * j);
                tie.Meshes[j] = new TieMesh(tieStream, false);
                ref var mesh = ref tie.Meshes[j];

                tieStream.Seek(vertSection.offset + VertexFormat0.Size * j);
                mesh.ReadVerticesBuffer(tieStream);

                LunaLog.LogDebug($"Vertices: {mesh.vertices.Length}");
                for (int k = 0; k < 100 || k < mesh.vertices.Length; k++)
                {
                    LunaLog.LogDebug($"Vertex {k}: {mesh.vertices[k]}");
                }

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
        if (!fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
        {
            var e = new FileNotFoundException($"Main file is missing in {fileManager.folderPath}.", "main.dat");
            LunaLog.LogError(e);
            throw e;
        }
        if (!fileManager.igfiles.TryGetValue("vertices.dat", out IGFile? vertIGFile) || vertIGFile is null)
        {
            var e = new FileNotFoundException($"Vertices file is missing {fileManager.folderPath}.", "vertices.dat");
            LunaLog.LogError(e);
            throw e;
        }

        var tieSection = main.QuerySection(TieMetadata.ID);
        var mainStream = new LunaStream(main.sh.BaseStream, main.sh.BaseStream);

        var vertStream = new LunaStream(vertIGFile.sh.BaseStream, vertIGFile.sh.BaseStream);
        var vertSection = vertIGFile.QuerySection(0x9000);
        var indxSection = vertIGFile.QuerySection(TieVertIndex.OldID);

        var loadState = new LoadingProgress("Loading ties...", tieSection.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < tieSection.count; i++)
        {
            mainStream.Seek(tieSection.offset + TieMetadata.Size);
            var tie = new Tie(mainStream, true, i);
            Ties.Add(tie.TUID, tie);

            var meshesLoading = new LoadingProgress("Loading tie meshes...", tie.MeshesCount);
            loadingTracker.LoadProgresses.Add(meshesLoading);
            for (uint j = 0; j < tie.MeshesCount; j++)
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
        if (!fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            var e = new FileNotFoundException($"Assetlookup is missing in {fileManager.folderPath}", "assetlookup.dat");
            LunaLog.LogError(e);
            throw e;
        }
        if (!fileManager.rawfiles.TryGetValue("zones.dat", out Stream? zonesFileStream) || zonesFileStream is null)
        {
            var e = new FileNotFoundException($"Zones file is missing in {fileManager.folderPath}", "zones.dat");
            LunaLog.LogError(e);
            throw e;
        }

        // Read TempZones pointers

        var alStream = new LunaStream(assetlookup.sh.BaseStream, assetlookup.sh.BaseStream);
        var zoneSection = assetlookup.QuerySection(Zone.PointerID);
        ZonePointers = ArrayPool<AssetPointer>.Shared.Rent((int)zoneSection.length / 0x10);
        var loadState = new LoadingProgress("Loading Zone pointers...", (uint)ZonePointers.Length);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < ZonePointers.Length; i++)
        {
            alStream.Seek(zoneSection.offset + AssetPointer.Size * i);
            ZonePointers[i] = new AssetPointer(alStream);

            loadState.SetProgress(i + 1);
        }

        // Read TempZones

        loadState.SetStatus("Loading zones...");
        loadState.SetProgress(0);
        loadState.SetTotal((uint)ZonePointers.Length);
        for (uint i = 0; i < ZonePointers.Length; i++)
        {
            ref var pointer = ref ZonePointers[i];
            var buffer = new byte[pointer.length];
            zonesFileStream.ReadExactly(buffer, (int)pointer.offset, (int)pointer.length);
            var memStream = new MemoryStream(buffer);
            var zoneStream = new LunaStream(memStream, memStream);

            var zone = new Zone(zoneStream);
            TempZones.Add(zone.TUID, zone);

            var tieInstLoading = new LoadingProgress("Loading tie instances...", zone.tieInstanceSection.count);
            loadingTracker.LoadProgresses.Add(tieInstLoading);
            for (uint j = 0; j < zone.tieInstanceSection.count; j++)
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
        if (!fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
        {
            var e = new FileNotFoundException($"Assetlookup is missing in {fileManager.folderPath}", "assetlookup.dat");
            LunaLog.LogError(e);
            throw e;
        }

        var mstream = new LunaStream(main.sh.BaseStream, main.sh.BaseStream);
        var zoneSection = main.QuerySection(Zone.OldID);
        var zone = new Zone(mstream);
        TempZones.Add(0, zone);
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
        if (!fileManager.igfiles.TryGetValue("main.dat", out IGFile? mainIG) || mainIG is null)
        {
            var e = new FileNotFoundException("Main file is missing.", "main.dat");
            LunaLog.LogError(e);
            throw e;
        }
        if (!fileManager.igfiles.TryGetValue("vertices.dat", out IGFile? vertices) || vertices is null)
        {
            var e = new FileNotFoundException("Vertices file is missing.", "vertices.dat");
            LunaLog.LogError(e);
            throw e;
        }

        var main = new LunaStream(mainIG.sh.BaseStream, mainIG.sh.BaseStream);
        var vertexStream = new LunaStream(vertices.sh.BaseStream, vertices.sh.BaseStream);

        var artZone = TempZones[0];

        for (int i = 0; i < artZone.ufragSection.count; i++)
        {
            main.Seek(artZone.ufragSection.offset + UFragMetadata.Size * i);
            var ufrag = new UFrag(main, true)
            {
                zoneStream = vertexStream
            };

            vertexStream.Seek(artZone.ufragVertSection.offset + ufrag.metadata.vertexOffset);
            ufrag.ReadVertices();

            vertexStream.Seek(artZone.ufragIndxSection.offset + ufrag.metadata.indexOffset);
            ufrag.ReadIndicesBuffer();
            artZone.ufrags[i] = ufrag;
        }
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

    #region Regions

    public void LoadRegionsNew()
    {
        if (!fileManager.igfiles.TryGetValue("gameplay.dat", out IGFile? iggp) || iggp is null)
        {
            var e = new FileNotFoundException("Gameplay file is missing !", "gameplay.dat");
            LunaLog.LogError(e);
            throw e;
        }

        var gameplay = new LunaStream(iggp.sh.BaseStream, iggp.sh.BaseStream);

        //gameplay.dat is a weird file in this version of the engine, the count field of section headers is the length and length field of section headers is 0

        var stringTableSec = iggp.QuerySection(Region.GameplayStringTableNewID);
        gameplay.Seek(stringTableSec.offset + stringTableSec.count - 0x10);
        var regionCount = gameplay.ReadUInt32(0x00);
        var regionNameTableOffset = gameplay.ReadUInt32(0x04);
        Regions = ArrayPool<Region>.Shared.Rent((int)regionCount);
        var regionNames = new List<string>();

        var loadState = new LoadingProgress("Reading region lookup table...", regionCount);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < regionCount; i++)
        {
            gameplay.Seek(regionNameTableOffset + sizeof(uint) * i);
            var regionNameOffset = gameplay.ReadUInt32(0x00);
            var regionName = gameplay.ReadString((int)regionNameOffset, false);
            regionNames.Add(regionName);
            LunaLog.LogDebug($"Discovered region {regionName}");
            loadState.SetProgress(i + 1);
        }

        loadState.SetStatus("Reading regions...");
        loadState.SetTotal((uint)regionNames.Count);
        loadState.SetProgress(0);
        foreach (var regionName in regionNames)
        {
            var regIndex = regionName.IndexOf(regionName);
            IGFile? igprius = (IGFile?)fileManager.LoadFile($"{regionName}/gp_prius.dat", false);
            IGFile? igregion = (IGFile?)fileManager.LoadFile($"{regionName}/region.dat", false);

            if (igprius is null || igregion is null)
            {
                var e = new FileNotFoundException($"One (or both) of the following files are missing: {fileManager.folderPath}/{regionName}/gp_prius.dat; {fileManager.folderPath}/{regionName}/region.dat", regionName);
                LunaLog.LogWarn(e);
                continue;
            }

            var prius = new LunaStream(igprius.sh.BaseStream, igprius.sh.BaseStream);
            var regStream = new LunaStream(igregion.sh.BaseStream, igregion.sh.BaseStream);

            var region = new Region(prius, regStream, regionName);

            var mobyInstSection = igprius.QuerySection(MobyInstanceNew.ID);
            var mobyMetaSection = igprius.QuerySection(InstanceMetadata.MobyInstMetadataID);
            var mobyTuidListSec = igregion.QuerySection(Region.MobyTuidsListID);

            var mobyInstLoading = new LoadingProgress("Loading moby instances...", mobyInstSection.count);
            loadingTracker.LoadProgresses.Add(mobyInstLoading);
            for (uint i = 0; i < mobyInstSection.count; i++)
            {
                prius.Seek(mobyInstSection.offset + MobyInstanceNew.Size * i);
                var mobyInst = new MobyInstanceNew(prius);
                prius.Seek(mobyMetaSection.offset + InstanceMetadata.Size * i);
                var mobyInstMeta = new InstanceMetadata(prius);
                var mobyName = prius.ReadString((int)mobyInstMeta.namePointer, false);
                regStream.Seek(mobyTuidListSec.offset + sizeof(ulong) * mobyInst.mobyIndex);
                var mobyRefTuid = regStream.ReadUInt64(0x00);

                if (Mobys.Count < 1)
                {
                    var e = new InvalidOperationException("Race error: Mobys must be initialized BEFORE reading their instances !");
                    LunaLog.LogError(e);
                    throw e;
                }

                if (!Mobys.TryGetValue(mobyRefTuid, out Moby? referredMoby))
                    continue;

                var mobyInstance = new MobyInstance(mobyInst, mobyInstMeta, referredMoby, mobyName);
                region.MobyInstances.Add(mobyInstance.TUID, mobyInstance);
                mobyInstLoading.SetProgress(i + 1);
            }
            loadingTracker.LoadProgresses.Remove(mobyInstLoading);

            var volMetaSec = igprius.QuerySection(InstanceMetadata.VolumeMetadataID);
            var volTransformSec = igprius.QuerySection(Volume.TransformSectionID);

            var volumesLoading = new LoadingProgress("Loading volumes...", volMetaSec.count);
            loadingTracker.LoadProgresses.Add(volumesLoading);
            for (uint i = 0; i < volMetaSec.count; i++)
            {
                prius.Seek(volTransformSec.offset + 0x40 * i); // 0x40 is the size of a matrix 4x4.
                var transform = prius.ReadMat4(0x00);
                prius.Seek(volMetaSec.offset + InstanceMetadata.Size * i);
                var volume = new Volume(prius, transform);
                region.Volumes.Add(volume.TUID, volume);

                volumesLoading.SetProgress(i + 1);
            }
            loadingTracker.LoadProgresses.Remove(volumesLoading);

            var zoneNamesSec = igregion.QuerySection(Region.ZoneNamePointerID);
            var zoneTUIDsSec = igregion.QuerySection(Region.ZoneTUIDsID);

            var zonesRefreshLoading = new LoadingProgress("Refreshing zones...", zoneNamesSec.count);
            for (uint i = 0; i < zoneNamesSec.count; i++)
            {
                //regStream.Seek(zoneNamesSec.offset + sizeof(uint) * i);
                //var Name = regStream.ReadString((int)regStream.ReadUInt32(0), false);
                regStream.Seek(zoneTUIDsSec.offset + sizeof(ulong) * i);
                var zoneTuid = regStream.ReadUInt64(0);
                var zone = TempZones[zoneTuid];

                region.Zones.Add(zone.TUID, zone);
                TempZones.Remove(zone.TUID);

                zonesRefreshLoading.SetProgress(i + 1);
            }

            loadState.SetProgress((uint)regIndex + 1);
        }
        loadingTracker.LoadProgresses.Remove(loadState);
    }

    public void LoadRegionsOld()
    {
        if (!fileManager.igfiles.TryGetValue("gameplay.dat", out IGFile? iggp) || iggp is null)
        {
            var e = new FileNotFoundException("Gameplay file was not found.", "gameplay.dat");
            LunaLog.LogError(e);
            throw e;
        }

        var gpstream = new LunaStream(iggp.sh.BaseStream, iggp.sh.BaseStream);
        var region = new Region(gpstream);

        var mobyInstSec = iggp.QuerySection(MobyInstanceOld.ID);

        var loadState = new LoadingProgress("Loading moby instances...", mobyInstSec.count);
        loadingTracker.LoadProgresses.Add(loadState);
        for (uint i = 0; i < mobyInstSec.count; i++)
        {
            gpstream.Seek(mobyInstSec.offset + MobyInstanceOld.Size * i);
            var mobyInst = new MobyInstanceOld(gpstream);

            if (Mobys.Count < 1)
            {
                var e = new InvalidOperationException("Race error: Mobys must be read BEFORE reading their instances!");
                LunaLog.LogError(e);
                throw e;
            }

            var referredMoby = Mobys[mobyInst.mobyIndex];

            var mobyInstance = new MobyInstance(mobyInst, referredMoby, i);
            region.MobyInstances.Add(i, mobyInstance);
        }
        loadingTracker.LoadProgresses.Remove(loadState);

        region.Zones = TempZones;

        Regions = [region];
    }

    #endregion

    #endregion

    public void Dispose()
    {
        if (MobyPointers != null) ArrayPool<AssetPointer>.Shared.Return(MobyPointers);
        if (TiePointers != null) ArrayPool<AssetPointer>.Shared.Return(TiePointers);
        if (ShaderPointers != null) ArrayPool<AssetPointer>.Shared.Return(ShaderPointers);
        if (ZonePointers != null) ArrayPool<AssetPointer>.Shared.Return(ZonePointers);

        foreach (var tie in Ties) tie.Value.Dispose();
        foreach (var moby in Mobys) moby.Value.Dispose();
        foreach (var zone in TempZones) zone.Value.Dispose();
        foreach (var region in Regions)
        {
            // Dispose mobyinstances and volumeinstances
        }

        ArrayPool<Region>.Shared.Return(Regions);

        GC.SuppressFinalize(this);
    }
}
