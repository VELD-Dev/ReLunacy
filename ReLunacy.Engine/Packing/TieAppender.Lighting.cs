using System.Buffers.Binary;

namespace ReLunacy.Engine.Packing;

public static partial class TieAppender
{
    /// <summary>
    /// A baked source placement's lighting copied into the target zone, so the copy looks like its source:
    ///   - its lightmap pair: record +0x5A indexes the source zone's 0x5400 (colour) and 0x5410 (direction) RSX
    ///     descriptors {+0x00 offset in the zone's lighting.dat slice, +0x04 u16 mips, +0x06 format, ..., +0x18 u16
    ///     width, +0x1A u16 height, +0x1C CONTROL3 (pitch of a linear texture)}; both payloads are copied to the end of
    ///     the target slice (0x80-aligned, as every retail payload is) and two descriptors pointing at them appended,
    ///     the record's +0x5A naming them. Retail gives every baked placement a pair of its own, so the pair is copied
    ///     even within one zone. A target with a 0x5460 high-mip table gets a zero entry (no high mip: the resident
    ///     chain is used);
    ///   - its prelight block (+0x54, the words of the primitives that are not lightmapped), copied after them;
    ///   - +0x60..+0x6C (the baked-light constant) and +0x64 / +0x66 stay as the source has them;
    ///   - assetstats +0x14, the per-region peak of lightmap pairs (ss1 328 = its zones' 0x5400 counts), raised.
    /// Null when the pair can not be copied (a format whose size is not known, a payload outside its slice): the
    /// caller then falls back to an unbaked copy.
    /// </summary>
    private static (byte[] Slice, string Note)? BakedLighting(Level level, List<(ulong Tuid, int Offset, int Length)> lightingPointers, int slice,
        Source src, byte[] srcBlob, byte[] blob, byte[] record, Dictionary<uint, byte[]> payloads)
    {
        byte[]? file = level.Get("lighting.dat");
        int srcSlice = lightingPointers.FindIndex(p => p.Tuid == src.ZoneTuid);
        if (file == null || srcSlice < 0) return null;
        var ss = IghwRelayout.Sections(srcBlob);
        var sl = IghwRelayout.Find(ss, Lightmaps);
        var sd = IghwRelayout.Find(ss, Directions);
        int k = BinaryPrimitives.ReadUInt16BigEndian(record.AsSpan(0x5A));
        if (sl == null || sd == null || sl.LengthWord != 0x20 || sd.LengthWord != 0x20 || k >= sl.Count || k >= sd.Count) return null;
        var ts = IghwRelayout.Sections(blob);
        var tl = IghwRelayout.Find(ts, Lightmaps)!;
        var td = IghwRelayout.Find(ts, Directions)!;
        var th = IghwRelayout.Find(ts, HighMips);
        if (tl.LengthWord != 0x20 || td.LengthWord != 0x20 || tl.Count != td.Count || tl.Count >= 0xFFFF) return null;
        if (th != null && (th.LengthWord != 0x10 || th.Count != tl.Count)) return null;

        var (_, so, slen) = lightingPointers[srcSlice];
        var (_, to, tlen) = lightingPointers[slice];
        byte[] light = srcBlob.AsSpan(sl.Offset + 0x20 * k, 0x20).ToArray();
        byte[] direction = srcBlob.AsSpan(sd.Offset + 0x20 * k, 0x20).ToArray();
        int lightBytes = LightmapBytes(light), directionBytes = LightmapBytes(direction);
        uint lightAt = BinaryPrimitives.ReadUInt32BigEndian(light), directionAt = BinaryPrimitives.ReadUInt32BigEndian(direction);
        if (lightBytes <= 0 || directionBytes <= 0 || lightAt + lightBytes > slen || directionAt + directionBytes > slen) return null;
        const uint none = 0xFFFFFFFF;
        int prelightBytes = src.Prelight != none ? PrelightBlockLength(srcBlob, src.Prelight, slen) : 0;

        int newLight = IghwRelayout.Align(tlen, 0x80);
        int newDirection = IghwRelayout.Align(newLight + lightBytes, 0x80);
        int newPrelight = IghwRelayout.Align(newDirection + directionBytes, 0x80);
        var bytes = new byte[prelightBytes > 0 ? newPrelight + prelightBytes : newDirection + directionBytes];
        file.AsSpan(to, tlen).CopyTo(bytes);
        file.AsSpan(so + (int)lightAt, lightBytes).CopyTo(bytes.AsSpan(newLight));
        file.AsSpan(so + (int)directionAt, directionBytes).CopyTo(bytes.AsSpan(newDirection));
        if (prelightBytes > 0) file.AsSpan(so + (int)src.Prelight, prelightBytes).CopyTo(bytes.AsSpan(newPrelight));

        BinaryPrimitives.WriteUInt32BigEndian(light, (uint)newLight);
        BinaryPrimitives.WriteUInt32BigEndian(direction, (uint)newDirection);
        payloads[Lightmaps] = Concat(blob.AsSpan(tl.Offset, tl.Size), light);
        payloads[Directions] = Concat(blob.AsSpan(td.Offset, td.Size), direction);
        if (th != null) payloads[HighMips] = Concat(blob.AsSpan(th.Offset, th.Size), new byte[0x10]);
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x5A), (ushort)tl.Count);
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(0x54), prelightBytes > 0 ? (uint)newPrelight : none);

        RaiseLightmapBudget(level, lightingPointers[slice].Tuid);
        return (bytes, $"baked lightmap pair copied from zone {src.ZoneTuid:X16} ({lightBytes} + {directionBytes} bytes" +
                       (prelightBytes > 0 ? $", prelight {prelightBytes} bytes)" : ")"));
    }

    /// <summary>
    /// A neutral lightmap pair for a placement of an imported lightmapped class, until a bake writes a real one: the baked source
    /// placement's pair sampler words (format, address, filter, remap) on a flat 8 x 8, one-mip texture of the source pair's average
    /// colour (its first mip's DXT1 endpoints averaged), for both the colour (0x5400) and the direction (0x5410) map; appended to the
    /// target slice (0x80 aligned) with two new descriptors, the record's +0x5A naming them, and a zero 0x5460 entry when the zone has
    /// one. The baked-light constants (+0x60..+0x6C) stay as the source has them. A class with unlit primitives also gets
    /// VertexCount neutral prelight words (as <see cref="ImportedLighting"/>). Null when the source pair is not DXT1 or not readable.
    /// The pair is the placement's own (no retail placements share one), so a bake can overwrite its texels or replace it.
    /// </summary>
    private static (byte[] Slice, string Note)? NeutralLightmap(Level level, List<(ulong Tuid, int Offset, int Length)> lightingPointers, int slice,
        Source src, byte[] srcBlob, byte[] blob, byte[] record, Dictionary<uint, byte[]> payloads, RegisteredTieClass cls)
    {
        byte[]? file = level.Get("lighting.dat");
        int srcSlice = lightingPointers.FindIndex(p => p.Tuid == src.ZoneTuid);
        if (file == null || srcSlice < 0 || slice < 0) return null;
        var ss = IghwRelayout.Sections(srcBlob);
        var sl = IghwRelayout.Find(ss, Lightmaps);
        var sd = IghwRelayout.Find(ss, Directions);
        int k = BinaryPrimitives.ReadUInt16BigEndian(record.AsSpan(0x5A));
        if (sl == null || sd == null || sl.LengthWord != 0x20 || sd.LengthWord != 0x20 || k >= sl.Count || k >= sd.Count) return null;
        var ts = IghwRelayout.Sections(blob);
        var tl = IghwRelayout.Find(ts, Lightmaps)!;
        var td = IghwRelayout.Find(ts, Directions)!;
        var th = IghwRelayout.Find(ts, HighMips);
        if (tl.LengthWord != 0x20 || td.LengthWord != 0x20 || tl.Count != td.Count || tl.Count >= 0xFFFF) return null;
        if (th != null && (th.LengthWord != 0x10 || th.Count != tl.Count)) return null;

        var (_, so, slen) = lightingPointers[srcSlice];
        var (_, to, tlen) = lightingPointers[slice];
        byte[]? Flat(byte[] descriptor)
        {
            if ((descriptor[6] & 0x1F) != 0x06) return null;
            int w = BinaryPrimitives.ReadUInt16BigEndian(descriptor.AsSpan(0x18)), h = BinaryPrimitives.ReadUInt16BigEndian(descriptor.AsSpan(0x1A));
            uint at = BinaryPrimitives.ReadUInt32BigEndian(descriptor);
            int blocks = Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4);
            if (w == 0 || h == 0 || at + 8L * blocks > slen) return null;
            double r = 0, g = 0, b = 0;
            for (int i = 0; i < blocks; i++)
                foreach (int e in new[] { 0, 2 })
                {
                    int c = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(so + (int)at + 8 * i + e));
                    r += c >> 11 & 31; g += c >> 5 & 63; b += c & 31;
                }
            int n = 2 * blocks;
            ushort colour = (ushort)((int)Math.Round(r / n) << 11 | (int)Math.Round(g / n) << 5 | (int)Math.Round(b / n));
            var texels = new byte[8 * 4];
            for (int i = 0; i < 4; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(texels.AsSpan(8 * i), colour);
                BinaryPrimitives.WriteUInt16LittleEndian(texels.AsSpan(8 * i + 2), colour);
            }
            return texels;
        }
        byte[] light = srcBlob.AsSpan(sl.Offset + 0x20 * k, 0x20).ToArray();
        byte[] direction = srcBlob.AsSpan(sd.Offset + 0x20 * k, 0x20).ToArray();
        if (Flat(light) is not { } lightTexels || Flat(direction) is not { } directionTexels) return null;

        const uint none = 0xFFFFFFFF;
        bool unlit = cls.Primitives > cls.LightmappedPrimitives;
        int newLight = IghwRelayout.Align(tlen, 0x80);
        int newDirection = IghwRelayout.Align(newLight + lightTexels.Length, 0x80);
        int newPrelight = IghwRelayout.Align(newDirection + directionTexels.Length, 0x80);
        int prelightBytes = unlit ? 4 * cls.VertexCount : 0;
        var bytes = new byte[unlit ? newPrelight + prelightBytes : newDirection + directionTexels.Length];
        file.AsSpan(to, tlen).CopyTo(bytes);
        lightTexels.CopyTo(bytes, newLight);
        directionTexels.CopyTo(bytes, newDirection);
        for (int i = 0; i < prelightBytes; i += 4) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(newPrelight + i), 0x822E8080);

        foreach (var (d, at) in new[] { (light, newLight), (direction, newDirection) })
        {
            BinaryPrimitives.WriteUInt32BigEndian(d, (uint)at);
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(4), 1);
            uint control0 = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(12));
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(12), control0 & ~(0xFFFu << 15));
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(0x18), 8);
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(0x1A), 8);
        }
        payloads[Lightmaps] = Concat(blob.AsSpan(tl.Offset, tl.Size), light);
        payloads[Directions] = Concat(blob.AsSpan(td.Offset, td.Size), direction);
        if (th != null) payloads[HighMips] = Concat(blob.AsSpan(th.Offset, th.Size), new byte[0x10]);
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x5A), (ushort)tl.Count);
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(0x54), unlit ? (uint)newPrelight : none);
        RaiseLightmapBudget(level, lightingPointers[slice].Tuid);
        return (bytes, $"neutral lightmap pair (flat average of {src.Name}'s)" + (unlit ? $", neutral prelight {cls.VertexCount} words" : ""));
    }

    /// <summary>assetstats +0x14 = the most lightmap pairs (0x5400 entries) any region's zones hold, with the one being
    /// added to <paramref name="zoneTuid"/>.</summary>
    private static void RaiseLightmapBudget(Level level, ulong zoneTuid)
    {
        byte[] lookup = level.Files["assetlookup.dat"];
        byte[] zones = level.Files["zones.dat"];
        var pointers = Pointers(lookup, ZonePointers);
        var masks = Masks(lookup, ZoneMasks);
        var perRegion = new int[32];
        for (int z = 0; z < pointers.Count; z++)
        {
            var (t, o, l) = pointers[z];
            int count = (IghwRelayout.Find(IghwRelayout.Sections(zones.AsSpan(o, l)), Lightmaps)?.Count ?? 0) + (t == zoneTuid ? 1 : 0);
            uint mask = z < masks.Length ? masks[z] : 1u;
            for (int b = 0; b < 32; b++) if ((mask >> b & 1) != 0) perRegion[b] += count;
        }
        byte[] stats = level.Get("assetstats.dat") ?? throw new Refusal("assetstats.dat not found");
        int at = StatsOffset(stats) + StatsLightmaps;
        int peak = perRegion.Max();
        if (peak <= BinaryPrimitives.ReadInt32BigEndian(stats.AsSpan(at))) return;
        stats = (byte[])stats.Clone();
        BinaryPrimitives.WriteInt32BigEndian(stats.AsSpan(at), peak);
        level.Set("assetstats.dat", stats);
    }

    /// <summary>The bytes of a lightmap descriptor's mip chain in its slice (the layout NewEngineZoneLightingReader
    /// reads): block-compressed and swizzled mips back to back, linear ones at CONTROL3's pitch; -1 for a format it
    /// does not know. Every retail lightmap is DXT1.</summary>
    private static int LightmapBytes(ReadOnlySpan<byte> descriptor)
    {
        int mips = Math.Max(1, (int)BinaryPrimitives.ReadUInt16BigEndian(descriptor[4..]));
        byte raw = descriptor[6];
        int width = BinaryPrimitives.ReadUInt16BigEndian(descriptor[0x18..]), height = BinaryPrimitives.ReadUInt16BigEndian(descriptor[0x1A..]);
        int pitch = (int)(BinaryPrimitives.ReadUInt32BigEndian(descriptor[0x1C..]) & 0xFFFFF);
        if (width == 0 || height == 0) return -1;
        int block = (raw & 0x1F) switch { 0x06 => 8, 0x07 or 0x08 => 16, _ => 0 };
        int bpp = raw == 0x9A ? 8 : (raw & 0x1F) switch { 0x01 => 1, 0x02 or 0x03 or 0x04 or 0x0B => 2, 0x05 => 4, _ => 0 };
        if (block == 0 && bpp == 0) return -1;
        bool linear = block == 0 && ((raw & 0x20) != 0 || raw == 0x9A);
        int total = 0;
        for (int m = 0; m < mips; m++)
        {
            if (block != 0) total += Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * block;
            else if (linear) total += Math.Max(pitch, width * bpp) * height;
            else total += width * height * bpp;
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        return total;
    }

    /// <summary>
    /// assetstats 0x9800 +0x00 sizes the loader's main streaming pool (0x57F368, in 0x80-byte blocks, after what
    /// 0x580508 already took). On 22/22 A Crack in Time levels it is the sum of cubemaps.dat, ties.dat, shrubs.dat,
    /// mobys.dat, animsets.dat, cinematics.dat, zones.dat and collision.dat (each 0x80-aligned) plus a level-specific
    /// 0..0xB300 bytes (front_end: exactly the sum). So zones.dat and collision.dat growth is added to it.
    /// lighting.dat is not in it: its pool is sized from free memory.
    /// </summary>
    private static void RaiseResidentSize(Level level, List<string> problems)
    {
        long growth = 0;
        foreach (string name in level.Changed)
        {
            if (!name.Equals("zones.dat", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("/collision.dat", StringComparison.OrdinalIgnoreCase)) continue;
            growth += IghwRelayout.Align(level.Files[name].Length, 0x80) - IghwRelayout.Align(level.OriginalLength[name], 0x80);
        }
        if (growth <= 0) return;
        byte[]? stats = level.Get("assetstats.dat");
        if (stats == null) { problems.Add("assetstats.dat not found: its resident size was not raised"); return; }
        stats = (byte[])stats.Clone();
        int at = StatsOffset(stats) + StatsResident;
        long size = BinaryPrimitives.ReadUInt32BigEndian(stats.AsSpan(at)) + growth;
        BinaryPrimitives.WriteUInt32BigEndian(stats.AsSpan(at), (uint)size);
        level.Set("assetstats.dat", stats);
    }
}
