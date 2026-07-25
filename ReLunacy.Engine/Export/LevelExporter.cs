using System.Numerics;
using ReLunacy.Engine.Assets.Geometry;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Scene;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace ReLunacy.Engine.Export;

public readonly record struct LevelExportOptions(bool ExportMobys, bool ExportTies, bool ExportUFrags);

/// <summary>
/// Exports an entire loaded level as a single .glb. Unlike single-asset export, this builds each
/// unique Moby/Tie asset's mesh exactly once and references it from every placed instance's node —
/// true glTF mesh instancing, so a level with hundreds of copies of the same prop doesn't duplicate
/// its geometry hundreds of times, and Blender/Unreal show them as linked duplicates (edit one,
/// every instance updates). Nodes are organized as Mobys/AssetName/Instance and
/// Zones/ZoneName/{Ties/AssetName,UFrags}/Instance, mirroring the level's own data model instead of
/// flattening everything into one unstructured mesh soup. UFrags are unique per-placement terrain
/// geometry (nothing to instance), so they're exported flat under their owning zone.
/// </summary>
public static class LevelExporter
{
    public static void Export(string filePath, string levelName, EntityManager entityManager, LevelExportOptions options, Action<float>? onProgress = null)
    {
        var sceneBuilder = new SceneBuilder();
        var materialCache = new Dictionary<ulong, MaterialBuilder>();
        var mobyMeshCache = new Dictionary<ulong, IReadOnlyList<(string Name, IMeshBuilder<MaterialBuilder> Mesh)>>();
        var tieMeshCache = new Dictionary<ulong, IReadOnlyList<(string Name, IMeshBuilder<MaterialBuilder> Mesh)>>();

        var root = new NodeBuilder(levelName);
        bool anyContentAdded = false;

        int totalInstances = (options.ExportMobys ? entityManager.MobysCount : 0)
                            + (options.ExportTies ? entityManager.TiesCount : 0)
                            + (options.ExportUFrags ? entityManager.UFragsCount : 0);
        int processedInstances = 0;
        void ReportProgress() => onProgress?.Invoke(totalInstances == 0 ? 1f : ++processedInstances / (float)totalInstances);

        if (options.ExportMobys)
        {
            var mobysRoot = root.CreateNode("Mobys");
            var byAsset = entityManager.Regions.SelectMany(r => r.MobyInstances.Entities).OfType<EntityMoby>().GroupBy(e => e.BaseMoby.Id);

            foreach (var assetGroup in byAsset)
            {
                var moby = assetGroup.First().BaseMoby;
                var assetMeshes = GetOrBuildMobyMeshes(moby, materialCache, mobyMeshCache);
                var assetNode = mobysRoot.CreateNode(ExportPaths.SanitizeFileName(moby.Name ?? $"Moby_{moby.Id:X}"));

                foreach (var instance in assetGroup)
                {
                    if (moby.Skeleton != null)
                        AddSkinnedInstanceNode(sceneBuilder, assetNode, instance.Name, instance.Transform.GetMatrix(), assetMeshes, moby.Skeleton);
                    else
                        AddInstanceNode(sceneBuilder, assetNode, instance.Name, instance.Transform.GetMatrix(), assetMeshes);
                    anyContentAdded = true;
                    ReportProgress();
                }
            }
        }

        if (options.ExportTies || options.ExportUFrags)
        {
            var zonesRoot = root.CreateNode("Zones");

            foreach (var region in entityManager.Regions)
            {
                foreach (var zone in region.Zones)
                {
                    bool hasTies = options.ExportTies && zone.TieInstances.Size > 0;
                    bool hasUFrags = options.ExportUFrags && zone.UFrags.Size > 0;
                    if (!hasTies && !hasUFrags) continue;

                    var zoneNode = zonesRoot.CreateNode(ExportPaths.SanitizeFileName(zone.ZoneName));

                    if (hasTies)
                    {
                        var tiesRoot = zoneNode.CreateNode("Ties");
                        var byAsset = zone.TieInstances.Entities.OfType<EntityTie>().GroupBy(e => e.BaseTie.Id);

                        foreach (var assetGroup in byAsset)
                        {
                            var tie = assetGroup.First().BaseTie;
                            var assetMeshes = GetOrBuildTieMeshes(tie, materialCache, tieMeshCache);
                            var assetNode = tiesRoot.CreateNode(ExportPaths.SanitizeFileName(tie.Name ?? $"Tie_{tie.Id:X}"));

                            foreach (var instance in assetGroup)
                            {
                                AddInstanceNode(sceneBuilder, assetNode, instance.Name, instance.Transform.GetMatrix(), assetMeshes);
                                anyContentAdded = true;
                                ReportProgress();
                            }
                        }
                    }

                    if (hasUFrags)
                    {
                        var ufragsRoot = zoneNode.CreateNode("UFrags");

                        foreach (var entityUFrag in zone.UFrags.Entities.OfType<EntityUFrag>())
                        {
                            var adapter = new UFragMeshAdapter(entityUFrag.UFrag, entityUFrag.Name);
                            var mesh = GltfExporter.BuildMeshBuilder(entityUFrag.Name, [adapter], materialCache);
                            var instanceNode = ufragsRoot.CreateNode(ExportPaths.SanitizeFileName(entityUFrag.Name));
                            instanceNode.LocalTransform = new AffineTransform(entityUFrag.Transform.GetMatrix());
                            sceneBuilder.AddRigidMesh(mesh, instanceNode);
                            anyContentAdded = true;
                            ReportProgress();
                        }
                    }
                }
            }
        }

        if (!anyContentAdded)
            throw new InvalidOperationException("Nothing to export — no assets found for the selected categories.");

        var model = sceneBuilder.ToGltf2();
        model.SaveGLB(filePath);
    }

