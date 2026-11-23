using System.Globalization;
using System.Text.Json.Nodes;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Projects;

namespace ReLunacy.Engine.Importing;

public sealed record AuthoredTextureRecord(string Name, ImportTextureKind Kind);

/// <summary>Project metadata for standalone ACiT textures and the templates of standalone shaders.</summary>
public static class AuthoredMaterialRecords
{
    private const string TextureKey = "authoredTextures", ShaderKey = "authoredShaders";

    public static Dictionary<ulong, AuthoredTextureRecord> ReadTextures(LunaProject? project, string? source)
    {
        var records = new Dictionary<ulong, AuthoredTextureRecord>();
        if (project == null || string.IsNullOrEmpty(source) || project.Relative(source) is not { } level) return records;
        lock (project.Manifest)
        {
            if (project.Manifest.Editor[TextureKey] is not JsonObject levels || levels[level] is not JsonObject textures) return records;
            foreach (var (id, node) in textures)
                if (node is JsonObject texture && ulong.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong tuid) &&
                    texture["name"] is JsonValue label && label.TryGetValue<string>(out var name) &&
                    texture["kind"] is JsonValue role && role.TryGetValue<string>(out var kindName) &&
                    Enum.TryParse<ImportTextureKind>(kindName, out var kind) && (uint)kind <= 3)
                    records[tuid] = new(name, kind);
        }
        return records;
    }

    /// <summary>Template TUIDs for authored shaders without Tie primitive history of their own.</summary>
    public static Dictionary<ulong, ulong> ReadShaderTemplates(LunaProject? project, string? source)
    {
        var records = new Dictionary<ulong, ulong>();
        if (project == null || string.IsNullOrEmpty(source) || project.Relative(source) is not { } level) return records;
        lock (project.Manifest)
        {
            if (project.Manifest.Editor[ShaderKey] is not JsonObject levels || levels[level] is not JsonObject shaders) return records;
            foreach (var (id, node) in shaders)
                if (ulong.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong tuid) &&
                    node is JsonValue value && value.TryGetValue<string>(out var templateId) &&
                    ulong.TryParse(templateId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong template))
                    records[tuid] = template;
        }
        return records;
    }

    public static void Write(LunaProject project, string source, IReadOnlyList<PendingTexture> textures, IReadOnlyList<PendingShader> shaders)
    {
        if ((textures.Count == 0 && shaders.Count == 0) || project.Relative(source) is not { } level) return;
        lock (project.Manifest)
        {
            if (textures.Count > 0)
            {
                var records = ForLevel(project.Manifest.Editor, TextureKey, level);
                foreach (var texture in textures)
                    records[$"{texture.Tuid:X16}"] = new JsonObject
                    {
                        ["name"] = texture.Name,
                        ["kind"] = texture.Texture.Kind.ToString(),
                    };
            }
            if (shaders.Count > 0)
            {
                var records = ForLevel(project.Manifest.Editor, ShaderKey, level);
                foreach (var shader in shaders)
                    records[$"{shader.Tuid:X16}"] = $"{shader.TemplateTuid:X16}";
            }
        }
        project.Save();
    }

    private static JsonObject ForLevel(JsonObject editor, string key, string level)
    {
        if (editor[key] is not JsonObject levels)
            editor[key] = levels = new JsonObject();
        if (levels[level] is not JsonObject records)
            levels[level] = records = new JsonObject();
        return records;
    }
}
