using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Textures;
using Bliss.CSharp.Transformations;
using LibLunacy.Objects;
using LibLunacy.Objects.Instances;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Bliss.CSharp.Effects;
using Veldrid;

namespace ReLunacy.Core.EntityManagement;

public enum EntityType
{
    Moby,
    Tie,
    UFrag,
    Shrub,
    Volume,
    Foliage,
}

public readonly struct MaterialOverrideState
{
    public readonly Material Material;
    public readonly Effect Effect;
    public readonly List<float> Parameters;
    public MaterialOverrideState(Material material, Effect effect, List<float> parameters)
    {
        Material = material;
        Effect = effect;
        Parameters = parameters;
    }
}

public abstract class Entity : IDisposable
{
    public static int EntityIndex = 0;
    public static int EntitiesRenderedThisFrame = 0;

    public int ID { get; protected set; }
    public bool allowRender = true;
    public bool selected = false;
    private Transform transform;
    public Transform Transform
    {
        get => transform;
        set
        {
            transform = value;
            IsDirty = true;
        }
    }
    public void SetTransform(Transform value) => Transform = value;

    public void SetRotation(Quaternion value)
    {
        transform.Rotation =  value;
        IsDirty = true;
    }

    public void SetTranslation(Vector3 value)
    {
        transform.Translation = value;
        IsDirty = true;
    }
    
    public void SetScale(Vector3 value) 
    {
        transform.Scale = value;
        IsDirty = true;
    }

    public void SetBoundingSphere(Vector4 bsphere)
    {
        BoundingSphere = bsphere;
    }
    
    public void SetBoundingSpherePosition(Vector3 position)
    {
        BoundingSphere = new Vector4(position, BoundingSphere.W);
    }
    
    public void SetBoundingSphereRadius(float radius) 
    {
        BoundingSphere = new Vector4(BoundingSphere.GetXYZ(), radius);
    }

    public abstract Vector4 BoundingSphere { get; set; }
    public abstract string Name { get; protected set; }
    private bool isDirty = true;
    public bool IsDirty
    {
        get => isDirty;
        set
        {
            isDirty = value;
        }
    }
    protected List<Renderable> cachedRenderables = [];

    public Entity()
    {
        ID = EntityIndex++;
    }

    public abstract void Draw(BasicForwardRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer);
    public abstract void DrawPicking(BasicForwardRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer, Effect pickingEffect, uint objectId, List<MaterialOverrideState> restoreList);

    public virtual void DrawBoundingSphere(OutputDescription outputDescription, CommandList commandList, ImmediateRenderer immediateRenderer)
    {
        immediateRenderer.DrawSphereWires(commandList, outputDescription, new Transform() { Translation = BoundingSphere.GetXYZ() }, BoundingSphere.W, 8, 8, Color.Cyan);
    }

    public virtual void DrawSelectionHighlight(OutputDescription outputDescription, CommandList commandList, ImmediateRenderer immediateRenderer)
    {
        immediateRenderer.DrawSphereWires(commandList, outputDescription, new Transform() { Translation = BoundingSphere.GetXYZ() }, BoundingSphere.W, 16, 16, Color.Yellow);
    }

    public virtual void Dispose() {}
}
