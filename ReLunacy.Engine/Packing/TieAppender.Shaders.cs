using System.Buffers.Binary;

namespace ReLunacy.Engine.Packing;

// New shaders and textures (ShaderRegistrar): registering them in a level before the Tie classes that use them.
public static partial class TieAppender
{
    private const uint TextureMeta = 0x1D140, TexturePointers = 0x1D180, TextureMasks = 0x1D181, HighmipPointers = 0x1D1C0;
    private const uint ShaderMasks = 0x1D101;
    private const int StatsShaders = 0x10;

    /// <summary>Texture data waiting to be appended: textures.dat and highmips.dat are large (highmips.dat is hundreds of MB), so
    /// each is rebuilt once after every shader is in, not once per texture.</summary>
    private sealed class TextureAppends
    {
        public int TexturesLength, HighmipsLength;
        public bool Loaded;
        public readonly List<(int At, byte[] Data)> Resident = [], High = [];
        public readonly HashSet<ulong> Written = [];

        public TextureAppends Copy()
        {
            var c = new TextureAppends { TexturesLength = TexturesLength, HighmipsLength = HighmipsLength, Loaded = Loaded };
            c.Resident.AddRange(Resident); c.High.AddRange(High); c.Written.UnionWith(Written);
            return c;
        }
    }

    internal static ShaderRegistration RegisterShaders(Func<string, byte[]?> read, IReadOnlyList<PendingShader> shaders)
    {
        var level = new Level { Read = read };
        var result = new ShaderRegistration();
        var appends = new TextureAppends();
        foreach (var pending in shaders.OrderBy(s => s.Tuid))
        {
            var snapshot = new Dictionary<string, byte[]>(level.Files, StringComparer.OrdinalIgnoreCase);
            var changed = new HashSet<string>(level.Changed, StringComparer.OrdinalIgnoreCase);
            var appendsBefore = appends.Copy();
            try
            {
                int textures = RegisterShader(level, pending, appends);
                result.Registered.Add(pending.Tuid);
                result.Changes.Add($"shader {pending.Name} imported ({textures} new texture(s), from {pending.TemplateTuid:X16})");
            }
            catch (Exception e) when (e is Refusal or InvalidDataException)
            {
                level.Files.Clear();
                foreach (var (k, v) in snapshot) level.Files[k] = v;
                level.Changed.Clear();
                level.Changed.UnionWith(changed);
                appends = appendsBefore;
                result.Refused.Add((pending, e.Message));
            }
        }
        if (result.Registered.Count == 0) return result;
        WriteAppends(level, "textures.dat", appends.TexturesLength, appends.Resident);
        WriteAppends(level, "highmips.dat", appends.HighmipsLength, appends.High);
        UpdateToc(level, result.Problems);
        foreach (string name in level.Changed) result.Files[name] = level.Files[name];
        return result;
    }

    /// <summary>Writes one shader and the textures of it not written yet; returns how many textures it wrote.</summary>
    private static int RegisterShader(Level level, PendingShader pending, TextureAppends appends)
    {
        byte[] bytes = pending.Bytes;
        var secs = IghwRelayout.Sections(bytes);
        var refs = IghwRelayout.Find(secs, 0x5D00) ?? throw new Refusal("the shader has no 0x5D00");
        if (refs.Size < 0x40 || BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(refs.Offset)) != pending.Tuid)
            throw new Refusal("the shader's own TUID (0x5D00 +0x00) is not the pending shader's");
        if (pending.Regions.Count == 0) throw new Refusal("the shader has no region to load in");

        byte[] lookup = level.Get("assetlookup.dat") ?? throw new Refusal("assetlookup.dat not found");
        var used = new HashSet<ulong>();
        foreach (var sec in IghwRelayout.Sections(lookup).Where(x => x.Id is >= 0x1D100 and < 0x1DC00 && (x.Id & 0xFF) == 0 && x.Size % 16 == 0))
            foreach (var p in Pointers(lookup, sec.Id)) used.Add(p.Tuid);
        if (used.Contains(pending.Tuid)) throw new Refusal($"TUID {pending.Tuid:X16} is already in use in this level");

