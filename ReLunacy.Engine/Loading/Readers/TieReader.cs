using ReLunacy.Engine.Assets.Geometry;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Meshes;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Readers;

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

    public Dictionary<ulong, Assets.Ties.Tie> ReadAllTies() => _fileManager.isOld ? ReadTiesOld() : ReadTiesNew();

    private Dictionary<ulong, Assets.Ties.Tie> ReadTiesOld()
    {
        var ties = new Dictionary<ulong, Assets.Ties.Tie>();
        IGFile main = _fileManager.igfiles["main.dat"]!;
        IGFile.SectionHeader tieSection = main.QuerySection(TieMetadataOld.ID);

        main.sh.Seek(tieSection.offset, SeekOrigin.Begin);
        for (uint i = 0; i < tieSection.count; i++)
        {
            var legacyTie = new Objects.Tie(main, _fileManager, old: true, index: i);

            // Old engine: TieInstance.tieIndex stores file offsets, not sequential indices —
            // key ties the same way to match.
            ulong key = tieSection.offset + i * TieMetadataOld.Size;
            ties.Add(key, ConvertTie(legacyTie, key));
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
        AssetPointer[] tiePtrs = AssetPointer.ReadArray(assetlookup.sh, tieSection.length / 0x10);
        Stream tieStream = _fileManager.rawfiles["ties.dat"]!;

        for (int i = 0; i < tiePtrs.Length; i++)
        {
            byte[] tiedat = new byte[tiePtrs[i].length];
            tieStream.Seek(tiePtrs[i].offset, SeekOrigin.Begin);
            tieStream.Read(tiedat, 0x00, (int)tiePtrs[i].length);

            MemoryStream tiems = new(tiedat);
            StreamHelper streamHelper = new(tiems, StreamHelper.Endianness.Big);

            var legacyTie = new Objects.Tie(new IGFile(tiems), _fileManager, old: false);
            // Keyed by the assetlookup pointer-table TUID, not the tie's own embedded TUID field
            // (0x68) — matches MobyReader/ZoneReader's pattern and the legacy AssetLoader, since
            // the embedded field isn't reliably unique (observed colliding at 0 across records).
            ties.Add(tiePtrs[i].TUID, ConvertTie(legacyTie, tiePtrs[i].TUID));

            tiems.Dispose();
            streamHelper.Dispose();
        }

        return ties;
    }

    private Assets.Ties.Tie ConvertTie(Objects.Tie legacyTie, ulong id)
    {
        ReadTieMeshes(legacyTie);

        var scaleVec = legacyTie.Scale;
        var meshes = new List<IMesh>();
        for (int i = 0; i < legacyTie.MeshesCount; i++)
        {
            meshes.Add(ConvertTieMesh(legacyTie.Meshes[i], scaleVec, legacyTie));
        }

        var debugName = _debugReader.GetTiePrototypeName(legacyTie.TUID);
        var name = debugName ?? (!string.IsNullOrEmpty(legacyTie.Name) ? legacyTie.Name : $"Tie_{id:X}");

        return new Assets.Ties.Tie(id: id, meshes: meshes, scale: 1.0f, name: name); // per-axis scale already applied during mesh conversion
    }

    private void ReadTieMeshes(Objects.Tie tie)
    {
        for (byte i = 0; i < tie.MeshesCount; i++)
        {
            ref TieMesh mesh = ref tie.Meshes[i];
            tie.verticesBuffer.Seek(mesh.verticesIndex * VertexFormat0.Size);
            mesh.ReadVerticesBuffer(tie.verticesBuffer);
        }

        for (byte i = 0; i < tie.MeshesCount; i++)
        {
            ref TieMesh mesh = ref tie.Meshes[i];
            tie.indicesBuffer.Seek(mesh.indicesIndex * sizeof(ushort));
            mesh.ReadIndicesBuffer(tie.indicesBuffer);
        }
    }

    private IMesh ConvertTieMesh(TieMesh legacyMesh, System.Numerics.Vector3 scale, Objects.Tie tie)
    {
        legacyMesh.GetBuffers(scale, out var positions, out var indices, out var uvs);

        var geometry = new GeometryData(id: 0, positions: positions, uvs: uvs, indices: indices);

        IMaterial material = legacyMesh.isOld
            ? _materialReader.GetMaterialByIndex(legacyMesh.oldShaderIndex)
            : _materialReader.GetMaterialForLocalIndex(tie.ShaderTUIDs, legacyMesh.newShaderIndex);

        return new Assets.Geometry.Mesh(geometry, material, "TieMesh");
    }
}
