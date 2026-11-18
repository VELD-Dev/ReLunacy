using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Packing;

namespace ReLunacy.Engine.Importing;

/// <summary>One shader an imported Tie can use: what the picker shows and the per-shader values the class writer
/// copies (docs/reverse/newengine_tie_mesh_splitting.md 1.6, 3.2).</summary>
public sealed class TieShaderCandidate
{
    public ulong Tuid;
    /// <summary>The shader's own name without its folders, or "Shader 0x..." when the level gives none.</summary>
    public string DisplayName = "";
    /// <summary>The full shader name as stored (the engine's name is cut at the start).</summary>
    public string FullName = "";
    /// <summary>Source texture names by slot (albedo, normal, expensive, detail, 4, 5); empty where the slot is unused.</summary>
    public string[] TextureNames = new string[6];
    public string AlbedoName => TextureNames[0];
    /// <summary>Used by at least one Tie primitive of the region's Ties: known to work on a Tie.</summary>
    public bool TieProven => TiePrimitives > 0;
    /// <summary>Tie primitives of the region's Ties that use the shader.</summary>
    public int TiePrimitives;
    /// <summary>Tie classes of the region that use it.</summary>
    public int TieClasses;
    /// <summary>Tie primitives of the whole level that use it (the majority values come from these when the region has none).</summary>
    public int LevelPrimitives;
    /// <summary>How many of the counted primitives are lightmapped (gate bit 0).</summary>
    public int LightmappedPrimitives;
    /// <summary>Most of its primitives are lightmapped: the shader is meant for baked geometry.</summary>
    public bool Lightmapped => LightmappedPrimitives * 2 > Math.Max(1, TiePrimitives > 0 ? TiePrimitives : LevelPrimitives);
    /// <summary>Most of its primitives are two-layer (blend mask or second slot): the importer does not write those.</summary>
    public bool TwoLayer;
    /// <summary>Primitive +0x24: the majority value of the shader.</summary>
    public byte FeatureMask = 0x1D;
    /// <summary>Primitive +0x30..+0x36: the majority value of the shader.</summary>
    public short[] Fades = [35, 120, 8, 1];
    /// <summary>The majority feature mask and fades are the only values seen on every counted primitive.</summary>
    public bool Unanimous;
    /// <summary>The template primitive's 0x3500 densities (UV units per world unit).</summary>
    public Vector2? UvDensity;
    /// <summary>A primitive that carries the majority values (unlit and single layer preferred); 0 / -1 when the shader is used by no Tie.</summary>
    public ulong TemplateClass;
    public int TemplatePrimitive = -1;
    public bool HasTemplate => TemplatePrimitive >= 0;

    /// <summary>The slot for <see cref="ImportModel.Slots"/>.</summary>
    public ImportMaterialSlot ToSlot(string? name = null) => new()
    {
        Name = name ?? DisplayName,
        ShaderTuid = Tuid,
        FeatureMask = FeatureMask,
        Fades = [.. Fades],
        UvDensity = UvDensity,
    };
}

/// <summary>The class whose header words a new import copies (0x3400 +0x0E, +0x1C, +0x74, +0x78 and the section set).</summary>
public sealed class TieTemplateChoice
{
    public ulong ClassTuid;
    public byte[] Bytes = [];
    /// <summary>For logs: how the class was chosen.</summary>
    public string Reason = "";
}

public sealed class TieShaderMatch
{
    public required TieShaderCandidate Candidate;
    /// <summary>0..1; 1 = exact, 0.6 and up is accepted by <see cref="TieShaderCatalog.AutoMatch"/>.</summary>
    public double Score;
    public string Via = "";
}

/// <summary>
/// The shaders an imported Tie can use in a region, with the per-shader primitive values the class writer copies, the
/// template class, and glTF material matching. Built from the region's 0x1C100 shader list and the Tie classes' own
/// primitives (A Crack in Time layout: slot +0x2A, feature mask +0x24).
/// </summary>
public sealed class TieShaderCatalog
{
    public const double AcceptScore = 0.6;

    /// <summary>Tie-proven shaders first (most used first), then the rest by name.</summary>
    public IReadOnlyList<TieShaderCandidate> Candidates { get; private init; } = [];
    public TieTemplateChoice? Template { get; private init; }
    /// <summary>Set when nothing could be built (not a new-engine level, no Ties).</summary>
    public string? Problem { get; private init; }
    /// <summary>Tie primitives scanned in the region's Ties and in the whole level.</summary>
    public int RegionPrimitives { get; private init; }
    public int LevelPrimitives { get; private init; }

