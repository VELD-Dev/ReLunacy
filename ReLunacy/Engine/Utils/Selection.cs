using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

namespace ReLunacy.Engine.Utils;

// Credits to github.com/RatchetModding/Replanetizer

public class Selection : INotifyCollectionChanged, ICollection<Entity>
{
    private readonly HashSet<Entity> OBJECTS = [];

    public Vec3 Mean
    {
        get
        {
            if(meanDirty)
            {
                mean = CalculateMean();
                meanDirty = false;
            }

            return mean;
        }
    }

    private bool meanDirty;
    private Vec3 mean;

    public Entity? NewestObject { get; private set; }
    public int Count => OBJECTS.Count;
    public bool IsReadOnly => false;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    private Vec3 CalculateMean()
    {
        var mean = new Vec3();
        int count = 0;
        foreach(var e in OBJECTS)
        {
            mean += e.Transform.Position;
            count++;
        }

        mean /= count;
        return mean;
    }

    public IEnumerator<Entity> GetEnumerator() => OBJECTS.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains(Entity e) => OBJECTS.Contains(e);

    public void CopyTo(Entity[] array, int arrayIndex)
    {
        OBJECTS.CopyTo(array, arrayIndex);
    }

    public List<Entity> ToList() => [.. OBJECTS];

    protected void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
    }

    public void SetDirty(bool dirty = true)
    {
        meanDirty = dirty;
    }

    public void Clear()
    {
        OBJECTS.Clear();
        NewestObject = null;

        SetDirty();
        OnCollectionChanged(new(NotifyCollectionChangedAction.Reset));
    }

    public void Add(Entity e)
    {
        OBJECTS.Add(e);
        NewestObject = e;

        SetDirty();
        OnCollectionChanged(new(NotifyCollectionChangedAction.Add, e));
    }

    public void Add(IEnumerable<Entity> entities)
    {
        List<Entity> newItems = [..entities];

        foreach(var e in entities)
            OBJECTS.Add(e);

        SetDirty();
        OnCollectionChanged(new(NotifyCollectionChangedAction.Add, newItems));
    }

    public void Set(Entity e)
    {
        Clear();
        Add(e);
    }

    public bool Remove(Entity e)
    {
        if (!OBJECTS.Remove(e))
            return false;
        if (ReferenceEquals(e, NewestObject))
            NewestObject = null;

        SetDirty();
        OnCollectionChanged(new(NotifyCollectionChangedAction.Remove, e));
        return true;
    }

    public void Remove(IEnumerable<Entity> entities)
    {
        List<Entity> removedItems = [..entities];
        foreach (var e in entities)
        {
            OBJECTS.Remove(e);
            if (ReferenceEquals(e, NewestObject))
                NewestObject = null;
        }

        SetDirty();
        OnCollectionChanged(new(NotifyCollectionChangedAction.Remove, removedItems));
    }

    public void Toggle(Entity e)
    {
        if (OBJECTS.Contains(e)) Remove(e);
        else Add(e);
    }

    public void ToggleOne(Entity e)
    {
        if (OBJECTS.Count == 1 && OBJECTS.Contains(e))  Clear();
        else Set(e);
    }

    public bool TryGetOne([NotNullWhen(true)] out Entity? e)
    {
        e = null;
        if (OBJECTS.Count != 1)
            return false;

        var enumerator = OBJECTS.GetEnumerator();
        enumerator.MoveNext();
        e = enumerator.Current;
        return true;
    }
}
