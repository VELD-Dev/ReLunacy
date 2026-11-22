using System.Numerics;
using SharpGLTF.Schema2;

namespace ReLunacy.Engine.Importing;

/// <summary>What the importer reads from a glTF and how it converts it.</summary>
public sealed class GltfImportOptions
{
    /// <summary>The class name (0x3410). Null = the file name without extension.</summary>
    public string? Name;

    /// <summary>Multiplier from glTF units (metres) to game units.</summary>
    public float Scale = 1f;

    /// <summary>Flip V (v = 1 - v). Off by default: our glTF exporter writes the game's UVs as they are, so the game's V orientation is glTF's.</summary>
    public bool FlipV;

    /// <summary>Collision material word per triangle: (collision node name, glTF material name) -> word. Null = <see cref="CollisionMaterials.Default"/>.</summary>
    public Func<string, string?, uint>? CollisionMaterialWord;

    /// <summary>Name prefixes (case-insensitive) that mark a node or mesh as collision.</summary>
    public string[] CollisionPrefixes = ["COL_", "UCX_"];
}

/// <summary>One glTF material the UI maps to a shader (its slot starts with ShaderTuid 0).</summary>
public sealed class GltfMaterialInfo
{
    public int GltfIndex = -1;
    public string Name = "";
    /// <summary>Slot in <see cref="ImportModel.Slots"/>.</summary>
    public int Slot;
    public string? BaseColorTextureName;
    public string? BaseColorTextureUri;
    public Vector4 BaseColorFactor = Vector4.One;
    public string AlphaMode = "OPAQUE";
    public bool DoubleSided;
    /// <summary>The material's images (embedded bytes or the file the glTF names), null where it has none: what a new shader is
    /// made from (<see cref="NewShaderRequest.From"/>).</summary>
    public ImportImage? BaseColorImage, NormalImage, MetallicRoughnessImage, EmissiveImage;
    /// <summary>The linear emissive factor (times KHR_materials_emissive_strength); zero when the material does not glow.</summary>
    public Vector3 EmissiveFactor;
    public int PartCount;
    public int TriangleCount;
}

public sealed class GltfImportResult
{
    public ImportModel Model = new();
    /// <summary>Null when the file(s) hold no collision geometry.</summary>
    public CollisionInput? Collision;
    public List<string> Warnings = [];
    public List<GltfMaterialInfo> Materials = [];

    public int RenderTriangles, RenderVertices, CollisionTriangles, MirroredNodes, GeneratedNormals, GeneratedTangents, TriangulatedPrimitives, SkippedPrimitives;
    /// <summary>Box of the render parts in class space (zero when there are none).</summary>
    public Vector3 BoundsMin, BoundsMax;
}

