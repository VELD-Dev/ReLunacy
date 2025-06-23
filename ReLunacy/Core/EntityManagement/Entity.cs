using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Textures;
using Bliss.CSharp.Transformations;
using LibLunacy.Objects;
using LibLunacy.Objects.Instances;
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

    public int ID { get; protected set; }
    public bool allowRender;
    public bool selected;
    public abstract Transform Transform { get; protected set; }
    public abstract Vector4 BoundingSphere { get; }
    public abstract string Name { get; protected set; }

    public Entity()
    {
        ID = EntityIndex++;
    }

    public abstract void Draw(OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer);

    public virtual void Dispose() {}
}
