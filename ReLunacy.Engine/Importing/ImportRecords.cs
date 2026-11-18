using System.Text.Json.Nodes;
using ReLunacy.Engine.Projects;

namespace ReLunacy.Engine.Importing;

/// <summary>
/// Keeps what a model import was made from in the project (manifest <c>editor.importedAssets</c>, the model files under
/// <c>&lt;project&gt;.d/imports/&lt;name&gt;/</c>), so it can be made again. An import done with no project open waits until a project
/// takes its first save. Installs nothing.
/// </summary>
public static class ImportRecords
{
    private sealed record Snapshot(string ModelPath, string CollisionPath, ModelCollisionSource Source, float Scale, bool FlipV,
        string Region, ulong Tuid, string Name, List<ImportMaterialRow> Materials, List<CollisionMaterialRow> CollisionRows);

    private static readonly List<Snapshot> Waiting = [];

    /// <summary>Records the import in <paramref name="project"/>, or holds it for the next <see cref="Flush"/> when there is none.</summary>
    public static void Record(LunaProject? project, ModelImportSession session)
    {
        if (session.Built == null) return;
        var snapshot = new Snapshot(session.ModelPath, session.CollisionPath, session.Source, session.Scale, session.FlipV,
            session.Region.RegionName, session.Built.Tuid, session.Built.Name, [.. session.Materials], [.. session.CollisionRows]);
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
                    ["shader"] = $"{m.ShaderTuid:X16}",
                    ["collisionWord"] = $"{m.CollisionWord:X8}",
                })]),
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
