using System.Numerics;
using LibLunacy.Experimental.Assets.Geometry;
using LibLunacy.Experimental.Assets.Mobys;
using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Meshes;
using LibLunacy.Objects;
using LibLunacy.Vertices;

namespace LibLunacy.Experimental.Loading.Readers;

/// <summary>
/// Reads Moby assets from IGFile format and converts to experimental Moby types
/// </summary>
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

    /// <summary>
    /// Reads all mobys from the level
    /// </summary>
    public Dictionary<ulong, Assets.Mobys.Moby> ReadAllMobys()
    {
        if (_fileManager.isOld)
            return ReadMobysOld();
        else
            return ReadMobysNew();
    }

    private Dictionary<ulong, Assets.Mobys.Moby> ReadMobysOld()
    {
        var mobys = new Dictionary<ulong, Assets.Mobys.Moby>();
        IGFile main = _fileManager.igfiles["main.dat"];
        IGFile.SectionHeader mobySection = main.QuerySection(0xD100);

        for (int i = 0; i < mobySection.count; i++)
        {
            var legacyMoby = new LibLunacy.Objects.Moby(main.sh, _fileManager, i);
            var expMoby = ConvertMoby(legacyMoby, (ulong)i);
            mobys.Add((ulong)i, expMoby);
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
        AssetPointer[] mobyPtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, mobySection.length / 0x10);
        Stream mobyStream = _fileManager.rawfiles["mobys.dat"];

        for (int i = 0; i < mobyPtrs.Length; i++)
        {
            byte[] mobydat = new byte[mobyPtrs[i].length];
            mobyStream.Seek(mobyPtrs[i].offset, SeekOrigin.Begin);
            mobyStream.Read(mobydat, 0x00, (int)mobyPtrs[i].length);

            MemoryStream mobyms = new MemoryStream(mobydat);
            StreamHelper streamHelper = new StreamHelper(mobyms, StreamHelper.Endianness.Big);

            var legacyMoby = new LibLunacy.Objects.Moby(streamHelper, _fileManager);
            var expMoby = ConvertMoby(legacyMoby, mobyPtrs[i].TUID);
            mobys.Add(mobyPtrs[i].TUID, expMoby);

            mobyms.Dispose();
            streamHelper.Dispose();
        }

        return mobys;
    }

    /// <summary>
    /// Converts legacy Moby to experimental Moby
    /// </summary>
    private Assets.Mobys.Moby ConvertMoby(LibLunacy.Objects.Moby legacyMoby, ulong tuid)
    {
        // Read bangles
        ReadMobyBanglesMeshes(legacyMoby);

        // Convert bangles
        var bangles = new List<Bangle>();
        for (int i = 0; i < legacyMoby.BanglesCount; i++)
        {
            var legacyBangle = legacyMoby.Bangles[i];
            if (legacyBangle.meshes == null || legacyBangle.meshesCount == 0)
                continue;

            var meshes = new List<IMesh>();

            for (int j = 0; j < legacyBangle.meshes.Length; j++)
            {
                var legacyMesh = legacyBangle.meshes[j];
                var mesh = ConvertMobyMesh(legacyMesh, legacyMoby.mobyStream, legacyMoby.IsOld);
                meshes.Add(mesh);
            }

            var bangle = new Bangle(meshes, $"Bangle_{i}");
            bangles.Add(bangle);
        }

        // Calculate bounding sphere
        var boundingSphere = legacyMoby.BoundingSphere;
        var boundingCenter = new Vector3(boundingSphere.X, boundingSphere.Y, boundingSphere.Z);
        float boundingRadius = boundingSphere.W;

        // Get debug name if available
        var debugName = _debugReader.GetMobyPrototypeName(tuid);
        var name = debugName ?? $"Moby_{tuid:X}";

        return new Assets.Mobys.Moby(
            id: tuid,
            bangles: bangles,
            scale: legacyMoby.Scale,
            name: name,
            boundingSphereCalculator: () => (boundingCenter, boundingRadius)
        );
    }

    /// <summary>
    /// Reads bangle data for a moby (meshes, vertices, indices)
    /// </summary>
    private void ReadMobyBanglesMeshes(LibLunacy.Objects.Moby moby)
    {
        // Read vertices for each mesh (using direct offsets from each mesh)
        moby.verticesStream.Seek(0);
        for (uint i = 0; i < moby.BanglesCount; i++)
        {
            for (int j = 0; j < moby.Bangles[i].meshesCount; j++)
            {
                ref MobyMesh mesh = ref moby.Bangles[i].meshes[j];
                moby.verticesStream.Seek(mesh.verticesOffset);  // This is not required anymore as the VerticesStream already only has the vertexBuffer !
                mesh.ReadVerticesBuffer(moby.verticesStream);
            }
        }

        // Read indices for each mesh (using direct offsets from each mesh)
        moby.indicesStream.Seek(0);
        for (uint i = 0; i < moby.BanglesCount; i++)
        {
            for (int j = 0; j < moby.Bangles[i].meshesCount; j++)
            {
                ref MobyMesh mesh = ref moby.Bangles[i].meshes[j];
                moby.indicesStream.Seek(mesh.indicesOffset * sizeof(ushort));  // Same than for Vertices !
                mesh.ReadIndicesBuffer(moby.indicesStream);
            }
        }
    }

    /// <summary>
    /// Converts a MobyMesh to experimental Mesh
    /// </summary>
    private IMesh ConvertMobyMesh(MobyMesh legacyMesh, StreamHelper sh, bool isOld)
    {
        // Extract vertex data
        var positions = new List<float>();
        var uvs = new List<float>();
        var indices = new List<uint>();

        if (legacyMesh.verticesType == 0 && legacyMesh.vertices0 != null)
        {
            foreach (var vertex in legacyMesh.vertices0.Take(legacyMesh.verticesCount))
            {
                positions.Add(vertex.position.Item1);
                positions.Add(vertex.position.Item2);
                positions.Add(vertex.position.Item3);
                uvs.Add((float)vertex.UVs.Item1);
                uvs.Add((float)vertex.UVs.Item2);
            }
        }
        else if (legacyMesh.verticesType == 1 && legacyMesh.vertices1 != null)
        {
            foreach (var vertex in legacyMesh.vertices1.Take(legacyMesh.verticesCount))
            {
                positions.Add(vertex.position.Item1);
                positions.Add(vertex.position.Item2);
                positions.Add(vertex.position.Item3);
                uvs.Add((float)vertex.UVs.Item1);
                uvs.Add((float)vertex.UVs.Item2);
            }
        }

        // Extract indices
        if (legacyMesh.indices != null)
        {
            foreach (var index in legacyMesh.indices.Take(legacyMesh.indicesCount))
            {
                indices.Add(index);
            }
        }

        // Create geometry
        var geometry = new GeometryData(
            id: 0, // No specific ID for mesh geometry
            positions: positions.ToArray(),
            uvs: uvs.ToArray(),
            indices: indices.ToArray()
        );

        // Get material (using shader ID if available)
        var material = _materialReader.GetMaterial(legacyMesh.shaderIndex);

        return new Mesh(geometry, material, "MobyMesh");
    }
}
