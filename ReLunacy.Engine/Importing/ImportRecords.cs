using System.Text.Json.Nodes;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Projects;

namespace ReLunacy.Engine.Importing;

/// <summary>
/// Keeps what a model import was made from in the project (manifest <c>editor.importedAssets</c>, the model files and the source
/// images of new shaders under <c>&lt;project&gt;.d/imports/&lt;name&gt;/</c>), so it can be made again. An import done with no project
/// open waits until a project takes its first save. Installs nothing.
/// </summary>
public static class ImportRecords
{
    private sealed record Snapshot(string ModelPath, string CollisionPath, ModelCollisionSource Source, float Scale, bool FlipV,
        string Region, ulong Tuid, string Name, List<ImportMaterialRow> Materials, List<CollisionMaterialRow> CollisionRows,
        Dictionary<NewShaderRequest, PendingShader> NewShaders, ImportImage? OverlayMask);

    private static readonly List<Snapshot> Waiting = [];

    /// <summary>Records the import in <paramref name="project"/>, or holds it for the next <see cref="Flush"/> when there is none.</summary>
    public static void Record(LunaProject? project, ModelImportSession session)
    {
        if (session.Built == null) return;
        // A new shader is named after its request.
        var shaders = new Dictionary<NewShaderRequest, PendingShader>(ReferenceEqualityComparer.Instance);
        foreach (var request in session.Materials.SelectMany(m => new[] { m.NewShader, m.Overlay?.NewShader }).OfType<NewShaderRequest>())
        {
            string key = TieShaderFactory.SafeName(request.Name);
            if (session.BuiltShaders.FirstOrDefault(p => p.Name == $"{key}/{key}") is { } made) shaders[request] = made;
        }
        var snapshot = new Snapshot(session.ModelPath, session.CollisionPath, session.Source, session.Scale, session.FlipV,
            session.RegionName, session.Built.Tuid, session.Built.Name, [.. session.Materials], [.. session.CollisionRows], shaders, session.OverlayMask);
        if (project == null) { Waiting.Add(snapshot); return; }
        Write(project, snapshot);
    }

    public static void Flush(LunaProject? project)
    {
        if (project == null || Waiting.Count == 0) return;
        foreach (var snapshot in Waiting.ToList()) Write(project, snapshot);
        Waiting.Clear();
    }