    private Dictionary<ulong, TieShaderCandidate> _byTuid = [];
    private List<(TieShaderCandidate C, string Key, string Kind)> _keys = [];

    public TieShaderCandidate? Find(ulong tuid) => _byTuid.GetValueOrDefault(tuid);

    // ---- loading -----------------------------------------------------------------------------------------------------

    /// <summary>Builds the catalog of <paramref name="regionName"/> (region.dat 0x1C100 / 0x1C300) from the loaded level's
    /// files; shader and texture names come from <paramref name="level"/>.</summary>
    public static TieShaderCatalog Build(FileManager files, LevelData level, string regionName)
    {
        if (files.isOld) return new TieShaderCatalog { Problem = "the old engine has no shader lists to pick from" };
        var region = files.GetOrLoadIGFile($"{regionName}/region.dat");
        if (region == null) return new TieShaderCatalog { Problem = $"{regionName}/region.dat not found" };
        var shaders = ReadTuids(region, 0x1C100);
        var ties = ReadTuids(region, 0x1C300);
        var pointers = NewEngineAssetLookup.ReadPointers(files, NewEngineAssetLookup.TiePointers, "Ties");
        if (pointers.Length == 0 || !NewEngineAssetLookup.TryGetPayloadStream(files, "ties.dat", "Ties", out Stream? stream) || stream == null)
            return new TieShaderCatalog { Problem = "the level has no Tie classes" };
        var byTuid = pointers.ToDictionary(p => p.TUID);
        byte[] Read(ulong tuid) => NewEngineAssetLookup.ReadPayload(stream, byTuid[tuid], "Tie");
        return Build(pointers.Select(p => p.TUID), ties, shaders, Read, tuid => level.Shaders.TryGetValue(tuid, out var s)
            ? new ShaderLabel(s.name, s.NewTextureSourceNames) : null);
    }

    /// <summary>Names a loaded shader gives.</summary>
    public readonly record struct ShaderLabel(string? Name, IReadOnlyList<string?> Textures);

    /// <param name="levelClasses">Every Tie class of the level.</param>
    /// <param name="regionClasses">The region's Tie classes (0x1C300).</param>
    /// <param name="regionShaders">The region's shaders (0x1C100).</param>
    /// <param name="read">The class container bytes.</param>
    public static TieShaderCatalog Build(IEnumerable<ulong> levelClasses, IReadOnlyCollection<ulong> regionClasses, IReadOnlyCollection<ulong> regionShaders,
        Func<ulong, byte[]> read, Func<ulong, ShaderLabel?> label)
    {
        var inRegion = regionClasses.ToHashSet();
        var acc = new Dictionary<ulong, Acc>();
        var templates = new List<(ulong Tuid, uint Word78, int Length)>();
        int regionPrims = 0, levelPrims = 0;
        foreach (ulong tuid in levelClasses)
        {
            byte[] bytes = read(tuid);
            var info = ScanClass(bytes);
            if (info == null) continue;
            bool region = inRegion.Contains(tuid);
            foreach (var p in info.Primitives)
            {
                if (p.Slot >= info.Shaders.Length) continue;
                ulong shader = info.Shaders[p.Slot];
                if (!acc.TryGetValue(shader, out var a)) acc[shader] = a = new Acc();
                a.Add(tuid, p, info.Densities.GetValueOrDefault(p.Slot), region);
                levelPrims++;
                if (region) regionPrims++;
            }
            if (region && info.PlainTemplate && TieClassBuilder.CheckTemplate(bytes) == null) templates.Add((tuid, info.Word78, bytes.Length));
        }

        var list = new List<TieShaderCandidate>();
        foreach (ulong tuid in regionShaders)
        {
            var c = new TieShaderCandidate { Tuid = tuid };
            var l = label(tuid);
            if (l is { } lab)
            {
                c.FullName = lab.Name ?? "";
                for (int i = 0; i < 6; i++) c.TextureNames[i] = i < lab.Textures.Count ? lab.Textures[i] ?? "" : "";
            }
            c.DisplayName = LastSegment(c.FullName) is { Length: > 0 } n ? n : $"Shader 0x{tuid:X16}";
            if (acc.TryGetValue(tuid, out var a)) a.Fill(c);
            list.Add(c);
        }
        list = [.. list.OrderByDescending(c => c.TieProven).ThenByDescending(c => c.TiePrimitives)
            .ThenByDescending(c => c.LevelPrimitives).ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)];

