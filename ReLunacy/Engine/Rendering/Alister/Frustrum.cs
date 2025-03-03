namespace ReLunacy.Engine.Rendering.Alister;

public readonly ref struct Frustrum
{
    private readonly Plane[] planes = new Plane[6];
    
    public Frustrum(Mat4 viewToProjection)
    {
        planes[0] = new Plane(viewToProjection.Row3 + viewToProjection.Row0);  // Left
        planes[1] = new Plane(viewToProjection.Row3 - viewToProjection.Row0);  // Right
        planes[2] = new Plane(viewToProjection.Row3 + viewToProjection.Row1);  // Bottom
        planes[3] = new Plane(viewToProjection.Row3 - viewToProjection.Row1);  // Top
        planes[4] = new Plane(viewToProjection.Row3 + viewToProjection.Row2);  // Near
        planes[5] = new Plane(viewToProjection.Row3 - viewToProjection.Row2);  // Far
    }

    public bool IsInside(Vec3 point)
    {
        for (int i = 0; i < 6; i++)
        {
            if ((Vec3.Dot(planes[i].Normal, point) + planes[i].D) < -5)
            {
                return false;
            }
        }
        return true;
    }

    public bool IsInside(Vec3 center, float radius)
    {
        for (int i = 0; i < 6; i++)
        {
            if ((Vec3.Dot(planes[i].Normal, center) + planes[i].D) < -radius)
            {
                return false;
            }
        }
        return true;
    }
}
