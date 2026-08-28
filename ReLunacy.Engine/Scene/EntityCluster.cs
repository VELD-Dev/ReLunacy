using System.Diagnostics.CodeAnalysis;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Rendering;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public class EntityCluster : IDisposable
{
    public static int TotalEntities { get; private set; }
    public List<Entity> Entities;
    public bool allowRender = true;

    public int Size => Entities.Count;

    private readonly AssetManager _assetManager;

    public EntityCluster(List<Entity> entities, AssetManager assetManager)
    {
        Entities = entities;
        TotalEntities += Size;
        _assetManager = assetManager;
    }

    public void Add(Entity entity)
    {
        TotalEntities++;
        Entities.Add(entity);
    }

    public void Add(IPlacedInstance<IMoby> mobyInstance)
    {
        TotalEntities++;
        Entities.Add(new EntityMoby(mobyInstance, _assetManager));
    }

    public void Add(IPlacedInstance<ITie> tieInstance)
    {
        TotalEntities++;
        Entities.Add(new EntityTie(tieInstance, _assetManager));
    }

    public void Add(IUFrag ufrag, GraphicsDevice gd)
    {
        TotalEntities++;
        Entities.Add(new EntityUFrag(ufrag, _assetManager));
    }

    public void Add(Volume volume, GraphicsDevice gd)
    {
        TotalEntities++;
        Entities.Add(new EntityVolume(volume));
    }

    public bool TryGetEntity(int id, [NotNullWhen(true)] out Entity? entity)
    {
        entity = Entities.FirstOrDefault(e => e.ID == id);
        return entity != null;
    }

    public void Dispose()
    {
        foreach (var e in Entities) e.Dispose();
        Entities.Clear();
        GC.SuppressFinalize(this);
    }
}
