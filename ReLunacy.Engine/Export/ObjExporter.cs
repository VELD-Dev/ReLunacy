using System.Text;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Export;

/// <summary>
/// Exports engine mesh groups (a Moby's bangles, or a Tie's single whole-model group) as
/// Wavefront OBJ + MTL + loose PNG textures. Only albedo/normal are written - MTL has no
/// standard slot for the specular/metallic/emissive data packed into the "expensive" texture
/// (see GltfExporter, which carries all of it via glTF's PBR extensions instead).
/// </summary>
public static class ObjExporter
{
    /// <summary>`skeleton` is accepted (and ignored) only so this matches GltfExporter.Export's
    /// signature - the two are called through the same delegate type in AssetViewer.ExportModel.
    /// OBJ/MTL has no representation for a bone hierarchy or vertex skin weights at all.</summary>
    public static void Export(string filePath, string modelName, IReadOnlyList<MeshGroup> groups, ISkeleton? skeleton = null, Action<float>? onProgress = null)
    {
        string directory = Path.GetDirectoryName(filePath) ?? ".";
        string baseName = Path.GetFileNameWithoutExtension(filePath);
        string mtlFileName = baseName + ".mtl";

        var obj = new StringBuilder();
        var mtl = new StringBuilder();
        var writtenMaterials = new HashSet<ulong>();

        obj.AppendLine($"mtllib {mtlFileName}");

        int totalMeshes = groups.Sum(g => g.Meshes.Count);
        int processedMeshes = 0;
        int vertexBase = 1; // OBJ indices are 1-based and shared across the whole file

        foreach (var group in groups)
        {
            var meshes = group.Meshes;
            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                var mesh = meshes[meshIndex];
                string materialName = GetMaterialName(mesh.Material);

                if (writtenMaterials.Add(mesh.Material.Id))
                    WriteMaterial(mtl, mesh.Material, materialName, directory);

                var positions = mesh.Geometry.GetVertexPositions();
                var uvs = mesh.Geometry.GetTextureCoordinates();
                var normals = mesh.Geometry.GetNormals();
                var indices = mesh.Geometry.GetIndices();
                int vertexCount = positions.Length / 3;

                string meshLabel = string.IsNullOrEmpty(mesh.Name) ? $"Mesh_{meshIndex}" : mesh.Name;
                // Groups.Count>1 means this asset has real submesh groups (a Moby's bangles) - keep
                // that grouping visible in the object name rather than flattening it away.
                obj.AppendLine(groups.Count > 1 && !string.IsNullOrEmpty(group.Name)
                    ? $"o {group.Name}_{meshLabel}"
                    : $"o {meshLabel}");

                for (int i = 0; i < vertexCount; i++)
                    obj.AppendLine(FormattableString.Invariant($"v {positions[i * 3]} {positions[i * 3 + 1]} {positions[i * 3 + 2]}"));

                for (int i = 0; i < vertexCount; i++)
                    obj.AppendLine(FormattableString.Invariant($"vt {uvs[i * 2]} {uvs[i * 2 + 1]}"));

                bool hasNormals = normals != null && normals.Length >= vertexCount * 3;
                if (hasNormals)
                    for (int i = 0; i < vertexCount; i++)
                        obj.AppendLine(FormattableString.Invariant($"vn {normals![i * 3]} {normals[i * 3 + 1]} {normals[i * 3 + 2]}"));

                obj.AppendLine($"usemtl {materialName}");
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    // OBJ faces are 1-based and shared across the whole file, so each mesh's own
                    // local indices need offsetting by every vertex written before it.
                    uint a = indices[i] + (uint)vertexBase, b = indices[i + 1] + (uint)vertexBase, c = indices[i + 2] + (uint)vertexBase;
                    obj.AppendLine(hasNormals
                        ? $"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}"
                        : $"f {a}/{a} {b}/{b} {c}/{c}");
                }

                vertexBase += vertexCount;
                processedMeshes++;
                onProgress?.Invoke(processedMeshes / (float)totalMeshes);
            }
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(filePath, obj.ToString());
        File.WriteAllText(Path.Combine(directory, mtlFileName), mtl.ToString());
    }

    private static string GetMaterialName(IMaterial material) =>
        string.IsNullOrEmpty(material.Name) ? $"Material_{material.Id:X}" : ExportPaths.SanitizeFileName(material.Name);

    private static void WriteMaterial(StringBuilder mtl, IMaterial material, string materialName, string directory)
    {
        mtl.AppendLine($"newmtl {materialName}");
        mtl.AppendLine("Ka 1.000 1.000 1.000");
        mtl.AppendLine("Kd 1.000 1.000 1.000");
        mtl.AppendLine("Ks 0.000 0.000 0.000");
        mtl.AppendLine("d 1.0");
        mtl.AppendLine("illum 2");

        if (material.AlbedoTexture != null)
        {
            string? file = WriteTexturePng(material.AlbedoTexture, directory, materialName + "_albedo");
            if (file != null)
                mtl.AppendLine($"map_Kd {file}");
        }

        if (material.NormalTexture != null)
        {
            string? file = WriteTexturePng(material.NormalTexture, directory, materialName + "_normal");
            if (file != null)
                mtl.AppendLine($"map_Bump {file}");
        }

        mtl.AppendLine();
    }

    private static string? WriteTexturePng(ITexture texture, string directory, string fileNameNoExt)
    {
        byte[]? png = TextureEncoding.DecodeToPng(texture);
        if (png == null)
            return null;

        string fileName = fileNameNoExt + ".png";
        File.WriteAllBytes(Path.Combine(directory, fileName), png);
        return fileName;
    }
}
