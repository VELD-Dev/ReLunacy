namespace ReLunacy.Utility;

public class DrawableListList : List<DrawableList>
{
    public DrawableListList(CMoby moby)
    {
        Capacity = moby.bangles.Length;
        for (int i = 0; i < moby.bangles.Length; i++)
        {
            Add(new DrawableList(moby, moby.bangles[i]));
        }
    }
    public void AddDrawCall(Transform transform)
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].AddDrawCall(transform);
        }
    }
    public void AddDrawCallWireframe(Transform transform, int instanceId)
    {
        for(int i = 0; i < Count; i++)
        {
            this[i].AddDrawCallWireframe(transform, instanceId);
        }
    }
    public void RemoveDrawCallWireframe(int instanceId)
    {
        for(int i = 0; i < Count; i++)
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
    public void UpdateTransform(Transform transform, int instanceId)
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].UpdateTransform(transform, instanceId);
        }
    }
}
