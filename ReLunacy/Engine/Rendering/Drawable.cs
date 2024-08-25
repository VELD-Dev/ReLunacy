namespace ReLunacy.Engine.Rendering;

public class Drawable
{
    public Dictionary<ulong, Transform> transforms = [];
    public Dictionary<ulong, Transform> WireframeTransforms = [];
    int VwBO;
    int VpBO;
    int VtcBO;
    int VAO;
    int EBO;
    int indexCount;
    public Material material { get; private set; }


    int wfVwBO;
    int wfVpBO;
    int wfVtcBO;
    int wfVAO;
    int wfEBO;
    public readonly Material WFMaterial = new(MaterialManager.Materials["stdv;volumef"]);

    public Drawable()
    {
        Prepare();
    }

    public Drawable(CMoby moby, CMoby.MobyMesh mesh)
    {
        Prepare();
        moby.GetBuffers(mesh, out uint[] indices, out float[] vPositions, out float[] vTexCoords);
        SetVertexPositions(vPositions);
        SetVertexTexCoords(vTexCoords);
        SetIndices(indices);
        SetMaterial(new Material(moby.shaderDB[mesh.shaderIndex]));
    }

    public Drawable(CTie tie, CTie.TieMesh mesh)
    {
        Prepare();

        tie.GetBuffers(mesh, out uint[] indices, out float[] vPositions, out float[] vTexCoords);

        SetVertexPositions(vPositions);
        SetVertexTexCoords(vTexCoords);
        SetIndices(indices);
        SetMaterial(new Material(mesh.shader));
    }

    public Drawable(CZone.UFrag mesh)
    {
        Prepare();
        SetVertexPositions(mesh.GetVertPositions());
        SetVertexTexCoords(mesh.GetUVs());
        SetIndices(mesh.GetIndices());
        //Texture? tex = (mesh.shader.albedo == null ? null : new Texture(mesh.shader.albedo));
        SetMaterial(new Material(mesh.GetShader()));
    }

    public void Prepare()
    {
        wfVAO = GL.GenVertexArray();
        wfEBO = GL.GenBuffer();
        VAO = GL.GenVertexArray();
        EBO = GL.GenBuffer();
    }

    public void SetIndices(uint[] indices)
    {
        GL.BindVertexArray(VAO);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, EBO);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

