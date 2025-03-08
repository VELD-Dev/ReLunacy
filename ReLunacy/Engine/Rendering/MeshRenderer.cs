using ReLunacy.Engine.Rendering.Alister;
using ReLunacy.Engine.Rendering.Alister.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering;

// Credits to github.com/RatchetModding/Replanetizer

public class MeshRenderer : Renderer
{
    // Mobys use every lists.
    // Ties use only the first list of list of lists.
    // UFrags only use the first drawable mesh of the first list of the first list of lists.
    public Model model;
    public Entity entity;
    public List<Animation>? animations = null;

    private int IBO = 0;
    private int VBO = 0;
    private int VAO = 0;

    private bool allocatedIBO = false;
    private bool allocatedVBO = false;
    private bool allocatedVAO = false;

    private int objectId;
    private Mat4 modelToWorld = Matrix4.Identity;
    private Mat4 worldToView = Matrix4.Identity;
    private Drawable? drawable;

    private bool selected;

    private bool renderPrepared = false;
    private bool renderPerform = true;
    private bool renderPerformBillboardOnly = false;

    private float renderDistance { get; set; }
    private float blendDistance { get; set; }

    public MeshRenderer() {}

    public override void Dispose()
    {
        throw new NotImplementedException();
    }

    public override void Include(Entity entity)
    {
        model = entity.Model;
        this.entity = entity;
    }

    public override void Include(List<Entity> entities) => throw new NotImplementedException();

    private void Select(Entity selectedEntity)
    {
        selected = selectedEntity == entity;
    }

    private void Select(ICollection<Entity> selectedObjects)
    {
        if (entity == null) return;

        selected = selectedObjects.Contains(entity);
    }

    private bool ComputeCulling(Camera camera, bool distanceCulling, bool frustrumCulling)
    {
        if (model == null) return false;
        if (entity == null) return false;

        if (distanceCulling)
        {
            float dist = (camera.transform.Position - entity.Transform.Position).Length;

            blendDistance = MathF.Max(0.125f * (dist - renderDistance), 0.0f);

            if (dist > renderDistance + 8.0f)
            {
                return true;
            }
        }
        else
        {
            blendDistance = 0;
        }

        if (frustrumCulling)
        {
            Vec3 center = entity.boundingSphere.XYZ;
            float radius = entity.boundingSphere.W;

            Frustrum frustrum = camera.Frustrum;
            return !frustrum.IsInside(center, radius);
        }

        return false;
    }

    public void PrepareRender(RenderPayload payload)
    {
        if (entity == null && model == null)
        {
            renderPrepared = true;
            renderPerform = false;
            return;
        }

        if (ComputeCulling(payload.camera, payload.visibility.enableDistanceCulling, payload.visibility.enableFurstrumCulling))
        {
            renderPrepared = true;
            renderPerform = false;
            return;
        }

        renderPrepared = true;
        renderPerform = true;

        if (model.IndicesCount == 0)
        {
            renderPerformBillboardOnly = true;
            return;
        }

        worldToView = payload.camera.WorldToView;
        Select(payload.selection);

        renderPerformBillboardOnly = false;
    }

    public override void Render(RenderPayload payload)
    {
        if (renderPrepared && !renderPerform)
        {
            renderPrepared = false;
            return;
        }
        else if (!renderPrepared)
        {
            PrepareRender(payload);
            renderPrepared = true;
            if (!renderPerform)
            {
                return;
            }
        }

        renderPrepared = false;

        if (renderPerformBillboardOnly)
        {
            if (payload.visibility.billboardOnMeshlessModels)
            {
                // To be implemented.
            }
            return;
        }

        /*  // ANIMATIONS SUPPORT
        if(payload.visibility.enableAnimations && animationRenderer != null)
        {
            if(animationRenderer.IsValid())
            {
                animationRenderer.Render(payload);
                return;
            }
        }
        */

        if (model == null) return;

        model.Draw(entity.Transform, selected);
    }
}