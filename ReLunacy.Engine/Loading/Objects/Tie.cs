using System.Buffers;
using System.Numerics;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Meshes;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Objects;

public class Tie : IDisposable
{
    public readonly TieMetadataOld? metadataOld;
    public readonly TieMetadataNew? metadataNew;
    public readonly StreamHelper tieStream;
    public readonly IGFile verticesFile;
    public readonly StreamHelper verticesBuffer;
    public readonly StreamHelper indicesBuffer;
    public bool isOld;

    public ulong TUID => isOld ? metadataOld!.Value.TUID : metadataNew!.Value.TUID;
    public Vector3 Scale => isOld ? metadataOld!.Value.scale : metadataNew!.Value.scale;
    public byte MeshesCount => isOld ? metadataOld!.Value.meshesCount : metadataNew!.Value.meshesCount;
    public TieMesh[] Meshes => isOld ? metadataOld!.Value.meshes : metadataNew!.Value.meshes;
    public string Name { get; private set; } = string.Empty;

    public ulong[]? ShaderTUIDs;

    /// <summary>RAW lightmap UV channel, flat [u0,v0,u1,v1,...], or null when the read isn't even in
    /// bounds. See <see cref="TieLightmapUV"/> - this is RSX attribute location 4, which the game's tie
    /// vertex shader routes into tc0.zw. A primitive's window starts at its +0x06 entry on both
    /// engines; the new engine stores the array packed (see ReadLightmapUVsNew). Not every window in
    /// here is real UV data; decide PER MESH before use, as TieReader does.</summary>
    public float[]? LightmapUVs { get; private set; }

    public Tie(IGFile file, FileManager fm, bool old = false, uint index = 0)
    {
        tieStream = file.sh;
        isOld = old;
        var igFile = new IGFile(tieStream.BaseStream);
        var section = igFile.QuerySection(TieMetadataOld.ID);
        tieStream.Seek(section.offset + TieMetadataOld.Size * index);

        if (old)
        {
            metadataOld = TieMetadataOld.Read(tieStream, index);
            verticesFile = fm.igfiles["vertices.dat"]!;
            var vertSec = verticesFile.QuerySection(VertexFormat0.OldID);
            var data = new byte[metadataOld.Value.verticesBufferSize];
            verticesFile.sh.Seek(vertSec.offset + metadataOld.Value.verticesBufferStart);
            verticesFile.sh.Read(data);
            verticesBuffer = new StreamHelper(new MemoryStream(data), tieStream._endianness);

            // Old-engine TieMesh fields live at different offsets than the [FileOffset]
            // attributes (new-engine layout) - re-read them manually.
            var meshesPtr = tieStream.ReadUInt32(section.offset + TieMetadataOld.Size * index);
            for (int mi = 0; mi < metadataOld.Value.meshesCount; mi++)
            {
                ref var m = ref metadataOld.Value.meshes[mi];
                uint meshBase = (uint)(meshesPtr + mi * TieMesh.Size);
                m.indicesIndex = tieStream.ReadUInt32(meshBase + 0x00);
                m.verticesIndex = tieStream.ReadUInt16(meshBase + 0x04);
                m.verticesCount = tieStream.ReadUInt16(meshBase + 0x08);
                m.indicesCount = tieStream.ReadUInt16(meshBase + 0x12);
                m.oldShaderIndex = tieStream.ReadUInt16(meshBase + 0x28);
                m.oldDistanceFadeScale = BitConverter.UInt32BitsToSingle(tieStream.ReadUInt32(meshBase + 0x30));
                m.oldDistanceFadeBias = BitConverter.UInt32BitsToSingle(tieStream.ReadUInt32(meshBase + 0x34));
                m.isOld = true;
            }
            ReadOldTwoLayer(meshesPtr);

            var indSec = verticesFile.QuerySection(TieVertIndex.OldID);
            uint maxIndEnd = 0;
            foreach (var m in metadataOld.Value.meshes)
            {
                uint end = (m.indicesIndex + m.indicesCount) * sizeof(ushort);
                if (end > maxIndEnd) maxIndEnd = end;
            }
            verticesFile.sh.Seek(indSec.offset);
            var inddata = new byte[maxIndEnd];
            verticesFile.sh.Read(inddata);
            indicesBuffer = new StreamHelper(new MemoryStream(inddata), tieStream._endianness);

            LightmapUVs = ReadLightmapUVsRaw(verticesFile, vertSec.offset, metadataOld.Value);
        }
        else
        {
            metadataNew = TieMetadataNew.Read(tieStream);
            verticesFile = file;
            var vertSec = verticesFile.QuerySection(VertexFormat0.ID);
            var data = new byte[metadataNew.Value.verticesBufferSize];
            verticesFile.sh.Seek(vertSec.offset + metadataNew.Value.verticesBufferStart);
            verticesFile.sh.Read(data);
            verticesBuffer = new StreamHelper(new MemoryStream(data), tieStream._endianness);

            var indSec = verticesFile.QuerySection(TieVertIndex.ID);
            uint maxIndEnd = 0;
            foreach (var m in metadataNew.Value.meshes)
            {
                uint end = (m.indicesIndex + m.indicesCount) * sizeof(ushort);
                if (end > maxIndEnd) maxIndEnd = end;
            }
            verticesFile.sh.Seek(indSec.offset);
            var inddata = new byte[maxIndEnd];
            verticesFile.sh.Read(inddata);
            indicesBuffer = new StreamHelper(new MemoryStream(inddata), tieStream._endianness);

            LightmapUVs = ReadLightmapUVsNew(verticesFile, vertSec, metadataNew.Value);
        }

        if (!isOld)
        {
            var shaderTuidSections = igFile.QuerySection(Shader.NewInternalTUIDSecID);

            ShaderTUIDs = ArrayPool<ulong>.Shared.Rent((int)shaderTuidSections.count);

            for (int i = 0; i < shaderTuidSections.count; i++)
            {
                file.sh.Seek((long)(shaderTuidSections.offset + (ulong)sizeof(ulong) * (ulong)i));
                ShaderTUIDs[i] = file.sh.ReadUInt64();
            }

            Name = tieStream.ReadString(metadataNew!.Value.nameOffset);
            ResolveNewShaderIndices((int)shaderTuidSections.count);
            ReadTwoLayer(igFile);
            ReadUvAnimations(igFile);
        }
    }

