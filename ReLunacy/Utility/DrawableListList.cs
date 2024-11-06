namespace ReLunacy.Utility;

public class DrawableListList : List<DrawableList>
{
    public DrawableListList(Moby moby)
    {
        Capacity = (int)moby.BanglesCount;
        for (int i = 0; i < moby.BanglesCount; i++)
        {
            Add(new DrawableList(moby, ref moby.Bangles[i]));
        }
    }

    public void DrawWireframe(Transform transform)
    {
        for(int i = 0; i < Count; i++)
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
}
