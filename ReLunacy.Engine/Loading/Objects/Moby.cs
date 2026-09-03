using System.Buffers;
using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Meshes;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Objects;

public class Moby : IDisposable
{
    public IMoby MobyObj { get; private set; }

    public StreamHelper mobyStream;
    public StreamHelper verticesStream;
    public StreamHelper indicesStream;
    public ulong TUID => MobyObj.TUID;
    public bool IsOld => MobyObj is OldMoby;
    public Vector4 BoundingSphere => MobyObj is OldMoby om ? om.boundingSphere : ((NewMoby)MobyObj).boundingSphere;
    public float Scale => MobyObj is OldMoby om ? om.scale : ((NewMoby)MobyObj).scale;
    public uint BanglesPointer => MobyObj is OldMoby ? 0 : ((NewMoby)MobyObj).banglesPointer;
    public uint BanglesCount => MobyObj is OldMoby om ? om.bangleCount : ((NewMoby)MobyObj).bangleCount1;
    public uint SkeletonPointer => MobyObj is OldMoby om ? om.skeletonPointer : ((NewMoby)MobyObj).skeletonPointer;
    public uint BonesCount => MobyObj is OldMoby om ? om.bonesCount : ((NewMoby)MobyObj).bonesCount1;
    public uint TransformPointer => MobyObj is OldMoby ? uint.MinValue : ((NewMoby)MobyObj).skeletonPointer;
    public uint VerticesOffset => MobyObj is OldMoby om ? om.verticesOffset : uint.MinValue;
    public uint IndicesOffset => MobyObj is OldMoby om ? om.indicesOffset : uint.MinValue;
    public ulong AnimsetID => MobyObj is OldMoby ? uint.MinValue : ((NewMoby)MobyObj).animsetTuid;
    public MobyBangle[] Bangles => MobyObj.bangles;
    public ulong[]? ShaderTUIDs;
    /// <summary>New-engine only. Empty for old engine (which has no per-file name section at all -
    /// see MobyReader, which falls back to debug.dat for those). NOT read from NewMoby.namePointer
    /// (0xB8) - that field is parsed but, per both LibLunacy's Legacy/Moby.cs and its ReLunacy-Ymir
    /// fork (independently, both predating this project and both actually working), is never what
    /// the name comes from. The real name is a plain null-terminated string starting at the offset
    /// of THIS moby's own per-file section 0xD200 - the same "one dedicated section per string"
    /// shape as the vertex/index sections this class already reads (0xE200/0xE100), not a pointer
    /// field inside the 0xD100 metadata struct the way Ties' nameOffset is.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Null if this moby has no skeleton (static props etc.) or if reading one failed -
    /// see the catch below. Read defensively: this is new, unverified-against-every-real-asset
    /// code, and a bug in it must not be able to break loading for mobys that don't even reach it.</summary>
    public MobySkeleton? Skeleton { get; private set; }