    /// <summary>New engine, two-layer primitives (material set 0): the second material's index in the
    /// 0x5600 list, 0xFFFF for a single-layer primitive; null when the tie has none.</summary>
    /// <summary>Section 0x5C00's animated UVs keyed by the frag they move (<see cref="Assets.Geometry.UvAnimation"/>).</summary>
    public IReadOnlyDictionary<uint, Assets.Geometry.UvAnimation> UvAnimations { get; internal set; } =
        new Dictionary<uint, Assets.Geometry.UvAnimation>();

    private void ReadUvAnimations(IGFile igFile)
    {
        var section = igFile.QuerySection(Assets.Geometry.UvAnimation.SectionId);
        if (section.id != Assets.Geometry.UvAnimation.SectionId) return;
        uint bytes = section.count <= 1 ? section.length : section.count * section.length;
        if (bytes < Assets.Geometry.UvAnimation.RecordSize || section.offset + (long)bytes > igFile.sh.BaseStream.Length) return;
        igFile.sh.Seek(section.offset);
        UvAnimations = Assets.Geometry.UvAnimation.ReadAll(igFile.sh.ReadBytes((int)bytes));
    }

    /// <summary>Each primitive's second material, 0xFFFF for a single-layer one: new engine its index in the
    /// 0x5600 list, Tools of Destruction its 0x5000 ordinal (primitive +0x2A).</summary>
    public ushort[]? SecondShaderIndices { get; private set; }

    /// <summary>Each primitive's blend-mask index (primitive +0x0A), 0xFFFF for none: new engine into
    /// <see cref="BlendMasks"/>, Tools of Destruction into main.dat 0x5700.</summary>
    public ushort[]? BlendMaskIndices { get; private set; }

    /// <summary>New engine: the tie's own blend masks (0x5700 descriptors over 0x5800 texels).</summary>
    public Assets.Interfaces.ITexture?[]? BlendMasks { get; private set; }

