using Bliss.CSharp;
using Bliss.CSharp.Effects;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.VertexTypes;
using Bliss.CSharp.Images;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Textures;
using LibLunacy.Experimental.Core.Interfaces;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TinyBCSharp;
using Veldrid;
using Vortice.Mathematics;

namespace ReLunacy.Core;

public class AssetManager : IDisposable
{
    public Dictionary<ulong, Model[]> Mobys = [];
    public Dictionary<ulong, Model> Ties = [];
    public Dictionary<ulong, Texture2D> Textures = [];
    public Dictionary<ulong, Material> Materials = [];

    public AssetManager(LunaLoader loader, GraphicsDevice gd)
    {
        BlockDecoder BC1Decoder = BlockDecoder.Create(BlockFormat.BC1);
        BlockDecoder BC2Decoder = BlockDecoder.Create(BlockFormat.BC2);
        BlockDecoder BC3Decoder = BlockDecoder.Create(BlockFormat.BC3);
        foreach (var texture in loader.Textures)
        {
            byte[] realData;
            int width = (int)texture.Value.Width, height = (int)texture.Value.Height;
            switch(texture.Value.TexFormat)
            {
                case LibLunacy.Textures.TextureFormat.R5G6B5:
                    realData = TextureUtils.RGB565ToRGBA8888(texture.Value.data, width, height);
                    break;
                case LibLunacy.Textures.TextureFormat.A8R8G8B8:
                    realData = texture.Value.data; //TextureUtils.ARGB8888ToRGBA8888(texture.Value.data, width, height);
                    break;
                case LibLunacy.Textures.TextureFormat.DXT1:
                    realData = BC1Decoder.Decode(width, height, texture.Value.data);
                    break;
                case LibLunacy.Textures.TextureFormat.DXT3:
                    realData = BC2Decoder.Decode(width, height, texture.Value.data);
                    break;
                case LibLunacy.Textures.TextureFormat.DXT5:
                    realData = BC3Decoder.Decode(width, height, texture.Value.data);
                    break;
                default:
                    LunaLog.LogWarn("Unknown compression format ! Skipping texture.");
                    Textures[texture.Key] = GlobalResource.DefaultModelTexture;
                    continue;
            }

            LunaLog.LogInfo($"Texture {texture.Value.id:X}: {texture.Value.TexFormat} {width}x{height}: {width * height}/{realData.Length}");
            var image = new Image(width, height, realData);
            Textures[texture.Key] = new Texture2D(gd, image, true);
        }

        foreach (var shader in loader.Shaders)
        {
            var material = new Material(
                GlobalResource.DefaultModelEffect,
                RasterizerStateDescription.CULL_NONE,
                BlendStateDescription.SINGLE_ALPHA_BLEND,
                shader.Value.RenderingMode == LibLunacy.Shaders.RenderingMode.AlphaBlend
                    ? Bliss.CSharp.Graphics.Rendering.RenderMode.Translucent
                    : Bliss.CSharp.Graphics.Rendering.RenderMode.Cutout
            );

            if (shader.Value.metadataNew is not null)
            {
                if (shader.Value.metadataNew?.albedo != 0)
                    material.AddMaterialMap(MaterialMapType.Albedo, new MaterialMap(Textures[(ulong)shader.Value.metadataNew?.albedo]!, color: Bliss.CSharp.Colors.Color.White));
                else
                {
                    LunaLog.LogWarn($"Missing texture for material {shader.Key}: {shader.Value.metadataNew?.albedo}");
                    material.AddMaterialMap(MaterialMapType.Albedo, new MaterialMap(GlobalResource.DefaultModelTexture, color: Bliss.CSharp.Colors.Color.White));
                }
                if (shader.Value.metadataNew?.expensive != 0)
                    material.AddMaterialMap(MaterialMapType.Emission, new MaterialMap(Textures[(ulong)shader.Value.metadataNew?.expensive]));
                if (shader.Value.metadataNew?.normal != 0)
                    material.AddMaterialMap(MaterialMapType.Normal, new MaterialMap(Textures[(ulong)shader.Value.metadataNew?.normal]));
            }
            else if(shader.Value.metadataOld is not null)
            {
                if (shader.Value.metadataOld?.albedo != 0)
                    material.AddMaterialMap(MaterialMapType.Albedo, new MaterialMap(Textures[(ulong)shader.Value.metadataOld?.albedo]!, color: Bliss.CSharp.Colors.Color.White));
                else
                {
                    LunaLog.LogWarn($"Missing texture for material {shader.Key}: {shader.Value.metadataOld?.albedo}");
                    material.AddMaterialMap(MaterialMapType.Albedo, new MaterialMap(GlobalResource.DefaultModelTexture, color: Bliss.CSharp.Colors.Color.White));
                }
                if (shader.Value.metadataOld?.expensive != 0)
                    material.AddMaterialMap(MaterialMapType.Emission, new MaterialMap(Textures[(ulong)shader.Value.metadataOld?.expensive]));
                if (shader.Value.metadataOld?.normal != 0)
                    material.AddMaterialMap(MaterialMapType.Normal, new MaterialMap(Textures[(ulong)shader.Value.metadataOld?.normal]));
            }
            Materials[shader.Key] = material;
        }

        foreach(var moby in loader.Mobys)
        {
            // Get actual bangle count from experimental moby
            int bangleCount = moby.Value.Bangles.Count;
            var models = new Model[bangleCount];

            LunaLog.LogDebug($"Moby_{moby.Key:X} has {bangleCount} bangles");
            for(int i = 0; i < bangleCount; i++)
            {
                var bangle = moby.Value.Bangles[i];
                var bangleMeshes = bangle.Meshes.ToList();

                var meshes = new Mesh[bangleMeshes.Count];
                for(int j = 0; j < bangleMeshes.Count; j++)
                {
                    var iMesh = bangleMeshes[j];
                    var geometry = iMesh.Geometry;

                    // Get material - try to match shader index with loaded materials
                    Material? material = null;
                    if (iMesh.Material is LibLunacy.Experimental.Assets.Materials.Material expMat)
                    {
                        if (Materials.TryGetValue(expMat.Id, out var mat))
                            material = mat;
                    }
                    material ??= new Material(GlobalResource.DefaultModelEffect, null, BlendStateDescription.SINGLE_ALPHA_BLEND);

                    // Convert geometry to Vertex3D format
                    var vertices = ConvertGeometryToVertices(geometry, moby.Value.Scale);
                    var indices = geometry.GetIndices();

                    var mesh = new Mesh(gd, material, vertices, indices);
                    meshes[j] = mesh;
                }

                var model = new Model(gd, meshes, null /* Skeletons not ready yet */, [] /* Animations not read yet */);
                models[i] = model;
            }

            Mobys[moby.Key] = models;
        }

        foreach(var tie in loader.Ties)
        {
            var tieMeshes = tie.Value.Meshes.ToList();
            var meshes = new Mesh[tieMeshes.Count];

            for(int i = 0; i < tieMeshes.Count; i++)
            {
                var iMesh = tieMeshes[i];
                var geometry = iMesh.Geometry;

                // Get material - try to match shader index with loaded materials
                Material? material = null;
                if (iMesh.Material is LibLunacy.Experimental.Assets.Materials.Material expMat)
                {
                    if (Materials.TryGetValue(expMat.Id, out var mat))
                        material = mat;
                }
                material ??= new Material(GlobalResource.DefaultModelEffect, null, BlendStateDescription.SINGLE_ALPHA_BLEND);

                // Convert geometry to Vertex3D format (use tie scale)
                var vertices = ConvertGeometryToVertices(geometry, tie.Value.Scale);
                var indices = geometry.GetIndices();

                var mesh = new Mesh(gd, material, vertices, indices);
                meshes[i] = mesh;
            }

            var model = new Model(gd, meshes, null, [] /* Ties are static and don't have skeletons or animations */);
            Ties[tie.Key] = model;
        }
    }

