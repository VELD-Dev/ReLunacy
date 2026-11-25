using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Importing;

/// <summary>Builds standalone level textures and standard shaders for A Crack in Time.</summary>
public static class AcitMaterialAuthoring
{
    public static HashSet<uint> ReservedTextureIds(FileManager files, IEnumerable<EntityRegion> regions)
    {
        var ids = ShaderRegistrar.TextureIds(files.ReadOriginal);
        foreach (var region in regions)
        {
            foreach (var t in region.PendingTextures) ids.Add(t.Id);
            foreach (var s in region.PendingShaders)
                foreach (var t in s.Textures) ids.Add(t.Id);
        }
        return ids;
    }

    public static PendingTexture NameTexture(EncodedTexture encoded, string name, ISet<uint> reservedIds)
    {
        string safe = TieShaderFactory.SafeName(name);
        if (safe.Length == 0) throw new ArgumentException("Enter a texture name with at least one letter or digit.");
        string suffix = encoded.Kind switch
        {
            ImportTextureKind.Albedo => "c",
            ImportTextureKind.Normal => "n",
            ImportTextureKind.Expensive => "e",
            ImportTextureKind.Detail => "d",
            _ => throw new ArgumentException("Choose a standard shader texture slot."),
        };
        string path = $"relunacy/{safe}_{suffix}";
        encoded.Name = path;
        return new PendingTexture { Tuid = ShaderRegistrar.TextureTuid(path, reservedIds), Texture = encoded };
    }

    public static NewShaderResult BuildShader(FileManager files, IReadOnlyList<EntityRegion> regions,
        IReadOnlyList<ShaderTemplate> templates, NewShaderRequest request, IReadOnlyDictionary<ImportTextureKind, ShaderTextureBinding> bindings)
    {
        var template = request.TemplateTuid == 0 ? TieShaderFactory.Pick(templates, request, bindings: bindings)
            : templates.FirstOrDefault(t => t.Tuid == request.TemplateTuid);
        if (template == null)
        {
            var result = new NewShaderResult();
            result.Problems.Add("No compatible standard Tie shader template is available in this region.");
            return result;
        }
        ulong tuid = ShaderRegistrar.FreshTuid(files.ReadOriginal,
            regions.SelectMany(r => r.PendingShaders).Select(s => s.Tuid),
            regions.SelectMany(r => r.PendingTieClasses.Select(c => c.Tuid).Concat(r.PendingTextures.Select(t => t.Tuid))));
        return TieShaderFactory.Build(template, request, tuid, ReservedTextureIds(files, regions), bindings: bindings, standalone: true);
    }
}
