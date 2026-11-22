using System.Numerics;
using ReLunacy.Engine.Assets.Geometry;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Lighting;
using ReLunacy.Engine.Assets.Mobys;
using ReLunacy.Engine.Assets.Primitives;
using ReLunacy.Engine.Assets.Ties;
using ReLunacy.Engine.Assets.UFrags;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Fall of Man's retail IGHW records, which precede the Tools of Destruction layouts.</summary>
internal static class FallOfManGeometry
{
    public static Dictionary<ulong, Tie> ReadTies(FileManager files, MaterialReader materials, DebugReader debug, LevelLoadProgress? progress)
    {
        IGFile main = files.igfiles["main.dat"]!;
        IGFile vertices = files.igfiles["vertices.dat"]!;
        var records = main.QuerySection(0x3400);
        uint vertexBase = vertices.QuerySection(0x9000).offset;
        uint indexBase = vertices.QuerySection(0x9100).offset;
        var ties = new Dictionary<ulong, Tie>();
        progress?.SetTotal(records.count);
        for (uint i = 0; i < records.count; i++)
        {
            uint offset = records.offset + i * records.length;
            uint primitive = main.sh.ReadUInt32(offset);
            int count = main.sh.ReadUInt16(offset + 8);
            uint vertexOffset = main.sh.ReadUInt32(offset + 0x10);
            uint secondaryUvOffset = main.sh.ReadUInt32();
            ushort classId = main.sh.ReadUInt16();
            main.sh.Seek(offset + 0x20);
            Vector3 scale = ReadVector(main.sh);
            var meshes = new List<IMesh>(count);
            for (uint p = 0; p < count; p++)
            {
                main.sh.Seek(primitive + p * 0x20);
                ushort material = main.sh.ReadUInt16();
                main.sh.ReadUInt16();
                uint firstIndex = main.sh.ReadUInt32();
                ushort indexCount = main.sh.ReadUInt16();
                ushort vertexCount = main.sh.ReadUInt16();
                ushort firstVertex = main.sh.ReadUInt16();
                ushort firstSecondaryUv = main.sh.ReadUInt16();
                bool hasSecondaryUvs = (main.sh.ReadByte() & 1) != 0;
                var data = ReadGeometry(vertices.sh, vertexBase + (long)vertexOffset + firstVertex * 20L,
                    indexBase + firstIndex * 2L, vertexCount, indexCount, 20, 8, scale, hasVertexAlpha: true,
                    secondaryUvs: hasSecondaryUvs ? vertexBase + (long)secondaryUvOffset + firstSecondaryUv * 4L : null);
                if (data != null)
                    meshes.Add(new Mesh(data, materials.GetMaterialByIndex(material), $"Mesh_{p}")
                    {
                        SourceVertexBase = data.GetLightmapUVs() != null ? firstSecondaryUv : -1,
                        FallOfManTiePrimitive = new FallOfManTiePrimitive(firstVertex, firstSecondaryUv, hasSecondaryUvs),
                    });
            }
            ties.Add(offset, new Tie(offset, meshes, name: debug.GetTiePrototypeName(i) ?? $"Tie_{classId}"));
            progress?.Advance();
        }
        return ties;
    }

