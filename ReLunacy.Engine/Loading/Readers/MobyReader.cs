using ReLunacy.Engine.Assets.Geometry;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Mobys;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Meshes;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Readers;

public sealed class MobyReader
{
    private readonly FileManager _fileManager;
    private readonly MaterialReader _materialReader;
    private readonly DebugReader _debugReader;

    public MobyReader(FileManager fileManager, MaterialReader materialReader, DebugReader debugReader)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _materialReader = materialReader ?? throw new ArgumentNullException(nameof(materialReader));
        _debugReader = debugReader ?? throw new ArgumentNullException(nameof(debugReader));
    }

    public Dictionary<ulong, Assets.Mobys.Moby> ReadAllMobys() => _fileManager.isOld ? ReadMobysOld() : ReadMobysNew();

    private Dictionary<ulong, Assets.Mobys.Moby> ReadMobysOld()
    {
        var mobys = new Dictionary<ulong, Assets.Mobys.Moby>();
        IGFile main = _fileManager.igfiles["main.dat"]!;
        IGFile.SectionHeader mobySection = main.QuerySection(0xD100);

        for (int i = 0; i < mobySection.count; i++)
        {
            var legacyMoby = new Objects.Moby(main.sh, _fileManager, i);
            mobys.Add((ulong)i, ConvertMoby(legacyMoby, (ulong)i));
        }

        return mobys;
    }

    private Dictionary<ulong, Assets.Mobys.Moby> ReadMobysNew()
    {
        var mobys = new Dictionary<ulong, Assets.Mobys.Moby>();

        if (!_fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            Console.WriteLine("Cannot find assetlookup.dat.");
            return mobys;
        }

        IGFile.SectionHeader mobySection = assetlookup.QuerySection(0x1D600);
        assetlookup.sh.Seek(mobySection.offset);
        AssetPointer[] mobyPtrs = AssetPointer.ReadArray(assetlookup.sh, mobySection.length / 0x10);
        Stream mobyStream = _fileManager.rawfiles["mobys.dat"]!;

        for (int i = 0; i < mobyPtrs.Length; i++)
        {
            byte[] mobydat = new byte[mobyPtrs[i].length];
            mobyStream.Seek(mobyPtrs[i].offset, SeekOrigin.Begin);
            mobyStream.Read(mobydat, 0x00, (int)mobyPtrs[i].length);

            MemoryStream mobyms = new(mobydat);
            StreamHelper streamHelper = new(mobyms, StreamHelper.Endianness.Big);

            var legacyMoby = new Objects.Moby(streamHelper, _fileManager);
            mobys.Add(mobyPtrs[i].TUID, ConvertMoby(legacyMoby, mobyPtrs[i].TUID));

            mobyms.Dispose();
            streamHelper.Dispose();
        }

        return mobys;
    }

    private Assets.Mobys.Moby ConvertMoby(Objects.Moby legacyMoby, ulong tuid)
    {
        ReadMobyBanglesMeshes(legacyMoby);

        var bangles = new List<Bangle>();
        for (int i = 0; i < legacyMoby.BanglesCount; i++)
        {
            var legacyBangle = legacyMoby.Bangles[i];
            if (legacyBangle.meshes == null || legacyBangle.meshesCount == 0)
                continue;

            var meshes = new List<IMesh>();
            for (int j = 0; j < legacyBangle.meshes.Length; j++)
            {
                meshes.Add(ConvertMobyMesh(legacyBangle.meshes[j], legacyMoby));
            }

            bangles.Add(new Bangle(meshes, $"Bangle_{i}"));
        }

        var boundingSphere = legacyMoby.BoundingSphere;
        var boundingCenter = new System.Numerics.Vector3(boundingSphere.X, boundingSphere.Y, boundingSphere.Z);
        float boundingRadius = boundingSphere.W;

        var name = _debugReader.GetMobyPrototypeName(tuid) ?? $"Moby_{tuid:X}";

        return new Assets.Mobys.Moby(
            id: tuid,
            bangles: bangles,
            scale: legacyMoby.Scale,
            name: name,
            boundingSphereCalculator: () => (boundingCenter, boundingRadius));
    }

    private void ReadMobyBanglesMeshes(Objects.Moby moby)
    {
        moby.verticesStream.Seek(0);
        for (uint i = 0; i < moby.BanglesCount; i++)
        {
            for (int j = 0; j < moby.Bangles[i].meshesCount; j++)
            {
                ref MobyMesh mesh = ref moby.Bangles[i].meshes[j];
                moby.verticesStream.Seek(mesh.verticesOffset);
                mesh.ReadVerticesBuffer(moby.verticesStream);
            }
        }

        moby.indicesStream.Seek(0);
        for (uint i = 0; i < moby.BanglesCount; i++)
        {
            for (int j = 0; j < moby.Bangles[i].meshesCount; j++)
            {
                ref MobyMesh mesh = ref moby.Bangles[i].meshes[j];
                moby.indicesStream.Seek(mesh.indicesOffset * sizeof(ushort));
                mesh.ReadIndicesBuffer(moby.indicesStream);
            }
        }
    }

    private IMesh ConvertMobyMesh(MobyMesh legacyMesh, Objects.Moby moby)
    {
        // Positions are fixed-point int16 in bangle-local space; the moby's own scale must be
        // applied here, matching what MobyMesh.GetBuffers already does for the legacy renderer.
        legacyMesh.GetBuffers(moby.Scale, out var positions, out var indices, out var uvs);

        var geometry = new GeometryData(id: 0, positions: positions, uvs: uvs, indices: indices);

        IMaterial material = moby.IsOld
            ? _materialReader.GetMaterialByIndex(legacyMesh.shaderIndex)
            : _materialReader.GetMaterialForLocalIndex(moby.ShaderTUIDs, legacyMesh.shaderIndex);

        return new Assets.Geometry.Mesh(geometry, material, "MobyMesh");
    }
}
