using System.Globalization;
using System.Text;
using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Export;

/// <summary>
/// Exports models to Wavefront OBJ format
/// </summary>
public sealed class ObjExporter : IExporter<IModel>
{
    public string FileExtension => ".obj";

    private readonly bool _exportMaterials;
    private readonly bool _flipUVs;

    public ObjExporter(bool exportMaterials = true, bool flipUVs = true)
    {
        _exportMaterials = exportMaterials;
        _flipUVs = flipUVs;
    }

    public void Export(IModel model, string outputPath)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        var sb = new StringBuilder();
        var mtlPath = Path.ChangeExtension(outputPath, ".mtl");

        // Header
        sb.AppendLine($"# Exported from LibLunacy");
        sb.AppendLine($"# Model: {model.Name ?? model.Id.ToString()}");
        sb.AppendLine();

        if (_exportMaterials)
        {
            sb.AppendLine($"mtllib {Path.GetFileName(mtlPath)}");
            sb.AppendLine();
        }

        int vertexOffset = 0;
        int uvOffset = 0;
        int normalOffset = 0;

        foreach (var mesh in model.Meshes)
        {
            var geometry = mesh.Geometry;

            // Write vertices
            var positions = geometry.GetVertexPositions();
            for (int i = 0; i < positions.Length; i += 3)
            {
                sb.AppendLine(FormatVertex(
                    positions[i] * model.Scale,
                    positions[i + 1] * model.Scale,
                    positions[i + 2] * model.Scale
                ));
            }

            // Write UVs
            var uvs = geometry.GetTextureCoordinates();
            for (int i = 0; i < uvs.Length; i += 2)
            {
                float u = uvs[i];
                float v = _flipUVs ? (1.0f - uvs[i + 1]) : uvs[i + 1];
                sb.AppendLine(FormatUV(u, v));
            }

            // Write normals if available
            var normals = geometry.GetNormals();
            if (normals != null)
            {
                for (int i = 0; i < normals.Length; i += 3)
                {
                    sb.AppendLine(FormatNormal(normals[i], normals[i + 1], normals[i + 2]));
                }
            }

            // Write faces
            sb.AppendLine();
            sb.AppendLine($"g {mesh.Name ?? $"mesh_{mesh.Material.Id}"}");

            if (_exportMaterials)
            {
                sb.AppendLine($"usemtl material_{mesh.Material.Id}");
            }

            var indices = geometry.GetIndices();
            for (int i = 0; i < indices.Length; i += 3)
            {
                sb.Append("f ");
                for (int j = 0; j < 3; j++)
                {
                    int idx = (int)indices[i + j] + 1;
                    sb.Append(vertexOffset + idx);
                    sb.Append('/');
                    sb.Append(uvOffset + idx);

                    if (normals != null)
                    {
                        sb.Append('/');
                        sb.Append(normalOffset + idx);
                    }

                    if (j < 2) sb.Append(' ');
                }
                sb.AppendLine();
            }

            int vertexCount = positions.Length / 3;
            vertexOffset += vertexCount;
            uvOffset += vertexCount;
            if (normals != null)
                normalOffset += vertexCount;

            sb.AppendLine();
        }

        // Write OBJ file
        File.WriteAllText(outputPath, sb.ToString());

        // Write MTL file if requested
        if (_exportMaterials)
        {
            ExportMaterials(model, mtlPath);
        }
    }

    private void ExportMaterials(IModel model, string mtlPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Material file exported from LibLunacy");
        sb.AppendLine();

        var materials = model.Meshes.Select(m => m.Material).Distinct();

        foreach (var material in materials)
        {
            sb.AppendLine($"newmtl material_{material.Id}");

            if (material.AlbedoTexture != null)
            {
                sb.AppendLine($"map_Kd {material.AlbedoTexture.Name ?? $"texture_{material.AlbedoTexture.Id}"}.dds");
            }

            if (material.NormalTexture != null)
            {
                sb.AppendLine($"map_Bump {material.NormalTexture.Name ?? $"texture_{material.NormalTexture.Id}"}.dds");
            }

            sb.AppendLine($"illum {(int)material.RenderMode}");

            if (material.RenderMode == RenderMode.AlphaClip)
            {
                sb.AppendLine($"d {material.AlphaClipThreshold:F3}");
            }

            sb.AppendLine();
        }

        File.WriteAllText(mtlPath, sb.ToString());
    }

    private static string FormatVertex(float x, float y, float z)
    {
        return $"v {x.ToString("F6", CultureInfo.InvariantCulture)} {y.ToString("F6", CultureInfo.InvariantCulture)} {z.ToString("F6", CultureInfo.InvariantCulture)}";
    }

    private static string FormatUV(float u, float v)
    {
        return $"vt {u.ToString("F6", CultureInfo.InvariantCulture)} {v.ToString("F6", CultureInfo.InvariantCulture)}";
    }

    private static string FormatNormal(float nx, float ny, float nz)
    {
        return $"vn {nx.ToString("F6", CultureInfo.InvariantCulture)} {ny.ToString("F6", CultureInfo.InvariantCulture)} {nz.ToString("F6", CultureInfo.InvariantCulture)}";
    }
}
