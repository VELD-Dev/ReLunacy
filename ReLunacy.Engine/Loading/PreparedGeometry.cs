using System.Collections.Concurrent;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Engine.Scene;
using IMesh = ReLunacy.Engine.Assets.Interfaces.IMesh;

namespace ReLunacy.Engine.Loading;

/// <summary>
/// Every mesh's and UFrag's renderer-side vertex, index and interleaved arrays, built off the main
/// thread and in parallel, before anything that needs the graphics device runs.
///
/// This exists for the window between "the level finished parsing" and "its textures are on the
/// GPU", which used to freeze the editor outright. The work in it splits cleanly in two: packing
/// decoded geometry into <see cref="Vertex3D"/> and then into the renderer's interleaved float
/// layout is pure arithmetic over arrays already in memory - the bulk of the cost, and perfectly
/// parallel - while allocating the GPU resources and filling the asset caches is not (NeoVeldrid is
/// consumed as a binary package here, nothing in this codebase shares a device across threads, and
/// the capture registry is a plain static dictionary). So the arithmetic happens here, on the
/// loading task, and the main thread is left with the part that genuinely has to be serial.
///
/// Entries are TAKEN rather than read, the same way AssetManager's decoded textures are: once a mesh
/// has been built the prepared copy is the one the RenderMesh holds, so keeping a second reference
/// would pin a whole level's geometry for nothing.
/// </summary>
public sealed class PreparedGeometry
{
    /// <param name="HasValidLightmapUVs">UFrags only: whether the chunk's UV2 channel survived
    /// validation, which also decides whether its baked-lighting index may be used. Computed here
    /// because the test walks every UV - see EntityUFrag.</param>
    public readonly record struct Entry(Vertex3D[] Vertices, uint[] Indices, float[] Interleaved, bool HasValidLightmapUVs);

    // Keyed by the asset instance itself (the IMesh / IUFrag), which is exactly how the capture
    // registry and the material caches key their own entries.
    private readonly ConcurrentDictionary<object, Entry> _entries = new(ReferenceEqualityComparer.Instance);

    public int Count => _entries.Count;

    /// <summary>Takes a prepared entry, removing it. Returns false for geometry the pre-pass never
    /// saw (a Moby variant built later by an editor path), which every caller handles by preparing
    /// it itself.</summary>
    public bool TryTake(object key, out Entry entry) => _entries.TryRemove(key, out entry);

    /// <summary>
    /// Prepares every mesh of every Moby and Tie, and every UFrag of every zone the level's regions
    /// claim - that is, exactly the geometry the AssetManager and the scene entities build at load.
    ///
    /// Safe to run concurrently: each item reads its own already-decoded asset arrays and writes one
    /// dictionary entry. Nothing here touches the material caches, the capture registry or the
    /// graphics device, which is what keeps it off the main thread.
    /// </summary>
    public static PreparedGeometry Build(LevelData level, LevelLoadProgress? progress = null)
    {
        var prepared = new PreparedGeometry();

        var meshes = new List<IMesh>();
        foreach (var (_, moby) in level.Mobys)
            foreach (var bangle in moby.Bangles)
                meshes.AddRange(bangle.Meshes);
        foreach (var (_, tie) in level.Ties)
            meshes.AddRange(tie.Meshes);

        // A zone can belong to several regions (All 4 One's lobby has two regions over one pair of
        // zones), and each region builds its own EntityZone over it - but they share the UFrag
        // instances, so one prepared entry serves both.
        var ufrags = new List<IUFrag>();
        var seenZones = new HashSet<ulong>();
        foreach (var region in level.Regions)
            foreach (var zone in region.Zones)
                if (seenZones.Add(zone.Id))
                    ufrags.AddRange(zone.UFrags);

        progress?.AddTotal(meshes.Count + ufrags.Count);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.ForEach(meshes, mesh =>
        {
            try { prepared._entries.TryAdd(mesh, PrepareMesh(mesh)); }
            catch (Exception ex)
            {
                // Left unprepared rather than rethrown: the build path falls back to preparing it
                // itself, which is where the failure belongs (it has the material and the logging).
                Console.WriteLine($"Warning: Failed to prepare mesh geometry '{mesh.Name}': {ex.Message}");
            }
            progress?.Advance();
        });

        Parallel.ForEach(ufrags, ufrag =>
        {
            try { prepared._entries.TryAdd(ufrag, EntityUFrag.PrepareGeometry(ufrag)); }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to prepare UFrag geometry '{ufrag.Name}': {ex.Message}");
            }
            progress?.Advance();
        });

        Console.WriteLine($"Geometry prepared in {sw.ElapsedMilliseconds}ms (loading task, parallel): " +
            $"{meshes.Count} mesh(es), {ufrags.Count} UFrag(s).");
        return prepared;
    }

    /// <summary>The CPU half of AssetManager.BuildModel, for variant 0 - the materials the pre-pass
    /// cannot resolve are not needed to pack the vertices, only to decide whether vertex alpha is
    /// read, which the mesh's own material already says.</summary>
    private static Entry PrepareMesh(IMesh mesh)
    {
        bool twoLayer = mesh as Assets.Geometry.Mesh is { SecondMaterial: not null, BlendMask: not null };
        var vertices = AssetManager.ConvertGeometryToVertices(mesh.Geometry, mesh.Material.UsesVertexAlpha || twoLayer);
        var indices = mesh.Geometry.GetIndices();
        return new Entry(vertices, indices, Interleaved(vertices, indices), HasValidLightmapUVs: false);
    }

    /// <summary>The interleaved copy the capture registry wants, or an empty array for geometry it
    /// would refuse anyway - Register is never called for those, so there is no reason to pack them.</summary>
    internal static float[] Interleaved(Vertex3D[] vertices, uint[] indices) =>
        vertices.Length > 0 && indices.Length >= 3 ? VulkanSceneCapture.Interleave(vertices) : [];
}
