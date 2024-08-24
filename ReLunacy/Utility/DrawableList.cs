using ReLunacy.Engine.Rendering;

namespace ReLunacy.Utility;

public class DrawableList : List<Drawable>
{
    public DrawableList(CMoby moby, CMoby.Bangle bangle)
    {
        Capacity = (int)bangle.count;
        for (int i = 0; i < bangle.count; i++)
        {
            Add(new Drawable(moby, bangle.meshes[i]));
        }
    }
    public DrawableList(CTie tie)
    {
        Capacity = tie.meshes.Length;
        for (int i = 0; i < tie.meshes.Length; i++)
        {
            Add(new Drawable(tie, tie.meshes[i]));
        }
    }

    public DrawableList(CZone zone)
    {
        Capacity = zone.ufrags.Length;
        foreach(var ufrag in  zone.ufrags)
        {
            Add(new Drawable(ufrag));
        }
    }
    public void AddDrawCall(Transform transform, ulong instanceId)
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].AddDrawCall(transform, instanceId);
        }
    }
    public void AddDrawCallWireframe(Transform transform, ulong instanceId)
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].AddDrawCallWireframe(transform, instanceId);
        }
    }
    public void RemoveDrawCallWireframe(ulong instanceId)
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].RemoveDrawCallWireframe(instanceId);
        }
    }
    public void ConsolidateDrawCalls()
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].ConsolidateDrawCalls();
        }
    }

    public void Draw()
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].Draw();
        }
    }

    public void Draw(Transform transform)
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].Draw(transform);
        }
    }

    public void UpdateTransform(Transform transform, ulong instanceId)
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].UpdateTransform(transform, instanceId);
        }
    }

}
