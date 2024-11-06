namespace ReLunacy.Utility;

public class DrawableList : List<Drawable>
{
    public DrawableList(Moby moby, ref MobyBangle bangle)
    {
        Capacity = (int)bangle.meshesCount;
        for (int i = 0; i < bangle.meshesCount; i++)
        {
            Add(new Drawable(moby, ref bangle.meshes[i]));
        }
    }
    public DrawableList(Tie tie)
    {
        Capacity = tie.MeshesCount;
        for (int i = 0; i < tie.MeshesCount; i++)
        {
            Add(new Drawable(tie, tie.Meshes[i]));
        }
    }

    public DrawableList(Zone zone)
    {
        Capacity = zone.ufrags.Length;
        foreach(var ufrag in zone.ufrags)
        {
            Add(new Drawable(ufrag));
        }
    }

    public void Draw()
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].Draw();
        }
    }

    public void DrawWireframe(Transform transform)
    {
        for(int i = 0; i < Count; ++i)
        {
            this[i].DrawWireframe(transform);
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