        // Every texture id the shader binds (0x5A00) is in the level or among its new textures.
        var texturePointers = Pointers(lookup, HighmipPointers);
        var levelIds = texturePointers.Select(p => (uint)p.Tuid).ToHashSet();
        var keys = IghwRelayout.Find(secs, 0x5A00);
        if (keys != null)
            for (int i = 0; i < keys.Size / 8; i++)
            {
                uint id = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(keys.Offset + 8 * i));
                if (!levelIds.Contains(id) && !pending.Textures.Any(t => t.Id == id))
                    throw new Refusal($"texture id {id:X8} of the shader is not in the level");
            }

        uint mask = 0;
        foreach (string region in pending.Regions.Distinct()) mask |= RegionBit(level, region);

        int count = 0;
        foreach (var texture in pending.Textures)
        {
            if (!appends.Written.Add(texture.Tuid)) continue;
            if (texturePointers.Any(p => p.Tuid == texture.Tuid)) continue;
            if (levelIds.Contains(texture.Id)) throw new Refusal($"texture id {texture.Id:X8} is already in use in this level");
            RegisterTexture(level, texture, mask, appends);
            levelIds.Add(texture.Id);
            count++;
        }

        // shaders.dat: the resource on the next 0x80 boundary, the file ending on one.
        byte[] shadersFile = level.Get("shaders.dat") ?? throw new Refusal("shaders.dat not found");
        int at = IghwRelayout.Align(shadersFile.Length, 0x80);
        var shaders2 = new byte[IghwRelayout.Align(at + bytes.Length, 0x80)];
        shadersFile.CopyTo(shaders2, 0);
        bytes.CopyTo(shaders2, at);
        level.Set("shaders.dat", shaders2);

        // assetlookup 0x1D100 / 0x1D101: inserted where the TUID sorts.
        lookup = level.Get("assetlookup.dat")!;
        var pointers = Pointers(lookup, ShaderPointers);
        var masks = Masks(lookup, ShaderMasks);
        if (masks.Length != pointers.Count) throw new Refusal("assetlookup 0x1D100 and 0x1D101 differ in length");
        int index = pointers.FindIndex(p => p.Tuid > pending.Tuid);
        if (index < 0) index = pointers.Count;
        pointers.Insert(index, (pending.Tuid, at, bytes.Length));
        var maskList = masks.ToList();
        maskList.Insert(index, mask);
        level.Set("assetlookup.dat", IghwRelayout.Relayout(lookup, new Dictionary<uint, byte[]>
        {
            [ShaderPointers] = PointerBytes(pointers),
            [ShaderMasks] = MaskBytes(maskList),
        }));

        // Each region.dat 0x1C100 (sorted); assetstats +0x10 = the largest list.
        byte[] stats = (byte[])(level.Get("assetstats.dat") ?? throw new Refusal("assetstats.dat not found")).Clone();
        int statsAt = StatsOffset(stats);
        foreach (string region in pending.Regions.Distinct())
        {
            string name = $"{region}/region.dat";
            byte[] regionFile = level.Get(name) ?? throw new Refusal($"{name} not found");
            var list = TuidList(regionFile, RegionShaders);
            if (IghwRelayout.Find(IghwRelayout.Sections(regionFile), RegionShaders) == null) throw new Refusal($"{name} has no 0x1C100");
            if (list.Contains(pending.Tuid)) continue;
            list = [.. list.Append(pending.Tuid).Order()];
            var payload = new byte[8 * list.Length];
            for (int i = 0; i < list.Length; i++) BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(8 * i), list[i]);
            byte[] regionFile2 = IghwRelayout.Relayout(regionFile, new Dictionary<uint, byte[]> { [RegionShaders] = payload });
            level.Set(name, regionFile2);
            BinaryPrimitives.WriteUInt32BigEndian(stats.AsSpan(statsAt + StatsShaders),
                Math.Max(BinaryPrimitives.ReadUInt32BigEndian(stats.AsSpan(statsAt + StatsShaders)), (uint)list.Length));
            BinaryPrimitives.WriteUInt32BigEndian(stats.AsSpan(statsAt + StatsRegionSize),
                Math.Max(BinaryPrimitives.ReadUInt32BigEndian(stats.AsSpan(statsAt + StatsRegionSize)), (uint)IghwRelayout.Align(regionFile2.Length, 0x80)));
        }
        level.Set("assetstats.dat", stats);
        return count;
    }

    /// <summary>One texture: mip 0 to highmips.dat, the rest to textures.dat (all of a single-mip texture to textures.dat), and its
    /// four lookup rows appended.</summary>
    private static void RegisterTexture(Level level, PendingTexture pending, uint mask, TextureAppends appends)
    {
        var t = pending.Texture;
        if (t.Format is not (0x06 or 0x08 or 0x05)) throw new Refusal($"texture {t.Name}: only DXT1, DXT5 and A8R8G8B8 are written");
        if (t.Format == 0x05 && t.Width != t.Height) throw new Refusal($"texture {t.Name}: an A8R8G8B8 texture must be square");
        if (t.Width < 1 || t.Height < 1 || !IsPowerOfTwo(t.Width) || !IsPowerOfTwo(t.Height) || t.MipCount < 1 || t.MipCount > 15)
            throw new Refusal($"texture {t.Name}: its size or mip count is not storable");
        if (t.MipCount > 1 && (t.Mip0Size <= 0 || t.Mip0Size >= t.Chain.Length)) throw new Refusal($"texture {t.Name}: its mip 0 size is wrong");

        if (!appends.Loaded)
        {
            appends.Loaded = true;
            appends.TexturesLength = (level.Get("textures.dat") ?? throw new Refusal("textures.dat not found")).Length;
            appends.HighmipsLength = (level.Get("highmips.dat") ?? throw new Refusal("highmips.dat not found")).Length;
        }
        bool split = t.MipCount > 1;
        byte[] resident = split ? t.Chain[t.Mip0Size..] : t.Chain;
        int residentAt = IghwRelayout.Align(appends.TexturesLength, 0x80);
        appends.Resident.Add((residentAt, resident));
        appends.TexturesLength = residentAt + resident.Length;
        int highAt = IghwRelayout.Align(appends.HighmipsLength, 0x80);
        if (split)
        {
            appends.High.Add((highAt, t.Chain[..t.Mip0Size]));
            appends.HighmipsLength = highAt + t.Mip0Size;
        }

        byte[] lookup = level.Get("assetlookup.dat") ?? throw new Refusal("assetlookup.dat not found");
        var ls = IghwRelayout.Sections(lookup);
        var meta = IghwRelayout.Find(ls, TextureMeta) ?? throw new Refusal("assetlookup has no 0x1D140");
        var low = IghwRelayout.Find(ls, TexturePointers) ?? throw new Refusal("assetlookup has no 0x1D180");
        var lowMasks = IghwRelayout.Find(ls, TextureMasks) ?? throw new Refusal("assetlookup has no 0x1D181");
        var high = IghwRelayout.Find(ls, HighmipPointers) ?? throw new Refusal("assetlookup has no 0x1D1C0");
        int n = meta.Size / 4;
        if (low.Size != 16 * n || high.Size != 16 * n || lowMasks.Size != 4 * n) throw new Refusal("assetlookup's texture tables differ in length");

        byte[] Row(int offset, int length)
        {
            var row = new byte[16];
            BinaryPrimitives.WriteUInt64BigEndian(row, pending.Tuid);
            BinaryPrimitives.WriteInt32BigEndian(row.AsSpan(8), offset);
            BinaryPrimitives.WriteInt32BigEndian(row.AsSpan(12), length);
            return row;
        }
        byte[] metaRow = [t.Format, (byte)t.MipCount, (byte)System.Numerics.BitOperations.Log2((uint)t.Width), (byte)System.Numerics.BitOperations.Log2((uint)t.Height)];
        var maskRow = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(maskRow, mask);
        level.Set("assetlookup.dat", IghwRelayout.Relayout(lookup, new Dictionary<uint, byte[]>
        {
            [TextureMeta] = Concat(lookup.AsSpan(meta.Offset, meta.Size), metaRow),
            [TexturePointers] = Concat(lookup.AsSpan(low.Offset, low.Size), Row(residentAt, resident.Length)),
            [TextureMasks] = Concat(lookup.AsSpan(lowMasks.Offset, lowMasks.Size), maskRow),
            [HighmipPointers] = Concat(lookup.AsSpan(high.Offset, high.Size), Row(highAt, split ? t.Mip0Size : 0)),
        }));
    }

    /// <summary><paramref name="name"/> with <paramref name="appends"/> written at their offsets, ending on a 0x80 boundary.</summary>
    private static void WriteAppends(Level level, string name, int length, List<(int At, byte[] Data)> appends)
    {
        if (appends.Count == 0) return;
        byte[] file = level.Get(name) ?? throw new InvalidDataException($"{name} not found");
        var output = new byte[IghwRelayout.Align(length, 0x80)];
        file.CopyTo(output, 0);
        foreach (var (at, data) in appends) data.CopyTo(output, at);
        level.Set(name, output);
    }

    private static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;

    private static byte[] PointerBytes(List<(ulong Tuid, int Offset, int Length)> pointers)
    {
        var bytes = new byte[16 * pointers.Count];
        for (int i = 0; i < pointers.Count; i++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16 * i), pointers[i].Tuid);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16 * i + 8), pointers[i].Offset);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16 * i + 12), pointers[i].Length);
        }
        return bytes;
    }

    private static byte[] MaskBytes(List<uint> masks)
    {
        var bytes = new byte[4 * masks.Count];
        for (int i = 0; i < masks.Count; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4 * i), masks[i]);
        return bytes;
    }
}