        // Wireframe set
        GL.BindVertexArray(wfVAO);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, wfEBO);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticRead);
        indexCount = indices.Length;

    }

    //It goes:
    //	0: vertex positions  (3 floats)
    //	1: vertex tex coords (2 floats)
    public void SetVertexPositions(float[] vpositions)
    {
        VpBO = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, VpBO);
        GL.BufferData(BufferTarget.ArrayBuffer, vpositions.Length * sizeof(float), vpositions, BufferUsageHint.StaticDraw);

        GL.BindVertexArray(VAO);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        wfVpBO = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, wfVpBO);
        GL.BufferData(BufferTarget.ArrayBuffer, vpositions.Length * sizeof(float), vpositions, BufferUsageHint.StaticDraw);

        GL.BindVertexArray(wfVAO);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
    }
    public void SetVertexTexCoords(float[] vtexcoords)
    {
        VtcBO = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, VtcBO);
        GL.BufferData(BufferTarget.ArrayBuffer, vtexcoords.Length * sizeof(float), vtexcoords, BufferUsageHint.StaticDraw);

        GL.BindVertexArray(VAO);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);

        wfVtcBO = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, wfVtcBO);
        GL.BufferData(BufferTarget.ArrayBuffer, vtexcoords.Length * sizeof(float), vtexcoords, BufferUsageHint.StaticDraw);

        GL.BindVertexArray(wfVAO);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
    }
    public void SetMaterial(Material mat)
    {
        material = mat;
    }

    public void AddDrawCall(Transform transform, ulong instanceId)
    {
        transforms.Add(instanceId, transform);
    }

    public void AddDrawCallWireframe(Transform transform, ulong instanceId)
    {
        WireframeTransforms.TryAdd(instanceId, transform);
        ConsolidateDrawCallsWireframe();
    }

    public void RemoveDrawCallWireframe(ulong instanceId)
    {
        WireframeTransforms.Remove(instanceId);
    }

    public void ConsolidateDrawCalls()
    {
        Matrix4[] transformMatrices = new Matrix4[transforms.Count];
        for (int i = 0; i < transformMatrices.Length; i++)
        {
            transformMatrices[i] = Matrix4.Transpose(transforms.Values.ToList()[i].GetLocalToWorldMatrix());
        }

        VwBO = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, VwBO);
        GL.BufferData(BufferTarget.ArrayBuffer, transformMatrices.Length * sizeof(float) * 16, transformMatrices, BufferUsageHint.DynamicDraw); //Note: this should be edited in the future so things can be moved

        GL.BindVertexArray(VAO);

        for (int i = 0; i < 4; i++)
        {
            GL.VertexAttribPointer(4 + i, 4, VertexAttribPointerType.Float, false, sizeof(float) * 16, sizeof(float) * 4 * i);
            GL.VertexAttribDivisor(4 + i, 1);
            GL.EnableVertexAttribArray(4 + i);
        }
    }

    public void ConsolidateDrawCallsWireframe()
    {
        Matrix4[] transformMatrices = new Matrix4[WireframeTransforms.Count];
        for(int i = 0; i < transformMatrices.Length; ++i)
        {
            transformMatrices[i] = Matrix4.Transpose(WireframeTransforms.Values.ToList()[i].GetLocalToWorldMatrix());
        }

        wfVwBO = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, wfVwBO);
        GL.BufferData(BufferTarget.ArrayBuffer, transformMatrices.Length * sizeof(float) * 16, transformMatrices, BufferUsageHint.DynamicDraw);
        
        GL.BindVertexArray(wfVAO);

        for(int i = 0; i < 4; i++)
        {
            GL.VertexAttribPointer(4 + i, 4, VertexAttribPointerType.Float, false, sizeof(float) * 16, sizeof(float) * 4 * i);
            GL.VertexAttribDivisor(4 + i, 1);
            GL.EnableVertexAttribArray(4 + i);
        }

    }

    public void Draw()
    {
        var worldToClip = Camera.Main.WorldToView * Camera.Main.ViewToClip;
        material.Use();
        material.SetMatrix4x4("worldToClip", ref worldToClip);

        GL.BindVertexArray(VAO);
        GL.DrawElementsInstanced(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, nint.Zero, transforms.Count);

        DrawWireframe();
    }

    public void DrawWireframe()
    {
        var worldToClip = Camera.Main.WorldToView * Camera.Main.ViewToClip;
        WFMaterial.SimpleUse();
        WFMaterial.SetMatrix4x4("worldToClip", ref worldToClip);

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
        GL.BindVertexArray(wfVAO);
        GL.LineWidth(3);
        GL.DrawElementsInstanced(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, nint.Zero, WireframeTransforms.Count);
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
    }

    public void DrawAsLines()
    {
        var worldToClip = Camera.Main.WorldToView * Camera.Main.ViewToClip;
        material.SimpleUse();
        material.SetMatrix4x4("worldToClip", ref worldToClip);

        //GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
        GL.BindVertexArray(VAO);
        GL.LineWidth(15);
        GL.DrawElementsInstanced(PrimitiveType.Lines, indexCount, DrawElementsType.UnsignedInt, nint.Zero, transforms.Count);
        //GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
    }

    public void Draw(Transform transform)
    {
        var world = transform.GetLocalToWorldMatrix() * Camera.Main.WorldToView * Camera.Main.ViewToClip;
        material.Use();
        material.SetMatrix4x4("world", ref world);

        GL.BindVertexArray(VAO);
        GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, nint.Zero);
    }

    public void SimpleDraw()
    {
        material.SimpleUse();
        GL.BindVertexArray(VAO);
        GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, nint.Zero);
    }

    public void UpdateTransform(Transform transform, ulong instanceId)
    {
        Matrix4[] matrix = [Matrix4.Transpose(transform.GetLocalToWorldMatrix())];
        if (transforms[instanceId] != null)
        {
            var ind = transforms.Keys.ToList().IndexOf(instanceId);
            GL.BindBuffer(BufferTarget.ArrayBuffer, VwBO);
            GL.BufferSubData(BufferTarget.ArrayBuffer, sizeof(float) * 16 * ind, sizeof(float) * 16, matrix);
        }
        if (WireframeTransforms[instanceId] != null)
        {
            var ind = WireframeTransforms.Keys.ToList().IndexOf(instanceId);
            GL.BindBuffer(BufferTarget.ArrayBuffer, wfVwBO);
            GL.BufferSubData(BufferTarget.ArrayBuffer, sizeof(float) * 16 * ind, sizeof(float) * 16, matrix);
        }
    }
}
