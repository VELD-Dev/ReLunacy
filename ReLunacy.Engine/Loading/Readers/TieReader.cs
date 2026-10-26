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

        // ToD's animated UVs are level-wide (main.dat 0x5C00), keyed by the TIE's class index.
        var uvAnimations = Assets.Geometry.OldEngineUvAnimations.Read(main);
        main.sh.Seek(tieSection.offset, SeekOrigin.Begin);
        for (uint i = 0; i < tieSection.count; i++)
        {
            var legacyTie = new Objects.Tie(main, _fileManager, old: true, index: i);
            if (uvAnimations != null)
                legacyTie.UvAnimations = uvAnimations.ForTie(i);

            // Old engine: TieInstance.tieIndex stores file offsets, not sequential indices -
            // key ties the same way to match.
            ulong key = tieSection.offset + i * TieMetadataOld.Size;
            ties.Add(key, ConvertTie(legacyTie, key));
        }

        return ties;
    }

    private Dictionary<ulong, Assets.Ties.Tie> ReadTiesNew()
    {
        var ties = new Dictionary<ulong, Assets.Ties.Tie>();

        AssetPointer[] tiePtrs = NewEngineAssetLookup.ReadPointers(
            _fileManager, NewEngineAssetLookup.TiePointers, "TIEs");
        if (!NewEngineAssetLookup.TryGetPayloadStream(_fileManager, "ties.dat", "TIEs", out Stream? tieStream) ||
            tieStream is null)
            return ties;

        for (int i = 0; i < tiePtrs.Length; i++)
        {
            byte[] tiedat = NewEngineAssetLookup.ReadPayload(tieStream, tiePtrs[i], "TIE");

            using MemoryStream tiems = new(tiedat);

            var legacyTie = new Objects.Tie(new IGFile(tiems), _fileManager, old: false);
            // Keyed by the assetlookup pointer-table TUID, not the tie's own embedded TUID field
            // (0x68) - the embedded field isn't reliably unique.
            ties.Add(tiePtrs[i].TUID, ConvertTie(legacyTie, tiePtrs[i].TUID));
        }

        return ties;
    }

    private Assets.Ties.Tie ConvertTie(Objects.Tie legacyTie, ulong id)
    {
        ReadTieMeshes(legacyTie);

        var scaleVec = legacyTie.Scale;
        var meshes = new List<IMesh>();

        // IMPORTANT: lightmap validity is PER MESH. Keep the validated slice itself beside that mesh
        // instead of using a zero-filled tie-wide reconstruction as the source of truth. A rejected
        // mesh used to leave (0,0) behind in that reconstruction, then SliceLightmapUVs accepted those
        // zeros on the second pass and silently re-enabled the lightmap we had just rejected.
        var perMeshLightmapUVs = new float[]?[legacyTie.MeshesCount];

        // Retain a tie-wide aggregate only for ITie.GetLightmapUVs/debug/export callers. It is NOT used
        // to decide whether an individual mesh receives baked lighting.
        float[]? tieLightmapUVs = null;

        for (int i = 0; i < legacyTie.MeshesCount; i++)
        {
            ref TieMesh mesh = ref legacyTie.Meshes[i];

            // Neither engine addresses the compact Half2 secondary stream with +0x04, which is the
            // primary VertexFormat0 index: a primitive's window starts at its +0x06. Old engine:
            // file/corpus/geometry proof across nine ToD levels, gated by a non-zero +0x0E. New
            // engine: read in the QForce EBOOT - the TIE renderer binds attribute 4 at
            // array + primitive[+0x06] * 4 and the bake only for primitives whose +0x0E bit 0 is set
            // (NewEngineBakedLighting). The game binds every such window, so the new engine only
            // refuses what cannot be sampled at all; the old engine still has to find its array
            // before trusting it, hence its range check and area correlation.
            bool lightmapped = legacyTie.isOld
                ? mesh.UsesOldSecondaryUv()
                : NewEngineBakedLighting.IsLightmappedPrimitive(mesh);
            if (!lightmapped)
                continue;

            ushort secondaryUvIndex = mesh.GetOldSecondaryUvIndex();
            var rawSlice = SliceLightmapUVs(legacyTie.LightmapUVs, secondaryUvIndex, mesh.verticesCount,
                requireUnitRange: legacyTie.isOld);
            if (rawSlice is null) continue;

            if (legacyTie.isOld && ClassifyUnwrap(mesh, rawSlice) != LightmapUvValidation.Confirmed)
                continue;

            perMeshLightmapUVs[i] = rawSlice;
            tieLightmapUVs ??= new float[legacyTie.LightmapUVs!.Length];
            Array.Copy(rawSlice, 0, tieLightmapUVs, secondaryUvIndex * 2, rawSlice.Length);
        }

        for (int i = 0; i < legacyTie.MeshesCount; i++)
        {
            meshes.Add(ConvertTieMesh(legacyTie.Meshes[i], scaleVec, legacyTie, perMeshLightmapUVs[i], i));
        }

        var debugName = _debugReader.GetTiePrototypeName(legacyTie.TUID);
        var name = debugName ?? (!string.IsNullOrEmpty(legacyTie.Name) ? legacyTie.Name : $"Tie_{id:X}");

        // per-axis scale already applied during mesh conversion
        return new Assets.Ties.Tie(id: id, meshes: meshes, scale: 1.0f, name: name, lightmapUVs: tieLightmapUVs);
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

    /// <summary>Rejected below this: a mesh's UV window has to actually behave like an unwrap of
    /// THIS mesh, not merely decode in range. Measured over metropolis's 494 testable tie meshes the
    /// score's median is 0.874, so 0.5 sits far below the real population and cuts the windows that
    /// pass the range check by luck and would otherwise repeat the bake across the surface.</summary>
    private const double MinUnwrapCorrelation = 0.5;

    /// <summary>Below this many usable triangles the correlation is too noisy to prove the window.
    /// Such meshes are now UNKNOWN, not automatically accepted: accepting them was equivalent to
    /// saying "we cannot prove these are lightmap UVs, therefore they are lightmap UVs".</summary>
    private const int MinTrianglesToTest = 40;

    private enum LightmapUvValidation
    {
        Invalid,
        Unknown,
        Confirmed,
    }

    /// <summary>Classifies whether this UV window unwraps THIS mesh. A lightmap packer allocates texels
    /// roughly in proportion to surface area, so per-triangle log(UV area) tracks log(3D area).
    ///
    /// Confirmed means the correlation is strong enough to safely bind the per-instance bake.
    /// Unknown means there was not enough information to prove it. Unknown deliberately does NOT bind
    /// a lightmap until the remaining Half2-vs-vertex-bake shader usage is decoded.
    /// Invalid means the geometry/indices contradict the candidate or the correlation is bad.</summary>
    private static LightmapUvValidation ClassifyUnwrap(in TieMesh mesh, float[] uvs)
    {
        var verts = mesh.vertices;
        var indices = mesh.indices;
        if (verts is null || indices is null)
            return LightmapUvValidation.Unknown;

        int vertexCount = mesh.verticesCount;
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        int n = 0;

        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
            if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount)
                return LightmapUvValidation.Invalid;

            var (ax, ay, az) = verts[i0].position;
            var (bx, by, bz) = verts[i1].position;
            var (cx, cy, cz) = verts[i2].position;

            double e1x = bx - ax, e1y = by - ay, e1z = bz - az;
            double e2x = cx - ax, e2y = cy - ay, e2z = cz - az;
            double nx = e1y * e2z - e1z * e2y;
            double ny = e1z * e2x - e1x * e2z;
            double nz = e1x * e2y - e1y * e2x;
            double area3 = 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (area3 <= 1e-6) continue;

            double u0 = uvs[i0 * 2], v0 = uvs[i0 * 2 + 1];
            double du1 = uvs[i1 * 2] - u0, dv1 = uvs[i1 * 2 + 1] - v0;
            double du2 = uvs[i2 * 2] - u0, dv2 = uvs[i2 * 2 + 1] - v0;
            double area2 = 0.5 * Math.Abs(du1 * dv2 - du2 * dv1);
            if (area2 <= 1e-12) continue;

            double x = Math.Log(area3), y = Math.Log(area2);
            sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y;
            n++;
        }

        if (n < MinTrianglesToTest)
            return LightmapUvValidation.Unknown;

        double cov = sxy - sx * sy / n;
        double varX = sxx - sx * sx / n;
        double varY = syy - sy * sy / n;
        if (varX <= 0 || varY <= 0)
            return LightmapUvValidation.Invalid;

        double correlation = cov / Math.Sqrt(varX * varY);
        return correlation >= MinUnwrapCorrelation
            ? LightmapUvValidation.Confirmed
            : LightmapUvValidation.Invalid;
    }

    private IMesh ConvertTieMesh(TieMesh legacyMesh, System.Numerics.Vector3 scale, Objects.Tie tie, float[]? lightmapUVs, int meshIndex)
    {
        legacyMesh.GetBuffers(scale, out var positions, out var indices, out var uvs, out var normals, out var tangents, out var vertexAlpha, out var bakedLightScales);

        // Two-layer primitive: the second material, its mask, and the per-vertex blend weight the
        // programs compare with the mask (TEX1.y = low 7 bits of |position.w| / 128).
        IMaterial? secondMaterial = null;
        ITexture? blendMask = null;
        if (!legacyMesh.isOld && tie.SecondShaderIndices is { } seconds && meshIndex < seconds.Length &&
            seconds[meshIndex] != 0xFFFF && tie.ShaderTUIDs != null &&
            tie.BlendMaskIndices is { } maskIndices && tie.BlendMasks is { } masks &&
            maskIndices[meshIndex] < masks.Length && masks[maskIndices[meshIndex]] is { } mask)
        {
            secondMaterial = _materialReader.GetMaterialForLocalIndex(tie.ShaderTUIDs, seconds[meshIndex]);
            blendMask = mask;
            vertexAlpha = new float[legacyMesh.verticesCount];
            for (int k = 0; k < vertexAlpha.Length; k++)
                vertexAlpha[k] = (Math.Abs((int)legacyMesh.vertices[k].boneIndex) & 0x7F) / 128f;
        }
        // Tools of Destruction: primitive +0x2A / +0x0A into 0x5000 / 0x5700. The vertex weight is 127/128 on
        // all 5 884 vertices of the 71 retail two-layer primitives, so the shader takes it as that constant.
        else if (legacyMesh.isOld && tie.SecondShaderIndices is { } oldSeconds && meshIndex < oldSeconds.Length &&
                 oldSeconds[meshIndex] != 0xFFFF && tie.BlendMaskIndices is { } oldMasks &&
                 _materialReader.GetOldBlendMask(oldMasks[meshIndex]) is { } oldMask)
        {
            secondMaterial = _materialReader.GetMaterialByIndex(oldSeconds[meshIndex]);
            blendMask = oldMask;
        }

        // lightmapUVs is already THIS mesh's validated slice. Do not re-slice a reconstructed
        // tie-wide array here: doing that is what turned rejected zero-filled regions back into UVs.
        var geometry = new GeometryData(id: 0, positions: positions, uvs: uvs, indices: indices, normals: normals, tangents: tangents, vertexAlpha: vertexAlpha, lightmapUVs: lightmapUVs, vertexBakedLightScales: bakedLightScales);

        IMaterial material = legacyMesh.isOld
            ? _materialReader.GetMaterialByIndex(legacyMesh.oldShaderIndex)
            : _materialReader.GetMaterialForLocalIndex(tie.ShaderTUIDs, legacyMesh.newShaderIndex);

        OldTieShaderUse? shaderUse = null;
        if (legacyMesh.isOld && material is Assets.Materials.Material oldMaterial)
        {
            // PROVEN loader reconstruction. Keep this as data: the terminal edge from
            // EffectiveUseMask to callback52.arg2.low8 remains OPEN until the passive probe closes it.
            shaderUse = new OldTieShaderUse(
                legacyMesh.OldAuthoredShaderUseMask,
                legacyMesh.OldAuthoredRaw2D,
                legacyMesh.OldAuthoredRaw2E,
                legacyMesh.OldAuthoredRaw2F,
                oldMaterial.OldFeatureFlags,
                oldMaterial.GameRenderMode,
                oldMaterial.OldRenderFlags,
                float.IsFinite(legacyMesh.oldDistanceFadeScale) ? legacyMesh.oldDistanceFadeScale : 0f,
                float.IsFinite(legacyMesh.oldDistanceFadeBias) ? legacyMesh.oldDistanceFadeBias : 0f,
                legacyMesh.verticesIndex);
        }

        return new Assets.Geometry.Mesh(
            geometry,
            material,
            "TieMesh",
            TieMesh.VertexFormatName,
            legacyMesh.DumpVertex,
            oldTieShaderUse: shaderUse)
        {
            SourceVertexBase = legacyMesh.isOld ? legacyMesh.verticesIndex : -1,
            SecondMaterial = secondMaterial,
            BlendMask = blendMask,
            // A TIE's 0x5C00 record names the primitive by its vertex index (ToD's level-wide one too).
            UvAnimation = tie.UvAnimations.TryGetValue(legacyMesh.verticesIndex, out var uv) ? uv : null,
            NewEngineTiePrimitive = legacyMesh.isOld
                ? null
                : new Assets.Geometry.NewEngineTiePrimitive(
                    legacyMesh.verticesIndex,
                    legacyMesh.GetOldSecondaryUvIndex(),
                    legacyMesh.verticesCount,
                    NewEngineBakedLighting.IsLightmappedPrimitive(legacyMesh),
                    new System.Numerics.Vector3(
                        legacyMesh.fadeNormalDistance,
                        legacyMesh.fadeReflectionDistance,
                        legacyMesh.fadeDetailDistance),
                    legacyMesh.newEngineFeatureMask),
        };
    }

    /// <summary>Copies out one mesh's window of the tie-wide candidate lightmap UV array, or null if
    /// this mesh has no usable window. This is only the cheap first gate; ClassifyUnwrap then decides,
    /// on the old engine, whether the range-valid bytes are actually proven to belong to this mesh.
    /// <paramref name="requireUnitRange"/> keeps the old engine's exact [0,1]; the new engine only
    /// refuses non-finite values - the console samples its lightmaps CLAMP_TO_EDGE (ADDRESS
    /// 0x30303 / 0x730303) and binds whatever the primitive's window holds.</summary>
    private static float[]? SliceLightmapUVs(float[]? tieLightmapUVs, ushort secondaryUvIndex, ushort verticesCount, bool requireUnitRange)
    {
        if (tieLightmapUVs is null || verticesCount == 0) return null;

        int start = secondaryUvIndex * 2;
        int count = verticesCount * 2;
        if (start < 0 || start + count > tieLightmapUVs.Length) return null;

        for (int i = start; i < start + count; i++)
        {
            float f = tieLightmapUVs[i];
            if (!float.IsFinite(f)) return null;
            if (requireUnitRange && (f < 0f || f > 1f)) return null;
        }

        var slice = new float[count];
        Array.Copy(tieLightmapUVs, start, slice, 0, count);
        return slice;
    }
}