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
    private readonly AnimationReader _animationReader;

    public MobyReader(FileManager fileManager, MaterialReader materialReader, DebugReader debugReader, AnimationReader animationReader)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _materialReader = materialReader ?? throw new ArgumentNullException(nameof(materialReader));
        _debugReader = debugReader ?? throw new ArgumentNullException(nameof(debugReader));
        _animationReader = animationReader ?? throw new ArgumentNullException(nameof(animationReader));
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
        AssetPointer[] mobyPtrs = AssetPointer.ReadArray(assetlookup.sh, mobySection.length / AssetPointer.Size);
        Stream mobyStream = _fileManager.rawfiles["mobys.dat"]!;
        for (int i = 0; i < mobyPtrs.Length; i++)
        {
            byte[] mobydat = ReadRawAsset(mobyStream, mobyPtrs[i].offset, mobyPtrs[i].length);
            using var mobyms = new MemoryStream(mobydat, writable: false);
            using var streamHelper = new StreamHelper(mobyms, StreamHelper.Endianness.Big);
            var legacyMoby = new Objects.Moby(streamHelper, _fileManager);
            mobys.Add(mobyPtrs[i].TUID, ConvertMoby(legacyMoby, mobyPtrs[i].TUID));
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
            if (legacyBangle.meshes == null || legacyBangle.meshesCount == 0) continue;
            var meshes = new List<IMesh>();
            for (int j = 0; j < legacyBangle.meshes.Length; j++)
                meshes.Add(ConvertMobyMesh(legacyBangle.meshes[j], legacyMoby));
            bangles.Add(new Bangle(meshes, $"Bangle_{i}"));
        }

        var boundingSphere = legacyMoby.BoundingSphere;
        var boundingCenter = new System.Numerics.Vector3(boundingSphere.X, boundingSphere.Y, boundingSphere.Z);
        float boundingRadius = boundingSphere.W;
        var debugName = _debugReader.GetMobyPrototypeName(tuid);
        var name = debugName ?? (!string.IsNullOrEmpty(legacyMoby.Name) ? legacyMoby.Name : $"Moby_{tuid:X}");
        var animationSet = _animationReader.ResolveForMoby(legacyMoby, tuid);

        return new Assets.Mobys.Moby(
            id: tuid,
            bangles: bangles,
            scale: legacyMoby.Scale,
            name: name,
            boundingSphereCalculator: () => (boundingCenter, boundingRadius),
            skeleton: ConvertSkeleton(legacyMoby.Skeleton),
            animationSet: animationSet);
    }

    private static Assets.Interfaces.ISkeleton? ConvertSkeleton(MobySkeleton? raw)
    {
        if (raw is not { } skeleton || skeleton.numBones == 0) return null;
        var bones = new List<Assets.Interfaces.IBone>(skeleton.numBones);
        int rootIndex = -1;
        for (int i = 0; i < skeleton.numBones; i++)
        {
            int parentIndex = skeleton.bones[i].parentIndex;
            bones.Add(new Assets.Mobys.Bone(
                $"Bone_{i}",
                parentIndex,
                skeleton.tms0[i],
                skeleton.tms1[i],
                skeleton.bones[i].flags));
            if (parentIndex < 0) rootIndex = i;
        }

        float positionScale = 1f / (0x8000 >> Math.Clamp((int)skeleton.translationShift, 0, 15));
        float scaleScale = 1f / (0x8000 >> Math.Clamp((int)skeleton.scaleShift, 0, 15));
        var referenceTranslations = new List<System.Numerics.Vector3>(skeleton.numBones);
        if (skeleton.referenceTranslationsQuantized is { Length: > 0 } rawTranslations && rawTranslations.Length >= skeleton.numBones * 3)
        {
            for (int i = 0; i < skeleton.numBones; i++)
            {
                referenceTranslations.Add(new System.Numerics.Vector3(
                    rawTranslations[i * 3 + 0] * positionScale,
                    rawTranslations[i * 3 + 1] * positionScale,
                    rawTranslations[i * 3 + 2] * positionScale));
            }
        }

        // tms0 is the world bind transform and tms1 the inverse world bind transform. Their
        // child * parentInverse product is therefore the authored local reference transform.
        // Cross-engine fixture audits show its translation matches D300+0x14 and its scale matches
        // authored animation scale values when those channels are present. Keeping this scale is
        // essential for bones whose clip does not author a scale channel at all.
        var referenceScales = new List<System.Numerics.Vector3>(skeleton.numBones);
        for (int i = 0; i < skeleton.numBones; i++)
        {
            int parentIndex = skeleton.bones[i].parentIndex;
            System.Numerics.Matrix4x4 localBind = parentIndex >= 0 && parentIndex < skeleton.numBones
                ? skeleton.tms0[i] * skeleton.tms1[parentIndex]
                : skeleton.tms0[i];
            referenceScales.Add(ExtractReferenceScale(localBind));
        }

        return new Assets.Mobys.Skeleton(
            bones,
            rootIndex,
            positionScale,
            scaleScale,
            referenceTranslations,
            skeleton.rotationShift,
            referenceScales);
    }

    private static System.Numerics.Vector3 ExtractReferenceScale(System.Numerics.Matrix4x4 localBind)
    {
        float x = new System.Numerics.Vector3(localBind.M11, localBind.M12, localBind.M13).Length();
        float y = new System.Numerics.Vector3(localBind.M21, localBind.M22, localBind.M23).Length();
        float z = new System.Numerics.Vector3(localBind.M31, localBind.M32, localBind.M33).Length();

        // The audited Q-Force sample contains two mirrored reference bones. Comparing their local
        // bind matrices with the animation reference quaternions proves the authored convention is
        // (-s,-s,-s), not an arbitrary single-axis sign flip. ToD fixtures audited so far contain
        // no negative-determinant local binds. Raw bind matrices remain preserved for future cases.
        float determinant3 =
            localBind.M11 * (localBind.M22 * localBind.M33 - localBind.M23 * localBind.M32) -
            localBind.M12 * (localBind.M21 * localBind.M33 - localBind.M23 * localBind.M31) +
            localBind.M13 * (localBind.M21 * localBind.M32 - localBind.M22 * localBind.M31);
        if (determinant3 < 0f)
            return new System.Numerics.Vector3(-x, -y, -z);
        return new System.Numerics.Vector3(x, y, z);
    }

    private void ReadMobyBanglesMeshes(Objects.Moby moby)
    {
        moby.verticesStream.Seek(0);
        for (uint i = 0; i < moby.BanglesCount; i++)
            for (int j = 0; j < moby.Bangles[i].meshesCount; j++)
            {
                ref MobyMesh mesh = ref moby.Bangles[i].meshes[j];
                moby.verticesStream.Seek(mesh.verticesOffset);
                mesh.ReadVerticesBuffer(moby.verticesStream);
            }

        moby.indicesStream.Seek(0);
        for (uint i = 0; i < moby.BanglesCount; i++)
            for (int j = 0; j < moby.Bangles[i].meshesCount; j++)
            {
                ref MobyMesh mesh = ref moby.Bangles[i].meshes[j];
                moby.indicesStream.Seek(mesh.indicesOffset * sizeof(ushort));
                mesh.ReadIndicesBuffer(moby.indicesStream);
            }

        for (uint i = 0; i < moby.BanglesCount; i++)
            for (int j = 0; j < moby.Bangles[i].meshesCount; j++)
            {
                ref MobyMesh mesh = ref moby.Bangles[i].meshes[j];
                try { mesh.ReadBoneMap(moby.mobyStream); }
                catch (Exception ex) { Console.WriteLine($"Failed to read bone map for moby {moby.TUID:X} bangle {i} mesh {j}: {ex.Message}"); }
            }
    }

    private IMesh ConvertMobyMesh(MobyMesh legacyMesh, Objects.Moby moby)
    {
        legacyMesh.GetBuffers(moby.Scale, out var positions, out var indices, out var uvs, out var normals, out var tangents, out var vertexAlphaCandidates);
        var (jointIndices, jointWeights) = ExtractSkinData(legacyMesh, (int)(moby.Skeleton?.NumBones ?? 0));
        var geometry = new GeometryData(id: 0, positions: positions, uvs: uvs, indices: indices, normals: normals, tangents: tangents, jointIndices: jointIndices, jointWeights: jointWeights, vertexAlphaCandidates: vertexAlphaCandidates);
        IMaterial material = moby.IsOld ? _materialReader.GetMaterialByIndex(legacyMesh.shaderIndex) : _materialReader.GetMaterialForLocalIndex(moby.ShaderTUIDs, legacyMesh.shaderIndex);
        return new Assets.Geometry.Mesh(geometry, material, "MobyMesh", legacyMesh.VertexFormatName, legacyMesh.DumpVertex);
    }

    private static (int[]? jointIndices, float[]? jointWeights) ExtractSkinData(MobyMesh mesh, int skeletonBoneCount)
    {
        if (mesh.boneMap.Length == 0) return (null, null);
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

    private static void SetBinding(int[] jointIndices, float[] jointWeights, ushort[] boneMap, int skeletonBoneCount, int vertex, int slot, int localIndex, byte weightByte)
    {
        if (weightByte == 0 || localIndex < 0 || localIndex >= boneMap.Length) return;
        int globalIndex = boneMap[localIndex];
        if (skeletonBoneCount > 0 && globalIndex >= skeletonBoneCount) return;
        jointIndices[vertex * 4 + slot] = globalIndex;
        jointWeights[vertex * 4 + slot] = weightByte / 255f;
    }

    private static byte[] ReadRawAsset(Stream stream, uint offset, uint length)
    {
        var data = new byte[checked((int)length)];
        stream.Seek(offset, SeekOrigin.Begin);
        int read = 0;
        while (read < data.Length)
        {
            int n = stream.Read(data, read, data.Length - read);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
        return data;
    }
}