    /// <summary>Creates the instance's own transform node under its asset-type group, then attaches
    /// the (possibly bangle-split) shared meshes — directly if there's only one, or as one child
    /// node per bangle if there's more, matching single-asset export's submesh grouping.</summary>
    private static void AddInstanceNode(SceneBuilder sceneBuilder, NodeBuilder assetNode, string instanceName, Matrix4x4 worldMatrix, IReadOnlyList<(string Name, IMeshBuilder<MaterialBuilder> Mesh)> assetMeshes)
    {
        var instanceNode = assetNode.CreateNode(ExportPaths.SanitizeFileName(instanceName));
        instanceNode.LocalTransform = new AffineTransform(worldMatrix);

        if (assetMeshes.Count == 1)
        {
            sceneBuilder.AddRigidMesh(assetMeshes[0].Mesh, instanceNode);
            return;
        }

        foreach (var (name, mesh) in assetMeshes)
            sceneBuilder.AddRigidMesh(mesh, instanceNode.CreateNode(name));
    }

    /// <summary>Skinned counterpart of AddInstanceNode. Unlike a rigid mesh, a skinned mesh is
    /// positioned by its joint nodes' world transforms rather than a mesh-attach transform, so
    /// each placed instance needs its own fresh joint hierarchy (parented under its own instance
    /// node) even though it reuses the same cached mesh/skin-weight data as every other
    /// instance.</summary>
    private static void AddSkinnedInstanceNode(SceneBuilder sceneBuilder, NodeBuilder assetNode, string instanceName, Matrix4x4 worldMatrix, IReadOnlyList<(string Name, IMeshBuilder<MaterialBuilder> Mesh)> assetMeshes, ISkeleton skeleton)
    {
        var instanceNode = assetNode.CreateNode(ExportPaths.SanitizeFileName(instanceName));
        instanceNode.LocalTransform = new AffineTransform(worldMatrix);

        var jointBindings = GltfExporter.BuildSkinnedJoints(skeleton, instanceNode);
        foreach (var (_, mesh) in assetMeshes)
            sceneBuilder.AddSkinnedMesh(mesh, jointBindings);
    }

