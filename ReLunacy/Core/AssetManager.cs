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
        foreach (var texture in loader.Textures)
        {
            var image = new Image((int)texture.Value.Width, (int)texture.Value.Height, texture.Value.data);
            Textures[texture.Key] = new Texture2D(gd, image);
        }

        foreach (var shader in loader.Shaders)
        {
            var material = new Material(gd, ShaderManager.Shaders["solid"], BlendStateDescription.SINGLE_ALPHA_BLEND);
            material.SetMapTexture("albedo", Textures[shader.Value.metadata.albedo]);
            material.SetMapTexture("expensive", Textures[shader.Value.metadata.expensive]);
            material.SetMapTexture("normal", Textures[shader.Value.metadata.normal]);
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
