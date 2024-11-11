using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public class RotationTool : BasicTransformTool
{
    public override ToolType ToolType => ToolType.Rotation;

    public RotationTool(Toolbox tb) : base(tb) { }

    public override void Transform(Entity entity, OpenTK.Mathematics.Vector3 pivot, TransformToolData data)
    {
        var transform = entity.transform;
        var rotPivot = transform.rotation;
        if(Toolbox.TransformSpace == TransformSpace.Global)
        {
            var transPivot = Matrix4.CreateTranslation(pivot);

        }
    }

    public override void Render(Matrix4 mat, Material material)
    {
        // To be rewritten
    }
}
