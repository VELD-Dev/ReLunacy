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
    public List<List<DrawableMesh>> meshes;
    public Entity entity;

    private int IBO = 0;
    private int VBO = 0;
    private int VAO = 0;

    private bool allocatedIBO = false;
    private bool allocatedVBO = false;
    private bool allocatedVAO = false;

    private int objectId;
    private Mat4 modelToWorld = Matrix4.Identity;
    private Mat4 worldToView = Matrix4.Identity;

    private bool selected;

    private bool renderPrepared = false;
    private bool renderPerform = true;
    private bool renderPerformBillboardOnly = false;

    // TODO: Animations stuff
    private Material material;
    private Dictionary<Texture, GLTexture> textureIDs;
    private List<Texture> textures;

    public MeshRenderer(Material mat, List<Texture> tex, Dictionary<Texture, GLTexture> texIds)
    {
        material = mat;
        textureIDs = texIds;
        textures = tex;
    }

    public override void Dispose()
    {
        throw new NotImplementedException();
    }

    public override void Include(Entity entity)
    {
        throw new NotImplementedException();
    }

    public override void Include(List<Entity> entities)
    {
        throw new NotImplementedException();
    }

    public override void Render()
    {
        throw new NotImplementedException();
    }
}
