using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Importing;

namespace ReLunacy.Engine.Packing;

// New Tie classes (TieClassRegistrar): registering them in a level, and placing a class that has no placement yet.
public static partial class TieAppender
{
    private const uint ShaderPointers = 0x1D100, RegionShaders = 0x1C100, RegionTies = 0x1C300;

    /// <summary>
    /// Adds <paramref name="requests"/> as <see cref="Append(Func{string, byte[]?}, IReadOnlyList{TieAppendRequest})"/> does, for a
    /// class <see cref="TieClassRegistrar.Register"/> just wrote that has no placement to copy: the new placement copies a
    /// plain placement of the zone (its sphere is the class's), gets a block of neutral prelight words, and its collision
    /// instances are made from the cooked chunks.
    /// </summary>
    public static TieAppendResult Append(Func<string, byte[]?> read, IReadOnlyList<TieAppendRequest> requests,
        IReadOnlyDictionary<ulong, RegisteredTieClass>? imported) =>
        Run(new Level { Read = read, Imported = imported }, requests, new TieAppendResult(), null);

    // ---------------------------------------------------------------------------------------------------------------
    // Registering a class

    internal static TieClassRegistration RegisterClasses(Func<string, byte[]?> read, IReadOnlyList<PendingTieClass> classes)
    {
        var level = new Level { Read = read };
        var result = new TieClassRegistration();
        foreach (var pending in classes.OrderBy(c => c.Tuid))
        {
            // A refusal must leave the files as they were.
            var snapshot = new Dictionary<string, byte[]>(level.Files, StringComparer.OrdinalIgnoreCase);
            var changed = new HashSet<string>(level.Changed, StringComparer.OrdinalIgnoreCase);
            int problems = result.Problems.Count;
            try
            {
                var registered = RegisterOne(level, pending, result.Problems);
                result.Registered.Add(registered);
                result.Changes.Add($"Tie class {pending.Name} imported ({pending.Render.PrimitiveCount} primitives, {pending.Render.VertexCount} vertices" +
                                   (registered.Collision.Count > 0 ? $", {pending.Collision!.Chunks.Count} collision chunk(s)" : ", not solid") + ")");
            }
            catch (Exception e) when (e is Refusal or InvalidDataException)
            {
                level.Files.Clear();
                foreach (var (k, v) in snapshot) level.Files[k] = v;
                level.Changed.Clear();
                level.Changed.UnionWith(changed);
                result.Problems.RemoveRange(problems, result.Problems.Count - problems);
                result.Refused.Add((pending, e.Message));
            }
        }
        if (result.Registered.Count == 0) return result;

        // The resident pool grows with ties.dat and the collision files (as RaiseResidentSize does for zones.dat).
        long growth = 0;
        foreach (string name in level.Changed)
        {
            if (!name.Equals("ties.dat", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("/collision.dat", StringComparison.OrdinalIgnoreCase)) continue;
            growth += IghwRelayout.Align(level.Files[name].Length, 0x80) - IghwRelayout.Align(level.OriginalLength[name], 0x80);
        }
        if (growth > 0)
        {
            byte[] stats = (byte[])level.Files["assetstats.dat"].Clone();
            int at = StatsOffset(stats) + StatsResident;
            BinaryPrimitives.WriteUInt32BigEndian(stats.AsSpan(at), (uint)(BinaryPrimitives.ReadUInt32BigEndian(stats.AsSpan(at)) + growth));
            level.Set("assetstats.dat", stats);
        }
        UpdateToc(level, result.Problems);
        foreach (string name in level.Changed) result.Files[name] = level.Files[name];
        return result;
    }

    private static RegisteredTieClass RegisterOne(Level level, PendingTieClass pending, List<string> problems)
    {
        var render = pending.Render;
        if (render.Problem != null) throw new Refusal($"the class was not built: {render.Problem}");
        byte[] bytes = render.Bytes;
        if (bytes.Length == 0) throw new Refusal("the class has no bytes");
        ulong tuid = render.Tuid;
        var secs = IghwRelayout.Sections(bytes);
        var def = IghwRelayout.Find(secs, 0x3400) ?? throw new Refusal("the class has no 0x3400 definition");
        if (def.Offset + 0x70 > bytes.Length || BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(def.Offset + 0x68)) != tuid)
            throw new Refusal("the class's own TUID (0x3400 +0x68) is not the pending class's");
        if (pending.Regions.Count == 0) throw new Refusal("the class has no region to load in");

        byte[] ties = level.Get("ties.dat") ?? throw new Refusal("ties.dat not found");
        byte[] lookup = level.Get("assetlookup.dat") ?? throw new Refusal("assetlookup.dat not found");
        byte[] stats = level.Get("assetstats.dat") ?? throw new Refusal("assetstats.dat not found");

        // The TUID: unused by every lookup table, and above every Tie class so 0x1D300 stays sorted.
        var used = new HashSet<ulong>();
        foreach (var sec in IghwRelayout.Sections(lookup).Where(x => x.Id is >= 0x1D100 and < 0x1DC00 && (x.Id & 0xFF) == 0 && x.Size % 16 == 0))
            foreach (var p in Pointers(lookup, sec.Id)) used.Add(p.Tuid);
        var tiePointers = Pointers(lookup, TiePointers);
        if (used.Contains(tuid)) throw new Refusal($"TUID {tuid:X16} is already in use in this level");
        if (tiePointers.Count > 0 && tuid <= tiePointers.Max(p => p.Tuid))
            throw new Refusal($"TUID {tuid:X16} is not above the level's largest Tie class (TieClassRegistrar.FreshTuid makes one)");
        var tieMasks = Masks(lookup, TieMasks);
        if (tieMasks.Length != tiePointers.Count) throw new Refusal("assetlookup 0x1D300 and 0x1D301 differ in length");
        var shaderTuids = Pointers(lookup, ShaderPointers).Select(p => p.Tuid).ToHashSet();

        // Every target region must hold the class's shaders.
        uint mask = 0;
        var regions = pending.Regions.Distinct().ToList();
        var regionLists = new Dictionary<string, ulong[]>();
        foreach (string region in regions)
        {
            byte[] regionFile = level.Get($"{region}/region.dat") ?? throw new Refusal($"{region}/region.dat not found");
            var have = TuidList(regionFile, RegionShaders).ToHashSet();
            foreach (ulong shader in render.ShaderTuids)
                if (!have.Contains(shader) || !shaderTuids.Contains(shader))
                    throw new Refusal($"shader {shader:X16} is not in {region}'s 0x1C100 or in assetlookup 0x1D100");
            mask |= RegionBit(level, region);
            regionLists[region] = TuidList(regionFile, RegionTies);
        }

        // ties.dat: the container on the next 0x80 boundary.
        int at = IghwRelayout.Align(ties.Length, 0x80);
        var ties2 = new byte[IghwRelayout.Align(at + bytes.Length, 0x80)];
        ties.CopyTo(ties2, 0);
        bytes.CopyTo(ties2, at);
        level.Set("ties.dat", ties2);

        // assetlookup 0x1D300 {TUID, offset, length}, 0x1D301 the regions' bits.
        var ls = IghwRelayout.Sections(lookup);
        var s1 = IghwRelayout.Find(ls, TiePointers) ?? throw new Refusal("assetlookup has no 0x1D300");
        var s2 = IghwRelayout.Find(ls, TieMasks) ?? throw new Refusal("assetlookup has no 0x1D301");
        var entry = new byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(entry, tuid);
        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(8), (uint)at);
        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(12), (uint)bytes.Length);
        var maskBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(maskBytes, mask);
        level.Set("assetlookup.dat", IghwRelayout.Relayout(lookup, new Dictionary<uint, byte[]>
        {
            [TiePointers] = Concat(lookup.AsSpan(s1.Offset, s1.Size), entry),
            [TieMasks] = Concat(lookup.AsSpan(s2.Offset, s2.Size), maskBytes),
        }));

        // Each region.dat: 0x1C300 sorted (the shaders are already in 0x1C100, textures are inside the shaders).
        stats = (byte[])stats.Clone();
        int statsAt = StatsOffset(stats);
        foreach (string region in regions)
        {
            string name = $"{region}/region.dat";
            byte[] regionFile = level.Files[name];
            var list = regionLists[region].Append(tuid).Order().ToArray();
            var payload = new byte[8 * list.Length];
            for (int i = 0; i < list.Length; i++) BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(8 * i), list[i]);
            byte[] regionFile2 = IghwRelayout.Relayout(regionFile, new Dictionary<uint, byte[]> { [RegionTies] = payload });
            level.Set(name, regionFile2);
            // +0x2C Tie classes (the largest list, rounded up to 16 by the loader), +0x08 the largest region.dat.
            const int tieClasses = 0x2C;
            BinaryPrimitives.WriteUInt32BigEndian(stats.AsSpan(statsAt + tieClasses),
                Math.Max(BinaryPrimitives.ReadUInt32BigEndian(stats.AsSpan(statsAt + tieClasses)), (uint)list.Length));
            BinaryPrimitives.WriteUInt32BigEndian(stats.AsSpan(statsAt + StatsRegionSize),
                Math.Max(BinaryPrimitives.ReadUInt32BigEndian(stats.AsSpan(statsAt + StatsRegionSize)), (uint)IghwRelayout.Align(regionFile2.Length, 0x80)));
        }
        level.Set("assetstats.dat", stats);

        var collision = new Dictionary<string, RegisteredCollision>();
        if (pending.Collision is { Chunks.Count: > 0 })
            foreach (string region in regions)
                if (RegisterCollision(level, region, pending, problems) is { } rc) collision[region] = rc;
        return new RegisteredTieClass(tuid, pending.Name, render.VertexCount, render.Sphere, collision, render.LightmappedPrimitives, render.PrimitiveCount);
    }

    private static ulong[] TuidList(byte[] file, uint id)
    {
        var s = IghwRelayout.Find(IghwRelayout.Sections(file), id);
        if (s == null) return [];
        var list = new ulong[s.Size / 8];
        for (int i = 0; i < list.Length; i++) list[i] = BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(s.Offset + 8 * i));
        return list;
    }

    /// <summary>The class entry in one region's collision.dat: its TUID at the end of the table (+0x68 / +0x6C), 0xFFFF at the
    /// end of the u16 table (+0x70), and the cooked chunks at the end of the shape blob, each 16-aligned with its palette
    /// after it. The instances, the tree and the bounds are untouched: no placement uses the shapes yet.</summary>
    private static RegisteredCollision? RegisterCollision(Level level, string region, PendingTieClass pending, List<string> problems)
    {
        string fileName = $"{region}/collision.dat";
        byte[]? file = level.Get(fileName);
        if (file == null) { problems.Add($"Tie class {pending.Name}: {fileName} not found, the class is not solid in {region}"); return null; }
        var section = IghwRelayout.Find(IghwRelayout.Sections(file), CollisionSection);
        if (section == null) { problems.Add($"Tie class {pending.Name}: {fileName} has no section 0x2900, the class is not solid in {region}"); return null; }
        byte[] payload = file.AsSpan(section.Offset, section.Size).ToArray();
        int instanceOffset = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0));
        int instanceCount = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4));
        int shapeBase = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(8));
        int shapeLength = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0x0C));
        int tuidCount = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0x68));
        int tuidOffset = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0x6C));
        int u16Offset = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0x70));
        for (int i = 0; i < tuidCount; i++)
            if (BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(tuidOffset + 8 * i)) == pending.Tuid)
                throw new Refusal($"{fileName} already lists the class");

        // The payload as BuildPayload reads it ("old"): header, the longer tables, the longer shape blob.
        var old = new List<byte>(payload.Length + 0x1000);
        old.AddRange(payload.AsSpan(0, 0x80).ToArray());
        old.AddRange(payload.AsSpan(tuidOffset, 8 * tuidCount).ToArray());
        old.AddRange(U64(pending.Tuid));
        old.AddRange(payload.AsSpan(u16Offset, 2 * tuidCount).ToArray());
        old.AddRange([0xFF, 0xFF]);
        void Pad(int alignment) { while (old.Count % alignment != 0) old.Add(0); }
        Pad(16);
        int newShapeBase = old.Count;
        old.AddRange(payload.AsSpan(shapeBase, shapeLength).ToArray());
        var offsets = new List<uint>();
        foreach (var chunk in pending.Collision!.Chunks)
        {
            while ((old.Count - newShapeBase) % 16 != 0) old.Add(0);
            offsets.Add((uint)(old.Count - newShapeBase));
            old.AddRange(chunk.Data);
        }
        Pad(16);
        byte[] widened = old.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(widened.AsSpan(0x08), newShapeBase);
        BinaryPrimitives.WriteInt32BigEndian(widened.AsSpan(0x0C), widened.Length - newShapeBase);
        BinaryPrimitives.WriteInt32BigEndian(widened.AsSpan(0x68), tuidCount + 1);
        BinaryPrimitives.WriteInt32BigEndian(widened.AsSpan(0x6C), 0x80);
        BinaryPrimitives.WriteInt32BigEndian(widened.AsSpan(0x70), 0x80 + 8 * (tuidCount + 1));

        var records = new List<byte[]>(instanceCount);
        for (int i = 0; i < instanceCount; i++) records.Add(payload.AsSpan(instanceOffset + 0x80 * i, 0x80).ToArray());
        var root = CollisionBvh.Parse(payload);
        byte[] built = CollisionBvh.BuildPayload(widened, records, root, V3(payload, 0x40), V3(payload, 0x50), out _);
        byte[] newFile = IghwRelayout.Relayout(file, new Dictionary<uint, byte[]> { [CollisionSection] = built });
        level.Set(fileName, newFile);
        UpdateCollisionSize(level, file.Length, newFile.Length);
        return new RegisteredCollision(tuidCount, offsets, pending.Collision);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Placing a class that has no placement

    /// <summary>The placements of zone <paramref name="zone"/> (every zone when it has none): what a class without a
    /// placement copies its record from.</summary>
    private static List<Source> TemplatePlacements(byte[] zones, List<(ulong Tuid, int Offset, int Length)> zonePointers, int zone)
    {
        var list = new List<Source>();
        for (int pass = 0; pass < 2 && list.Count == 0; pass++)
            for (int z = 0; z < zonePointers.Count; z++)
            {
                if (pass == 0 && z != zone) continue;
                var (zt, zo, zl) = zonePointers[z];
                var zb = zones.AsSpan(zo, zl);
                var zs = IghwRelayout.Sections(zb);
                var zp = IghwRelayout.Find(zs, Placements);
                var zn = IghwRelayout.Find(zs, Names);
                if (zp == null || zp.LengthWord != 0x80) continue;
                for (int i = 0; i < zp.Count; i++)
                {
                    int rec = zp.Offset + 0x80 * i;
                    string label = zn != null && i < zn.Count ? CString(zb, BinaryPrimitives.ReadInt32BigEndian(zb[(zn.Offset + 16 * i + 8)..])) : $"#{i}";
                    list.Add(new Source(z, i, rec, BinaryPrimitives.ReadUInt16BigEndian(zb[(rec + 0x5A)..]) != 0xFFFF,
                        BinaryPrimitives.ReadUInt32BigEndian(zb[(rec + 0x54)..]), zb[rec + 0x5D], zt, label,
                        zn != null && i < zn.Count ? BinaryPrimitives.ReadUInt64BigEndian(zb[(zn.Offset + 16 * i)..]) : 0));
                }
            }
        return list;
    }

    /// <summary>The class's sphere in the record, carried by the template's matrix (the later move then gives the right
    /// sphere for the new matrix).</summary>
    private static void SetImportedSphere(byte[] record, Matrix4x4 held, RegisteredTieClass cls)
    {
        var centre = Vector3.Transform(new Vector3(cls.Sphere.X, cls.Sphere.Y, cls.Sphere.Z), held);
        PutF3(record, 0x40, centre);
        PutF(record, 0x4C, cls.Sphere.W * MaxAxis(held));
    }

    /// <summary>Unbaked lighting for a placement of a class with no placement: no lightmap pair, and VertexCount prelight words
    /// at the end of the zone's slice, all the template's most common word (a neutral value; the baker's job later).</summary>
    private static string ImportedLighting(Level level, List<(ulong Tuid, int Offset, int Length)> lightingPointers, int slice, Source src,
        byte[] srcBlob, byte[] record, RegisteredTieClass cls, out byte[]? newSlice)
    {
        const uint none = 0xFFFFFFFF;
        newSlice = null;
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x5A), 0xFFFF);
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x64), 0);
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x66), 0);
        if (slice < 0 || cls.VertexCount <= 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(0x54), none);
            return "no prelight (the zone has no lighting slice)";
        }
        byte[] lightingFile = level.Get("lighting.dat") ?? throw new Refusal("lighting.dat not found");
        var (_, to, tl) = lightingPointers[slice];

        // The template's most common word.
        uint word = 0x822E8080;
        string how = "a fixed neutral word";
        int srcSlice = lightingPointers.FindIndex(p => p.Tuid == src.ZoneTuid);
        if (!src.Baked && src.Prelight != none && srcSlice >= 0)
        {
            var (_, so, sl) = lightingPointers[srcSlice];
            try
            {
                int length = PrelightBlockLength(srcBlob, src.Prelight, sl) / 4 * 4;
                var counts = new Dictionary<uint, int>();
                for (int i = 0; i < length; i += 4)
                {
                    uint w = BinaryPrimitives.ReadUInt32BigEndian(lightingFile.AsSpan(so + (int)src.Prelight + i));
                    counts[w] = counts.GetValueOrDefault(w) + 1;
                }
                if (counts.Count > 0) { word = counts.MaxBy(c => c.Value).Key; how = $"the most common word of {src.Name}"; }
            }
            catch (Refusal) { }
        }
        int at = IghwRelayout.Align(tl, 0x80);
        int size = 4 * cls.VertexCount;
        newSlice = new byte[at + size];
        lightingFile.AsSpan(to, tl).CopyTo(newSlice);
        for (int i = 0; i < size; i += 4) BinaryPrimitives.WriteUInt32BigEndian(newSlice.AsSpan(at + i), word);
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(0x54), (uint)at);
        return $"neutral prelight, {cls.VertexCount} words of {how}";
    }

    /// <summary>The collision instances of a class's first placement, from its cooked chunks: one per chunk, owner = the class's
    /// TUID table index, then leaves in the tree as <see cref="AppendCollision"/> adds them.</summary>
    private static int AppendImportedCollision(Level level, TieAppendRequest request, byte[] file, byte[] payload, RegisteredCollision rc,
        List<string> problems)
    {
        string fileName = $"{request.Region}/collision.dat";
        int instanceOffset = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0));
        int instanceCount = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4));
        var chunks = rc.Cooked.Chunks;
        if (instanceCount + chunks.Count > 0xFFFF) { problems.Add($"Tie {request.Name}: {fileName} has no room for more instances (u16 leaf index), the Tie is not solid"); return 0; }
        var m = Affine(request.Transform);
        var records = new List<byte[]>(instanceCount + chunks.Count);
        for (int i = 0; i < instanceCount; i++) records.Add(payload.AsSpan(instanceOffset + 0x80 * i, 0x80).ToArray());
        var boxes = new List<(Vector3 Min, Vector3 Max)>();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < chunks.Count; i++)
        {
            byte[] r = CollisionCooker.BuildInstanceRecord(chunks[i], rc.ShapeOffsets[i], rc.OwnerIndex, m);
            records.Add(r);
            var lo = V3(r, 0x00);
            var hi = V3(r, 0x10);
            boxes.Add((lo, hi));
            min = Vector3.Min(min, lo); max = Vector3.Max(max, hi);
        }

        var root = CollisionBvh.Parse(payload);
        for (int start = 0; start < chunks.Count; start += 255)
        {
            var part = boxes.Skip(start).Take(255).ToList();
            var pl = part.Aggregate(new Vector3(float.MaxValue), (a, b) => Vector3.Min(a, b.Min));
            var ph = part.Aggregate(new Vector3(float.MinValue), (a, b) => Vector3.Max(a, b.Max));
            if (!CollisionBvh.AddLeaf(root, instanceCount + start, part.Count, pl, ph))
            { problems.Add($"Tie {request.Name}: the collision tree has no node with room for a leaf, the Tie is not solid"); return 0; }
        }
        var boundsMin = V3(payload, 0x40);
        var boundsMax = V3(payload, 0x50);
        var newMin = Vector3.Min(boundsMin, min);
        var newMax = Vector3.Max(boundsMax, max);
        if (newMin != boundsMin || newMax != boundsMax)
            problems.Add($"Tie {request.Name}: its collision is outside the region's collision bounds, which were grown (untested in game)");
        byte[] newPayload = CollisionBvh.BuildPayload(payload, records, root, newMin, newMax, out int treeBlock);
        if (treeBlock > CollisionBvh.MaxTreeBlock)
        {
            root = CollisionBvh.Parse(payload);
            if (!CollisionBvh.ExtendLastLeaf(root, instanceCount, chunks.Count, min, max))
            { problems.Add($"Tie {request.Name}: the collision tree is full (64 KB) and its last leaf has no room, the Tie is not solid"); return 0; }
            newPayload = CollisionBvh.BuildPayload(payload, records, root, newMin, newMax, out treeBlock);
            if (treeBlock > CollisionBvh.MaxTreeBlock)
            { problems.Add($"Tie {request.Name}: the collision tree would pass 64 KB, the Tie is not solid"); return 0; }
            problems.Add($"Tie {request.Name}: the collision tree is full (64 KB), so its last leaf was grown to hold the new collision");
        }
        byte[] newFile = IghwRelayout.Relayout(file, new Dictionary<uint, byte[]> { [CollisionSection] = newPayload });
        level.Set(fileName, newFile);
        UpdateCollisionSize(level, file.Length, newFile.Length);
        return chunks.Count;
    }

    private static bool OwnsInstances(byte[] payload, int owner)
    {
        int instanceOffset = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0));
        int instanceCount = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4));
        for (int i = 0; i < instanceCount; i++)
            if (BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(instanceOffset + 0x80 * i + 0x28)) == (0x20000000u | (uint)owner)) return true;
        return false;
    }
}
