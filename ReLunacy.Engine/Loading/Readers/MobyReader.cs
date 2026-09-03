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

        // debug.dat's prototype table is old-engine only (see DebugReader) and always wins when it
        // has an entry - legacyMoby.Name (section 0xD200, new engine only) is the fallback for
        // everything debug.dat doesn't cover, same priority order Tie.cs/TieReader use for ties.
        var debugName = _debugReader.GetMobyPrototypeName(tuid);
        var name = debugName ?? (!string.IsNullOrEmpty(legacyMoby.Name) ? legacyMoby.Name : $"Moby_{tuid:X}");

        return new Assets.Mobys.Moby(
            id: tuid,
            bangles: bangles,
            scale: legacyMoby.Scale,
            name: name,
            boundingSphereCalculator: () => (boundingCenter, boundingRadius),
            skeleton: ConvertSkeleton(legacyMoby.Skeleton));
    }

    /// <summary>Raw MobySkeleton (bone hierarchy + tms0/tms1 bind matrices, both engines share the
    /// same layout - see MobySkeletonReader) into the clean IMoby-facing ISkeleton/IBone shape.
    /// Bones carry no name in this format, so they're indexed as "Bone_{i}".</summary>
    private static Assets.Interfaces.ISkeleton? ConvertSkeleton(MobySkeleton? raw)
    {
        if (raw is not { } skeleton || skeleton.numBones == 0)
            return null;

        var bones = new List<Assets.Interfaces.IBone>(skeleton.numBones);
        int rootIndex = -1;

        for (int i = 0; i < skeleton.numBones; i++)
        {
            int parentIndex = skeleton.bones[i].parentIndex;
            bones.Add(new Assets.Mobys.Bone($"Bone_{i}", parentIndex, skeleton.tms0[i], skeleton.tms1[i]));
            if (parentIndex < 0)
                rootIndex = i;
        }

        return new Assets.Mobys.Skeleton(bones, rootIndex);
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

        // boneMapOffset is a header field on the mesh's own record, resolved via mobyStream (the
        // same absolute-from-stream-start convention as skeletonPointer/banglesPointer) - NOT
        // verticesStream/indicesStream, which only hold the bulk vertex/index buffer data. Read
        // defensively: new, unverified-against-every-real-asset code shouldn't be able to break
        // mesh loading for mobys that don't even have a skeleton to skin against.
        for (uint i = 0; i < moby.BanglesCount; i++)
        {
            for (int j = 0; j < moby.Bangles[i].meshesCount; j++)
            {
                ref MobyMesh mesh = ref moby.Bangles[i].meshes[j];
                try
                {
                    mesh.ReadBoneMap(moby.mobyStream);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to read bone map for moby {moby.TUID:X} bangle {i} mesh {j}: {ex.Message}");
                }
            }
        }
    }

    private IMesh ConvertMobyMesh(MobyMesh legacyMesh, Objects.Moby moby)
    {
        // Positions are fixed-point int16 in bangle-local space; the moby's own scale must be
        // applied here, matching what MobyMesh.GetBuffers already does for the legacy renderer.
        legacyMesh.GetBuffers(moby.Scale, out var positions, out var indices, out var uvs, out var normals, out var tangents, out var vertexAlphaCandidates);

        var (jointIndices, jointWeights) = ExtractSkinData(legacyMesh, (int)(moby.Skeleton?.NumBones ?? 0));
        // vertexAlphaCandidates is only ever non-null for verticesType==1 (VertexFormat1) meshes -
        // see MobyMesh.GetBuffers's own comment for why verticesType==0's VertexFormat0.boneIndex
        // can't be read as alpha (it's genuinely a bone index there, the very field ExtractSkinData
        // resolves above).
        var geometry = new GeometryData(id: 0, positions: positions, uvs: uvs, indices: indices, normals: normals, tangents: tangents, jointIndices: jointIndices, jointWeights: jointWeights, vertexAlphaCandidates: vertexAlphaCandidates);

        IMaterial material = moby.IsOld
            ? _materialReader.GetMaterialByIndex(legacyMesh.shaderIndex)
            : _materialReader.GetMaterialForLocalIndex(moby.ShaderTUIDs, legacyMesh.shaderIndex);

        return new Assets.Geometry.Mesh(geometry, material, "MobyMesh", legacyMesh.VertexFormatName, legacyMesh.DumpVertex);
    }

    /// <summary>
    /// Resolves each vertex's raw bone reference(s) through this primitive's local joint palette
    /// (mesh.boneMap) into skeleton-global bone indices + normalized weights - algorithm
    /// transliterated from InsomniaToolset's extract_gltf.cpp (AttributeBoneIndex/
    /// AttributeBoneIndices codecs), not independently derived:
    /// - VertexFormat1 (verticesType 1): 4 explicit (localIndex byte, weight byte) pairs.
    /// - VertexFormat0 (verticesType 0): a single implied full-weight binding, whose local palette
    ///   index is packed into the "purpose"/boneIndex int16 field as abs((purpose+1)/3) - the
    ///   toolset itself names that field "purpose", not "boneIndex", suggesting even its author
    ///   wasn't fully certain of the encoding; flagged here as the least-confident piece of this
    ///   feature.
    /// Returns (null, null) if this mesh has no joint palette (no skin data).
    /// </summary>
    private static (int[]? jointIndices, float[]? jointWeights) ExtractSkinData(MobyMesh mesh, int skeletonBoneCount)
    {
        if (mesh.boneMap.Length == 0)
            return (null, null);

        int vertexCount = (int)mesh.verticesCount;
        var jointIndices = new int[vertexCount * 4];
        var jointWeights = new float[vertexCount * 4];
        Array.Fill(jointIndices, -1);

        if (mesh.verticesType == 1)
        {
            for (int v = 0; v < vertexCount; v++)
            {
                var vertex = mesh.vertices1[v];
                SetBinding(jointIndices, jointWeights, mesh.boneMap, skeletonBoneCount, v, 0, vertex.bones.Item1, vertex.weights.Item1);
                SetBinding(jointIndices, jointWeights, mesh.boneMap, skeletonBoneCount, v, 1, vertex.bones.Item2, vertex.weights.Item2);
                SetBinding(jointIndices, jointWeights, mesh.boneMap, skeletonBoneCount, v, 2, vertex.bones.Item3, vertex.weights.Item3);
                SetBinding(jointIndices, jointWeights, mesh.boneMap, skeletonBoneCount, v, 3, vertex.bones.Item4, vertex.weights.Item4);
            }
        }
        else if (mesh.verticesType == 0)
        {
            for (int v = 0; v < vertexCount; v++)
            {
                int localIndex = Math.Abs((mesh.vertices0[v].boneIndex + 1) / 3);
                SetBinding(jointIndices, jointWeights, mesh.boneMap, skeletonBoneCount, v, 0, localIndex, 255);
            }
        }

        return (jointIndices, jointWeights);
    }

    // skeletonBoneCount bounds-checks boneMap's resolved value too, not just the local palette
    // index into boneMap itself - boneMap[localIndex] is a skeleton-global bone index, and nothing
    // previously verified it was actually within the skeleton before it reached GltfExporter's
    // joint-node array (built with exactly skeleton.Bones.Count entries), where an out-of-range
    // value would throw. Treated the same as an unweighted slot (skipped) rather than clamped, so
    // a corrupt/misread binding silently drops that influence instead of binding to a wrong bone.
    private static void SetBinding(int[] jointIndices, float[] jointWeights, ushort[] boneMap, int skeletonBoneCount, int vertex, int slot, int localIndex, byte weightByte)
    {
        if (weightByte == 0 || localIndex < 0 || localIndex >= boneMap.Length)
            return;

        int globalIndex = boneMap[localIndex];
        if (skeletonBoneCount > 0 && globalIndex >= skeletonBoneCount)
            return;

        jointIndices[vertex * 4 + slot] = globalIndex;
        jointWeights[vertex * 4 + slot] = weightByte / 255f;
    }
}