    public static Assets.Levels.Zone ReadZone(FileManager files, MaterialReader materials, AssetLibrary<Tie> ties, DebugReader debug)
    {
        IGFile main = files.igfiles["main.dat"]!;
        IGFile vertices = files.igfiles["vertices.dat"]!;
        var records = main.QuerySection(0x6200);
        uint vertexBase = vertices.QuerySection(0x9000).offset;
        uint indexBase = vertices.QuerySection(0x9100).offset;
        uint mapCount = main.QuerySection(0x5500).count;
        uint cubeCount = main.QuerySection(CubemapReader.ID).count;
        var environment = main.QuerySection(0x7000);
        uint defaultCubeIndex = environment.count != 0 && environment.length >= 0x7C
            ? main.sh.ReadUInt32(environment.offset + 0x78) : 0;
        byte defaultCube = defaultCubeIndex < cubeCount ? (byte)defaultCubeIndex : (byte)0;

        // game.self 0x146718 (Tie), 0x1506D0 (UFrag) and 0xFC324 (level default).
        byte? ReadCubeIndex(uint offset)
        {
            if (cubeCount == 0) return null;
            byte index = main.sh.ReadByte(offset);
            return index < cubeCount ? index : defaultCube;
        }

        Stream textures = files.rawfiles["textures.dat"]!;
        var terrain = new List<IUFrag>((int)records.count);
        for (uint i = 0; i < records.count; i++)
        {
            uint offset = records.offset + i * records.length;
            main.sh.Seek(offset + 0x30);
            Vector3 center = ReadVector(main.sh);
            float radius = main.sh.ReadSingle();
            Vector3 anchor = ReadVector(main.sh) / 256f;
            ushort material = main.sh.ReadUInt16();
            main.sh.Seek(offset + 0x54);
            uint firstIndex = main.sh.ReadUInt32();
            uint vertexOffset = main.sh.ReadUInt32();
            ushort indexCount = main.sh.ReadUInt16();
            ushort vertexCount = main.sh.ReadUInt16();
            main.sh.ReadUInt16();
            ushort lightmap = main.sh.ReadUInt16();
            // game.self 0x153628..0x153658 passes the index and +0x74 to the terrain
            // material setup. At 0x150454 that setup uploads the scale in c[5].z.
            float bakeScale = ReadSingleAt(main.sh, offset + 0x74);
            byte[]? chroma = float.IsFinite(bakeScale) && bakeScale >= 0
                ? new byte[vertexCount * 2] : null;
            // 0x1537A8 configures attribute 2 as unsigned bytes for this route, instead of Half2.
            byte[]? prelightDirections = chroma != null && lightmap == ushort.MaxValue
                ? new byte[vertexCount * 3] : null;
            var data = ReadGeometry(vertices.sh, vertexBase + (long)vertexOffset, indexBase + firstIndex * 2L,
                vertexCount, indexCount, 26, 8, Vector3.One, terrain: true, hasVertexAlpha: true, chroma: chroma,
                prelightDirections: prelightDirections);
            if (data != null)
                terrain.Add(new OldUFrag(i, data.GetVertexPositions(), data.GetTextureCoordinates(), data.GetIndices(),
                    materials.GetMaterialByIndex(material), anchor, center, radius, data.GetNormals(), data.GetTangents(),
                    data.GetLightmapUVs(), data.GetVertexAlpha(), lightmapIndex: lightmap)
                {
                    FallOfManCubeIndex = ReadCubeIndex(offset + 0x67),
                    FallOfManBake = chroma == null ? null : new FallOfManBake(chroma, bakeScale,
                        data.GetVertexBakedLightScales(), prelightDirections),
                });
        }

        records = main.QuerySection(0x9300);
        var chromaSizes = ties.Loaded.ToDictionary(entry => entry.Key, entry => entry.Value.Meshes.OfType<Mesh>()
            .Where(mesh => mesh.SourceVertexBase >= 0)
            .Select(mesh => mesh.SourceVertexBase * 2 + mesh.Geometry.GetLightmapUVs()!.Length)
            .DefaultIfEmpty().Max());
        var prelightSizes = ties.Loaded.ToDictionary(entry => entry.Key, entry =>
            (Lightmapped: PrelightSize(entry.Value, true), Unmapped: PrelightSize(entry.Value, false)));
        var placements = new List<IPlacedInstance<ITie>>((int)records.count);
        for (uint i = 0; i < records.count; i++)
        {
            uint offset = records.offset + i * records.length;
            uint classOffset = main.sh.ReadUInt32(offset + 0x8C);
            if (ties.Get(classOffset) is not { } tie) continue;
            main.sh.Seek(offset);
            var matrix = new Matrix4x4(
                main.sh.ReadSingle(), main.sh.ReadSingle(), main.sh.ReadSingle(), 0,
                ReadSingleAt(main.sh, offset + 0x10), main.sh.ReadSingle(), main.sh.ReadSingle(), 0,
                ReadSingleAt(main.sh, offset + 0x20), main.sh.ReadSingle(), main.sh.ReadSingle(), 0,
                ReadSingleAt(main.sh, offset + 0x30), main.sh.ReadSingle(), main.sh.ReadSingle(), 1);
            ushort lightmap = main.sh.ReadUInt16(offset + 0x80);
            bool hasLightmap = lightmap < mapCount;
            float lightmappedScale = ReadSingleAt(main.sh, offset + 0x98);
            float prelightScale = ReadSingleAt(main.sh, offset + 0x9C);
            FallOfManBake? bake = null;
            if (hasLightmap)
            {
                uint start = main.sh.ReadUInt32(offset + 0x84);
                int size = chromaSizes[classOffset];
                // game.self 0x14E7F4 indexes chroma and Half2 UVs by the same primitive field.
                // Bound chroma by those vertex windows. The +0x88 stream can overlap it (level41).
                if (start != 0 && size > 0 && start + (long)size <= textures.Length
                    && float.IsFinite(lightmappedScale) && lightmappedScale >= 0)
                {
                    var chroma = new byte[size];
                    textures.Position = start;
                    textures.ReadExactly(chroma);
                    bake = new FallOfManBake(chroma, lightmappedScale);
                }
            }
            // game.self 0x14E384 / 0x14ED64 bind attribute 4 from placement +0x88.
            // 0x14E22C picks +0x98 for primitive bit 0 set and +0x9C for bit 0 clear.
            uint prelightStart = main.sh.ReadUInt32(offset + 0x88);
            var sizes = prelightSizes[classOffset];
            int prelightSize = hasLightmap ? sizes.Lightmapped : sizes.Unmapped;
            FallOfManTiePrelight? prelight = null;
            if (prelightStart != 0 && prelightSize > 0 && prelightStart + (long)prelightSize <= textures.Length)
            {
                var words = new byte[prelightSize];
                textures.Position = prelightStart;
                textures.ReadExactly(words);
                prelight = new FallOfManTiePrelight(words, hasLightmap, lightmappedScale, prelightScale);
            }
            string name = debug.GetTieInstanceName((int)i) ?? $"{tie.Name}_Instance_{i}";
            placements.Add(new PlacedInstance<ITie>(tie, matrix, i, name: name)
            {
                LightmapIndex = lightmap,
                FallOfManCubeIndex = ReadCubeIndex(offset + 0x94),
                FallOfManBake = bake,
                FallOfManTiePrelight = prelight,
            });
        }
        return new Assets.Levels.Zone(0, terrain, placements);
    }

