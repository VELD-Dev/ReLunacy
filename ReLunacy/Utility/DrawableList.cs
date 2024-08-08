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
    public void AddDrawCall(Transform transform)
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].AddDrawCall(transform);
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

    public void UpdateTransform(Transform transform)
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].UpdateTransform(transform);
        }
    }

}
