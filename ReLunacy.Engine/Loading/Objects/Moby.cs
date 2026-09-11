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
    public string Name { get; private set; } = string.Empty;
    public MobySkeleton? Skeleton { get; private set; }

    public Moby(StreamHelper sh, FileManager fm, int index = 0)
    {
        mobyStream = sh;
        var igFile = new IGFile(mobyStream.BaseStream);
        IGFile.SectionHeader section = igFile.QuerySection(OldMoby.ID);
        if (section.length == 0x100) mobyStream.Seek(section.offset);
        else mobyStream.Seek(section.offset + OldMoby.Size * index);

        ReadMoby(isOld: section.length != 0x100, index);

        try
        {
            Skeleton = MobySkeletonReader.Read(mobyStream, SkeletonPointer, BonesCount, readAnimationReferencePose: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read skeleton for moby {TUID:X}: {ex.Message}");
        }

        if (!IsOld)
        {
            var nameSection = igFile.QuerySection(0xD200);
            if (nameSection.id == 0xD200) Name = mobyStream.ReadString(nameSection.offset);

            var shaderReferencesSec = igFile.QuerySection(Shader.NewInternalTUIDSecID);
            ShaderTUIDs = ArrayPool<ulong>.Shared.Rent((int)shaderReferencesSec.count);
            for (int i = 0; i < shaderReferencesSec.count; i++)
            {
                sh.Seek((long)(shaderReferencesSec.offset + sizeof(ulong) * (ulong)i));
                ShaderTUIDs[i] = sh.ReadUInt64();
            }

            var vertSec = igFile.QuerySection(MobyMesh.VerticesSecID);
            mobyStream.Seek(vertSec.offset);
            verticesStream = new StreamHelper(new MemoryStream(mobyStream.ReadBytes(vertSec.length)), StreamHelper.Endianness.Big);
            var indSec = igFile.QuerySection(MobyMesh.IndicesSecID);
            mobyStream.Seek(indSec.offset);
            indicesStream = new StreamHelper(new MemoryStream(mobyStream.ReadBytes(indSec.length)), StreamHelper.Endianness.Big);
        }
        else
        {
            if (MobyObj is not OldMoby omoby) return;
            if (!TryGetLastMesh(omoby.bangles, out var lastMesh))
            {
                verticesStream = new StreamHelper(new MemoryStream(), StreamHelper.Endianness.Big);
                indicesStream = new StreamHelper(new MemoryStream(), StreamHelper.Endianness.Big);
                return;
            }

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

    private static bool TryGetLastMesh(MobyBangle[]? bangles, out MobyMesh lastMesh)
    {
        lastMesh = default;
        if (bangles == null) return false;
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

    public void ReadMoby(bool isOld, int index = 0) => MobyObj = isOld ? OldMoby.Read(mobyStream, index) : NewMoby.Read(mobyStream);
    public byte[] ToBytes() => MobyObj.ToBytes(false);

    public void Dispose()
    {
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
