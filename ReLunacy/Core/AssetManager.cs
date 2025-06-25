using Bliss.CSharp;
using Bliss.CSharp.Effects;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Images;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Textures;
using LibLunacy.Objects;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TinyBCSharp;
using Veldrid;

namespace ReLunacy.Core;

public class AssetManager : IDisposable
{
    public Dictionary<ulong, Model[]> Mobys = [];
    public Dictionary<ulong, Model> Ties = [];
    public Dictionary<ulong, Texture2D> Textures = [];
    public Dictionary<ulong, Material> Materials = [];

    public AssetManager(LunaLoader loader, GraphicsDevice gd)
    {
        BlockDecoder dxt1Decoder = BlockDecoder.Create(BlockFormat.BC1);
        BlockDecoder dxt3Decoder = BlockDecoder.Create(BlockFormat.BC3);
        BlockDecoder dxt5Decoder = BlockDecoder.Create(BlockFormat.BC5U);
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
                    realData = TextureUtils.ARGB8888ToRGBA8888(texture.Value.data, width, height);
                    break;
                case LibLunacy.Textures.TextureFormat.DXT1:
                    realData = dxt1Decoder.Decode(width, height, texture.Value.data);
                    break;
                case LibLunacy.Textures.TextureFormat.DXT3:
                    realData = dxt3Decoder.Decode(width, height, texture.Value.data);
                    break;
                case LibLunacy.Textures.TextureFormat.DXT5:
                    realData = dxt5Decoder.Decode(width, height, texture.Value.data);
                    break;
                default:
                    LunaLog.LogWarn("Unknown compression format ! Skipping texture.");
                    Textures[texture.Key] = GlobalResource.DefaultModelTexture;
                    continue;
            }

            var image = new Image(width, height, realData);
            Textures[texture.Key] = new Texture2D(gd, image);
        }

        foreach (var shader in loader.Shaders)
        {
            var material = new Material(gd, GlobalResource.DefaultModelEffect, BlendStateDescription.SINGLE_ALPHA_BLEND);
            if (shader.Value.metadata.albedo != 0)
                material.AddMaterialMap("fAlbedo", new MaterialMap(Textures[shader.Value.metadata.albedo]));
            if(shader.Value.metadata.expensive != 0)
                material.AddMaterialMap("expensive", new MaterialMap(Textures[shader.Value.metadata.expensive]));
            if(shader.Value.metadata.normal != 0)
                material.AddMaterialMap("normal", new MaterialMap(Textures[shader.Value.metadata.normal]));
            Materials[shader.Key] = material;
        }

        foreach(var moby in loader.Mobys)
        {
            var models = new Model[moby.Value.BanglesCount];

            for(int i = 0; i < moby.Value.BanglesCount; i++)
            {
                var bangle = moby.Value.Bangles[i];

                var meshes = new Mesh[bangle.meshesCount];
                for(int j = 0; j < bangle.meshesCount; j++)
                {
                    var bangleMesh = bangle.meshes[j];
                    var mesh = new Mesh(
                        gd,
                        Materials[bangleMesh.shaderIndex],
                        (bangleMesh.verticesType == 0 ? bangleMesh.vertices0.ToVert3D() : bangleMesh.vertices1.ToVert3D()),
                        [.. bangleMesh.indices.Select(n => (uint)n)]
                    );
                    meshes[j] = mesh;
                }

                var model = new Model(gd, meshes, [] /* Animations not read yet */);
                models[i] = model;
            }

            Mobys[moby.Key] = models;
        }

        foreach(var tie in loader.Ties)
        {
            var meshes = new Mesh[tie.Value.MeshesCount];

            for(int i = 0; i < tie.Value.MeshesCount; i++)
            {
                var tieMesh = tie.Value.Meshes[i];
                var mesh = new Mesh(
                    gd,
                    Materials[(tieMesh.isOld ? tieMesh.oldShaderIndex : tieMesh.newShaderIndex)],
                    tieMesh.vertices.ToVert3D(),
                    [.. tieMesh.indices.Select(n => (uint)n)]
                );
                meshes[i] = mesh;
            }

            var model = new Model(gd, meshes, [] /* Ties are static */);
            Ties[tie.Key] = model;
        }
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
