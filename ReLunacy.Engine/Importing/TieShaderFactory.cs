using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Importing;

/// <summary>How a new shader draws: shader 0x5000 +0x21 render mode 0 (opaque), 4 (alpha-tested, opaque pass) or 6 (sorted blend).</summary>
public enum ImportAlphaMode { Opaque, Cutout, Blend }

/// <summary>What a new shader is made from: the source images per slot and how it draws. The glTF importer fills one per
/// material (<see cref="From(GltfMaterialInfo)"/>); the UI may change any field.</summary>
public sealed class NewShaderRequest
{
    /// <summary>The shader's name; its textures are named after it.</summary>
    public string Name = "";
    /// <summary>0x5000 +0x22 (<see cref="ShaderClasses"/>); only creatable classes build.</summary>
    public byte Class = ShaderClasses.Standard;
    /// <summary>Slot 0. Null: a flat texture of <see cref="AlbedoFactor"/>.</summary>
    public ImportImage? Albedo;
    /// <summary>Slot 1, tangent space (+X right, +Y up, as the glTF exporter writes them).</summary>
    public ImportImage? Normal;
    /// <summary>Slot 2 as authored: R specular, G parallax height, B emissive, A detail mask. Written as given (DXT1, DXT5 when its
    /// alpha is used with a detail map).</summary>
    public ImportImage? Expensive;
    /// <summary>Slot 3, A8R8G8B8 read signed around 128: R / G perturb the normal, B the albedo, A the specular.</summary>
    public ImportImage? Detail;
    /// <summary>Manual: the images below are picked per channel instead of taken from the glTF material; the emissive image and
    /// colour are not used.</summary>
    public bool Manual;
    /// <summary>Manual: the expensive texture comes from the four channel images instead of the packed <see cref="Expensive"/>.</summary>
    public bool ExpensiveSplit;
    /// <summary>Manual, split expensive: grayscale R (specular), G (parallax height), B (emissive) and A (detail mask).</summary>
    public ImportImage? ExpensiveSpecular, ExpensiveHeight, ExpensiveEmissive, ExpensiveMask;
    /// <summary>Manual: the detail texture is built from the three images below instead of the packed <see cref="Detail"/>.</summary>
    public bool DetailSplit;
    /// <summary>Manual, split detail: a tangent-space normal map (RG), grayscale albedo lift (B) and specular add (A).</summary>
    public ImportImage? DetailNormal, DetailAlbedo, DetailSpecular;
    /// <summary>The expensive texture's blue channel (emissive), as linear luminance x <see cref="EmissiveFactor"/>.</summary>
    public ImportImage? Emissive;
    /// <summary>Linear emissive colour (the glTF emissive factor); its luminance alone when there is no <see cref="Emissive"/> image.</summary>
    public Vector3 EmissiveFactor = Vector3.Zero;
    /// <summary>The expensive texture's red channel when it has no image (0..1); null writes no expensive slot unless emissive.</summary>
    public float? Specular;
    /// <summary>Linear RGBA multiplier of the albedo (the glTF base colour factor).</summary>
    public Vector4 AlbedoFactor = Vector4.One;
    public ImportAlphaMode Alpha = ImportAlphaMode.Opaque;
    /// <summary>Shader +0x23 bit 0x02: no back-face culling.</summary>
    public bool DoubleSided;
    public bool FlipNormalGreen;
    /// <summary>Longest texture side, at most <see cref="TextureImporter.SafeSize"/> unless <see cref="KeepFullSize"/>.</summary>
    public int MaxSize = TextureImporter.SafeSize;
    /// <summary>Larger sources keep their size (up to 4096) instead of being scaled down to <see cref="MaxSize"/>.</summary>
    public bool KeepFullSize;
    public int EffectiveMaxSize => KeepFullSize ? 4096 : Math.Min(MaxSize, TextureImporter.SafeSize);

    public IEnumerable<ImportImage> Images => new[] { Albedo, Normal, Expensive, Detail, ExpensiveSpecular, ExpensiveHeight, ExpensiveEmissive,
        ExpensiveMask, DetailNormal, DetailAlbedo, DetailSpecular, Emissive }.OfType<ImportImage>();
    /// <summary>The longest side among the source images.</summary>
    public int LargestSource => Images.Select(i => i.LongestSide).DefaultIfEmpty(0).Max();
    /// <summary>ACiT +0x70: detail UVs = UV x this (retail 3 on 2750 of 2945); null keeps the template's.</summary>
    public float? DetailTiling;
    /// <summary>The shader to clone; 0 = <see cref="TieShaderFactory.Pick"/>.</summary>
    public ulong TemplateTuid;

