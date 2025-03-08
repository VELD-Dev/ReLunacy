using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering;

public abstract class Renderer : IDisposable
{

    public abstract void Include(Entity entity);
    public abstract void Include(List<Entity> entities);
    public abstract void Render(RenderPayload payload);

    public abstract void Dispose();
}
