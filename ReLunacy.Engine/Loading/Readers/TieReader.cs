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
            // (0x68) - the embedded field isn't reliably unique.
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
        // array from only the windows that pass. Meshes without a usable window keep zeros; null
        // means no mesh had one at all (what EntityTie checks before binding a bake).
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

    /// <summary>Minimum correlation for a UV window to be accepted as a real unwrap of its mesh.</summary>
    private const double MinUnwrapCorrelation = 0.5;

    /// <summary>Below this many usable triangles the correlation is noise, so the mesh is accepted
    /// untested rather than discarded.</summary>
    private const int MinTrianglesToTest = 40;

    /// <summary>Tests whether a UV window is a real unwrap of this mesh: a lightmap packer allocates
    /// texels roughly in proportion to world-space surface area, so per-triangle log(UV area) should
    /// correlate with log(3D area) for a genuine unwrap but not for unrelated bytes. Does not
    /// distinguish a lightmap unwrap from an albedo unwrap - only real-for-this-mesh from
    /// not-for-this-mesh. Uses raw int16 positions; any per-axis scale only shifts the intercept.</summary>
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
        legacyMesh.GetBuffers(scale, out var positions, out var indices, out var uvs, out var normals, out var tangents, out var vertexAlpha);

        // The tie's lightmap UV array is indexed over its WHOLE vertex buffer, so each mesh takes
        // the window starting at its own verticesIndex (see ITie.GetLightmapUVs). tieLightmapUVs is
        // already the validated array, so this slice can't fail the range check a second time.
        var lightmapUVs = SliceLightmapUVs(tieLightmapUVs, legacyMesh.verticesIndex, legacyMesh.verticesCount);

        var geometry = new GeometryData(id: 0, positions: positions, uvs: uvs, indices: indices, normals: normals, tangents: tangents, vertexAlpha: vertexAlpha, lightmapUVs: lightmapUVs);

        IMaterial material = legacyMesh.isOld
            ? _materialReader.GetMaterialByIndex(legacyMesh.oldShaderIndex)
            : _materialReader.GetMaterialForLocalIndex(tie.ShaderTUIDs, legacyMesh.newShaderIndex);

        return new Assets.Geometry.Mesh(geometry, material, "TieMesh", TieMesh.VertexFormatName, legacyMesh.DumpVertex);
    }

    /// <summary>Copies out one mesh's window of the tie-wide lightmap UV array, or null if this mesh
    /// has no usable one (renders unlit while siblings still light). Validity is checked per mesh,
    /// not tie-wide, since not every mesh in a tie carries real UV data at this offset. The check is
    /// "every value decodes inside [0,1]"; a short or out-of-bounds window returns null rather than a
    /// partial copy.</summary>
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
