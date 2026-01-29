using Bliss.CSharp;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Graphics.VertexTypes;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Transformations;
using LibLunacy.Experimental.Core.Interfaces;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Veldrid;
using Vortice.Mathematics;

namespace ReLunacy.Core.EntityManagement;

public class EntityUFrag : Entity
{
    public readonly IUFrag UFrag;
    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    public Mesh UFragMesh { get; protected set; }

    public EntityUFrag(GraphicsDevice gd, IUFrag ufrag, AssetManager assetManager) : base()
    {
        UFrag = ufrag;
        Name = !string.IsNullOrEmpty(ufrag.Name) ? $"{ufrag.Name}_{ID}" : $"UFrag_{ID}";

        // Convert experimental geometry to Vertex3D
        var vertices = ConvertUFragToVertices(ufrag);
        var indices = ufrag.GetIndices();

        // Get material
        Material? material = null;
        if (ufrag.Material is LibLunacy.Experimental.Assets.Materials.Material expMat)
        {
            if (assetManager.Materials.TryGetValue(expMat.Id, out var mat))
                material = mat;
        }
        material ??= new Material(GlobalResource.DefaultModelEffect, RasterizerStateDescription.CULL_NONE, BlendStateDescription.SINGLE_ALPHA_BLEND, Bliss.CSharp.Graphics.Rendering.RenderMode.Cutout);

        UFragMesh = new Mesh(gd, material, vertices, indices);

        // Experimental UFrags have vertices already in world space, so use identity transform
        Transform = new Transform()
        {
            Translation = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = new Vector3(1f)
        };

        var center = ufrag.GetBoundingCenter();
        var radius = ufrag.GetBoundingRadius();
        BoundingSphere = new Vector4(center, radius);
    }

    private static Vertex3D[] ConvertUFragToVertices(IUFrag ufrag)
    {
        var positions = ufrag.GetVertexPositions();
        var uvs = ufrag.GetTextureCoordinates();
        var normals = ufrag.GetNormals();

        int vertexCount = positions.Length / 3;
        var vertices = new Vertex3D[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            int posIdx = i * 3;
            int uvIdx = i * 2;

            var position = new Vector3(
                positions[posIdx],
                positions[posIdx + 1],
                positions[posIdx + 2]
            );

            var uv = new Vector2(
                uvs[uvIdx],
                uvs[uvIdx + 1]
            );

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

    public override void Draw(BasicForwardRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender || !EntityManager.Singleton.renderUFrags)
            return;

        if (Program.Settings.FrustrumCulling && !camera.GetFrustum().ContainsSphere(BoundingSphere.GetXYZ(), BoundingSphere.W))
            return;

        if (EntityManager.Singleton.renderBoundingSpheres)
            DrawBoundingSphere(outputDescription, commandList, immediateRenderer);

        if(IsDirty)
        {
            cachedRenderables.Clear();
            cachedRenderables.Add(new Renderable(UFragMesh, Transform));
            IsDirty = false;
        }

        foreach (var renderable in cachedRenderables)
        {
            renderer.DrawRenderable(renderable);
        }

        EntitiesRenderedThisFrame++;
    }

    public override void Dispose()
    {
        UFragMesh.Dispose();
        GC.SuppressFinalize(this);
    }
}