    private static int PrelightSize(Tie tie, bool placementHasLightmap)
    {
        int vertices = 0;
        foreach (var mesh in tie.Meshes.OfType<Mesh>())
        {
            if (mesh.FallOfManTiePrimitive is not { } primitive) continue;
            int first = primitive.PrelightFirstWord(placementHasLightmap);
            if (first >= 0)
                vertices = Math.Max(vertices, first + mesh.Geometry.GetVertexPositions().Length / 3);
        }
        return vertices * 4;
    }

    public static Dictionary<ulong, Moby> ReadMobys(FileManager files, MaterialReader materials, DebugReader debug, LevelLoadProgress? progress)
    {
        IGFile main = files.igfiles["main.dat"]!;
        var records = main.QuerySection(0xD100);
        var pixels = new StreamHelper(files.rawfiles["textures.dat"]!, StreamHelper.Endianness.Big);
        var mobys = new Dictionary<ulong, Moby>();
        progress?.SetTotal(records.count);
        for (uint i = 0; i < records.count; i++)
        {
            uint offset = records.offset + i * records.length;
            int meshCount = main.sh.ReadUInt16(offset + 0x18);
            ushort classId = main.sh.ReadUInt16();
            uint meshOffset = main.sh.ReadUInt32(offset + 0x28);
            uint indexBase = main.sh.ReadUInt32(offset + 0x34);
            int vertexBase = main.sh.ReadInt32();
            float scale = main.sh.ReadSingle();
            var bangles = new List<IBangle>(meshCount);
            for (uint b = 0; b < meshCount && meshOffset != 0; b++)
            {
                uint firstPrimitive = main.sh.ReadUInt32(meshOffset + b * 8);
                uint count = main.sh.ReadUInt32();
                if (!Fits(main.sh, firstPrimitive, count * 0x20L)) continue;
                var meshes = new List<IMesh>((int)count);
                for (uint p = 0; p < count; p++)
                {
                    main.sh.Seek(firstPrimitive + p * 0x20);
                    ushort material = main.sh.ReadUInt16();
                    ushort vertexCount = main.sh.ReadUInt16();
                    ushort indexCount = main.sh.ReadUInt16();
                    main.sh.ReadByte();
                    bool skinned = main.sh.ReadByte() != 0;
                    uint firstIndex = main.sh.ReadUInt32();
                    uint firstVertex = main.sh.ReadUInt32();
                    var data = ReadGeometry(pixels, vertexBase + (long)firstVertex, indexBase + firstIndex * 2L,
                        vertexCount, indexCount, skinned ? 28 : 20, skinned ? 16 : 8, new Vector3(scale));
                    if (data != null)
                        meshes.Add(new Mesh(data, materials.GetMaterialByIndex(material), $"Mesh_{p}"));
                }
                bangles.Add(new Bangle(meshes));
            }
            mobys.TryAdd(classId, new Moby(classId, bangles, name: debug.GetMobyPrototypeName(i) ?? $"Moby_{classId}"));
            progress?.Advance();
        }
        return mobys;
    }