    /// <summary>The request makes an emissive shader.</summary>
    public bool IsEmissive => Manual ? ExpensiveSplit && ExpensiveEmissive != null : Emissive != null || EmissiveFactor != Vector3.Zero;
    /// <summary>The request writes slot 2.</summary>
    public bool WritesExpensive => Manual && ExpensiveSplit
        ? ExpensiveSpecular != null || ExpensiveHeight != null || ExpensiveEmissive != null || ExpensiveMask != null || Specular != null
        : Expensive != null || IsEmissive || Specular != null;
    /// <summary>The request writes slot 3.</summary>
    public bool WritesDetail => Manual && DetailSplit ? DetailNormal != null || DetailAlbedo != null || DetailSpecular != null : Detail != null;
    /// <summary>Slot 2 has an image of its own (not only constants); sets the expensive sampler's emissive bit.</summary>
    internal bool ExpensiveHasImage => Manual && ExpensiveSplit
        ? ExpensiveSpecular != null || ExpensiveHeight != null || ExpensiveEmissive != null || ExpensiveMask != null
        : Expensive != null;

    /// <summary>Base colour, normal map, emissive, alpha mode and sidedness of a glTF material (metallic / roughness has no clear
    /// expensive-channel mapping and is not used).</summary>
    public static NewShaderRequest From(GltfMaterialInfo info) => new()
    {
        Name = info.Name,
        Albedo = info.BaseColorImage,
        Normal = info.NormalImage,
        Emissive = info.EmissiveImage,
        EmissiveFactor = info.EmissiveFactor,
        AlbedoFactor = info.BaseColorFactor,
        Alpha = info.AlphaMode.ToUpperInvariant() switch { "MASK" => ImportAlphaMode.Cutout, "BLEND" => ImportAlphaMode.Blend, _ => ImportAlphaMode.Opaque },
        DoubleSided = info.DoubleSided,
    };

    public NewShaderRequest Clone() => (NewShaderRequest)MemberwiseClone();
}

/// <summary>
/// A region shader that new shaders can be cloned from: Tie-proven (<see cref="TieShaderCatalog"/>), with the plain layout every
/// standard shader has (A Crack in Time, PROVEN 349/349 on great_clock_a unless noted):
///   - sections 0x5A00, 0x5000, 0x5D00, 0x11300, 0x5200 in that order (329 of 349; the others are class 0x13 with embedded
///     programs or bind no texture, and are not templates);
///   - 0x5000 (0x80): +0x00 six u32 slot indices into the local texture list (-1 unused), +0x20 feature bits (0x08 normal map,
///     0x04 specular, 0x10 detail, 0x01 parallax), +0x21 render mode, +0x22 class (0 here), +0x23 flags (0x02 double-sided);
///   - 0x5A00: per local texture {u32 texture id, u32 bytes}, bytes = the whole mip chain on a 0x80 boundary (911/911);
///   - 0x5200: per local texture the RSX descriptor {offset = the sum of the earlier 0x5A00 bytes (941/941), u16 mips, u16 format
///     (0x80 | the 0x1D140 byte, then 0x29), address, control0 (max LOD = mips - 1 at bit 15, 941/941), control1 (remap), filter,
///     u16 width, u16 height, control3}; mips, format and size equal the texture's 0x1D140 row (911/911);
///   - 0x5D00 (0x40): +0x00 the shader TUID, +0x08 name pointer, +0x10 six texture ids, +0x28 six texture name pointers into 0x11300;
///     these seven are the container's only fix-ups (349/349); unused slots point at an empty string each.
/// The texture TUID is CRC32(name) &lt;&lt; 32 | id (the high word checked on great_clock_a's names).
/// </summary>
public sealed class ShaderTemplate
{
    public required TieShaderCandidate Candidate { get; init; }
    public required byte[] Bytes { get; init; }
    /// <summary>Local texture index per slot, -1 when the slot is unused.</summary>
    public int[] LocalIndex { get; init; } = new int[6];
    /// <summary>The 0x5200 descriptor per slot (null when unused).</summary>
    public byte[]?[] Descriptors { get; init; } = new byte[6][];
    public byte Features { get; init; }
    public byte RenderMode { get; init; }
    public byte Flags { get; init; }
    /// <summary>0x5000 +0x22.</summary>
    public byte Class { get; init; }
    /// <summary>The level the template was read from when it is not the open one (null: the open level).</summary>
    public string? SourceLevel { get; init; }
    public bool Uses(int slot) => LocalIndex[slot] >= 0;
    public ulong Tuid => Candidate.Tuid;
    public ImportAlphaMode Alpha => RenderMode switch { 4 => ImportAlphaMode.Cutout, 1 or 2 or 3 or 6 => ImportAlphaMode.Blend, _ => ImportAlphaMode.Opaque };
    /// <summary>"albedo + normal + expensive" style.</summary>
    public string SlotSummary => string.Join(" + ", Enumerable.Range(0, 6).Where(Uses).Select(s => SlotNames[s]));

