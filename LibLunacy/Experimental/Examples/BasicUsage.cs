using System.Numerics;
using LibLunacy.Experimental.Assets.Geometry;
using LibLunacy.Experimental.Assets.Materials;
using LibLunacy.Experimental.Assets.Textures;
using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Experimental.Core.Primitives;
using LibLunacy.Experimental.Export;
using LibLunacy.Experimental.Loading;

namespace LibLunacy.Experimental.Examples;

/// <summary>
/// Example demonstrating basic usage of the Experimental library
/// </summary>
public static class BasicUsage
{
    public static void Example()
    {
        // 1. Create a simple cube geometry
        var cubeGeometry = CreateCube();
        Console.WriteLine($"Created cube with {cubeGeometry.GetVertexPositions().Length / 3} vertices");

        // 2. Create a texture (normally loaded from file)
        var texture = CreateSampleTexture();
        Console.WriteLine($"Created {texture.Width}x{texture.Height} texture");

        // 3. Create a material using the texture
        var material = Material.Create(
            id: 1,
            albedo: texture,
            renderMode: RenderMode.Opaque
        );
        Console.WriteLine($"Created material with texture #{texture.Id}");

        // 4. Create a mesh combining geometry and material
        var mesh = new Mesh(cubeGeometry, material, "CubeMesh");

        // 5. Create a model from the mesh
        var model = Model.FromMeshes(
            id: 1,
            meshes: new[] { mesh },
            scale: 1.0f,
            name: "Cube"
        );

        var (center, radius) = model.GetBoundingSphere();
        Console.WriteLine($"Model bounding sphere: center={center}, radius={radius:F2}");

        // 6. Use AssetLibrary to manage assets
        var library = new AssetLibrary();
        library.Textures.Register(texture);
        library.Materials.Register(material);
        library.Models.Register(model);

        Console.WriteLine($"Library contains: {library.Textures.Count} textures, " +
                         $"{library.Materials.Count} materials, {library.Models.Count} models");

        // 7. Create a placed instance
        var transform = new Transform3D(
            position: new Vector3(10, 0, 5),
            rotation: new Vector3(0, MathF.PI / 4, 0),
            scale: 2.0f
        );
        var instance = new PlacedInstance<Model>(model, transform, tuid: 1, group: 0, name: "CubeInstance");

        Console.WriteLine($"Created instance at position {instance.Position}");

        // 8. Export to OBJ
        var objExporter = new ObjExporter();
        // objExporter.Export(model, "output/cube.obj");
        Console.WriteLine("Ready to export (uncomment to write files)");

        // 9. Export texture to DDS
        var ddsExporter = new DdsExporter();
        // ddsExporter.Export(texture, "output/texture.dds");
    }

    /// <summary>
    /// Creates a simple cube geometry
    /// </summary>
    private static GeometryData CreateCube()
    {
        // Simple cube vertices (8 corners, expanded to 24 for proper normals)
        float[] positions = new float[]
        {
            // Front face
            -1, -1,  1,    1, -1,  1,    1,  1,  1,   -1,  1,  1,
            // Back face
             1, -1, -1,   -1, -1, -1,   -1,  1, -1,    1,  1, -1,
            // Top face
            -1,  1,  1,    1,  1,  1,    1,  1, -1,   -1,  1, -1,
            // Bottom face
            -1, -1, -1,    1, -1, -1,    1, -1,  1,   -1, -1,  1,
            // Right face
             1, -1,  1,    1, -1, -1,    1,  1, -1,    1,  1,  1,
            // Left face
            -1, -1, -1,   -1, -1,  1,   -1,  1,  1,   -1,  1, -1
        };

        // UV coordinates
        float[] uvs = new float[]
        {
            // Repeat same UVs for each face
            0, 0,  1, 0,  1, 1,  0, 1,  // Front
            0, 0,  1, 0,  1, 1,  0, 1,  // Back
            0, 0,  1, 0,  1, 1,  0, 1,  // Top
            0, 0,  1, 0,  1, 1,  0, 1,  // Bottom
            0, 0,  1, 0,  1, 1,  0, 1,  // Right
            0, 0,  1, 0,  1, 1,  0, 1   // Left
        };

        // Indices (2 triangles per face)
        uint[] indices = new uint[]
        {
            0,  1,  2,   0,  2,  3,   // Front
            4,  5,  6,   4,  6,  7,   // Back
            8,  9, 10,   8, 10, 11,   // Top
            12, 13, 14,  12, 14, 15,  // Bottom
            16, 17, 18,  16, 18, 19,  // Right
            20, 21, 22,  20, 22, 23   // Left
        };

        return new GeometryData(
            id: 1,
            positions: positions,
            uvs: uvs,
            indices: indices,
            normals: null  // Could calculate normals here
        );
    }

    /// <summary>
    /// Creates a sample 2x2 texture
    /// </summary>
    private static Texture CreateSampleTexture()
    {
        // Create a simple 2x2 checkerboard pattern (ARGB format)
        byte[] pixels = new byte[]
        {
            // Row 0: White, Black
            255, 255, 255, 255,    0, 0, 0, 255,
            // Row 1: Black, White
            0, 0, 0, 255,          255, 255, 255, 255
        };

        return Texture.FromData(
            id: 1,
            width: 2,
            height: 2,
            format: TextureFormat.A8R8G8B8,
            data: pixels
        );
    }
}
