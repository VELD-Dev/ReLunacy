using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using ReLunacy.Engine.Loading.Vertices;
using ReLunacy.Engine.Packing;

namespace ReLunacy.Engine.Importing;

/// <summary>A built Tie class container and what registering it in a level needs: the bytes go to ties.dat (0x80
/// aligned) with {<see cref="Tuid"/>, offset, length} in assetlookup 0x1D300 and 0x1D301, <see cref="ShaderTuids"/> must be in
/// the region's 0x1C100, <see cref="Tuid"/> in 0x1C300, and a placement needs <see cref="VertexCount"/> prelight
/// words for its unlit primitives and, when it has lightmapped ones, a lightmap pair (<see cref="Lightmapped"/>).</summary>
public sealed class TieClassResult
{
    /// <summary>Why the class was not built; the other members are then empty.</summary>
    public string? Problem;
    public byte[] Bytes = [];
    public ulong Tuid;
    public string Name = "";
    /// <summary>The 0x5600 list: the model's used slots in model order.</summary>
    public ulong[] ShaderTuids = [];
    /// <summary>Model slot to class slot, -1 for a slot no primitive uses.</summary>
    public int[] SlotMap = [];
    public int PrimitiveCount, VertexCount, IndexCount;
    /// <summary>Primitives with the lightmap gate, and those with an overlay layer.</summary>
    public int LightmappedPrimitives, OverlayPrimitives;
    public bool Lightmapped => LightmappedPrimitives > 0;
    public Vector3 Scale, BoundsCentre, BoundsHalf;
    /// <summary>xyz centre, w radius (0x3400 +0x30).</summary>
    public Vector4 Sphere;
    public SplitResult? Split;
}

/// <summary>
/// Writes a new-engine (A Crack in Time) Tie class asset: an IGHW 1.1 container with the nine standard sections
/// (docs/reverse/newengine_tie_asset_writing.md 1, 3), laid out by <see cref="IghwRelayout"/> from a template class of the
/// same level so the header, fix-ups and the fields nobody has decoded come from a loadable class: 0x3400 +0x0E, +0x1C,
/// +0x74, +0x78 and the rest not listed below are copied. Written fresh: the vertex block (20 B, per-axis scale = max(1, max
/// |coordinate|) / 32767, no offset), the u16 indices, the 0x3300 primitives (unlit, one LOD repeated four times, slot stored
/// twice), the 0x3F00 boxes (half extents, centre, half-diagonal), the 0x3500 densities, 0x5600, 0x5B00 (0), the name, and 0x3400's count, vertex
/// bytes, scale, sphere, AABB and TUID.
/// Lightmapped primitives (+0x0E bit 0) get their UV1 in the Half2 array after the vertex block (0x3000), from +0x06; an unlit
/// primitive holds the next lightmapped start there (PROVEN, ACiT). Overlay primitives (PROVEN on the 55 retail overlay classes:
/// every one of their 593 overlay primitives is lightmapped) have +0x0A 0 (the class's one mask), +0x22 / +0x2B the second slot,
/// +0x28 their window into a second Half2 run after the lightmap array, which 0x3400 +0x74 points at (byte offset in 0x3000 =
/// vertex bytes + 4 x lightmap entries; 0x3000 ends with the run), and the class gets 0x5800 (the mask's DXT1 texels), 0x5700
/// (its descriptor: offset 0, one mip, 0x86 | 0x29, address 0x30101, CONTROL0 0x80000000, CONTROL1 0xAAE4, filter 0x02022000,
/// CONTROL3 0x00100000, as all 55 retail ones) and 0x5900 {id, bytes} before 0x3000. The layer weight is the vertex alpha in w.
/// </summary>
public static class TieClassBuilder
{
    private static readonly uint[] SectionIds = [0x5600, 0x5B00, 0x3000, 0x3200, 0x3F00, 0x3300, 0x3500, 0x3410, 0x3400];
    private const int PrimitiveSize = 0x40, BoxSize = 0x20, DefinitionSize = 0x80, SlotRecordSize = 0x10, VertexSize = 20;