    public Moby(StreamHelper sh, FileManager fm, int index = 0) // Index only for old mobys
    {
        mobyStream = sh;

        var igFile = new IGFile(mobyStream.BaseStream);
        IGFile.SectionHeader section = igFile.QuerySection(OldMoby.ID); // Old and new mobys share the section ID
        if (section.length == 0x100)
            mobyStream.Seek(section.offset);
        else
            mobyStream.Seek(section.offset + OldMoby.Size * index);

        ReadMoby(isOld: section.length != 0x100, index);

        try
        {
            Skeleton = MobySkeletonReader.Read(mobyStream, SkeletonPointer, BonesCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read skeleton for moby {TUID:X}: {ex.Message}");
        }

        if (!IsOld)
        {
            // Section 0xD200 holds exactly one string - this moby's own name - starting right at
            // the section's offset. id is checked (not just offset != 0) since QuerySection returns
            // a zeroed SectionHeader, id included, when a section doesn't exist - offset 0 could in
            // principle be a real (if very unlikely) location for a found section to point at.
            var nameSection = igFile.QuerySection(0xD200);
            if (nameSection.id == 0xD200)
                Name = mobyStream.ReadString(nameSection.offset);

            var shaderReferencesSec = igFile.QuerySection(Shader.NewInternalTUIDSecID);

            ShaderTUIDs = ArrayPool<ulong>.Shared.Rent((int)shaderReferencesSec.count);

            for (int i = 0; i < shaderReferencesSec.count; i++)
            {
                sh.Seek((long)(shaderReferencesSec.offset + sizeof(ulong) * (ulong)i));
                ShaderTUIDs[i] = sh.ReadUInt64();
            }

            // New engine: geometry lives inside this moby's own IGFile as dedicated sections,
            // not a raw-file offset field (there is none on NewMoby) - mirrors Tie's new-engine
            // vertex/index section reads.
            var vertSec = igFile.QuerySection(MobyMesh.VerticesSecID);
            mobyStream.Seek(vertSec.offset);
            verticesStream = new StreamHelper(new MemoryStream(mobyStream.ReadBytes(vertSec.length)), StreamHelper.Endianness.Big);

            var indSec = igFile.QuerySection(MobyMesh.IndicesSecID);
            mobyStream.Seek(indSec.offset);
            indicesStream = new StreamHelper(new MemoryStream(mobyStream.ReadBytes(indSec.length)), StreamHelper.Endianness.Big);
        }
        else
        {
            if (MobyObj is not OldMoby omoby)
                return;

            // Some old-engine mobys (logic-only props: triggers, camera targets, path markers,
            // etc. - confirmed present in Tools of Destruction's meridian_city) have zero bangles,
            // or a bangle with zero meshes: no visual geometry at all. bangles/meshes are
            // [Reference(...)]-deserialized arrays that stay null when their count is zero, so
            // blindly indexing bangles[^1].meshes[^1] (as this used to, four times below) threw a
            // NullReferenceException for any such moby instead of just... having no mesh data.
            if (!TryGetLastMesh(omoby.bangles, out var lastMesh))
            {
                // Empty, not left null: MobyReader.ReadMobyBanglesMeshes unconditionally seeks
                // these streams before checking bangle/mesh counts, so a null stream here would
                // just move the same crash one call further down instead of fixing it.
                verticesStream = new StreamHelper(new MemoryStream(), StreamHelper.Endianness.Big);
                indicesStream = new StreamHelper(new MemoryStream(), StreamHelper.Endianness.Big);
                return;
            }

            // Old engine: geometry lives in either vertices.dat or textures.dat, selected by
            // the high bit of the offset field itself.
            if ((omoby.verticesOffset & 0x80000000) != 0)
            {
                var vertigfile = fm.igfiles["vertices.dat"]!;
                var vertSec = vertigfile.QuerySection(0x9000);
                vertigfile.sh.Seek(vertSec.offset + (omoby.verticesOffset & ~0x80000000));
                var length = lastMesh.verticesOffset + lastMesh.verticesCount * (lastMesh.verticesType == 0 ? VertexFormat0.Size : VertexFormat1.Size);
                verticesStream = new StreamHelper(new MemoryStream(vertigfile.sh.ReadBytes(length)), StreamHelper.Endianness.Big);
            }
            else
            {
                if (!fm.rawfiles.TryGetValue("textures.dat", out var txstream) || txstream is null)
                    throw new FileNotFoundException("File is missing.", "textures.dat");

                omoby.verticesOffset &= ~0x80000000;
                txstream.Seek(omoby.verticesOffset, SeekOrigin.Begin);
                var length = lastMesh.verticesOffset + lastMesh.verticesCount * (lastMesh.verticesType == 0 ? VertexFormat0.Size : VertexFormat1.Size);
                byte[] verticesData = new byte[length];
                txstream.Read(verticesData, 0, (int)length);
                verticesStream = new StreamHelper(new MemoryStream(verticesData), StreamHelper.Endianness.Big);
            }

            if ((omoby.indicesOffset & 0x80000000) != 0)
            {
                var indigfile = fm.igfiles["vertices.dat"]!;
                var indSec = indigfile.QuerySection(0x9100);
                indigfile.sh.Seek(indSec.offset + (omoby.indicesOffset & ~0x80000000));
                var length = lastMesh.indicesOffset * sizeof(ushort) + lastMesh.indicesCount * (uint)sizeof(ushort);
                indicesStream = new StreamHelper(new MemoryStream(indigfile.sh.ReadBytes(length)), StreamHelper.Endianness.Big);
            }
            else
            {
                if (!fm.rawfiles.TryGetValue("textures.dat", out var txstream) || txstream is null)
                    throw new FileNotFoundException("File is missing.", "textures.dat");

                omoby.indicesOffset &= ~0x80000000;
                txstream.Seek(omoby.indicesOffset, SeekOrigin.Begin);
                var length = lastMesh.indicesOffset * sizeof(ushort) + lastMesh.indicesCount * (uint)sizeof(ushort);
                byte[] indexData = new byte[length];
                txstream.Read(indexData, 0, (int)length);
                indicesStream = new StreamHelper(new MemoryStream(indexData), StreamHelper.Endianness.Big);
            }
        }
    }

    // Searches backward for the last bangle that actually has meshes (not necessarily the very
    // last bangle - a moby could have trailing empty bangles too), since the whole point is
    // finding the true final mesh's offset/count to compute the total buffer length. Returns
    // false if this moby has no mesh data anywhere (null/empty bangles, or every bangle empty).
    private static bool TryGetLastMesh(MobyBangle[]? bangles, out MobyMesh lastMesh)
    {
        lastMesh = default;
        if (bangles == null)
            return false;

        for (int i = bangles.Length - 1; i >= 0; i--)
        {
            if (bangles[i].meshes != null && bangles[i].meshes.Length > 0)
            {
                lastMesh = bangles[i].meshes[^1];
                return true;
            }
        }

        return false;
    }

    public void ReadMoby(bool isOld, int index = 0) // index only for old mobys
    {
        MobyObj = isOld ? OldMoby.Read(mobyStream, index) : NewMoby.Read(mobyStream);
    }

    public byte[] ToBytes() => MobyObj.ToBytes(false);

    public void Dispose()
    {
        // Same null-bangles/null-meshes possibility as the constructor guards against above (a
        // moby with no visual geometry) - nothing was rented from either pool in that case, so
        // there's nothing to return either.
        if (MobyObj.bangles != null)
        {
            for (int i = 0; i < MobyObj.bangles.Length; i++)
            {
                if (MobyObj.bangles[i].meshes == null) continue;

                for (int j = 0; j < MobyObj.bangles[i].meshes.Length; j++)
                {
                    ref var mesh = ref MobyObj.bangles[i].meshes[j];
                    if (mesh.verticesType == 0) ArrayPool<VertexFormat0>.Shared.Return(mesh.vertices0);
                    if (mesh.verticesType == 1) ArrayPool<VertexFormat1>.Shared.Return(mesh.vertices1);
                }
            }
            ArrayPool<MobyBangle>.Shared.Return(MobyObj.bangles);
        }

        verticesStream?.Close();
        indicesStream?.Close();

        mobyStream.Close();
        GC.SuppressFinalize(this);
    }
}
