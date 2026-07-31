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

        // Validate the lightmap UVs mesh by mesh (see SliceLightmapUVs) and rebuild the tie-wide
        // array from only the windows that pass, so ITie.GetLightmapUVs stays consistent with what
        // the meshes actually got. Meshes without a usable window keep zeros there — the same thing
        // an unshaded mesh's slot holds in the file — and null means no mesh had one at all, which
        // is what EntityTie tests before binding a bake.
        float[]? tieLightmapUVs = null;
        for (int i = 0; i < legacyTie.MeshesCount; i++)
        {
            ref TieMesh m = ref legacyTie.Meshes[i];
            var slice = SliceLightmapUVs(legacyTie.LightmapUVs, m.verticesIndex, m.verticesCount);
            if (slice is null || !LooksLikeUnwrap(m, slice)) continue;

            tieLightmapUVs ??= new float[legacyTie.LightmapUVs!.Length];
            Array.Copy(slice, 0, tieLightmapUVs, m.verticesIndex * 2, slice.Length);
        }

        for (int i = 0; i < legacyTie.MeshesCount; i++)
        {
            meshes.Add(ConvertTieMesh(legacyTie.Meshes[i], scaleVec, legacyTie, tieLightmapUVs));
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
    /// score's median is 0.874, so 0.5 sits far below the real population and cuts 16% — the windows
    /// that pass the range check by luck and would otherwise repeat the bake across the surface.</summary>
    private const double MinUnwrapCorrelation = 0.5;

    /// <summary>Below this many usable triangles the correlation is noise, so the mesh is accepted
    /// untested rather than discarded. That covers 1329 small meshes on metropolis (34,764 vertices
    /// total) — the remaining blind spot, and the first place to look if stray ties still show a
    /// doubled bake.</summary>
    private const int MinTrianglesToTest = 40;

    /// <summary>Does this UV window unwrap this mesh? A lightmap packer allocates texels roughly in
    /// proportion to world-space surface area, so per triangle log(UV area) tracks log(3D area).
    /// Real windows score ~0.87 here; a random in-range window from elsewhere in the vertex blob
    /// scores ~0.0, so this is the test that separates them — the [0,1] range check alone does not
    /// (see Loading.Vertices.TieLightmapUV, which documents why several other natural metrics here
    /// are vacuous).
    ///
    /// Note what this canNOT do: it does not distinguish a lightmap unwrap from an ALBEDO unwrap.
    /// Base UVs score ~0.64 by themselves, because artists unwrap those with roughly uniform texel
    /// density too. It only separates real-for-this-mesh from unrelated bytes, which is exactly the
    /// failure being screened out here.
    ///
    /// Uses raw int16 positions: any per-axis scale is a constant factor inside the logarithm and
    /// shifts the intercept, not the correlation.</summary>
    private static bool LooksLikeUnwrap(in TieMesh mesh, float[] uvs)
    {
        var verts = mesh.vertices;
        var indices = mesh.indices;
        if (verts is null || indices is null) return true;

        int vertexCount = mesh.verticesCount;
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        int n = 0;

        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
            if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount) return true;

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

        if (n < MinTrianglesToTest) return true;

        double cov = sxy - sx * sy / n;
        double varX = sxx - sx * sx / n;
        double varY = syy - sy * sy / n;
        if (varX <= 0 || varY <= 0) return true;

        return cov / Math.Sqrt(varX * varY) >= MinUnwrapCorrelation;
    }

    private IMesh ConvertTieMesh(TieMesh legacyMesh, System.Numerics.Vector3 scale, Objects.Tie tie, float[]? tieLightmapUVs)
    {
        legacyMesh.GetBuffers(scale, out var positions, out var indices, out var uvs, out var normals, out var tangents, out var vertexAlphaCandidates);

        // The tie's lightmap UV array is indexed over its WHOLE vertex buffer, so each mesh takes
        // the window starting at its own verticesIndex (see ITie.GetLightmapUVs). tieLightmapUVs is
        // already the validated array, so this slice can't fail the range check a second time.
        var lightmapUVs = SliceLightmapUVs(tieLightmapUVs, legacyMesh.verticesIndex, legacyMesh.verticesCount);

        var geometry = new GeometryData(id: 0, positions: positions, uvs: uvs, indices: indices, normals: normals, tangents: tangents, vertexAlphaCandidates: vertexAlphaCandidates, lightmapUVs: lightmapUVs);

        IMaterial material = legacyMesh.isOld
            ? _materialReader.GetMaterialByIndex(legacyMesh.oldShaderIndex)
            : _materialReader.GetMaterialForLocalIndex(tie.ShaderTUIDs, legacyMesh.newShaderIndex);

        return new Assets.Geometry.Mesh(geometry, material, "TieMesh", TieMesh.VertexFormatName, legacyMesh.DumpVertex);
    }

    /// <summary>Copies out one mesh's window of the tie-wide lightmap UV array, or null if this mesh
    /// has no usable one — in which case the mesh renders unlit while its siblings still light.
    ///
    /// THE VALIDITY CHECK IS PER MESH, and that is the whole point. Objects.Tie hands back the raw
    /// bytes at metadata 0x18 without judging them; a mesh's window starts at verticesIndex * 4
    /// inside that. On metropolis 2082 of 3771 tie meshes hold real UV data there but only 61 of 193
    /// ties hold it for ALL of their meshes, so validating tie-wide discards 112 partly-baked ties —
    /// including every tie carrying one of the level's 256x256 lightmaps. See
    /// Loading.Vertices.TieLightmapUV.
    ///
    /// The test is just "every half decodes inside [0,1]". At an unknown offset that is nearly
    /// vacuous, but at this fixed one it does the job: the windows that pass score a median 0.870 on
    /// the area-preservation test that actually proves the decode, versus ~0.0 for matched random
    /// windows. A short or out-of-bounds window returns null rather than a partial copy, since that
    /// would silently shift every subsequent vertex's UV.</summary>
    private static float[]? SliceLightmapUVs(float[]? tieLightmapUVs, ushort verticesIndex, ushort verticesCount)
    {
        if (tieLightmapUVs is null || verticesCount == 0) return null;

        int start = verticesIndex * 2;
        int count = verticesCount * 2;
        if (start < 0 || start + count > tieLightmapUVs.Length) return null;

        for (int i = start; i < start + count; i++)
        {
            float f = tieLightmapUVs[i];
            if (!float.IsFinite(f) || f < 0f || f > 1f) return null;
        }

        var slice = new float[count];
        Array.Copy(tieLightmapUVs, start, slice, 0, count);
        return slice;
    }
}