        var catalog = new TieShaderCatalog
        {
            Candidates = list,
            Template = ChooseTemplate(templates, read, out string reason) is { } t ? new TieTemplateChoice { ClassTuid = t.Tuid, Bytes = read(t.Tuid), Reason = reason } : null,
            RegionPrimitives = regionPrims,
            LevelPrimitives = levelPrims,
        };
        catalog._byTuid = list.ToDictionary(c => c.Tuid);
        foreach (var c in list)
        {
            AddKey(catalog._keys, c, c.DisplayName, "shader");
            AddKey(catalog._keys, c, c.FullName, "shader");
            AddKey(catalog._keys, c, StripTextureSuffix(LastSegment(c.AlbedoName)), "albedo");
            for (int i = 1; i < 6; i++) AddKey(catalog._keys, c, StripTextureSuffix(LastSegment(c.TextureNames[i])), "texture");
        }
        return catalog;
    }

    private static List<ulong> ReadTuids(IGFile file, uint id)
    {
        var s = file.QuerySection(id);
        var list = new List<ulong>();
        if (s.id != id || s.count == 0 || s.length != 8) return list;
        file.sh.Seek(s.offset);
        for (int i = 0; i < s.count; i++) list.Add(file.sh.ReadUInt64());
        return list;
    }

    // ---- scanning a class --------------------------------------------------------------------------------------------

    private readonly record struct Prim(int Index, int Slot, bool Lit, ushort Blend, ushort Second, byte Feature, short F0, short F1, short F2, short F3, int Lods);

    private sealed class ClassInfo
    {
        public ulong[] Shaders = [];
        public List<Prim> Primitives = [];
        public Dictionary<int, Vector2> Densities = [];
        /// <summary>No lightmap, one LOD, single layer, +0x0E = 0 and +0x1C = 4096: a neutral header to copy.</summary>
        public bool PlainTemplate;
        public uint Word78;
    }

    private static ClassInfo? ScanClass(byte[] b)
    {
        try
        {
            var sections = IghwRelayout.Sections(b);
            var s5600 = IghwRelayout.Find(sections, 0x5600);
            var s3500 = IghwRelayout.Find(sections, 0x3500);
            var def = IghwRelayout.Find(sections, 0x3400);
            if (s5600 == null || def == null || def.Size != 0x80) return null;
            var info = new ClassInfo { Shaders = new ulong[s5600.Size / 8] };
            for (int i = 0; i < info.Shaders.Length; i++) info.Shaders[i] = BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(s5600.Offset + 8 * i));
            if (s3500 != null)
                for (int i = 0; i < s3500.Size / 0x10; i++)
                {
                    int o = s3500.Offset + 0x10 * i;
                    info.Densities[i] = new Vector2(BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(o + 4)), BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(o + 8)));
                }
            int prims = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(def.Offset));
            int count = b[def.Offset + 0x0F];
            bool plain = b[def.Offset + 0x0E] == 0;
            plain &= BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(def.Offset + 0x1C)) == 4096f;
            for (int p = 0; p < count; p++)
            {
                int a = prims + 0x40 * p;
                var prim = new Prim(p, b[a + 0x2A], (b[a + 0x0E] & 1) != 0, BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(a + 0x0A)),
                    BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(a + 0x22)), b[a + 0x24],
                    BinaryPrimitives.ReadInt16BigEndian(b.AsSpan(a + 0x30)), BinaryPrimitives.ReadInt16BigEndian(b.AsSpan(a + 0x32)),
                    BinaryPrimitives.ReadInt16BigEndian(b.AsSpan(a + 0x34)), BinaryPrimitives.ReadInt16BigEndian(b.AsSpan(a + 0x36)), b[a + 0x0F]);
                info.Primitives.Add(prim);
                plain &= !prim.Lit && prim.Blend == 0xFFFF && prim.Second == 0xFFFF && prim.Lods == 1;
            }
            info.PlainTemplate = plain && count > 0;
            info.Word78 = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(def.Offset + 0x78));
            return info;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    /// <summary>Per shader: the values of its primitives.</summary>
    private sealed class Acc
    {
        private readonly Dictionary<(byte Mask, short F0, short F1, short F2, short F3), int> _values = [];
        private readonly List<(ulong Class, Prim P, Vector2 Density, bool Region)> _prims = [];
        private readonly HashSet<ulong> _classes = [];
        public int Region, Level, Lit, TwoLayer;

        public void Add(ulong cls, Prim p, Vector2 density, bool region)
        {
            _prims.Add((cls, p, density, region));
            // Region Ties decide when the region has any; the level is the fallback.
            Level++;
            if (region) { Region++; _classes.Add(cls); }
        }

        public void Fill(TieShaderCandidate c)
        {
            bool useRegion = Region > 0;
            var rows = _prims.Where(r => !useRegion || r.Region).ToList();
            c.TiePrimitives = Region;
            c.TieClasses = _classes.Count;
            c.LevelPrimitives = Level;
            c.LightmappedPrimitives = rows.Count(r => r.P.Lit);
            c.TwoLayer = rows.Count(r => r.P.Blend != 0xFFFF || r.P.Second != 0xFFFF) * 2 > rows.Count;
            var votes = new Dictionary<(byte, short, short, short, short), int>();
            foreach (var r in rows) votes[(r.P.Feature, r.P.F0, r.P.F1, r.P.F2, r.P.F3)] = votes.GetValueOrDefault((r.P.Feature, r.P.F0, r.P.F1, r.P.F2, r.P.F3)) + 1;
            // The feature mask and the fades vote separately: the mask is a property of the shader, the fades only mostly.
            var mask = rows.GroupBy(r => r.P.Feature).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First();
            var fades = rows.GroupBy(r => (r.P.F0, r.P.F1, r.P.F2, r.P.F3)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.F0).First();
            c.FeatureMask = mask.Key;
            c.Fades = [fades.Key.F0, fades.Key.F1, fades.Key.F2, fades.Key.F3];
            c.Unanimous = votes.Count == 1;
            // Template: the majority values, preferring unlit, single layer, single LOD, then the region's, then the smallest class id.
            var best = rows.Where(r => r.P.Feature == c.FeatureMask && r.P.F0 == c.Fades[0] && r.P.F1 == c.Fades[1] && r.P.F2 == c.Fades[2] && r.P.F3 == c.Fades[3])
                .OrderBy(r => r.P.Lit).ThenBy(r => r.P.Blend != 0xFFFF || r.P.Second != 0xFFFF).ThenBy(r => r.P.Lods != 1).ThenBy(r => !r.Region)
                .ThenBy(r => r.Class).ThenBy(r => r.P.Index).FirstOrDefault();
            if (best.Class != 0)
            {
                c.TemplateClass = best.Class;
                c.TemplatePrimitive = best.P.Index;
                c.UvDensity = best.Density;
            }
        }
    }

    /// <summary>The most common 0x3400 +0x78 among the plain classes; the smallest class of that pattern.</summary>
    private static (ulong Tuid, uint Word78, int Length)? ChooseTemplate(List<(ulong Tuid, uint Word78, int Length)> plain, Func<ulong, byte[]> read, out string reason)
    {
        reason = "no plain unlit single-LOD class in the region";
        if (plain.Count == 0) return null;
        var top = plain.GroupBy(t => t.Word78).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First();
        var pick = top.OrderBy(t => t.Length).ThenBy(t => t.Tuid).First();
        reason = $"{plain.Count} plain unlit single-LOD classes in the region (+0x0E = 0, +0x1C = 4096, no auxiliary block, nine sections); " +
                 $"+0x78 = 0x{top.Key:X8} on {top.Count()} of them; smallest of those: {pick.Tuid:X16} ({pick.Length} bytes)";
        return pick;
    }

    // ---- matching ----------------------------------------------------------------------------------------------------

    /// <summary>The best candidate for a glTF material, or null under <see cref="AcceptScore"/>. A material name that is
    /// a hex TUID of a candidate is exact. Names are compared lower case, folders and the _c/_d/_albedo style suffixes dropped.</summary>
    public TieShaderMatch? AutoMatch(string? materialName, string? baseColorTexture) => Rank(materialName, baseColorTexture, 1).FirstOrDefault() is { Score: >= AcceptScore } m ? m : null;

    /// <summary>The top <paramref name="take"/> candidates by score (any score above 0), best first.</summary>
    public List<TieShaderMatch> Rank(string? materialName, string? baseColorTexture, int take = 5)
    {
        var best = new Dictionary<ulong, TieShaderMatch>();
        void Offer(TieShaderCandidate c, double score, string via)
        {
            if (score <= 0) return;
            // A proven shader wins ties, an unproven or two-layer one loses a little.
            score += c.TieProven ? 0.01 : 0;
            score -= c.TwoLayer ? 0.02 : 0;
            score = Math.Min(1.0, score);
            if (!best.TryGetValue(c.Tuid, out var m) || m.Score < score) best[c.Tuid] = new TieShaderMatch { Candidate = c, Score = score, Via = via };
        }
        string mat = Normalize(materialName), tex = Normalize(baseColorTexture);
        if (materialName != null && TryParseTuid(materialName, out ulong id) && _byTuid.TryGetValue(id, out var byId)) Offer(byId, 1.0, "tuid");
        foreach (var (c, key, kind) in _keys)
        {
            if (mat.Length > 0) Offer(c, Similarity(mat, key) * (kind == "shader" ? 1.0 : 0.95), $"material ~ {kind}");
            if (tex.Length > 0) Offer(c, Similarity(tex, key) * (kind == "albedo" ? 1.0 : 0.9), $"texture ~ {kind}");
        }
        return [.. best.Values.OrderByDescending(m => m.Score).ThenByDescending(m => m.Candidate.TiePrimitives).Take(take)];
    }

    private static bool TryParseTuid(string s, out ulong id)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        id = 0;
        return s.Length == 16 && ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out id);
    }

    private static void AddKey(List<(TieShaderCandidate, string, string)> keys, TieShaderCandidate c, string name, string kind)
    {
        string k = Normalize(name);
        if (k.Length >= 2 && !keys.Any(x => x.Item1 == c && x.Item2 == k && x.Item3 == kind)) keys.Add((c, k, kind));
    }

    private static string LastSegment(string path)
    {
        int i = path.LastIndexOfAny(['/', '\\']);
        return i >= 0 ? path[(i + 1)..] : path;
    }

    private static readonly string[] TextureSuffixes = ["_albedo", "_basecolor", "_base_color", "_diffuse", "_color", "_col", "_c", "_d", "_n", "_e"];

    private static string StripTextureSuffix(string name)
    {
        foreach (string s in TextureSuffixes)
            if (name.Length > s.Length + 1 && name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return name[..^s.Length];
        return name;
    }

    /// <summary>Lower case, no folders or extension, no texture suffix, no trailing ".001" style copy counter; separators kept as '_'.</summary>
    private static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string s = LastSegment(name.Trim());
        int dot = s.LastIndexOf('.');
        if (dot > 0 && dot >= s.Length - 5 && s[(dot + 1)..].Any(char.IsLetter)) s = s[..dot];
        else if (dot > 0 && s[(dot + 1)..].All(char.IsDigit)) s = s[..dot];
        var sb = new System.Text.StringBuilder();
        foreach (char ch in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return StripTextureSuffix(sb.ToString().Trim('_'));
    }

    /// <summary>1 for equal, up to 0.9 when one contains the other (by length share), else the token overlap and bigram similarity, damped.</summary>
    private static double Similarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (a.Length >= 4 && b.Length >= 4 && (a.Contains(b) || b.Contains(a)))
        {
            double share = (double)Math.Min(a.Length, b.Length) / Math.Max(a.Length, b.Length);
            return 0.7 + 0.2 * share;
        }
        var ta = a.Split('_', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tb = b.Split('_', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        double jaccard = ta.Count == 0 || tb.Count == 0 ? 0 : (double)ta.Intersect(tb).Count() / ta.Union(tb).Count();
        return Math.Max(jaccard * 0.85, Dice(a, b) * 0.8);
    }

    private static double Dice(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2) return 0;
        var grams = new Dictionary<string, int>();
        for (int i = 0; i < a.Length - 1; i++) { string g = a.Substring(i, 2); grams[g] = grams.GetValueOrDefault(g) + 1; }
        int hit = 0;
        for (int i = 0; i < b.Length - 1; i++)
        {
            string g = b.Substring(i, 2);
            if (grams.TryGetValue(g, out int n) && n > 0) { grams[g] = n - 1; hit++; }
        }
        return 2.0 * hit / (a.Length - 1 + b.Length - 1);
    }
}