/// <summary>
/// Reads a .gltf/.glb into an <see cref="ImportModel"/> (render parts, one per glTF mesh primitive) and a
/// <see cref="CollisionInput"/> (nodes or meshes named COL_ or UCX_). Node world transforms are baked in, so the scene root is
/// the class origin. No axis change: Y-up, the same as <see cref="Export.GltfExporter"/> writes, so export then import round-trips.
/// </summary>
public static class GltfTieImporter
{
    /// <summary>Imports <paramref name="path"/>; <paramref name="collisionPath"/> is an optional second file whose meshes are all collision.</summary>
    public static GltfImportResult Import(string path, GltfImportOptions? options = null, string? collisionPath = null)
    {
        options ??= new GltfImportOptions();
        var result = new GltfImportResult { Model = { Name = options.Name ?? Path.GetFileNameWithoutExtension(path) } };
        var collision = new CollisionBuffer();
        var materialSlots = new Dictionary<int, int>(); // glTF material index (-1 = none) -> slot

        Walk(Load(path), options, result, collision, materialSlots, allCollision: false);
        if (collisionPath != null)
            Walk(Load(collisionPath), options, result, collision, new Dictionary<int, int>(), allCollision: true);

        if (collision.Indices.Count > 0)
            result.Collision = new CollisionInput { Positions = [.. collision.Positions], Indices = [.. collision.Indices], TriangleMaterialWord = [.. collision.Words] };
        result.CollisionTriangles = collision.Indices.Count / 3;

        bool any = false; Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var p in result.Model.Parts)
            foreach (var v in p.Positions) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); any = true; }
        if (any) { result.BoundsMin = min; result.BoundsMax = max; }
        result.RenderVertices = result.Model.Parts.Sum(p => p.Positions.Length);
        if (result.Model.Parts.Count == 0) result.Warnings.Add("The file has no render geometry.");
        return result;
    }

    private sealed class CollisionBuffer { public List<Vector3> Positions = []; public List<int> Indices = []; public List<uint> Words = []; }

    private static ModelRoot Load(string path) => ModelRoot.Load(path, new ReadSettings { Validation = SharpGLTF.Validation.ValidationMode.TryFix });

    private static void Walk(ModelRoot root, GltfImportOptions options, GltfImportResult result, CollisionBuffer collision, Dictionary<int, int> materialSlots, bool allCollision)
    {
        var scene = root.DefaultScene ?? root.LogicalScenes.FirstOrDefault();
        if (scene == null) { result.Warnings.Add("The file has no scene."); return; }
        foreach (var node in scene.VisualChildren)
            Visit(node, options, result, collision, materialSlots, allCollision);
    }

    private static bool IsCollisionName(string? name, GltfImportOptions options) =>
        !string.IsNullOrEmpty(name) && options.CollisionPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static void Visit(Node node, GltfImportOptions options, GltfImportResult result, CollisionBuffer collision, Dictionary<int, int> materialSlots, bool inCollision)
    {
        string nodeName = node.Name ?? "";
        bool isCollision = inCollision || IsCollisionName(nodeName, options) || IsCollisionName(node.Mesh?.Name, options);
        if (node.Mesh != null)
        {
            Matrix4x4 world = node.WorldMatrix;
            bool mirrored = world.GetDeterminant() < 0;
            if (mirrored) result.MirroredNodes++;
            string label = !string.IsNullOrEmpty(nodeName) ? nodeName : node.Mesh.Name ?? $"node{node.LogicalIndex}";
            int pi = 0;
            foreach (var prim in node.Mesh.Primitives)
            {
                string partName = node.Mesh.Primitives.Count > 1 ? $"{label}.{pi}" : label;
                pi++;
                try { ReadPrimitive(prim, partName, label, world, mirrored, isCollision, options, result, collision, materialSlots); }
                catch (Exception e) { result.SkippedPrimitives++; result.Warnings.Add($"{partName}: skipped ({e.Message})"); }
            }
        }
        foreach (var child in node.VisualChildren)
            Visit(child, options, result, collision, materialSlots, isCollision);
    }

    private static int[]? Triangulate(MeshPrimitive prim, int vertexCount, string partName, GltfImportResult result, out bool triangulated)
    {
        triangulated = false;
        int[] src = prim.IndexAccessor != null ? prim.IndexAccessor.AsIndicesArray().Select(i => (int)i).ToArray() : Enumerable.Range(0, vertexCount).ToArray();
        switch (prim.DrawPrimitiveType)
        {
            case PrimitiveType.TRIANGLES:
                return src.Length / 3 * 3 == src.Length ? src : src[..(src.Length / 3 * 3)];
            case PrimitiveType.TRIANGLE_STRIP:
            {
                triangulated = true;
                var list = new List<int>();
                for (int i = 0; i + 2 < src.Length; i++)
                {
                    int a = src[i], b = src[i + 1], c = src[i + 2];
                    if (a == b || b == c || a == c) continue;
                    if ((i & 1) == 0) list.AddRange([a, b, c]); else list.AddRange([b, a, c]);
                }
                return [.. list];
            }
            case PrimitiveType.TRIANGLE_FAN:
            {
                triangulated = true;
                var list = new List<int>();
                for (int i = 1; i + 1 < src.Length; i++)
                {
                    int a = src[0], b = src[i], c = src[i + 1];
                    if (a == b || b == c || a == c) continue;
                    list.AddRange([a, b, c]);
                }
                return [.. list];
            }
            default:
                result.SkippedPrimitives++;
                result.Warnings.Add($"{partName}: {prim.DrawPrimitiveType} primitive skipped (only triangles are imported)");
                return null;
        }
    }

    private static void ReadPrimitive(MeshPrimitive prim, string partName, string nodeLabel, Matrix4x4 world, bool mirrored, bool isCollision, GltfImportOptions options,
        GltfImportResult result, CollisionBuffer collision, Dictionary<int, int> materialSlots)
    {
        var posAcc = prim.GetVertexAccessor("POSITION");
        if (posAcc == null) { result.SkippedPrimitives++; result.Warnings.Add($"{partName}: no POSITION, skipped"); return; }
        var positions = posAcc.AsVector3Array().ToArray();
        int[]? tris = Triangulate(prim, positions.Length, partName, result, out bool triangulated);
        if (tris == null) return;
        if (triangulated) result.TriangulatedPrimitives++;
        foreach (int i in tris)
            if ((uint)i >= (uint)positions.Length) throw new InvalidDataException("index out of range");

        // Bake the node transform (and the unit scale) into class space.
        Matrix4x4 m = world * Matrix4x4.CreateScale(options.Scale);
        for (int i = 0; i < positions.Length; i++) positions[i] = Vector3.Transform(positions[i], m);
        if (mirrored) // a mirror flips the winding
            for (int t = 0; t + 2 < tris.Length; t += 3) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);

        if (isCollision)
        {
            string? matName = prim.Material?.Name;
            uint word = options.CollisionMaterialWord?.Invoke(nodeLabel, matName) ?? CollisionMaterials.Default;
            int baseIndex = collision.Positions.Count;
            collision.Positions.AddRange(positions);
            foreach (int i in tris) collision.Indices.Add(baseIndex + i);
            for (int t = 0; t < tris.Length / 3; t++) collision.Words.Add(word);
            return;
        }

        // Normals: the transformed ones, or flat ones when the file has none (the glTF rule).
        var normalAcc = prim.GetVertexAccessor("NORMAL");
        Vector3[]? normals = normalAcc?.AsVector3Array().ToArray();
        var uvAcc = prim.GetVertexAccessor("TEXCOORD_0");
        Vector2[]? uv0 = uvAcc?.AsVector2Array().ToArray();
        var uv1Acc = prim.GetVertexAccessor("TEXCOORD_1");
        Vector2[]? uv1 = uv1Acc?.AsVector2Array().ToArray();
        var tanAcc = prim.GetVertexAccessor("TANGENT");
        Vector4[]? tangents = tanAcc?.AsVector4Array().ToArray();
        var colAcc = prim.GetVertexAccessor("COLOR_0");
        Vector4[]? colors = colAcc?.AsColorArray(1f).ToArray();
        if (uv0 == null) result.Warnings.Add($"{partName}: no TEXCOORD_0, UVs are zero");
        uv0 ??= new Vector2[positions.Length];

        if (normals != null)
        {
            Matrix4x4.Invert(m, out var inv);
            var normalMatrix = Matrix4x4.Transpose(inv);
            for (int i = 0; i < normals.Length; i++)
            {
                var n = Vector3.TransformNormal(normals[i], normalMatrix);
                normals[i] = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitY;
            }
        }
        if (tangents != null)
        {
            float sign = mirrored ? -1f : 1f; // B = w * cross(N, T): a mirror turns cross(N, T) over
            for (int i = 0; i < tangents.Length; i++)
            {
                var t = Vector3.TransformNormal(new Vector3(tangents[i].X, tangents[i].Y, tangents[i].Z), m);
                t = t.LengthSquared() > 1e-20f ? Vector3.Normalize(t) : Vector3.UnitX;
                tangents[i] = new Vector4(t, (tangents[i].W < 0 ? -1f : 1f) * sign);
            }
        }

        if (options.FlipV)
        {
            for (int i = 0; i < uv0.Length; i++) uv0[i].Y = 1f - uv0[i].Y;
            if (uv1 != null) for (int i = 0; i < uv1.Length; i++) uv1[i].Y = 1f - uv1[i].Y;
            if (tangents != null) for (int i = 0; i < tangents.Length; i++) tangents[i].W = -tangents[i].W;
        }

        if (normals == null)
        {
            // Flat normals need one vertex per corner.
            var np = new Vector3[tris.Length]; var nn = new Vector3[tris.Length]; var nu = new Vector2[tris.Length];
            Vector2[]? nu1 = uv1 != null ? new Vector2[tris.Length] : null; Vector4[]? nc = colors != null ? new Vector4[tris.Length] : null; Vector4[]? nt = tangents != null ? new Vector4[tris.Length] : null;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                var fn = Vector3.Cross(positions[tris[t + 1]] - positions[tris[t]], positions[tris[t + 2]] - positions[tris[t]]);
                fn = fn.LengthSquared() > 1e-20f ? Vector3.Normalize(fn) : Vector3.UnitY;
                for (int k = 0; k < 3; k++)
                {
                    int o = t + k, s = tris[o];
                    np[o] = positions[s]; nn[o] = fn; nu[o] = uv0[s];
                    if (nu1 != null) nu1[o] = uv1![s];
                    if (nc != null) nc[o] = colors![s];
                    if (nt != null) nt[o] = tangents![s];
                }
            }
            positions = np; normals = nn; uv0 = nu; uv1 = nu1; colors = nc; tangents = nt;
            tris = Enumerable.Range(0, tris.Length).ToArray();
            result.GeneratedNormals++;
        }

        if (tangents == null)
        {
            tangents = GenerateTangents(positions, normals, uv0, tris);
            result.GeneratedTangents++;
        }

        int slot = SlotFor(prim.Material, result, materialSlots);
        var info = result.Materials.First(x => x.Slot == slot);
        info.PartCount++; info.TriangleCount += tris.Length / 3;
        result.RenderTriangles += tris.Length / 3;
        result.Model.Parts.Add(new ImportPart
        {
            Name = partName, Slot = slot, Positions = positions, Normals = normals, Tangents = tangents, Uv0 = uv0, Uv1 = uv1, Colors = colors, Indices = tris,
        });
    }

    private static int SlotFor(Material? material, GltfImportResult result, Dictionary<int, int> slots)
    {
        int key = material?.LogicalIndex ?? -1;
        if (slots.TryGetValue(key, out int slot)) return slot;
        slot = result.Model.Slots.Count;
        slots[key] = slot;
        var info = new GltfMaterialInfo { GltfIndex = key, Slot = slot, Name = material == null ? "Default" : string.IsNullOrEmpty(material.Name) ? $"Material {key}" : material.Name };
        if (material != null)
        {
            info.AlphaMode = material.Alpha.ToString();
            info.DoubleSided = material.DoubleSided;
            var channel = material.FindChannel("BaseColor");
            if (channel != null)
            {
                info.BaseColorFactor = channel.Value.Parameter;
                var image = channel.Value.Texture?.PrimaryImage;
                if (image != null)
                {
                    info.BaseColorTextureName = image.Name;
                    try { info.BaseColorTextureUri = image.Content.SourcePath; } catch { }
                }
            }
            info.BaseColorImage = ImageOf(material, "BaseColor");
            info.NormalImage = ImageOf(material, "Normal");
            info.MetallicRoughnessImage = ImageOf(material, "MetallicRoughness");
            info.EmissiveImage = ImageOf(material, "Emissive");
            if (material.FindChannel("Emissive") is { } emissive)
            {
                float strength = 1f;
                foreach (var parameter in emissive.Parameters)
                {
                    if (parameter.Value is Vector3 rgb) info.EmissiveFactor = rgb;
                    else if (parameter.Value is float f && parameter.Name.Contains("Strength", StringComparison.OrdinalIgnoreCase)) strength = f;
                }
                info.EmissiveFactor *= strength;
            }
        }
        result.Materials.Add(info);
        result.Model.Slots.Add(new ImportMaterialSlot { Name = info.Name, ShaderTuid = 0 });
        return slot;
    }

    /// <summary>The image of a material channel: its bytes (PNG / JPEG, embedded or read by SharpGLTF) and source path. Null when the
    /// channel has none or the image is in a format the importer does not decode.</summary>
    private static ImportImage? ImageOf(Material material, string channelName)
    {
        var image = material.FindChannel(channelName)?.Texture?.PrimaryImage;
        if (image == null) return null;
        try
        {
            var content = image.Content;
            if (!content.IsPng && !content.IsJpg) return null;
            string? source = null;
            try { source = content.SourcePath; } catch { }
            string name = !string.IsNullOrEmpty(image.Name) ? image.Name : source != null ? Path.GetFileNameWithoutExtension(source) : $"{material.Name}_{channelName}";
            return new ImportImage { Name = name, SourcePath = string.IsNullOrEmpty(source) ? null : source, Bytes = content.Content.ToArray() };
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException) { return null; }
    }

    /// <summary>Per-vertex tangents from the UV derivatives, in the engine's convention (B = dP/dV, w = sign(dot(cross(N, T), B)); the same as
    /// the reader's GeometryMath), orthonormalised against the normal.</summary>
    public static Vector4[] GenerateTangents(Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        var tan = new Vector3[positions.Length]; var bit = new Vector3[positions.Length];
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            Vector3 e1 = positions[b] - positions[a], e2 = positions[c] - positions[a];
            Vector2 d1 = uvs[b] - uvs[a], d2 = uvs[c] - uvs[a];
            float det = d1.X * d2.Y - d2.X * d1.Y;
            if (MathF.Abs(det) < 1e-12f) continue;
            float r = 1f / det;
            Vector3 t = (d2.Y * e1 - d1.Y * e2) * r, bt = (d1.X * e2 - d2.X * e1) * r;
            tan[a] += t; tan[b] += t; tan[c] += t; bit[a] += bt; bit[b] += bt; bit[c] += bt;
        }
        var result = new Vector4[positions.Length];
        for (int v = 0; v < positions.Length; v++)
        {
            Vector3 n = normals[v];
            Vector3 t = tan[v] - n * Vector3.Dot(n, tan[v]);
            if (t.LengthSquared() < 1e-12f)
                t = MathF.Abs(n.X) < 0.9f ? Vector3.Cross(n, Vector3.UnitX) : Vector3.Cross(n, Vector3.UnitY);
            t = Vector3.Normalize(t);
            result[v] = new Vector4(t, Vector3.Dot(Vector3.Cross(n, t), bit[v]) < 0 ? -1f : 1f);
        }
        return result;
    }
}
