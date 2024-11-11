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

    public override void Render(Matrix4 mat, Material material)
    {
        Update();
        material.SimpleUse();

        material.SetMatrix4x4("modelToWorld", ref mat);

        material.SetInt("levelObjectNumber", 0);
        // material.SetFloat()  // REWORK OF MATERIAL SYSTEM NEEDED
    }

    public override void Transform(Entity entity, OpenTK.Mathematics.Vector3 pivot, TransformToolData data)
    {
        var transform = entity.transform;
        var matrix = Matrix4.CreateTranslation(transform.position.ToOpenTK()) * Matrix4.CreateFromQuaternion(transform.rotation) * Matrix4.CreateScale(transform.scale.ToOpenTK()); ;

        if (Toolbox.TransformSpace == TransformSpace.Global)
        {
            var startDist = getLineIntersectedDist(data.cameraPos, data.mousePrevDir, pivot, data.axisDir);
            var startPos = data.cameraPos + startDist * data.mousePrevDir;

            var finalDist = getLineIntersectedDist(startPos, data.axisDir, data.cameraPos, data.mouseCurrDir);

            var trans = finalDist * data.axisDir;
            transform.position *= trans.ToNumerics();
        }
        else if (Toolbox.TransformSpace == TransformSpace.Local)
        {
            // Transform system has to be remade
        }
    }
}