    /// <summary>
    /// Converts experimental IGeometry to Bliss Vertex3D array
    /// </summary>
    private static Vertex3D[] ConvertGeometryToVertices(IGeometry geometry, float scale)
    {
        var positions = geometry.GetVertexPositions();
        var uvs = geometry.GetTextureCoordinates();
        var normals = geometry.GetNormals();

        int vertexCount = positions.Length / 3;
        var vertices = new Vertex3D[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            int posIdx = i * 3;
            int uvIdx = i * 2;

            var position = new Vector3(
                positions[posIdx] * scale,
                positions[posIdx + 1] * scale,
                positions[posIdx + 2] * scale
            );

            var uv = new Vector2(
                uvs[uvIdx],
                uvs[uvIdx + 1]
            );

            // Pack normals if available
            uint normalPacked = 0;
            if (normals != null && normals.Length >= (i * 3 + 3))
            {
                // Pack normal into uint (simplified - you may need proper packing)
                var nx = (byte)((normals[posIdx] + 1.0f) * 127.5f);
                var ny = (byte)((normals[posIdx + 1] + 1.0f) * 127.5f);
                var nz = (byte)((normals[posIdx + 2] + 1.0f) * 127.5f);
                normalPacked = (uint)(nx | (ny << 8) | (nz << 16));
            }

            vertices[i] = new Vertex3D(
                position,
                Vector4.Zero,           // weights
                UInt4.Zero,             // bones
                uv,                     // uv1
                uv,                     // uv2
                Vector3.Zero,           // normal (simplified - proper normal conversion needed)
                Vector4.Zero,           // tangent
                Vector4.Zero            // color
            );
        }

        return vertices;
    }

    public void Dispose()
    {
        foreach (var moby in Mobys.Values)
            foreach (var model in moby)
                model.Dispose();

        foreach (var model in Ties.Values)
            model.Dispose();

        foreach (var texture in Textures.Values)
            texture.Dispose();

        Mobys.Clear();
        Ties.Clear();
        Materials.Clear();
        Textures.Clear();
    }
}
