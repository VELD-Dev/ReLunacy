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

    public TranslationTool(Toolbox tb) : base(tb)
    {
        // TODO: Define the gizmo with ImGizmo
    }

    public override void Render(Matrix4 mat, Material material)
    {
        BindVAO();
        material.SimpleUse();

        material.SetMatrix4x4("modelToWorld", ref mat);

        material.SetInt("levelObjectNumber", 0);
        // material.SetFloat()  // REWORK OF MATERIAL SYSTEM NEEDED
    }

    public override void Transform(Entity entity, OpenTK.Mathematics.Vector3 pivot, TransformToolData data)
    {
        throw new NotImplementedException();
    }
}