    /// <summary>The template must be a plain ACiT class: exactly the nine standard sections (no UV animation, no embedded
    /// texture), the 0x80 definition with +0x74 = FFFFFFFF, and pointers at the start of their sections.</summary>
    public static string? CheckTemplate(byte[] template)
    {
        List<IghwRelayout.Section> sections;
        try { sections = IghwRelayout.Sections(template); }
        catch (InvalidDataException e) { return e.Message; }
        if (sections.Count != SectionIds.Length || SectionIds.Any(id => IghwRelayout.Find(sections, id) == null))
            return "the template has sections other than the nine standard ones (UV animation or an embedded texture)";
        var def = IghwRelayout.Find(sections, 0x3400)!;
        if (def.Size != DefinitionSize) return "the template's 0x3400 is not the 0x80 ACiT record";
        if (BinaryPrimitives.ReadUInt32BigEndian(template.AsSpan(def.Offset + 0x74)) != 0xFFFFFFFF)
            return "the template carries an auxiliary block at 0x3400 +0x74";
        foreach (var (field, id) in new[] { (0x00, 0x3300u), (0x04, 0x3F00u), (0x64, 0x3410u) })
            if (BinaryPrimitives.ReadUInt32BigEndian(template.AsSpan(def.Offset + field)) != IghwRelayout.Find(sections, id)!.Offset)
                return $"the template's pointer at 0x3400 +0x{field:X2} is not the start of 0x{id:X}";
        return null;
    }

    /// <summary>A class's material slots as the writer would need them for the same shaders: the shader TUID, and the
    /// feature mask, fades, blend mask and density of the first primitive that uses the slot (the slot's own 0x3500 record).</summary>
    public static List<ImportMaterialSlot> ReadSlots(byte[] classBytes)
    {
        var sections = IghwRelayout.Sections(classBytes);
        var s5600 = IghwRelayout.Find(sections, 0x5600)!;
        var s3500 = IghwRelayout.Find(sections, 0x3500)!;
        int def = IghwRelayout.Find(sections, 0x3400)!.Offset;
        int prims = (int)BinaryPrimitives.ReadUInt32BigEndian(classBytes.AsSpan(def));
        int count = classBytes[def + 0x0F];
        var slots = new List<ImportMaterialSlot>();
        for (int i = 0; i < s5600.Size / 8; i++)
        {
            var slot = new ImportMaterialSlot { ShaderTuid = BinaryPrimitives.ReadUInt64BigEndian(classBytes.AsSpan(s5600.Offset + 8 * i)) };
            slot.Name = slot.ShaderTuid.ToString("X16");
            if (i < s3500.Size / SlotRecordSize)
            {
                int o = s3500.Offset + SlotRecordSize * i;
                slot.UvDensity = new Vector2(BinaryPrimitives.ReadSingleBigEndian(classBytes.AsSpan(o + 4)), BinaryPrimitives.ReadSingleBigEndian(classBytes.AsSpan(o + 8)));
            }
            for (int p = 0; p < count; p++)
            {
                int a = prims + PrimitiveSize * p;
                if (classBytes[a + 0x2A] != i) continue;
                slot.FeatureMask = classBytes[a + 0x24];
                slot.BlendMask = BinaryPrimitives.ReadUInt16BigEndian(classBytes.AsSpan(a + 0x0A));
                slot.Fades = [.. Enumerable.Range(0, 4).Select(k => BinaryPrimitives.ReadInt16BigEndian(classBytes.AsSpan(a + 0x30 + 2 * k)))];
                break;
            }
            slots.Add(slot);
        }
        return slots;
    }

