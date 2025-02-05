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

    public override void Transform(Entity entity, Vec3 pivot, TransformToolData data)
    {
        var mat = entity.Transform.Matrix;
        var rotPivot = Mat4.CreateFromQuaternion(entity.Transform.Rotation);
        if(Toolbox.TransformSpace == TransformSpace.Global)
        {
            var transPivot = Mat4.CreateTranslation(pivot);
            mat *= transPivot.Inverted() * rotPivot * transPivot;
        }
        else if(Toolbox.TransformSpace == TransformSpace.Local)
        {
            var transPivotOffset = Mat4.CreateTranslation(entity.Transform.Position - pivot);
            var transObj = Mat4.CreateTranslation(entity.Transform.Position);
            var rotObj = Mat4.CreateFromQuaternion(entity.Transform.Rotation);

            // complex meth
            mat *=
                // Move to origin and remove rotation
                transObj.Inverted() * rotObj.Inverted() *
                // Offset by the pivot, rotate and undo pivot offset
                transPivotOffset * rotPivot * transPivotOffset.Inverted() *
                // Add back object rotation and position
                rotObj * transObj;
        }

        entity.Transform.SetMatrix(mat);
    }

    public override void Render(Mat4 mat, Material material)
    {
        // To be rewritten
    }
}
