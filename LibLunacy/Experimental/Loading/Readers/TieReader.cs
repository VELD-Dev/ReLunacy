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
        IGFile.SectionHeader tieSection = main.QuerySection(0x3400);

        for (uint i = 0; i < tieSection.count; i++)
        {
            var legacyTie = new LibLunacy.Objects.Tie(main.sh, old: true, index: i);
            var expTie = ConvertTie(legacyTie);
            ties.Add(legacyTie.TUID, expTie);
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

            var legacyTie = new LibLunacy.Objects.Tie(streamHelper, old: false);
            var expTie = ConvertTie(legacyTie);
            ties.Add(legacyTie.TUID, expTie);

            tiems.Dispose();
            streamHelper.Dispose();
        }

        return ties;
    }

    /// <summary>
    /// Converts legacy Tie to experimental Tie
    /// </summary>
    private Assets.Ties.Tie ConvertTie(LibLunacy.Objects.Tie legacyTie)
    {
        // Read tie meshes
        ReadTieMeshes(legacyTie);

        // Convert meshes
        var meshes = new List<IMesh>();
        for (int i = 0; i < legacyTie.MeshesCount; i++)
        {
            var legacyMesh = legacyTie.Meshes[i];
            var mesh = ConvertTieMesh(legacyMesh);
            meshes.Add(mesh);
        }

        // Calculate average scale from Vec3
        var scaleVec = legacyTie.Scale;
        float avgScale = (scaleVec.X + scaleVec.Y + scaleVec.Z) / 3.0f;

        // Get name: prefer debug name, then legacy name, then fallback
        var debugName = _debugReader.GetTiePrototypeName(legacyTie.TUID);
        var name = debugName ??
                   (!string.IsNullOrEmpty(legacyTie.Name) ? legacyTie.Name : $"Tie_{legacyTie.TUID:X}");

        return new Assets.Ties.Tie(
            id: legacyTie.TUID,
            meshes: meshes,
            scale: avgScale,
            name: name
        );
    }

    /// <summary>
    /// Reads mesh data for a tie (vertices, indices)
    /// </summary>
    private void ReadTieMeshes(LibLunacy.Objects.Tie tie)
    {
        IGFile tieIGFile = new IGFile(tie.tieStream.BaseStream);

        // Read meshes headers
        tie.tieStream.Seek(tie.MeshesOffset);
        for (byte i = 0; i < tie.MeshesCount; i++)
        {
            tie.Meshes[i] = new TieMesh(tie.tieStream, tie.isOld);
            tie.tieStream.BaseStream.Position += TieMesh.Size;
        }

        // Read vertices
        IGFile.SectionHeader verticesSection = tieIGFile.QuerySection(tie.isOld ? VertexFormat0.OldID : VertexFormat0.ID);
        tie.tieStream.Seek(verticesSection.offset);

        for (byte i = 0; i < tie.MeshesCount; i++)
        {
            ref TieMesh mesh = ref tie.Meshes[i];
            mesh.ReadVerticesBuffer(tie.tieStream);
        }

        // Read indices - use TieVertIndex
        IGFile.SectionHeader indicesSection = tieIGFile.QuerySection(tie.isOld ? TieVertIndex.OldID : TieVertIndex.ID);
        tie.tieStream.Seek(indicesSection.offset);

        for (byte i = 0; i < tie.MeshesCount; i++)
        {
            ref TieMesh mesh = ref tie.Meshes[i];
            mesh.ReadIndicesBuffer(tie.tieStream);
        }
    }

    /// <summary>
    /// Converts a TieMesh to experimental Mesh
    /// </summary>
    private IMesh ConvertTieMesh(TieMesh legacyMesh)
    {
        // Extract vertex data
        var positions = new List<float>();
        var uvs = new List<float>();
        var indices = new List<uint>();

        if (legacyMesh.vertices != null)
        {
            foreach (var vertex in legacyMesh.vertices.Take(legacyMesh.verticesCount))
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
        uint shaderIndex = legacyMesh.isOld ? legacyMesh.oldShaderIndex : legacyMesh.newShaderIndex;
        var material = _materialReader.GetMaterial(shaderIndex);

        return new Mesh(geometry, material, "TieMesh");
    }
}
