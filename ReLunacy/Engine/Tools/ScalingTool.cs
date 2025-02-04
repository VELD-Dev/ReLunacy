namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public class ScalingTool : BasicTransformTool
{
    public override ToolType ToolType => ToolType.Scaling;

    public ScalingTool(Toolbox tb) : base(tb) { }

    public override void Render(Mat4 mat, Material material)
    {
        throw new NotImplementedException();
    }

    public override void Transform(Entity entity, OpenTK.Mathematics.Vector3 pivot, TransformToolData data)
    {
        float prevDist = GetLineIntersectedDist(pivot, data.axisDir, data.cameraPos, data.mousePrevDir);
        float currDist = GetLineIntersectedDist(pivot, data.axisDir, data.cameraPos, data.mouseCurrDir);

        float prevScale = MathF.Abs(prevDist);
        float currScale = Math.Abs(currDist);

        float sign = MathF.Sign(prevDist * currDist);
        float change = currScale - prevScale;

        float signX = (entity.transform.scale.X < 1 && data.axisDir.X != 0) ? sign : 1;
        float signY = (entity.transform.scale.Y < 1 && data.axisDir.Y != 0) ? sign : 1;
        float signZ = (entity.transform.scale.Z < 1 && data.axisDir.Z != 0) ? sign : 1;

        float scaleX = signX * MathF.Max(0.01f, (data.axisDir.X * change / prevScale + 1));
        float scaleY = signY * MathF.Max(0.01f, (data.axisDir.Y * change / prevScale + 1));
        float scaleZ = signZ * MathF.Max(0.01f, (data.axisDir.Z * change / prevScale + 1));

        entity.transform.scale *= new Vec3(scaleX, scaleY, scaleZ);
    }
}