    internal static readonly string[] SlotNames = ["albedo", "normal", "expensive", "detail", "slot 4", "slot 5"];

    /// <summary>The template for a shader resource, or null with why it cannot be one.</summary>
    public static ShaderTemplate? Read(TieShaderCandidate candidate, byte[] bytes, out string? why, string? sourceLevel = null)
    {
        why = null;
        try
        {
            var secs = IghwRelayout.Sections(bytes);
            uint[] order = [0x5A00, 0x5000, 0x5D00, 0x11300, 0x5200];
            if (secs.Count != order.Length || secs.Select(s => s.Id).Zip(order).Any(p => p.First != p.Second))
            { why = "not the plain five-section layout"; return null; }
            var keys = secs[0]; var meta = secs[1]; var refs = secs[2]; var desc = secs[4];
            if (meta.Size != 0x80 || refs.Size != 0x40 || keys.Size % 8 != 0 || desc.Size % 0x20 != 0 || keys.Size / 8 != desc.Size / 0x20)
            { why = "unexpected section sizes"; return null; }
            int fileLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0x10));
            int fixCount = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0x14));
            var fix = Enumerable.Range(0, fixCount).Select(i => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(fileLength + 4 * i)) - refs.Offset).ToArray();
            if (!fix.SequenceEqual(new[] { 0x08, 0x28, 0x2C, 0x30, 0x34, 0x38, 0x3C })) { why = "unexpected fix-ups"; return null; }
            byte cls = bytes[meta.Offset + 0x22], flags = bytes[meta.Offset + 0x23];
            if (ShaderClasses.Find(cls) is not { Creatable: true } || (flags & 0x20) != 0) { why = $"shader class 0x{cls:X2} is not creatable"; return null; }
            int textures = keys.Size / 8;
            var local = new int[6];
            var descriptors = new byte[6][];
            for (int s = 0; s < 6; s++)
            {
                local[s] = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(meta.Offset + 4 * s));
                if (local[s] >= textures) { why = "a slot index is out of range"; return null; }
                if (local[s] >= 0) descriptors[s] = bytes.AsSpan(desc.Offset + 0x20 * local[s], 0x20).ToArray();
            }
            if (local[0] < 0) { why = "no albedo slot"; return null; }
            return new ShaderTemplate
            {
                Candidate = candidate, Bytes = bytes, LocalIndex = local, Descriptors = descriptors, SourceLevel = sourceLevel,
                Features = bytes[meta.Offset + 0x20], RenderMode = bytes[meta.Offset + 0x21], Flags = flags, Class = cls,
            };
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            why = e.Message;
            return null;
        }
    }
}

/// <summary>A shader <see cref="TieShaderFactory.Build"/> made: the pending shader with its textures, and the candidate a Tie class
/// slot is filled from (the template's values under the new TUID). Null shader when a problem stops it.</summary>
public sealed class NewShaderResult
{
    public PendingShader? Shader;
    public ShaderTemplate? Template;
    public List<string> Warnings { get; } = [];
    public List<string> Problems { get; } = [];
}