    public static IReadOnlyList<IPlacedInstance<IMoby>> ReadMobyInstances(IGFile gameplay, uint group, AssetLibrary<Moby> mobys,
        IReadOnlyDictionary<uint, string> names, DebugReader? legacyDebug)
    {
        var instances = new List<IPlacedInstance<IMoby>>();
        var stream = gameplay.sh;
        uint start = stream.ReadUInt32(group + 0x40);
        uint count = stream.ReadUInt32();
        if (start == 0 || !Fits(stream, start, count * 0x40L)) return instances;

        for (uint i = 0; i < count; i++)
        {
            uint offset = start + i * 0x40;
            stream.Seek(offset + 0x08);
            float displayDistance = stream.ReadSingle();
            float updateDistance = stream.ReadSingle();
            Vector3 position = ReadVector(stream);
            Vector3 rotation = ReadVector(stream);
            float scale = ReadSingleAt(stream, offset + 0x38);
            ushort classId = stream.ReadUInt16();
            if (mobys.Get(classId) is not { } moby) continue;
            var transform = new Transform3D(position, rotation, scale);
            string name = names.GetValueOrDefault(i) ?? legacyDebug?.GetMobyInstanceName((int)i) ?? $"{moby.Name}_Instance_{i}";
            uint settingsOffset = stream.ReadUInt32(offset);
            uint settingsSize = stream.ReadUInt32(offset + 0x34);
            instances.Add(new PlacedInstance<IMoby>(moby, transform, i, name: name,
                displayDistance: displayDistance > 0 ? displayDistance : -1, updateDistance: updateDistance > 0 ? updateDistance : -1)
            {
                GameplayIndex = (int)i,
                Settings = OldGameplayReader.ReadClassStruct(stream, settingsOffset, settingsSize) ?? [],
            });
        }
        return instances;
    }