    /// <summary>Splits <paramref name="model"/> (unless <paramref name="split"/> is given) and writes the class.</summary>
    public static TieClassResult Build(ImportModel model, ulong tuid, byte[] template, SplitResult? split = null)
    {
        var result = new TieClassResult { Tuid = tuid, Name = model.Name };
        string? problem = CheckTemplate(template);
        if (problem != null) return Fail(result, problem);
        split ??= TieSplitter.Split(model);
        result.Split = split;
        if (split.Problem != null) return Fail(result, split.Problem);
        var prims = split.Primitives;
        if (prims.Count is 0 or > TieSplitter.MaxPrimitives) return Fail(result, $"{prims.Count} primitives");
        foreach (var p in prims)
        {
            var slot = model.Slots[p.Slot];
            if (slot.Lightmapped && p.Uv1 == null) return Fail(result, $"slot '{slot.Name}' is lightmapped and the model has no second UV set (TEXCOORD_1)");
            if (slot.OverlaySlot >= 0)
            {
                if (slot.OverlaySlot >= model.Slots.Count || slot.OverlaySlot == p.Slot) return Fail(result, $"slot '{slot.Name}' has no valid overlay layer");
                if (!slot.Lightmapped) return Fail(result, $"slot '{slot.Name}' has an overlay layer and is not lightmapped (every retail overlay primitive is)");
                if (slot.BlendMask != 0 || model.OverlayMask == null) return Fail(result, $"slot '{slot.Name}' has an overlay layer and the class has no overlay mask");
            }
            else if (slot.BlendMask != 0xFFFF) return Fail(result, $"slot '{slot.Name}' has a blend mask and no overlay layer");
            if (slot.Fades.Length != 4) return Fail(result, $"slot '{slot.Name}' needs four fades");
        }
        int vertexTotal = prims.Sum(p => p.Positions.Length);
        if (vertexTotal > ushort.MaxValue) return Fail(result, $"{vertexTotal} vertices (the vertex start is u16)");
        if (prims.Where(p => model.Slots[p.Slot].Lightmapped).Sum(p => p.Positions.Length) > ushort.MaxValue)
            return Fail(result, "more than 65535 lightmapped vertices (+0x06 is u16)");

        // Used slots only, in model order.
        var slotMap = new int[model.Slots.Count];
        Array.Fill(slotMap, -1);
        var used = prims.Select(p => p.Slot).ToHashSet();
        used.UnionWith(used.Select(s => model.Slots[s].OverlaySlot).Where(s => s >= 0).ToList());
        foreach (int s in used.Order()) slotMap[s] = slotMap.Count(x => x >= 0);
        int usedSlots = slotMap.Count(x => x >= 0);
        if (usedSlots > 255) return Fail(result, $"{usedSlots} slots");

        // Per-axis scale and the quantised positions.
        var max = Vector3.Zero;
        foreach (var p in prims) foreach (var v in p.Positions) max = Vector3.Max(max, Vector3.Abs(v));
        if (!float.IsFinite(max.X + max.Y + max.Z)) return Fail(result, "a position is not finite");
        var scale = new Vector3(MathF.Max(1f, max.X), MathF.Max(1f, max.Y), MathF.Max(1f, max.Z)) / 32767f;

        var sections = IghwRelayout.Sections(template);
        int defAt = IghwRelayout.Find(sections, 0x3400)!.Offset;
        var vertices = new byte[vertexTotal * VertexSize];
        var indices = new byte[prims.Sum(p => p.Indices.Length) * 2];
        var primitives = new byte[prims.Count * PrimitiveSize];
        var boxes = new byte[prims.Count * BoxSize];
        var classMin = new Vector3(float.MaxValue); var classMax = new Vector3(float.MinValue);
        var decoded = new List<Vector3>(vertexTotal);
        int vertexAt = 0, indexAt = 0;
        var lightmapUvs = new List<Vector2>();
        var overlayUvs = new List<Vector2>();
        int lit = 0, overlays = 0;
        for (int i = 0; i < prims.Count; i++)
        {
            var p = prims[i];
            var slot = model.Slots[p.Slot];
            var min = new Vector3(float.MaxValue); var maxp = new Vector3(float.MinValue);
            for (int v = 0; v < p.Positions.Length; v++)
            {
                Vector3 pos = p.Positions[v];
                short qx = Quantise(pos.X, scale.X), qy = Quantise(pos.Y, scale.Y), qz = Quantise(pos.Z, scale.Z);
                var d = new Vector3(qx * scale.X, qy * scale.Y, qz * scale.Z);
                min = Vector3.Min(min, d); maxp = Vector3.Max(maxp, d);
                decoded.Add(d);
                var o = vertices.AsSpan((vertexAt + v) * VertexSize);
                BinaryPrimitives.WriteInt16BigEndian(o, qx);
                BinaryPrimitives.WriteInt16BigEndian(o[2..], qy);
                BinaryPrimitives.WriteInt16BigEndian(o[4..], qz);
                BinaryPrimitives.WriteInt16BigEndian(o[6..], VertexW(p.Tangents[v].W, p.Colors?[v].W ?? 1f, slot.OverlaySlot >= 0));
                BinaryPrimitives.WriteHalfBigEndian(o[8..], (Half)p.Uv0[v].X);
                BinaryPrimitives.WriteHalfBigEndian(o[10..], (Half)p.Uv0[v].Y);
                BinaryPrimitives.WriteUInt32BigEndian(o[12..], PackedNormal.EncodeForImport(Unit(p.Normals[v], Vector3.UnitY)));
                BinaryPrimitives.WriteUInt32BigEndian(o[16..], PackedNormal.EncodeForImport(Unit(new Vector3(p.Tangents[v].X, p.Tangents[v].Y, p.Tangents[v].Z), Vector3.UnitX)));
            }
            classMin = Vector3.Min(classMin, min); classMax = Vector3.Max(classMax, maxp);
            for (int k = 0; k < p.Indices.Length; k++)
                BinaryPrimitives.WriteUInt16BigEndian(indices.AsSpan((indexAt + k) * 2), p.Indices[k]);

            var a = primitives.AsSpan(i * PrimitiveSize, PrimitiveSize);
            BinaryPrimitives.WriteUInt32BigEndian(a, (uint)indexAt);
            BinaryPrimitives.WriteUInt16BigEndian(a[4..], (ushort)vertexAt);
            // +0x06: a lightmapped primitive's first entry in the lightmap array; an unlit one holds the next lightmapped start.
            BinaryPrimitives.WriteUInt16BigEndian(a[6..], (ushort)lightmapUvs.Count);
            if (slot.Lightmapped)
            {
                lightmapUvs.AddRange(p.Uv1!);
                a[0x0E] = 1;
                lit++;
            }
            BinaryPrimitives.WriteUInt16BigEndian(a[8..], (ushort)p.Positions.Length);
            BinaryPrimitives.WriteUInt16BigEndian(a[0x0A..], slot.BlendMask);
            a[0x0D] = (byte)p.Batch;
            a[0x0F] = 1;
            for (int r = 0; r < 4; r++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(a[(0x10 + 4 * r)..], 0);
                BinaryPrimitives.WriteUInt16BigEndian(a[(0x12 + 4 * r)..], (ushort)p.Indices.Length);
            }
            byte cs = (byte)slotMap[p.Slot];
            a[0x21] = cs; a[0x2A] = cs; a[0x2B] = 0xFF;
            BinaryPrimitives.WriteUInt16BigEndian(a[0x22..], 0xFFFF);
            BinaryPrimitives.WriteUInt16BigEndian(a[0x28..], 0xFFFF);
            if (slot.OverlaySlot >= 0)
            {
                byte second = (byte)slotMap[slot.OverlaySlot];
                BinaryPrimitives.WriteUInt16BigEndian(a[0x22..], second);
                a[0x2B] = second;
                BinaryPrimitives.WriteUInt16BigEndian(a[0x28..], (ushort)overlayUvs.Count);
                foreach (var uv in p.Uv0) overlayUvs.Add(uv * slot.OverlayUvScale + slot.OverlayUvOffset);
                overlays++;
            }
            a[0x24] = slot.FeatureMask;
            for (int k = 0; k < 4; k++) BinaryPrimitives.WriteInt16BigEndian(a[(0x30 + 2 * k)..], slot.Fades[k]);

            var b = boxes.AsSpan(i * BoxSize, BoxSize);
            WriteVector3(b, (maxp - min) * 0.5f);
            Vector3 boxCentre = (maxp + min) * 0.5f;
            WriteVector3(b[0x10..], boxCentre);
            // +0x1C: the half-diagonal of the box (measured on krell_canyon: |half extents| on every sampled primitive).
            BinaryPrimitives.WriteSingleBigEndian(b[0x1C..], ((maxp - min) * 0.5f).Length());
            vertexAt += p.Positions.Length;
            indexAt += p.Indices.Length;
        }

        Vector3 centre = (classMin + classMax) * 0.5f, half = (classMax - classMin) * 0.5f;
        float radius = decoded.Max(d => Vector3.Distance(d, centre));

        var shaders = new byte[usedSlots * 8];
        var slotRecords = new byte[usedSlots * SlotRecordSize];
        var flags = new byte[usedSlots];
        var shaderTuids = new ulong[usedSlots];
        for (int s = 0; s < model.Slots.Count; s++)
        {
            if (slotMap[s] < 0) continue;
            int c = slotMap[s];
            shaderTuids[c] = model.Slots[s].ShaderTuid;
            BinaryPrimitives.WriteUInt64BigEndian(shaders.AsSpan(c * 8), model.Slots[s].ShaderTuid);
            Vector2 density = model.Slots[s].UvDensity ?? MeasureDensity(prims, s);
            var r = slotRecords.AsSpan(c * SlotRecordSize);
            BinaryPrimitives.WriteUInt32BigEndian(r, (uint)c);
            BinaryPrimitives.WriteSingleBigEndian(r[4..], density.X);
            BinaryPrimitives.WriteSingleBigEndian(r[8..], density.Y);
        }

        byte[] name = [.. Encoding.ASCII.GetBytes(model.Name), (byte)0];
        var definition = template.AsSpan(defAt, DefinitionSize).ToArray();
        definition[0x0F] = (byte)prims.Count;
        BinaryPrimitives.WriteUInt32BigEndian(definition.AsSpan(0x18), (uint)vertices.Length);
        BinaryPrimitives.WriteSingleBigEndian(definition.AsSpan(0x20), scale.X);
        BinaryPrimitives.WriteSingleBigEndian(definition.AsSpan(0x24), scale.Y);
        BinaryPrimitives.WriteSingleBigEndian(definition.AsSpan(0x28), scale.Z);
        BinaryPrimitives.WriteSingleBigEndian(definition.AsSpan(0x2C), 1f);
        WriteVector3(definition.AsSpan(0x30), centre);
        BinaryPrimitives.WriteSingleBigEndian(definition.AsSpan(0x3C), radius);
        WriteVector3(definition.AsSpan(0x40), half);
        WriteVector3(definition.AsSpan(0x50), centre);
        BinaryPrimitives.WriteUInt64BigEndian(definition.AsSpan(0x68), tuid);
        // +0x74: the overlay run's byte offset in 0x3000 (not fixed up), FFFFFFFF without one.
        BinaryPrimitives.WriteUInt32BigEndian(definition.AsSpan(0x74), overlays > 0 ? (uint)(vertices.Length + 4 * lightmapUvs.Count) : 0xFFFFFFFF);

        var block = new byte[vertices.Length + 4 * (lightmapUvs.Count + overlayUvs.Count)];
        vertices.CopyTo(block, 0);
        int uvAt = vertices.Length;
        foreach (var uv in lightmapUvs.Concat(overlayUvs))
        {
            BinaryPrimitives.WriteHalfBigEndian(block.AsSpan(uvAt), (Half)uv.X);
            BinaryPrimitives.WriteHalfBigEndian(block.AsSpan(uvAt + 2), (Half)uv.Y);
            uvAt += 4;
        }
        var payloads = new Dictionary<uint, byte[]>
        {
            [0x5600] = shaders, [0x5B00] = flags, [0x3000] = block, [0x3200] = indices, [0x3F00] = boxes,
            [0x3300] = primitives, [0x3500] = slotRecords, [0x3410] = name, [0x3400] = definition,
        };
        byte[] layout = template;
        if (overlays > 0)
        {
            var mask = model.OverlayMask!;
            if (mask.Format != 0x06 || mask.MipCount != 1 || mask.Width != mask.Height) return Fail(result, "the overlay mask must be a square single-mip DXT1");
            layout = IghwRelayout.WithSections(template, [(0x3000, 0x5800, 1, 0), (0x3000, 0x5700, 0x10000000, 0x20), (0x3000, 0x5900, 0x10000000, 8)]);
            var descriptor = new byte[0x20];
            BinaryPrimitives.WriteUInt16BigEndian(descriptor.AsSpan(4), 1);
            descriptor[6] = 0x86; descriptor[7] = 0x29;
            BinaryPrimitives.WriteUInt32BigEndian(descriptor.AsSpan(8), 0x00030101);
            BinaryPrimitives.WriteUInt32BigEndian(descriptor.AsSpan(12), 0x80000000);
            BinaryPrimitives.WriteUInt32BigEndian(descriptor.AsSpan(16), 0xAAE4);
            BinaryPrimitives.WriteUInt32BigEndian(descriptor.AsSpan(20), 0x02022000);
            BinaryPrimitives.WriteUInt16BigEndian(descriptor.AsSpan(24), (ushort)mask.Width);
            BinaryPrimitives.WriteUInt16BigEndian(descriptor.AsSpan(26), (ushort)mask.Height);
            BinaryPrimitives.WriteUInt32BigEndian(descriptor.AsSpan(28), 0x00100000);
            var id = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(id, model.OverlayMaskId);
            BinaryPrimitives.WriteInt32BigEndian(id.AsSpan(4), mask.Chain.Length);
            payloads[0x5800] = mask.Chain;
            payloads[0x5700] = descriptor;
            payloads[0x5900] = id;
        }
        result.Bytes = IghwRelayout.Relayout(layout, payloads);
        result.LightmappedPrimitives = lit;
        result.OverlayPrimitives = overlays;
        result.ShaderTuids = shaderTuids;
        result.SlotMap = slotMap;
        result.PrimitiveCount = prims.Count;
        result.VertexCount = vertexTotal;
        result.IndexCount = indices.Length / 2;
        result.Scale = scale;
        result.BoundsCentre = centre;
        result.BoundsHalf = half;
        result.Sphere = new Vector4(centre, radius);
        return result;
    }