    /// <summary>The class was removed before it was saved: its record goes too.</summary>
    public static void Forget(LunaProject? project, ulong tuid)
    {
        Waiting.RemoveAll(w => w.Tuid == tuid);
        if (project == null) return;
        lock (project.Manifest)
            if (project.Manifest.Editor["importedAssets"] is JsonArray list)
            {
                string hex = $"{tuid:X16}";
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i]?["kind"]?.ToString() == "tieClass" && list[i]?["tuid"]?.ToString() == hex) list.RemoveAt(i);
            }
        try { project.Save(); } catch (IOException) { }
    }

    private static void Write(LunaProject project, Snapshot s)
    {
        try
        {
            string safe = Safe(s.Name);
            string folder = Path.Combine(project.DataFolder, "imports", safe);
            Directory.CreateDirectory(folder);
            string model = CopyModel(s.ModelPath, folder);
            string? collision = s.Source == ModelCollisionSource.File && File.Exists(s.CollisionPath) ? CopyModel(s.CollisionPath, folder) : null;
            var entry = new JsonObject
            {
                ["kind"] = "tieClass",
                ["name"] = s.Name,
                ["tuid"] = $"{s.Tuid:X16}",
                ["region"] = s.Region,
                ["model"] = $"imports/{safe}/{model}",
                ["modelSource"] = s.ModelPath,
                ["collisionFile"] = collision == null ? null : $"imports/{safe}/{collision}",
                ["collisionSource"] = s.Source.ToString(),
                ["scale"] = s.Scale,
                ["flipV"] = s.FlipV,
                ["materials"] = new JsonArray([.. s.Materials.Select(m => (JsonNode)new JsonObject
                {
                    ["material"] = m.Info.Name,
                    ["shader"] = $"{(m.NewShader != null && s.NewShaders.TryGetValue(m.NewShader, out var made) ? made.Tuid : m.ShaderTuid):X16}",
                    ["lightmapped"] = m.Lightmapped,
                    ["overlay"] = m.Overlay is { } o ? new JsonObject
                    {
                        ["shader"] = $"{(o.NewShader != null && s.NewShaders.TryGetValue(o.NewShader, out var layer) ? layer.Tuid : o.ShaderTuid):X16}",
                        ["uvScale"] = new JsonArray(o.UvScale.X, o.UvScale.Y),
                        ["uvOffset"] = new JsonArray(o.UvOffset.X, o.UvOffset.Y),
                        ["newShader"] = o.NewShader is { } lr ? NewShaderEntry(lr, s.NewShaders.GetValueOrDefault(lr), folder, safe) : null,
                    } : null,
                    ["collisionWord"] = $"{m.CollisionWord:X8}",
                    ["newShader"] = m.NewShader is { } request ? NewShaderEntry(request, s.NewShaders.GetValueOrDefault(request), folder, safe) : null,
                })]),
                ["overlayMask"] = CopyImage(s.OverlayMask, folder, safe, "overlay_mask"),
                ["collisionMaterials"] = new JsonArray([.. s.CollisionRows.Select(c => (JsonNode)new JsonObject
                {
                    ["material"] = c.Name,
                    ["word"] = $"{c.Word:X8}",
                })]),
                ["added"] = DateTime.Now.ToString("s"),
            };
            lock (project.Manifest)
            {
                if (project.Manifest.Editor["importedAssets"] is not JsonArray list)
                    project.Manifest.Editor["importedAssets"] = list = new JsonArray();
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i]?["kind"]?.ToString() == "tieClass" && list[i]?["name"]?.ToString() == s.Name) list.RemoveAt(i);
                list.Add(entry);
            }
            project.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[Import] The import of {s.Name} could not be recorded: {e.Message}");
        }
    }

    /// <summary>Copies an image to <c>imports/&lt;name&gt;/textures/&lt;file&gt;</c>; returns its project path.</summary>
    private static string? CopyImage(ImportImage? image, string folder, string safe, string file)
    {
        if (image == null || image.Bytes.Length == 0) return null;
        string extension = image.Bytes.Length > 3 && image.Bytes[0] == 0xFF && image.Bytes[1] == 0xD8 ? ".jpg" : ".png";
        Directory.CreateDirectory(Path.Combine(folder, "textures"));
        File.WriteAllBytes(Path.Combine(folder, "textures", file + extension), image.Bytes);
        return $"imports/{safe}/textures/{file}{extension}";
    }

    /// <summary>A new shader's settings, with its source images copied to <c>imports/&lt;name&gt;/textures/</c>.</summary>
    private static JsonObject NewShaderEntry(NewShaderRequest r, PendingShader? made, string folder, string safe)
    {
        string? Copy(ImportImage? image, string slot)
        {
            if (image == null || image.Bytes.Length == 0) return null;
            string extension = image.Bytes.Length > 3 && image.Bytes[0] == 0xFF && image.Bytes[1] == 0xD8 ? ".jpg" : ".png";
            string file = $"{Safe(TieShaderFactory.SafeName(r.Name))}_{slot}{extension}";
            Directory.CreateDirectory(Path.Combine(folder, "textures"));
            File.WriteAllBytes(Path.Combine(folder, "textures", file), image.Bytes);
            return $"imports/{safe}/textures/{file}";
        }
        return new JsonObject
        {
            ["name"] = r.Name,
            ["tuid"] = made == null ? null : $"{made.Tuid:X16}",
            ["template"] = made == null ? null : $"{made.TemplateTuid:X16}",
            ["alpha"] = r.Alpha.ToString(),
            ["doubleSided"] = r.DoubleSided,
            ["flipNormalGreen"] = r.FlipNormalGreen,
            ["maxSize"] = r.MaxSize,
            ["keepFullSize"] = r.KeepFullSize,
            ["albedoFactor"] = new JsonArray(r.AlbedoFactor.X, r.AlbedoFactor.Y, r.AlbedoFactor.Z, r.AlbedoFactor.W),
            ["albedo"] = Copy(r.Albedo, "albedo"),
            ["albedoSource"] = r.Albedo?.SourcePath,
            ["normal"] = Copy(r.Normal, "normal"),
            ["normalSource"] = r.Normal?.SourcePath,
            ["class"] = r.Class,
            ["manual"] = r.Manual,
            ["expensiveSplit"] = r.ExpensiveSplit,
            ["expensiveSpecular"] = Copy(r.ExpensiveSpecular, "expensive_r"),
            ["expensiveSpecularSource"] = r.ExpensiveSpecular?.SourcePath,
            ["expensiveHeight"] = Copy(r.ExpensiveHeight, "expensive_g"),
            ["expensiveHeightSource"] = r.ExpensiveHeight?.SourcePath,
            ["expensiveEmissive"] = Copy(r.ExpensiveEmissive, "expensive_b"),
            ["expensiveEmissiveSource"] = r.ExpensiveEmissive?.SourcePath,
            ["expensiveMask"] = Copy(r.ExpensiveMask, "expensive_a"),
            ["expensiveMaskSource"] = r.ExpensiveMask?.SourcePath,
            ["detailSplit"] = r.DetailSplit,
            ["detailNormal"] = Copy(r.DetailNormal, "detail_n"),
            ["detailNormalSource"] = r.DetailNormal?.SourcePath,
            ["detailAlbedo"] = Copy(r.DetailAlbedo, "detail_c"),
            ["detailAlbedoSource"] = r.DetailAlbedo?.SourcePath,
            ["detailSpecular"] = Copy(r.DetailSpecular, "detail_s"),
            ["detailSpecularSource"] = r.DetailSpecular?.SourcePath,
            ["expensive"] = Copy(r.Expensive, "expensive"),
            ["expensiveSource"] = r.Expensive?.SourcePath,
            ["detail"] = Copy(r.Detail, "detail"),
            ["detailSource"] = r.Detail?.SourcePath,
            ["emissive"] = Copy(r.Emissive, "emissive"),
            ["emissiveSource"] = r.Emissive?.SourcePath,
            ["emissiveFactor"] = new JsonArray(r.EmissiveFactor.X, r.EmissiveFactor.Y, r.EmissiveFactor.Z),
            ["specular"] = r.Specular,
            ["detailTiling"] = r.DetailTiling,
        };
    }

    /// <summary>Copies a glTF with the files it names (buffers, images) next to it; returns the model's file name.</summary>
    private static string CopyModel(string path, string folder)
    {
        string file = Path.GetFileName(path);
        File.Copy(path, Path.Combine(folder, file), overwrite: true);
        if (path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var json = JsonNode.Parse(File.ReadAllText(path));
                string source = Path.GetDirectoryName(Path.GetFullPath(path))!;
                foreach (string key in new[] { "buffers", "images" })
                    foreach (var item in json?[key]?.AsArray() ?? [])
                        if (item?["uri"]?.ToString() is { } uri && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            string relative = Uri.UnescapeDataString(uri);
                            string from = Path.GetFullPath(Path.Combine(source, relative));
                            if (!from.StartsWith(source, StringComparison.Ordinal) || !File.Exists(from)) continue;
                            string to = Path.GetFullPath(Path.Combine(folder, relative));
                            if (!to.StartsWith(Path.GetFullPath(folder), StringComparison.Ordinal)) continue;
                            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                            File.Copy(from, to, overwrite: true);
                        }
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        }
        return file;
    }

    private static string Safe(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_'));
}
