using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public class TranslationTool : BasicTransformTool
{
    public override ToolType ToolType => ToolType.Translation;

    public TranslationTool(Toolbox tb) : base(tb) { }

    public override void Render(Mat4 mat, Material material)
    {
        Update();
        material.SimpleUse();

        material.SetMatrix4x4("modelToWorld", mat);

        material.SetInt("levelObjectNumber", 0);
        // Material.SetFloat()  // REWORK OF MATERIAL SYSTEM NEEDED
    }

    public override void Transform(Entity entity, Vec3 pivot, TransformToolData data)
    {
        var transform = entity.Transform;
        var mat = Mat4.CreateScale(transform.Scale) * Mat4.CreateTranslation(transform.Position) * Mat4.CreateFromQuaternion(transform.Rotation);
        
        if (Toolbox.TransformSpace == TransformSpace.Global)
        {
            var startDist = GetLineIntersectedDist(data.cameraPos, data.mousePrevDir, pivot, data.axisDir);
            var startPos = data.cameraPos + startDist * data.mousePrevDir;

            var finalDist = GetLineIntersectedDist(startPos, data.axisDir, data.cameraPos, data.mouseCurrDir);

            var trans = Mat4.CreateTranslation(finalDist * data.axisDir);
            mat *= trans;
        }
        else if (Toolbox.TransformSpace == TransformSpace.Local)
        {
            // Transform system has to be remade
            Vec3 aDir = (mat.Inverted() * new Vec4(data.axisDir, 0)).XYZ;

            float startDist = GetLineIntersectedDist(data.cameraPos, data.mousePrevDir, pivot, aDir);
            Vec3 startPos = data.cameraPos + startDist * data.mousePrevDir;

            float finalDist = GetLineIntersectedDist(startPos, aDir, data.cameraPos, data.mouseCurrDir);

            Mat4 trans = Mat4.CreateTranslation(finalDist * aDir);
            mat *= trans;
        }

        entity.Transform.SetMatrix(mat);
    }
}