/// <summary>
/// Makes new shaders for imported Ties by cloning a standard shader (<see cref="ShaderTemplate"/>): the template keeps its program
/// choice, flags and material constants; the clone gets a fresh TUID, its own name and new textures. A slot the material fills
/// keeps the template's sampler words when the template has it, else gets the retail majority ones
/// (<see cref="ShaderClasses.DefaultSampler"/>); a slot it does not fill is unused. Feature bits follow the slots, as on every
/// retail standard shader: normal map 0x08 (parallax 0x01 only with a normal map and an expensive texture), expensive 0x04,
/// detail 0x10. CONTROL1: albedo 0xA9E4 opaque / 0xAAE4 alpha, normal 0x6AE0, expensive <see cref="ShaderClasses.ExpensiveControl1"/>,
/// detail 0xAAE4. A Crack in Time only.
/// </summary>
public static class TieShaderFactory
{
    /// <summary>Null when new shaders can be written for the level, else why not.</summary>
    public static string? Problem(LevelData? level) => level is { GameDefinition.Id: GameId.ACrackInTime }
        ? null : "new shaders can only be made for A Crack in Time levels";

    /// <summary>The catalog's shaders that can be cloned: Tie-proven, plain layout, a creatable class; unlit ones first, then the most used.</summary>
    public static List<ShaderTemplate> Templates(FileManager files, TieShaderCatalog catalog) =>
        Templates(files.ReadOriginal, catalog);

    public static List<ShaderTemplate> Templates(Func<string, byte[]?> read, TieShaderCatalog catalog, string? sourceLevel = null)
    {
        var list = new List<ShaderTemplate>();
        if (catalog.Problem != null) return list;
        byte[]? lookup = read("assetlookup.dat"), shaders = read("shaders.dat");
        if (lookup == null || shaders == null) return list;
        var s = IghwRelayout.Find(IghwRelayout.Sections(lookup), 0x1D100);
        if (s == null) return list;
        var where = new Dictionary<ulong, (int Offset, int Length)>();
        for (int i = 0; i < s.Size / 16; i++)
        {
            int o = s.Offset + 16 * i;
            where[BinaryPrimitives.ReadUInt64BigEndian(lookup.AsSpan(o))] = (BinaryPrimitives.ReadInt32BigEndian(lookup.AsSpan(o + 8)), BinaryPrimitives.ReadInt32BigEndian(lookup.AsSpan(o + 12)));
        }
        foreach (var c in catalog.Candidates)
        {
            if (!c.TieProven || !c.HasTemplate || !where.TryGetValue(c.Tuid, out var at)) continue;
            if (at.Offset < 0 || at.Length <= 0 || at.Offset + at.Length > shaders.Length) continue;
            if (ShaderTemplate.Read(c, shaders.AsSpan(at.Offset, at.Length).ToArray(), out _, sourceLevel) is { } t) list.Add(t);
        }
        return [.. list.OrderBy(t => t.Candidate.Lightmapped).ThenByDescending(t => t.Candidate.TiePrimitives)];
    }

