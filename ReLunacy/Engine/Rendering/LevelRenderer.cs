using LibLunacy.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering;

public class LevelRenderer : Renderer
{
    public readonly static Color4 ClearColour = new(0x02, 0x02, 0x02, 0xFF);

    // Lights list

    private List<MeshRenderer> mobys = [];
    private List<MeshRenderer> ties = [];
    private List<MeshRenderer> ufrags = [];
    private List<MeshRenderer> shrubs = [];
    // volumes renderer
    // splines renderer
    // For tool renderer, I'll use ImGuizmos... i'll try
    // Billboard renderers

    public LevelRenderer()
    {
        // Tool renderer stuff
    }

    private void Add(List<Entity> entities)
    {
        foreach(var e in entities)
        {
            var meshRenderer = new MeshRenderer();
            meshRenderer.Include(e);
            switch (e)
            {
                case MobyObject moby:
                    mobys.Add(meshRenderer);
                    break;
                case TieObject tie:
                    ties.Add(meshRenderer);
                    break;
                case UFragObject ufrag:
                    ufrags.Add(meshRenderer);
                    break;
                case VolumeObject volume:
                    // Add to volume wireframe renderers
                    break;
            }

        }
    }

    private void Add<T>(T entity) where T : Entity
    {
        var meshRenderer = new MeshRenderer();
        meshRenderer.Include(entity);
        switch (entity)
        {
            case MobyObject moby:
                mobys.Add(meshRenderer);
                break;
            case TieObject tie:
                ties.Add(meshRenderer);
                break;
            case UFragObject ufrag:
                ufrags.Add(meshRenderer);
                break;
            case VolumeObject volume:
                // Add to volume wireframe renderers
                break;
        }
    }

    public override void Include(Entity entity)
    {
        Add(entity);
    }

    public override void Include(List<Entity> entities)
    {
        Add(entities);
    }

    public override void Render(RenderPayload payload)
    {
        // Implement eventual level variables with fog color etc...
        GL.ClearColor(ClearColour);

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.DepthFunc(DepthFunction.Lequal);

        if (payload.visibility.renderSkybox)
        {
            // Render skybox
        }

        if (payload.visibility.renderMobys)
        {
            foreach (MeshRenderer mr in mobys)
            {
                mr.Render(payload);
            }
        }

        if (payload.visibility.renderTies)
        {
            foreach (MeshRenderer mr in ties)
            {
                mr.Render(payload);
            }
        }

        if(payload.visibility.renderUFrags)
        {
            foreach(MeshRenderer mr in ufrags)
            {
                mr.Render(payload);
            }
        }

        if(payload.visibility.renderVolumes)
        {
            // Render volumes
        }
    }

    public override void Dispose()
    {

    }
}