    private static IReadOnlyList<(string Name, IMeshBuilder<MaterialBuilder> Mesh)> GetOrBuildMobyMeshes(
        IMoby moby, Dictionary<ulong, MaterialBuilder> materialCache, Dictionary<ulong, IReadOnlyList<(string, IMeshBuilder<MaterialBuilder>)>> cache)
    {
        if (cache.TryGetValue(moby.Id, out var cached))
            return cached;

        var skeleton = moby.Skeleton;
        var result = moby.Bangles
            .Select((bangle, i) => string.IsNullOrEmpty(bangle.Name) ? $"Bangle_{i}" : bangle.Name)
            .Zip(moby.Bangles, (name, bangle) => (name, skeleton != null
                ? (IMeshBuilder<MaterialBuilder>)GltfExporter.BuildSkinnedMeshBuilder(name, bangle.Meshes, materialCache, skeleton.RootBoneIndex)
                : (IMeshBuilder<MaterialBuilder>)GltfExporter.BuildMeshBuilder(name, bangle.Meshes, materialCache)))
            .ToList();

        cache[moby.Id] = result;
        return result;
    }

    private static IReadOnlyList<(string Name, IMeshBuilder<MaterialBuilder> Mesh)> GetOrBuildTieMeshes(
        ITie tie, Dictionary<ulong, MaterialBuilder> materialCache, Dictionary<ulong, IReadOnlyList<(string, IMeshBuilder<MaterialBuilder>)>> cache)
    {
        if (cache.TryGetValue(tie.Id, out var cached))
            return cached;

        string name = tie.Name ?? $"Tie_{tie.Id:X}";
        var result = new List<(string, IMeshBuilder<MaterialBuilder>)> { (name, GltfExporter.BuildMeshBuilder(name, tie.Meshes, materialCache)) };

        cache[tie.Id] = result;
        return result;
    }

    /// <summary>Adapts IUFrag (which carries geometry+material directly, not split into
    /// IMesh/IGeometry like Mobys/Ties) so GltfExporter.BuildMeshBuilder can build UFrag terrain
    /// through the exact same code path — including the "expensive" texture channel mapping.</summary>
    private sealed class UFragMeshAdapter(IUFrag ufrag, string name) : IMesh, IGeometry
    {
        public IGeometry Geometry => this;
        public IMaterial Material => ufrag.Material;
        public string? Name => name;
        public string? VertexFormatName => null;
        public Func<int, string?>? VertexDumper => null;

        public ulong Id => ufrag.Id;
        public bool IsLoaded => ufrag.IsLoaded;

        public float[] GetVertexPositions() => ufrag.GetVertexPositions();
        public float[] GetTextureCoordinates() => ufrag.GetTextureCoordinates();
        public float[]? GetNormals() => ufrag.GetNormals();

        // UFrag terrain carries no baked tangent (or, on some readers, even normal) data — derive
        // both from the triangle/UV data itself via the same fallback GeometryData uses for
        // formats that don't decode real vertex attributes.
        public float[]? GetTangents()
        {
            var positions = ufrag.GetVertexPositions();
            var indices = ufrag.GetIndices();
            var normals = ufrag.GetNormals() ?? GeometryMath.ComputeNormals(positions, indices);
            return GeometryMath.ComputeTangents(positions, ufrag.GetTextureCoordinates(), normals, indices, null);
        }

        public float[]? GetVertexAlphaCandidates() => null;
        public uint[] GetIndices() => ufrag.GetIndices();
        public Vector3 GetBoundingCenter() => ufrag.GetBoundingCenter();
        public float GetBoundingRadius() => ufrag.GetBoundingRadius();
        public int[]? GetJointIndices() => null;
        public float[]? GetJointWeights() => null;
    }
}
