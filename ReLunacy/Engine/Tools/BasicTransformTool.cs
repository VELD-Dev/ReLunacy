using Vector3 = OpenTK.Mathematics.Vector3;

namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public abstract class BasicTransformTool(Toolbox tb) : Tool(tb)
{
    public abstract void Transform(Entity entity, Vector3 pivot, TransformToolData data);

    public void Transform(Selection selection, TransformToolData data)
    {
        Vector3 pivot = selection.Mean;
        foreach(var obj in selection)
        {
            if (Toolbox.PivotPositioning == PivotPositioning.IndividualOrigins)
                pivot = obj.transform.position.ToOpenTK();
            Transform(obj, pivot, data);
        }
    }
}
