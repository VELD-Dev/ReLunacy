using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using LibLunacy.Objects;
using LibLunacy.Objects.Instances;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Veldrid;

namespace ReLunacy.Core.EntityManagement;

public class EntityCluster : IDisposable
{
    public static int TotalEntities { get; private set; } = 0;
    public List<Entity> Entities = [];
    public bool allowRender = true;

    public int Size => Entities.Count;

    private AssetManager AssetManager;
    private LunaLoader Loader;

    public EntityCluster(List<Entity> entities, AssetManager assetManager, LunaLoader loader)
    {
        Entities = entities;
        TotalEntities += Size;
        AssetManager = assetManager;
        Loader = loader;
    }

    public void Add(Entity entity)
    {
        TotalEntities++;
        Entities.Add(entity);
    }

    public void Add(MobyInstance mobyInstance)
    {
        TotalEntities++;
        Entities.Add(new EntityMoby(mobyInstance, AssetManager));
    }

    public void Add(Volume volume, GraphicsDevice gd)
    {
        TotalEntities++;
        Entities.Add(new EntityVolume(volume, gd));
    }

    public void Add(TieInstance tieInstance)
    {
        TotalEntities++;
        Entities.Add(new EntityTie(tieInstance, AssetManager, Loader));
    }

    public void Add(UFrag ufrag, GraphicsDevice gd)
    {
        TotalEntities++;
        Entities.Add(new EntityUFrag(gd, ufrag, AssetManager));
    }

    public bool TryGetEntity(int id, [NotNullWhen(true)] out Entity? entity)
    {
        entity = null;
        var filter = Entities.Where(e => e.ID == id);
        if (!filter.Any())
            return false;

        entity = filter.First();
        return true;
    }

    public void Draw(OutputDescription od, CommandList cl, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender)
            return;

        foreach(var e in Entities)
        {
            e.Draw(od, cl, camera, immediateRenderer);
        }
    }

    public void Dispose()
    {
        foreach(var e in Entities)
        {
            e.Dispose();
        }

        Entities.Clear();

        GC.SuppressFinalize(this);
    }
}
