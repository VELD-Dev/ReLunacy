using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward.Renderables;
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
        protected set
        {
            transform = value;
            IsDirty = true;
        }
    }
    public abstract Vector4 BoundingSphere { get; set; }
    public abstract string Name { get; protected set; }
    private bool isDirty = false;
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

    public abstract void Draw(ForwardRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer);

    public virtual void DrawBoundingSphere(OutputDescription outputDescription, CommandList commandList, ImmediateRenderer immediateRenderer)
    {
        immediateRenderer.DrawSphereWires(commandList, outputDescription, new Transform() { Translation = BoundingSphere.GetXYZ() }, BoundingSphere.W, 8, 8, Color.Cyan);
    }

    public virtual void Dispose() {}
}