    private static TieClassResult Fail(TieClassResult r, string problem) { r.Problem = problem; r.Bytes = []; return r; }

    private static short Quantise(float v, float scale) => (short)Math.Clamp(MathF.Round(v / scale), -32767f, 32767f);

    /// <summary>+0x06: sign = minus the UV handedness, magnitude 0x7F80 + 127 x alpha. Opaque is what retail writes,
    /// 0x8000 for a negative and 0x7F80 for a positive word. On an overlay primitive the low 7 bits are the layer weight
    /// (|w| &amp; 0x7F = 127 on most retail overlay vertices), so a full weight is written as 127.</summary>
    private static short VertexW(float handedness, float alpha, bool overlay = false)
    {
        bool negative = handedness >= 0;
        if (alpha >= 1f && !overlay) return negative ? short.MinValue : (short)0x7F80;
        int magnitude = Math.Min(0x7F80 + (int)MathF.Round(127f * Math.Clamp(alpha, 0f, 1f)), 0x7FFF);
        return (short)(negative ? -magnitude : magnitude);
    }

    private static Vector3 Unit(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-12f && float.IsFinite(v.LengthSquared()) ? Vector3.Normalize(v) : fallback;

    private static void WriteVector3(Span<byte> o, Vector3 v)
    {
        BinaryPrimitives.WriteSingleBigEndian(o, v.X);
        BinaryPrimitives.WriteSingleBigEndian(o[4..], v.Y);
        BinaryPrimitives.WriteSingleBigEndian(o[8..], v.Z);
    }

    /// <summary>UV units per world unit, sqrt(UV area / 3D area) over the slot's triangles; 1 when it cannot be measured.</summary>
    private static Vector2 MeasureDensity(List<SplitPrimitive> prims, int slot)
    {
        double area3 = 0, areaUv = 0;
        foreach (var p in prims.Where(p => p.Slot == slot))
            for (int t = 0; t + 2 < p.Indices.Length; t += 3)
            {
                int a = p.Indices[t], b = p.Indices[t + 1], c = p.Indices[t + 2];
                area3 += 0.5 * Vector3.Cross(p.Positions[b] - p.Positions[a], p.Positions[c] - p.Positions[a]).Length();
                Vector2 e1 = p.Uv0[b] - p.Uv0[a], e2 = p.Uv0[c] - p.Uv0[a];
                areaUv += 0.5 * Math.Abs(e1.X * e2.Y - e1.Y * e2.X);
            }
        float d = area3 > 0 && areaUv > 0 ? (float)Math.Sqrt(areaUv / area3) : 1f;
        return new Vector2(d, d);
    }
}