    private static GeometryData? ReadGeometry(StreamHelper stream, long vertexOffset, long indexOffset,
        int vertexCount, int indexCount, int stride, int uvOffset, Vector3 scale,
        bool terrain = false, bool hasVertexAlpha = false, long? secondaryUvs = null, byte[]? chroma = null,
        byte[]? prelightDirections = null)
    {
        if (vertexCount == 0 || indexCount == 0 || indexCount % 3 != 0
            || !Fits(stream, vertexOffset, vertexCount * stride) || !Fits(stream, indexOffset, indexCount * 2))
            return null;
        var indices = new uint[indexCount];
        stream.Seek(indexOffset);
        for (int i = 0; i < indices.Length; i++)
        {
            uint index = stream.ReadUInt16();
            if (index >= vertexCount) return null;
            indices[i] = index;
        }

        var positions = new float[vertexCount * 3];
        var uvs = new float[vertexCount * 2];
        var normals = new float[vertexCount * 3];
        var tangents = new float[vertexCount * 3];
        float[]? alpha = hasVertexAlpha ? new float[vertexCount] : null;
        float[]? lightScales = hasVertexAlpha ? new float[vertexCount] : null;
        bool separateUvs = secondaryUvs is { } uvStart && Fits(stream, uvStart, vertexCount * 4);
        float[]? lightmapUvs = (terrain && prelightDirections == null) || separateUvs ? new float[vertexCount * 2] : null;
        for (int i = 0; i < vertexCount; i++)
        {
            long vertex = vertexOffset + i * stride;
            stream.Seek(vertex);
            Store(positions, i, new Vector3(stream.ReadInt16(), stream.ReadInt16(), stream.ReadInt16()) * scale);
            if (alpha != null)
            {
                int packed = Math.Abs((int)stream.ReadInt16());
                alpha[i] = (packed & 0x7F) / 128f;
                lightScales![i] = (packed & 0x7FFF) / 16384f;
            }
            stream.Seek(vertex + uvOffset);
            ReadUv(stream, uvs, i);
            if (prelightDirections != null)
            {
                prelightDirections[i * 3] = stream.ReadByte();
                prelightDirections[i * 3 + 1] = stream.ReadByte();
                prelightDirections[i * 3 + 2] = stream.ReadByte();
                stream.ReadByte();
            }
            else if (terrain) ReadUv(stream, lightmapUvs!, i);
            Store(normals, i, PackedNormal.Decode(stream.ReadUInt32()));
            Store(tangents, i, PackedNormal.Decode(stream.ReadUInt32()));
            if (chroma != null)
            {
                chroma[i * 2] = stream.ReadByte();
                chroma[i * 2 + 1] = stream.ReadByte();
            }
        }
        if (separateUvs)
        {
            stream.Seek(secondaryUvs!.Value);
            for (int i = 0; i < vertexCount; i++)
                ReadUv(stream, lightmapUvs!, i);
        }
        return new GeometryData(0, positions, uvs, indices, normals, vertexAlpha: alpha, tangents: tangents,
            lightmapUVs: lightmapUvs, vertexBakedLightScales: lightScales);
    }

    private static void ReadUv(StreamHelper stream, float[] output, int index)
    {
        output[index * 2] = (float)stream.ReadHalf();
        output[index * 2 + 1] = (float)stream.ReadHalf();
    }

    private static void Store(float[] output, int index, Vector3 value)
    {
        output[index * 3] = value.X;
        output[index * 3 + 1] = value.Y;
        output[index * 3 + 2] = value.Z;
    }

    private static Vector3 ReadVector(StreamHelper stream) => new(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());

    private static float ReadSingleAt(StreamHelper stream, uint offset)
    {
        stream.Seek(offset);
        return stream.ReadSingle();
    }

    private static bool Fits(StreamHelper stream, long offset, long length) =>
        offset >= 0 && offset + length <= stream.BaseStream.Length;
}
