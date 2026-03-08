using System.Numerics;
using LibLunacy.Experimental.Assets.Geometry;
using LibLunacy.Experimental.Assets.Ties;
using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Meshes;
using LibLunacy.Objects;
using LibLunacy.Vertices;
using TieVertIndex = LibLunacy.Vertices.TieVertIndex;

namespace LibLunacy.Experimental.Loading.Readers;

/// <summary>
/// Reads Tie assets from IGFile format and converts to experimental Tie types
/// </summary>
public sealed class TieReader
{
    private readonly FileManager _fileManager;
    private readonly MaterialReader _materialReader;
    private readonly DebugReader _debugReader;

    public TieReader(FileManager fileManager, MaterialReader materialReader, DebugReader debugReader)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _materialReader = materialReader ?? throw new ArgumentNullException(nameof(materialReader));
        _debugReader = debugReader ?? throw new ArgumentNullException(nameof(debugReader));
    }

    /// <summary>
    /// Reads all ties from the level
    /// </summary>
    public Dictionary<ulong, Assets.Ties.Tie> ReadAllTies()
    {
        if (_fileManager.isOld)
            return ReadTiesOld();
        else
            return ReadTiesNew();
    }

    private Dictionary<ulong, Assets.Ties.Tie> ReadTiesOld()
    {
        var ties = new Dictionary<ulong, Assets.Ties.Tie>();
        IGFile main = _fileManager.igfiles["main.dat"];
        IGFile.SectionHeader tieSection = main.QuerySection(TieMetadataOld.ID);

        for (uint i = 0; i < tieSection.count; i++)
        {
            var legacyTie = new LibLunacy.Objects.Tie(main, _fileManager ,old: true, index: i);
            // Old engine: TieInstance.tieIndex stores file offsets, not sequential indices.
            // Use offset-based keys to match (same as legacy CTie).
            ulong key = tieSection.offset + i * TieMetadataOld.Size;
            var expTie = ConvertTie(legacyTie, key);
            ties.Add(key, expTie);
        }

        return ties;
    }

    private Dictionary<ulong, Assets.Ties.Tie> ReadTiesNew()
    {
        var ties = new Dictionary<ulong, Assets.Ties.Tie>();

        if (!_fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            Console.WriteLine("Cannot find assetlookup.dat.");
            return ties;
        }

        IGFile.SectionHeader tieSection = assetlookup.QuerySection(0x1D300);
        assetlookup.sh.Seek(tieSection.offset);
        AssetPointer[] tiePtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, tieSection.length / 0x10);
        Stream tieStream = _fileManager.rawfiles["ties.dat"];

        for (int i = 0; i < tiePtrs.Length; i++)
        {
            byte[] tiedat = new byte[tiePtrs[i].length];
            tieStream.Seek(tiePtrs[i].offset, SeekOrigin.Begin);
            tieStream.Read(tiedat, 0x00, (int)tiePtrs[i].length);

            MemoryStream tiems = new MemoryStream(tiedat);
            StreamHelper streamHelper = new StreamHelper(tiems, StreamHelper.Endianness.Big);

            var legacyTie = new LibLunacy.Objects.Tie(new IGFile(tiems), _fileManager, old: false);
            var expTie = ConvertTie(legacyTie, legacyTie.TUID);
            ties.Add(legacyTie.TUID, expTie);

            tiems.Dispose();
            streamHelper.Dispose();
        }

        return ties;
    }

    /// <summary>
    /// Converts legacy Tie to experimental Tie
    /// </summary>
    private Assets.Ties.Tie ConvertTie(LibLunacy.Objects.Tie legacyTie, ulong id)
    {
        // Read tie meshes
        ReadTieMeshes(legacyTie);

        // Convert meshes (apply per-axis scale during conversion)
        var scaleVec = legacyTie.Scale;
        var meshes = new List<IMesh>();
        for (int i = 0; i < legacyTie.MeshesCount; i++)
        {
            var legacyMesh = legacyTie.Meshes[i];
            var mesh = ConvertTieMesh(legacyMesh, scaleVec);
            meshes.Add(mesh);
        }

        // Get name: prefer debug name, then legacy name, then fallback
        var debugName = _debugReader.GetTiePrototypeName(legacyTie.TUID);
        var name = debugName ??
                   (!string.IsNullOrEmpty(legacyTie.Name) ? legacyTie.Name : $"Tie_{id:X}");

        return new Assets.Ties.Tie(
            id: id,
            meshes: meshes,
            scale: 1.0f, // Per-axis scale already applied during mesh conversion
            name: name
        );
    }

    /// <summary>
    /// Reads mesh data for a tie (vertices, indices)
    /// </summary>
    private void ReadTieMeshes(LibLunacy.Objects.Tie tie)
    {
        IGFile tieIGFile = new IGFile(tie.tieStream.BaseStream);

        // Read vertices
        for (byte i = 0; i < tie.MeshesCount; i++)
        {
            ref TieMesh mesh = ref tie.Meshes[i];
            tie.verticesBuffer.Seek(mesh.verticesIndex * VertexFormat0.Size);
            mesh.ReadVerticesBuffer(tie.verticesBuffer);
        }

        // Read indices - use TieVertIndex
        for (byte i = 0; i < tie.MeshesCount; i++)
        {
            ref TieMesh mesh = ref tie.Meshes[i];
            tie.indicesBuffer.Seek(mesh.indicesIndex * sizeof(ushort));
            mesh.ReadIndicesBuffer(tie.indicesBuffer);
        }
    }

    /// <summary>
    /// Converts a TieMesh to experimental Mesh
    /// </summary>
    private IMesh ConvertTieMesh(TieMesh legacyMesh, LibLunacy.Numerics.Vec3 scale)
    {
        // Extract vertex data with per-axis scale applied
        var positions = new List<float>();
        var uvs = new List<float>();
        var indices = new List<uint>();

        if (legacyMesh.vertices != null)
        {
            foreach (var vertex in legacyMesh.vertices.Take(legacyMesh.verticesCount))
            {
                positions.Add(vertex.position.Item1 * scale.X);
                positions.Add(vertex.position.Item2 * scale.Y);
                positions.Add(vertex.position.Item3 * scale.Z);
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
            positions: [.. positions],
            uvs: [.. uvs],
            indices: [.. indices]
        );

        // Get material (using shader ID if available)
        uint shaderIndex = legacyMesh.isOld ? legacyMesh.oldShaderIndex : legacyMesh.newShaderIndex;
        var material = _materialReader.GetMaterial(shaderIndex);

        return new Mesh(geometry, material, "TieMesh");
    }
}