    /// <summary>
    /// Templates from another A Crack in Time level (its level_cached.psarc). Safe to clone across levels for the standard class
    /// (INFERRED, layout): a standard shader points at nothing outside itself but its texture ids, which the clone replaces; +0x40
    /// picks one of the placement's four cubes and +0x48 (program base) is 0 on all 2945 standard shaders. The same shader TUID ships
    /// in several levels with the same record. The candidate values (feature mask, fades, density) come from that level's Ties.
    /// </summary>
    public static List<ShaderTemplate> TemplatesFromLevel(string levelCachedPsarc)
    {
        using var archive = new LibreFios.PSARC(File.OpenRead(levelCachedPsarc));
        byte[]? Read(string name)
        {
            string? path = archive.Paths.FirstOrDefault(p => p.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase));
            if (path == null) return null;
            using var buffer = archive.OpenFile(path);
            return buffer.Data.ToArray();
        }
        byte[]? lookup = Read("assetlookup.dat"), ties = Read("ties.dat"), region = Read("default/region.dat");
        if (lookup == null || ties == null || region == null) return [];
        var tieSection = IghwRelayout.Find(IghwRelayout.Sections(lookup), 0x1D300);
        if (tieSection == null) return [];
        var classes = new Dictionary<ulong, (int Offset, int Length)>();
        for (int i = 0; i < tieSection.Size / 16; i++)
        {
            int o = tieSection.Offset + 16 * i;
            classes[BinaryPrimitives.ReadUInt64BigEndian(lookup.AsSpan(o))] = (BinaryPrimitives.ReadInt32BigEndian(lookup.AsSpan(o + 8)), BinaryPrimitives.ReadInt32BigEndian(lookup.AsSpan(o + 12)));
        }
        ulong[] List(uint id)
        {
            var sec = IghwRelayout.Find(IghwRelayout.Sections(region), id);
            if (sec == null) return [];
            return [.. Enumerable.Range(0, sec.Size / 8).Select(i => BinaryPrimitives.ReadUInt64BigEndian(region.AsSpan(sec.Offset + 8 * i)))];
        }
        byte[]? shaders = Read("shaders.dat");
        var shaderSection = IghwRelayout.Find(IghwRelayout.Sections(lookup), 0x1D100);
        var labels = new Dictionary<ulong, TieShaderCatalog.ShaderLabel>();
        if (shaders != null && shaderSection != null)
            for (int i = 0; i < shaderSection.Size / 16; i++)
            {
                int o = shaderSection.Offset + 16 * i;
                ulong tuid = BinaryPrimitives.ReadUInt64BigEndian(lookup.AsSpan(o));
                int at = BinaryPrimitives.ReadInt32BigEndian(lookup.AsSpan(o + 8)), length = BinaryPrimitives.ReadInt32BigEndian(lookup.AsSpan(o + 12));
                if (at < 0 || length <= 0 || at + length > shaders.Length) continue;
                if (ShaderNames(shaders.AsSpan(at, length).ToArray()) is { } names) labels[tuid] = names;
            }
        var catalog = TieShaderCatalog.Build(classes.Keys, List(0x1C300), List(0x1C100),
            tuid => ties.AsSpan(classes[tuid].Offset, classes[tuid].Length).ToArray(), tuid => labels.TryGetValue(tuid, out var l) ? l : null);
        string level = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(levelCachedPsarc))) ?? "";
        return Templates(Read, catalog, level);
    }

    /// <summary>A shader resource's name and its six texture names (0x5D00 +0x08, +0x28..+0x3C); null when it has no 0x5D00.</summary>
    private static TieShaderCatalog.ShaderLabel? ShaderNames(byte[] shader)
    {
        try
        {
            var refs = IghwRelayout.Find(IghwRelayout.Sections(shader), 0x5D00);
            if (refs == null || refs.Size < 0x40) return null;
            string Text(int pointer)
            {
                if (pointer <= 0 || pointer >= shader.Length) return "";
                int end = Array.IndexOf(shader, (byte)0, pointer);
                return Encoding.ASCII.GetString(shader, pointer, (end < 0 ? shader.Length : end) - pointer);
            }
            var textures = new string?[6];
            for (int k = 0; k < 6; k++) textures[k] = Text(BinaryPrimitives.ReadInt32BigEndian(shader.AsSpan(refs.Offset + 0x28 + 4 * k)));
            return new TieShaderCatalog.ShaderLabel(Text(BinaryPrimitives.ReadInt32BigEndian(shader.AsSpan(refs.Offset + 8))), textures);
        }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>The template for a material: the class, then the render mode, then lit or unlit as asked (a lit template carries the
    /// lit fades), then the slots (the most filled slots it already has, then the fewest unused ones), then the most used. Null when
    /// none has the class.</summary>
    public static ShaderTemplate? Pick(IReadOnlyList<ShaderTemplate> templates, NewShaderRequest request, bool lightmapped = false)
    {
        bool[] want = [true, request.Normal != null, request.WritesExpensive, request.Detail != null, false, false];
        return templates.Where(t => t.Class == request.Class)
            .OrderBy(t => t.Alpha == request.Alpha ? 0 : 1)
            .ThenBy(t => t.Candidate.Lightmapped != lightmapped)
            .ThenBy(t => Enumerable.Range(0, 6).Count(s => want[s] && !t.Uses(s)))
            .ThenBy(t => Enumerable.Range(0, 6).Count(s => !want[s] && t.Uses(s)))
            .ThenByDescending(t => t.Candidate.TiePrimitives)
            .FirstOrDefault();
    }

    /// <summary>Encodes the request's images and clones <paramref name="template"/> into a pending shader with TUID
    /// <paramref name="shaderTuid"/>. <paramref name="takenTextureIds"/> (the level's texture ids and the pending ones) gets the new
    /// textures' ids. <paramref name="encode"/> encodes one image (null: <see cref="TextureImporter.Encode(Image, ImportTextureKind, TextureImportOptions?)"/>),
    /// so a caller can cache.</summary>
    public static NewShaderResult Build(ShaderTemplate template, NewShaderRequest request, ulong shaderTuid, ISet<uint> takenTextureIds,
        Func<ImportImage, ImportTextureKind, TextureImportOptions, EncodedTexture>? encode = null)
    {
        var result = new NewShaderResult { Template = template };
        encode ??= TextureImporter.Encode;
        string name = SafeName(request.Name);
        if (name.Length == 0) { result.Problems.Add("The shader name is empty."); return result; }
        if (ShaderClasses.Find(request.Class) is not { Creatable: true } info)
        { result.Problems.Add($"{request.Name}: shader class 0x{request.Class:X2} can not be created."); return result; }
        if (template.Class != request.Class) { result.Problems.Add($"{request.Name}: the template is not a {info.Name} shader."); return result; }
        if (template.Alpha != request.Alpha) result.Warnings.Add($"{request.Name}: no {request.Alpha.ToString().ToLowerInvariant()} template in the region; render mode changed on another one.");

        var textures = new List<(int Slot, PendingTexture Texture)>();
        try
        {
            var o = new TextureImportOptions { MaxSize = request.EffectiveMaxSize, Factor = request.AlbedoFactor, KeepAlpha = request.Alpha != ImportAlphaMode.Opaque, FlipNormalGreen = request.FlipNormalGreen };
            EncodedTexture albedo = request.Albedo != null ? encode(request.Albedo, ImportTextureKind.Albedo, o) : TextureImporter.Solid(LinearToSrgb(request.AlbedoFactor), ImportTextureKind.Albedo, o);
            textures.Add((0, Named(albedo, $"relunacy/{name}_c")));
            if (request.Normal != null) textures.Add((1, Named(encode(request.Normal, ImportTextureKind.Normal, o), $"relunacy/{name}_n")));
            if (request.WritesExpensive)
            {
                // The detail mask (A) is only kept when a detail map reads it.
                var eo = new TextureImportOptions { MaxSize = request.EffectiveMaxSize, KeepAlpha = request.WritesDetail };
                Image Compose()
                {
                    static Image? Load(ImportImage? i) => i == null ? null : new Image(i.Bytes);
                    if (request.Manual && request.ExpensiveSplit)
                        return TextureImporter.ComposeExpensiveChannels(Load(request.ExpensiveSpecular), Load(request.ExpensiveHeight),
                            Load(request.ExpensiveEmissive), Load(request.ExpensiveMask), request.Specular ?? 0f);
                    return TextureImporter.ComposeExpensive(Load(request.Expensive), request.Manual ? null : Load(request.Emissive),
                        request.Manual ? Vector3.Zero : request.EmissiveFactor, request.Specular ?? 0f);
                }
                var composed = Compose();
                textures.Add((2, Named(TextureImporter.Encode(composed, ImportTextureKind.Expensive, eo), $"relunacy/{name}_e")));
            }
            if (request.WritesDetail)
            {
                var dopts = new TextureImportOptions { MaxSize = request.EffectiveMaxSize };
                var detailTexture = request.Manual && request.DetailSplit
                    ? TextureImporter.Encode(TextureImporter.ComposeDetail(
                        request.DetailNormal is { } dn ? new Image(dn.Bytes) : null, request.DetailAlbedo is { } da ? new Image(da.Bytes) : null,
                        request.DetailSpecular is { } ds ? new Image(ds.Bytes) : null, request.FlipNormalGreen), ImportTextureKind.Detail, dopts)
                    : encode(request.Detail!, ImportTextureKind.Detail, dopts);
                textures.Add((3, Named(detailTexture, $"relunacy/{name}_d")));
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException)
        {
            result.Problems.Add($"{request.Name}: a texture could not be read ({e.Message}).");
            return result;
        }
        PendingTexture Named(EncodedTexture t, string textureName)
        {
            var copy = new EncodedTexture
            {
                Name = textureName, Kind = t.Kind, Format = t.Format, Width = t.Width, Height = t.Height, MipCount = t.MipCount,
                Chain = t.Chain, Mip0Size = t.Mip0Size, SourceRgba = t.SourceRgba,
            };
            return new PendingTexture { Tuid = ShaderRegistrar.TextureTuid(textureName, takenTextureIds), Texture = copy };
        }

        string shaderName = $"{name}/{name}";
        byte[] bytes;
        try { bytes = Clone(template, shaderTuid, shaderName, textures, request); }
        catch (InvalidDataException e) { result.Problems.Add($"{request.Name}: the template could not be cloned ({e.Message})."); return result; }

        var c = template.Candidate;
        var candidate = new TieShaderCandidate
        {
            Tuid = shaderTuid, DisplayName = name, FullName = shaderName,
            TiePrimitives = c.TiePrimitives, TieClasses = c.TieClasses, LevelPrimitives = c.LevelPrimitives,
            LightmappedPrimitives = c.LightmappedPrimitives, TwoLayer = c.TwoLayer, FeatureMask = c.FeatureMask, Fades = [.. c.Fades],
            Unanimous = c.Unanimous, UvDensity = c.UvDensity, TemplateClass = c.TemplateClass, TemplatePrimitive = c.TemplatePrimitive,
        };
        for (int s = 0; s < 6; s++) candidate.TextureNames[s] = textures.FirstOrDefault(t => t.Slot == s).Texture?.Name ?? "";
        result.Shader = new PendingShader
        {
            Tuid = shaderTuid, Name = shaderName, Bytes = bytes, Textures = [.. textures.Select(t => t.Texture)],
            TemplateTuid = template.Tuid, Candidate = candidate,
        };
        return result;
    }

    /// <summary>The template's resource with the new TUID, name, textures, slot indices, feature bits, render mode and sidedness.</summary>
    internal static byte[] Clone(ShaderTemplate template, ulong tuid, string shaderName, IReadOnlyList<(int Slot, PendingTexture Texture)> textures,
        NewShaderRequest request)
    {
        byte[] src = template.Bytes;
        var secs = IghwRelayout.Sections(src);
        var meta = IghwRelayout.Find(secs, 0x5000)!;

        // 0x5000.
        byte[] m = src.AsSpan(meta.Offset, 0x80).ToArray();
        var local = new int[6];
        Array.Fill(local, -1);
        for (int i = 0; i < textures.Count; i++) local[textures[i].Slot] = i;
        for (int s = 0; s < 6; s++) BinaryPrimitives.WriteInt32BigEndian(m.AsSpan(4 * s), local[s]);
        int features = m[0x20];
        features = local[1] >= 0 ? features | 0x08 : features & ~0x09;
        features = local[2] >= 0 ? (template.Uses(2) ? features : features | 0x04) : features & ~0x04;
        features = local[3] >= 0 ? features | 0x10 : features & ~0x10;
        if (local[1] < 0 || local[2] < 0) features &= ~0x01;
        m[0x20] = (byte)features;
        if (template.Alpha != request.Alpha) m[0x21] = request.Alpha switch { ImportAlphaMode.Cutout => 4, ImportAlphaMode.Blend => 6, _ => 0 };
        m[0x23] = request.DoubleSided ? (byte)(m[0x23] | 0x02) : (byte)(m[0x23] & ~0x02);
        if (request.DetailTiling is { } tiling) BinaryPrimitives.WriteSingleBigEndian(m.AsSpan(0x70), tiling);

        // 0x5A00 and 0x5200.
        var keys = new byte[8 * textures.Count];
        var desc = new byte[0x20 * textures.Count];
        int offset = 0;
        for (int i = 0; i < textures.Count; i++)
        {
            var (slot, pending) = textures[i];
            var t = pending.Texture;
            BinaryPrimitives.WriteUInt32BigEndian(keys.AsSpan(8 * i), pending.Id);
            BinaryPrimitives.WriteInt32BigEndian(keys.AsSpan(8 * i + 4), t.AllocationSize);
            var d = desc.AsSpan(0x20 * i, 0x20);
            if (template.Descriptors[slot] is { } own) own.CopyTo(d);
            else
            {
                var (address, _, filter) = ShaderClasses.DefaultSampler(slot);
                d[7] = 0x29;
                BinaryPrimitives.WriteUInt32BigEndian(d[8..], address);
                BinaryPrimitives.WriteUInt32BigEndian(d[12..], 0x80000000u | (slot == 1 ? 0x10u : 0u));
                BinaryPrimitives.WriteUInt32BigEndian(d[20..], filter);
                BinaryPrimitives.WriteUInt32BigEndian(d[28..], 0x00100000);
            }
            BinaryPrimitives.WriteInt32BigEndian(d, offset);
            BinaryPrimitives.WriteUInt16BigEndian(d[4..], (ushort)t.MipCount);
            d[6] = (byte)(0x80 | t.Format);
            uint control0 = BinaryPrimitives.ReadUInt32BigEndian(d[12..]);
            control0 = (control0 & ~(0xFFFu << 15)) | ((uint)(t.MipCount - 1) & 0xFFF) << 15;
            BinaryPrimitives.WriteUInt32BigEndian(d[12..], control0);
            uint control1 = slot switch
            {
                0 => request.Alpha == ImportAlphaMode.Opaque ? 0xA9E4u : 0xAAE4u,
                1 => 0x6AE0u,
                2 => ShaderClasses.ExpensiveControl1(request.IsEmissive || request.ExpensiveHasImage, t.Format == 0x08),
                3 => 0xAAE4u,
                _ => BinaryPrimitives.ReadUInt32BigEndian(d[16..]),
            };
            BinaryPrimitives.WriteUInt32BigEndian(d[16..], control1);
            BinaryPrimitives.WriteUInt16BigEndian(d[24..], (ushort)t.Width);
            BinaryPrimitives.WriteUInt16BigEndian(d[26..], (ushort)t.Height);
            offset += t.AllocationSize;
        }

        // 0x11300: the shader name, the used slots' texture names, an empty string per unused slot.
        var pool = new List<byte>();
        int Add(string s) { int at = pool.Count; pool.AddRange(Encoding.ASCII.GetBytes(s)); pool.Add(0); return at; }
        int nameAt = Add(shaderName);
        var nameAts = new int[6];
        for (int s = 0; s < 6; s++) if (local[s] >= 0) nameAts[s] = Add(textures[local[s]].Texture.Name);
        for (int s = 0; s < 6; s++) if (local[s] < 0) nameAts[s] = Add("");

        // 0x5D00.
        var r = new byte[0x40];
        BinaryPrimitives.WriteUInt64BigEndian(r, tuid);
        for (int s = 0; s < 6; s++) BinaryPrimitives.WriteUInt32BigEndian(r.AsSpan(0x10 + 4 * s), local[s] >= 0 ? textures[local[s]].Texture.Id : 0);

        // The seven old pointer fields are dropped and written again as new pointers into the new pool.
        int[] fields = [0x08, 0x28, 0x2C, 0x30, 0x34, 0x38, 0x3C];
        var pointers = new List<IghwRelayout.NewPointer> { new(0x5D00, 0x08, 0x11300, nameAt) };
        for (int s = 0; s < 6; s++) pointers.Add(new IghwRelayout.NewPointer(0x5D00, 0x28 + 4 * s, 0x11300, nameAts[s]));
        return IghwRelayout.Relayout(src, new Dictionary<uint, byte[]>
        {
            [0x5A00] = keys,
            [0x5000] = m,
            [0x5D00] = r,
            [0x11300] = [.. pool],
            [0x5200] = desc,
        }, pointers, new Dictionary<uint, Func<int, int?>>
        {
            [0x5D00] = o => fields.Any(f => o >= f && o < f + 4) ? null : o,
        });
    }

    /// <summary>Lower case letters, digits and '_' (the retail naming), at most 48 characters.</summary>
    public static string SafeName(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name.Trim().ToLowerInvariant()) sb.Append(ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : '_');
        string s = sb.ToString().Trim('_');
        return s.Length > 48 ? s[..48] : s;
    }

    private static Vector4 LinearToSrgb(Vector4 c)
    {
        static float F(float v) { v = Math.Clamp(v, 0f, 1f); return v <= 0.0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f; }
        return new Vector4(F(c.X), F(c.Y), F(c.Z), Math.Clamp(c.W, 0f, 1f));
    }
}
