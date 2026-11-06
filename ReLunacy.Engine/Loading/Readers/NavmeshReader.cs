using System.Numerics;
using ReLunacy.Engine.Assets.Navigation;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Reads a zone's navigation mesh from sections 0x19000 and 0x19200.
///
/// Layout, measured over all 55 retail new-engine levels (docs/reverse/newengine_special_zones.md):
///
///   0x19000  one 0x40-byte record per chunk
///            +0x02 u16 vertex count V        +0x06 u16 edge count E
///            +0x0A u16 face count F          +0x20 u32 offset into 0x19200, 128-byte aligned
///            +0x30 4x f32 bounding sphere (centre xyz, radius)
///   0x19200  at each chunk's offset: V vertices (16B: float x, y, z and a zero word), then
///            E edges (16B: u16 v0, v1, ..., +0x08 the owning chunk index, +0x0A the chunk on the
///            far side or 0xFFFF), then F faces (48B: three u16 edge indices, bit 15 = traverse
///            that edge backwards).
///
/// Faces are built from the edges' vertices as an UNORDERED triangle. The direction bits do close
/// the loop on 18993 of 19681 retail faces, but on the remaining 688 they do not, while the three
/// edges still span exactly three vertices on all 19681 - so taking the vertex set is what reads
/// every face rather than most of them. Winding is therefore not meaningful here, which is why the
/// surface is drawn double-sided.
/// </summary>
internal static class NavmeshReader
{
    public const uint ChunkTable = 0x19000;
    public const uint ChunkData = 0x19200;

    private const int ChunkRecordSize = 0x40;
    private const int VertexSize = 16;
    private const int EdgeSize = 16;
    private const int FaceSize = 48;
    private const ushort NoNeighbour = 0xFFFF;

    /// <summary>The zone's navmesh, or null when it has none - which is most zones.</summary>
    public static Navmesh? Read(IGFile zone, StreamHelper sh)
    {
        IGFile.SectionHeader table = zone.QuerySection(ChunkTable);
        IGFile.SectionHeader data = zone.QuerySection(ChunkData);
        if (table.count == 0 || data.count == 0)
            return null;

        long dataStart = data.offset;
        long dataEnd = dataStart + (long)data.count * data.length;
        var chunks = new List<NavmeshChunk>((int)table.count);

        for (int i = 0; i < table.count; i++)
        {
            sh.Seek(table.offset + (long)i * ChunkRecordSize);
            sh.ReadUInt16();                       // +0x00, zero on every retail chunk
            int vertexCount = sh.ReadUInt16();     // +0x02
            sh.ReadUInt16();                       // +0x04, vertex count again
            int edgeCount = sh.ReadUInt16();       // +0x06
            sh.ReadUInt16();                       // +0x08, vertex + edge count
            int faceCount = sh.ReadUInt16();       // +0x0A
            sh.Seek(table.offset + (long)i * ChunkRecordSize + 0x20);
            long blockOffset = sh.ReadUInt32();
            sh.Seek(table.offset + (long)i * ChunkRecordSize + 0x30);
            var sphere = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());

            long vertexBase = dataStart + blockOffset;
            long edgeBase = vertexBase + (long)vertexCount * VertexSize;
            long faceBase = edgeBase + (long)edgeCount * EdgeSize;
            if (vertexCount <= 0 || faceBase + (long)faceCount * FaceSize > dataEnd)
            {
                Console.WriteLine(
                    $"Navmesh chunk {i}: V={vertexCount} E={edgeCount} F={faceCount} at 0x{blockOffset:X} " +
                    $"runs past section 0x{ChunkData:X}; skipped.");
                continue;
            }

            var vertices = new Vector3[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                sh.Seek(vertexBase + (long)v * VertexSize);
                vertices[v] = new Vector3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            }

            var edges = new (ushort A, ushort B)[edgeCount];
            var neighbours = new int?[edgeCount];
            for (int e = 0; e < edgeCount; e++)
            {
                sh.Seek(edgeBase + (long)e * EdgeSize);
                ushort a = sh.ReadUInt16();
                ushort b = sh.ReadUInt16();
                sh.Seek(edgeBase + (long)e * EdgeSize + 0x0A);
                ushort far = sh.ReadUInt16();
                edges[e] = (a < vertexCount ? a : (ushort)0, b < vertexCount ? b : (ushort)0);
                neighbours[e] = far == NoNeighbour ? null : far;
            }

            var indices = new List<int>(faceCount * 3);
            for (int f = 0; f < faceCount; f++)
            {
                sh.Seek(faceBase + (long)f * FaceSize);
                int e0 = sh.ReadUInt16() & 0x7FFF;
                int e1 = sh.ReadUInt16() & 0x7FFF;
                int e2 = sh.ReadUInt16() & 0x7FFF;
                if (e0 >= edgeCount || e1 >= edgeCount || e2 >= edgeCount)
                    continue;

                // The three edges of a triangle span exactly three vertices; collect them in the
                // order the edges introduce them. See the class comment on why not the loop order.
                Span<int> corners = stackalloc int[3];
                int n = 0;
                foreach (int e in (Span<int>)[e0, e1, e2])
                {
                    foreach (ushort v in (Span<ushort>)[edges[e].A, edges[e].B])
                    {
                        bool seen = false;
                        for (int k = 0; k < n; k++) seen |= corners[k] == v;
                        if (!seen && n < 3) corners[n++] = v;
                    }
                }
                if (n != 3) continue;
                indices.Add(corners[0]);
                indices.Add(corners[1]);
                indices.Add(corners[2]);
            }

            chunks.Add(new NavmeshChunk
            {
                Index = i,
                BoundingSphere = sphere,
                Vertices = vertices,
                TriangleIndices = [.. indices],
                Edges = edges,
                EdgeNeighbours = neighbours,
            });
        }

        return chunks.Count > 0 ? new Navmesh { Chunks = chunks } : null;
    }
}