    /// <summary>
    /// Tools of Destruction's two-layer primitives (Docs/reverse/tod_catchup_2026-10-10/02_textures_manquantes.md,
    /// PROVEN on the 23 readable levels): +0x2A the second material (0x5000 ordinal), +0x0A its mask in main.dat
    /// 0x5700, set together on 71 primitives of 7 levels (ml_grass_c, ja_crag_rubble, tb_6_grass...), every mask
    /// in range and every first material opaque; 0xFFFF = single layer. The new engine's layout, minus the table.
    /// </summary>
    private void ReadOldTwoLayer(uint meshesPtr)
    {
        int n = metadataOld!.Value.meshesCount;
        var second = new ushort[n];
        var masks = new ushort[n];
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            uint meshBase = (uint)(meshesPtr + i * TieMesh.Size);
            second[i] = tieStream.ReadUInt16(meshBase + 0x2A);
            masks[i] = tieStream.ReadUInt16(meshBase + 0x0A);
            any |= second[i] != 0xFFFF;
        }
        if (!any)
            return;
        SecondShaderIndices = second;
        BlendMaskIndices = masks;
    }

    /// <summary>
    /// Reads the two-layer table (Docs/reverse/NEW_ENGINE_TWO_LAYER_AND_GAMEPLAY_CONSTANTS_2026-10-07.md,
    /// PROVEN): metadata +0x74 points at section 0x3900, {u16 material, u16 second material} per
    /// primitive and per material set (set 0 first; a placement's +0x7E picks another set on 35 of 55,033
    /// QForce placements, not followed here). A second material blends in by the mask of primitive +0x0A
    /// (0x5700 descriptor, 0x5800 texels: 5 shared DXT1 textures, keyed by 0x5900).
    /// </summary>
    private void ReadTwoLayer(IGFile igFile)
    {
        TieMetadataNew meta = metadataNew!.Value;
        if (meta.auxiliaryPointer == 0xFFFFFFFF || igFile.QuerySection(0x3900).id != 0x3900)
            return;
        int n = MeshesCount;
        var second = new ushort[n];
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            second[i] = tieStream.ReadUInt16(meta.auxiliaryPointer + (uint)(i * 4 + 2));
            any |= second[i] != 0xFFFF;
        }
        if (!any)
            return;
        SecondShaderIndices = second;
        var masks = new ushort[n];
        for (int i = 0; i < n; i++)
            masks[i] = (ushort)(Meshes[i].Unk2 >> 48);
        BlendMaskIndices = masks;

        IGFile.SectionHeader descriptors = igFile.QuerySection(0x5700);
        IGFile.SectionHeader texels = igFile.QuerySection(0x5800);
        IGFile.SectionHeader ids = igFile.QuerySection(0x5900);
        if (descriptors.id != 0x5700 || texels.id != 0x5800)
            return;
        uint count = descriptors.count == 1 ? descriptors.length / 0x20 : descriptors.count;
        BlendMasks = new Assets.Interfaces.ITexture?[count];
        for (uint k = 0; k < count; k++)
        {
            try
            {
                var descriptor = Readers.NewEngineZoneLightingReader.ReadDescriptor(tieStream, descriptors.offset + k * 0x20);
                uint id = ids.id == 0x5900 ? tieStream.ReadUInt32(ids.offset + k * 8) : (uint)(TUID ^ k);
                uint texelBytes = texels.count == 1 ? texels.length : texels.length * Math.Max(texels.count, 1u);
                BlendMasks[k] = Readers.NewEngineZoneLightingReader.DecodeTexture(
                    tieStream.BaseStream, descriptor, texels.offset, texels.offset + texelBytes,
                    0xB1E0_0000_0000_0000UL | id, $"BlendMask_{id:X8}");
            }
            catch (Exception e) when (e is InvalidDataException or IOException or OverflowException or ArgumentException)
            {
                Console.WriteLine($"Tie {TUID:X16}: blend mask {k} not decoded: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Picks where this tie's meshes store their shader index. ACiT uses +0x2A; All 4 One / QForce
    /// use +0x26 and leave +0x2A zero (see TieMesh.newShaderIndexA4O). Reading +0x2A on a QForce
    /// tie gave EVERY mesh of a multi-material tie shader 0, i.e. the first material's textures on
    /// all of its parts - the "wrong textures on Ties". The +0x26 layout is chosen only when it is
    /// unambiguous for this tie: +0x2A all zero, +0x26 not all zero, and every +0x26 in range.
    /// </summary>
    private void ResolveNewShaderIndices(int shaderCount)
    {
        TieMesh[] meshes = Meshes;
        if (shaderCount < 2 || meshes.Length == 0) return;
        bool alt = true, anyAlt = false;
        foreach (var mesh in meshes)
        {
            if (mesh.newShaderIndex != 0 || mesh.newShaderIndexA4O >= shaderCount) { alt = false; break; }
            anyAlt |= mesh.newShaderIndexA4O != 0;
        }
        if (!alt || !anyAlt) return;
        for (int i = 0; i < meshes.Length; i++)
            meshes[i].newShaderIndex = meshes[i].newShaderIndexA4O;
    }

    /// <summary>Reads the tie's lightmap UV array from the shared vertex blob, or null if this tie
    /// doesn't have one. One <see cref="TieLightmapUV"/> per vertex, packed immediately after the
    /// tie's own vertex block - metadata 0x18 is the block's end offset, doubling as this array's
    /// start; a mesh's window begins at 0x18 + TieMesh.verticesIndex * 4.
    ///
    /// Returned raw and unvalidated deliberately - validation is per-mesh and lives in
    /// TieReader.SliceLightmapUVs, since baked lighting is a per-mesh decision, not a per-tie
    /// one.</summary>
    private static float[]? ReadLightmapUVsRaw(IGFile verticesFile, uint sectionOffset, in TieMetadataOld meta)
    {
        long span = (long)meta.verticesBufferSize - meta.verticesBufferStart;
        if (span <= 0 || span % VertexFormat0.Size != 0) return null;

        int vertexCount = (int)(span / VertexFormat0.Size);
        long start = sectionOffset + meta.verticesBufferSize;
        if (start + (long)vertexCount * TieLightmapUV.Size > verticesFile.sh.BaseStream.Length) return null;

        float[] uvs;
        try
        {
            uvs = TieLightmapUV.ReadArray(verticesFile.sh, (uint)start, vertexCount);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        // No V flip - the game's vertex shader passes location 4 into tc0.zw untransformed, so
        // these bytes are already in the sampler's convention.
        return uvs;
    }

    /// <summary>
    /// New-engine counterpart of <see cref="ReadLightmapUVsRaw"/>: the same per-vertex Half2 array,
    /// packed right after the tie's vertex block INSIDE its own 0x3000 section.
    ///
    /// Measured 2026-09-19 on acid_refinery_pre: for 76 of 86 Ties the 0x3000 section is exactly
    /// vertexCount * (20 + 4) bytes, 524 of 534 meshes decode inside [0,1] there, and their UV area
    /// tracks their 3D area per triangle (median r = 0.76, 285 meshes above 0.7) - the same unwrap
    /// signature as the old-engine array. The control (the same read shifted into the vertex block)
    /// puts 0 of 534 meshes in range. Before this, no new-engine tie had lightmap UVs, so none of the
    /// ~7,000 per-instance bakes of a QForce level were ever sampled. Validation per mesh stays in
    /// TieReader, exactly as for the old engine.
    ///
    /// The array is PACKED and a primitive finds its window through its own +0x06 - read in the QForce
    /// EBOOT (0x007474E8: attribute 4 = array + primitive[+0x06] * 4), the same field the old engine
    /// indexes with. Padding vertices between primitives have no entry, and the array may stop after
    /// the lightmapped primitives, so it is returned as stored, shorter than the vertex block when the
    /// file says so. Requiring vertexCount entries rejected 35 QForce resources outright, and indexing
    /// by verticesIndex put the 17 padded ones' later meshes on a neighbour's UVs. Which primitives
    /// take a bake is decided per primitive in TieReader (<see cref="NewEngineBakedLighting"/>).
    /// </summary>
    private static float[]? ReadLightmapUVsNew(IGFile file, IGFile.SectionHeader vertSec, in TieMetadataNew meta)
    {
        long size = meta.verticesBufferSize;
        if (size <= 0 || size % VertexFormat0.Size != 0) return null;
        long relative = (long)meta.verticesBufferStart + size;
        // A non-array section's +0x0C is its byte length.
        long entries = ((long)vertSec.length - relative) / TieLightmapUV.Size;
        long start = vertSec.offset + relative;
        if (entries <= 0 || entries > ushort.MaxValue + 1L ||
            start + entries * TieLightmapUV.Size > file.sh.BaseStream.Length) return null;
        try { return TieLightmapUV.ReadArray(file.sh, (uint)start, (int)entries); }
        catch (EndOfStreamException) { return null; }
    }

    public byte[] ToBytes(params object[]? args) => isOld ? metadataOld!.Value.ToBytes(isOld, args) : metadataNew!.Value.ToBytes(isOld, args);

    public void Dispose()
    {
        for (int i = 0; i < Meshes.Length; i++)
        {
            ref var mesh = ref Meshes[i];
            mesh.vertices = [];
            mesh.indices = [];
        }
        tieStream.Close();
        GC.SuppressFinalize(this);
    }
}
