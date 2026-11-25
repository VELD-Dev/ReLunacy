using System.Buffers.Binary;
using System.Text;

namespace ReLunacy.Engine.Scripting;

/// <summary>Where a pointer field of a script element points.</summary>
public abstract record ScriptPointer;

/// <summary>Another element of the same script: a branch (+0x04 next, +0x08 else).</summary>
public sealed record ScriptElementPointer(ScriptElement Element) : ScriptPointer;

/// <summary>An f32 slot of a variable block. The slot's address is the variable: it has no name.</summary>
public sealed record ScriptVariablePointer(ScriptVariableBlock Block, int Offset) : ScriptPointer;

/// <summary>A C string. The builder pooled equal strings, so a script name read here is that script's own name
/// pointer, which is what WaitOnScriptEnd compares.</summary>
public sealed record ScriptStringPointer(string Text) : ScriptPointer;

/// <summary>A block of f32 variable slots.</summary>
public sealed class ScriptVariableBlock(byte[] data)
{
    public byte[] Data { get; set; } = data;
    public int SlotCount => Data.Length / 4;
    public float this[int offset] => BinaryPrimitives.ReadSingleBigEndian(Data.AsSpan(offset));
}

/// <summary>One element: a GameClass settings object whose class decides what it does.</summary>
public sealed class ScriptElement
{
    public const int NextOffset = 0x4, ElseOffset = 0x8;

    /// <summary>The settings bytes, class number at +0. Pointer words read 0 here; their targets are in <see cref="Pointers"/>.</summary>
    public byte[] Data { get; set; }
    public Dictionary<int, ScriptPointer> Pointers { get; } = [];

    public ScriptElement(byte[] data)
    {
        if (data.Length < 0x3C || data.Length % 4 != 0) throw new InvalidDataException($"element size 0x{data.Length:X}");
        Data = data;
    }

    public static ScriptElement Create(uint classId, int size)
    {
        var element = new ScriptElement(new byte[size]);
        BinaryPrimitives.WriteUInt32BigEndian(element.Data, classId);
        return element;
    }

    public uint ClassId => BinaryPrimitives.ReadUInt32BigEndian(Data);
    public uint Word(int offset) => BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(offset));
    public void SetWord(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(Data.AsSpan(offset), value);

    public ScriptElement? Next => Branch(NextOffset);
    public ScriptElement? Else => Branch(ElseOffset);

    public ScriptElement? Branch(int offset) => Pointers.GetValueOrDefault(offset) is ScriptElementPointer p ? p.Element : null;

    public void SetPointer(int offset, ScriptPointer? target)
    {
        SetWord(offset, 0);
        if (target == null) Pointers.Remove(offset);
        else Pointers[offset] = target;
    }

    public IEnumerable<(int Offset, ScriptElement Target)> Links() =>
        Pointers.Where(p => p.Value is ScriptElementPointer).OrderBy(p => p.Key)
            .Select(p => (p.Key, ((ScriptElementPointer)p.Value).Element));
}

/// <summary>A named script: its own run of the setup's element list, Start first and End last.</summary>
public sealed class ScriptRecord(string name, List<ScriptElement> elements)
{
    public const uint StartClass = 0xFB, EndClass = 0xFC;

    public string Name { get; set; } = name;
    public List<ScriptElement> Elements { get; } = elements;
    public ScriptElement Start => Elements[0];
    public ScriptElement End => Elements[^1];

    public static ScriptRecord Create(string name) =>
        new(name, [ScriptElement.Create(StartClass, 0x3C), ScriptElement.Create(EndClass, 0x3C)]);

    /// <summary>Adds an element before End, so End stays last as the builder left it.</summary>
    public void Add(ScriptElement element) => Elements.Insert(Elements.Count - 1, element);

    /// <summary>Removes an element and every branch to it.</summary>
    public void Remove(ScriptElement element)
    {
        Elements.Remove(element);
        foreach (var other in Elements)
            foreach (var (offset, target) in other.Links().ToList())
                if (target == element) other.SetPointer(offset, null);
    }
}

/// <summary>One setup: the scripts a gameplay group of the same name runs.</summary>
public sealed class ScriptSetup(string name)
{
    public string Name { get; set; } = name;
    public List<ScriptRecord> Scripts { get; } = [];
    /// <summary>Names SetCurrentSetup / IsSetupActive choose between by index ("None", "Attract Camera 1"...).</summary>
    public List<string> SubSetups { get; } = [];
    public uint Reserved { get; set; }
    public IEnumerable<ScriptElement> Elements => Scripts.SelectMany(s => s.Elements);
}

/// <summary>
/// Fall of Man's ps3script.dat (docs/reverse/rfom_script_format.md): IGHW 0.2 whose root section 0x25100 lists
/// setups; each setup lists scripts, and each script is a chain of ScriptElem settings objects joined by pointers.
/// Every pointer is a file offset named in the fix-up table after the last block.
///
/// Reading builds the object graph and refuses any file it could not write back the same: an unknown pointer target, a
/// fix-up it does not account for, a script whose elements are not its own run of the list. Writing lays the blocks out
/// in one fixed order (the shipped files each have their own), pools equal strings as the builder did, and keeps the
/// blocks nothing points at (one per setup in the retail files) unchanged.
/// </summary>
public sealed class FallOfManScripts
{
    public const uint RootSection = 0x25100;
    private const uint Magic = 0x49474857, Version = 0x00000002, Anonymous = 0xFFFFFFFF;
    private const int SetupSize = 0x60, ScriptSize = 0x20, SetupNameLength = 0x40;
    private static readonly Encoding Text = Encoding.Latin1;

    public List<ScriptSetup> Setups { get; } = [];
    public List<ScriptVariableBlock> Variables { get; } = [];
    /// <summary>Blocks nothing points at and that hold no pointer, written back as they are.</summary>
    public List<byte[]> Unreferenced { get; } = [];

    // ---------------------------------------------------------------- reading

    public static FallOfManScripts Parse(byte[] d)
    {
        if (d.Length < 0x10 || U32(d, 0) != Magic || U32(d, 4) != Version)
            throw new InvalidDataException("not an IGHW 0.2 file");
        int count = (int)U32(d, 8);
        if (count <= 0 || 0x10 + 16L * count > d.Length) throw new InvalidDataException("bad section count");
        var blocks = new List<(uint Id, int Offset, int Size)>(count);
        for (int i = 0; i < count; i++)
        {
            int o = 0x10 + 16 * i;
            var block = (U32(d, o), (int)U32(d, o + 4), (int)U32(d, o + 8));
            if (block.Item2 < 0 || block.Item3 < 0 || (long)block.Item2 + block.Item3 > d.Length) throw new InvalidDataException("section out of the file");
            blocks.Add(block);
        }
        var fixups = ReadFixups(d, blocks.Max(b => b.Offset + b.Size));
        var root = blocks.Find(b => b.Id == RootSection);
        if (root.Size < 8) throw new InvalidDataException("no 0x25100 root");

        var file = new FallOfManScripts();
        var accounted = new HashSet<int>();
        var referenced = new HashSet<int>();  // offsets of blocks something points into
        int BlockOf(uint at) => blocks.FindIndex(b => at >= b.Offset && at < b.Offset + Math.Max(b.Size, 1));
        uint Pointer(int at)
        {
            uint value = U32(d, at);
            if (value == 0) return 0;
            if (!fixups.Contains(at)) throw new InvalidDataException($"pointer at 0x{at:X} has no fix-up");
            accounted.Add(at);
            int b = BlockOf(value);
            if (b < 0) throw new InvalidDataException($"pointer at 0x{at:X} leads outside every block");
            referenced.Add(b);
            return value;
        }
        string CString(uint at)
        {
            int end = Array.IndexOf(d, (byte)0, (int)at);
            if (end < 0) throw new InvalidDataException($"string at 0x{at:X} has no end");
            return Text.GetString(d, (int)at, end - (int)at);
        }

        referenced.Add(blocks.IndexOf(root));
        uint setups = Pointer(root.Offset);
        int setupCount = (int)U32(d, root.Offset + 4);
        var elementAt = new Dictionary<uint, (ScriptSetup Setup, ScriptElement Element)>();
        var nameBlocks = new HashSet<int>();
        var pending = new List<(ScriptSetup Setup, uint Address, ScriptElement Element, int Size)>();
        for (int s = 0; s < setupCount; s++)
        {
            int at = (int)setups + s * SetupSize;
            uint scripts = Pointer(at), elements = Pointer(at + 4), sizes = Pointer(at + 8), names = Pointer(at + 0xC);
            int scriptCount = (int)U32(d, at + 0x10), elementCount = (int)U32(d, at + 0x14), nameCount = (int)U32(d, at + 0x18);
            var setup = new ScriptSetup(CString((uint)at + 0x20)) { Reserved = U32(d, at + 0x1C) };
            for (int i = 0; i < nameCount; i++)
            {
                uint name = Pointer((int)names + 4 * i);
                nameBlocks.Add(BlockOf(name));
                setup.SubSetups.Add(CString(name));
            }
            var list = new List<ScriptElement>(elementCount);
            for (int i = 0; i < elementCount; i++)
            {
                uint address = Pointer((int)elements + 4 * i);
                int size = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan((int)sizes + 2 * i));
                if ((long)address + size > d.Length) throw new InvalidDataException($"element {i} runs past the file");
                var element = new ScriptElement(d.AsSpan((int)address, size).ToArray());
                list.Add(element);
                elementAt[address] = (setup, element);
                pending.Add((setup, address, element, size));
            }
            var firsts = new List<(string Name, int First)>();
            for (int i = 0; i < scriptCount; i++)
            {
                int record = (int)scripts + ScriptSize * i;
                uint name = Pointer(record);
                nameBlocks.Add(BlockOf(name));
                for (int w = 4; w < ScriptSize; w += 4)
                    if (w != 8 && U32(d, record + w) != 0) throw new InvalidDataException($"script {i} of {setup.Name} has run-time state");
                firsts.Add((CString(name), (int)U32(d, record + 8)));
            }
            for (int i = 0; i < firsts.Count; i++)
            {
                int first = firsts[i].First, end = i + 1 < firsts.Count ? firsts[i + 1].First : list.Count;
                if ((i == 0 && first != 0) || end <= first || end > list.Count)
                    throw new InvalidDataException($"script {firsts[i].Name}'s elements are not its own run of the list");
                var own = list.GetRange(first, end - first);
                if (own[0].ClassId != ScriptRecord.StartClass || own[^1].ClassId != ScriptRecord.EndClass)
                    throw new InvalidDataException($"script {firsts[i].Name} does not run from Start to End");
                setup.Scripts.Add(new ScriptRecord(firsts[i].Name, own));
            }
            if (firsts.Count == 0 && list.Count > 0) throw new InvalidDataException($"setup {setup.Name} has elements but no scripts");
            file.Setups.Add(setup);
        }

        // Element pointers, once every element and every name block is known.
        var variables = new Dictionary<int, ScriptVariableBlock>();
        foreach (var (setup, address, element, size) in pending)
        {
            var script = setup.Scripts.First(s => s.Elements.Contains(element));
            for (int offset = 4; offset < size; offset += 4)
            {
                int at = (int)address + offset;
                if (!fixups.Contains(at)) continue;
                uint value = Pointer(at);
                element.SetWord(offset, 0);
                if (value == 0) continue;
                if (elementAt.TryGetValue(value, out var target))
                {
                    if (!script.Elements.Contains(target.Element)) throw new InvalidDataException($"element at 0x{address:X} branches into another script");
                    element.Pointers[offset] = new ScriptElementPointer(target.Element);
                    continue;
                }
                int b = BlockOf(value);
                if (nameBlocks.Contains(b)) { element.Pointers[offset] = new ScriptStringPointer(CString(value)); continue; }
                if (value % 4 != 0) throw new InvalidDataException($"element at 0x{address:X} +0x{offset:X} points at 0x{value:X}");
                if (!variables.TryGetValue(b, out var block))
                    variables[b] = block = new ScriptVariableBlock(d.AsSpan(blocks[b].Offset, blocks[b].Size).ToArray());
                element.Pointers[offset] = new ScriptVariablePointer(block, (int)value - blocks[b].Offset);
            }
        }
        foreach (int b in variables.Keys.Order()) file.Variables.Add(variables[b]);

        if (accounted.Count != fixups.Count)
            throw new InvalidDataException($"{fixups.Count - accounted.Count} fix-ups the reader does not account for");
        // A variable block must hold nothing else the reader knows: its bytes are written on their own.
        foreach (int b in variables.Keys)
            if (elementAt.Keys.Any(a => BlockOf(a) == b) || nameBlocks.Contains(b) || BlockOf(setups) == b)
                throw new InvalidDataException("a variable shares a block with other data");
        for (int b = 0; b < blocks.Count; b++)
        {
            if (referenced.Contains(b)) continue;
            var (_, offset, size) = blocks[b];
            if (fixups.Any(f => f >= offset && f < offset + size)) throw new InvalidDataException($"unreferenced block at 0x{offset:X} holds pointers");
            file.Unreferenced.Add(d.AsSpan(offset, size).ToArray());
        }
        return file;
    }

    private static HashSet<int> ReadFixups(byte[] d, int end)
    {
        // The table starts at the next 16-byte boundary; the PAD fill before it stops there, mid-word or not.
        int o = Align(end);
        if (o + 4 > d.Length) throw new InvalidDataException("no fix-up table");
        int count = (int)U32(d, o);
        if (count < 0 || o + 4 + 4L * count > d.Length) throw new InvalidDataException("bad fix-up table");
        var fixups = new HashSet<int>(count);
        for (int i = 0; i < count; i++) fixups.Add((int)U32(d, o + 4 + 4 * i));
        return fixups;
    }

    private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o));

    // ---------------------------------------------------------------- writing

    private sealed class Layout
    {
        public readonly List<(uint Id, byte[] Data)> Blocks = [];
        /// <summary>Pointer words to fill once blocks have offsets: (block, offset in it) -> (target block, offset in it).</summary>
        public readonly List<(int Block, int At, int Target, int TargetAt)> Pointers = [];

        public int Add(byte[] data, uint id = Anonymous)
        {
            Blocks.Add((id, data));
            return Blocks.Count - 1;
        }
    }

    public byte[] ToBytes()
    {
        var layout = new Layout();
        int setups = layout.Add(new byte[SetupSize * Setups.Count]);

        // Strings, pooled: script names, sub-setup names and element text, each once.
        var strings = new Dictionary<string, int>(StringComparer.Ordinal);
        var pool = new MemoryStream();
        int StringAt(string text)
        {
            if (strings.TryGetValue(text, out int at)) return at;
            at = (int)pool.Length;
            pool.Write(Text.GetBytes(text));
            pool.WriteByte(0);
            return strings[text] = at;
        }
        foreach (var setup in Setups)
        {
            foreach (var script in setup.Scripts) StringAt(script.Name);
            foreach (string name in setup.SubSetups) StringAt(name);
        }
        foreach (var element in Setups.SelectMany(s => s.Elements))
            foreach (var pointer in element.Pointers.Values)
                if (pointer is ScriptStringPointer s) StringAt(s.Text);

        var variableBlocks = new Dictionary<ScriptVariableBlock, int>();
        int stringBlock = -1;
        var stringPointers = new List<(int Block, int At, string Text)>();
        var elementPointers = new List<(int Block, int At, ScriptElement Target)>();
        var elementPlace = new Dictionary<ScriptElement, (int Block, int At)>();

        for (int s = 0; s < Setups.Count; s++)
        {
            var setup = Setups[s];
            var setupBytes = layout.Blocks[setups].Data;
            int at = s * SetupSize;
            var all = setup.Elements.ToList();
            if (Text.GetByteCount(setup.Name) >= SetupNameLength) throw new InvalidDataException($"setup name {setup.Name} is too long");
            Text.GetBytes(setup.Name).CopyTo(setupBytes, at + 0x20);
            Put(setupBytes, at + 0x10, (uint)setup.Scripts.Count);
            Put(setupBytes, at + 0x14, (uint)all.Count);
            Put(setupBytes, at + 0x18, (uint)setup.SubSetups.Count);
            Put(setupBytes, at + 0x1C, setup.Reserved);

            if (setup.Scripts.Count > 0)
            {
                var records = new byte[ScriptSize * setup.Scripts.Count];
                int block = layout.Add(records);
                layout.Pointers.Add((setups, at, block, 0));
                int first = 0;
                for (int i = 0; i < setup.Scripts.Count; i++)
                {
                    var script = setup.Scripts[i];
                    if (script.Elements.Count < 2 || script.Start.ClassId != ScriptRecord.StartClass || script.End.ClassId != ScriptRecord.EndClass)
                        throw new InvalidDataException($"script {script.Name} does not run from Start to End");
                    stringPointers.Add((block, ScriptSize * i, script.Name));
                    Put(records, ScriptSize * i + 8, (uint)first);
                    first += script.Elements.Count;
                }
            }
            if (all.Count > 0)
            {
                int list = layout.Add(new byte[4 * all.Count]);
                layout.Pointers.Add((setups, at + 4, list, 0));
                var bytes = new byte[all.Sum(e => e.Data.Length)];
                int elements = layout.Add(bytes);
                var sizes = new byte[2 * all.Count];
                int offset = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    var element = all[i];
                    if (element.Data.Length > ushort.MaxValue) throw new InvalidDataException("element too large");
                    element.Data.CopyTo(bytes, offset);
                    BinaryPrimitives.WriteUInt16BigEndian(sizes.AsSpan(2 * i), (ushort)element.Data.Length);
                    layout.Pointers.Add((list, 4 * i, elements, offset));
                    elementPlace[element] = (elements, offset);
                    foreach (var (field, pointer) in element.Pointers)
                    {
                        if (field < 4 || field + 4 > element.Data.Length || field % 4 != 0) throw new InvalidDataException($"pointer at +0x{field:X}");
                        Put(bytes, offset + field, 0);
                        switch (pointer)
                        {
                            case ScriptElementPointer e:
                                elementPointers.Add((elements, offset + field, e.Element));
                                break;
                            case ScriptStringPointer t:
                                stringPointers.Add((elements, offset + field, t.Text));
                                break;
                            case ScriptVariablePointer v:
                                if (!variableBlocks.TryGetValue(v.Block, out _)) variableBlocks[v.Block] = -1;
                                break;
                        }
                    }
                    offset += element.Data.Length;
                }
                int sizeBlock = layout.Add(sizes);
                layout.Pointers.Add((setups, at + 8, sizeBlock, 0));
            }
            if (setup.SubSetups.Count > 0)
            {
                int names = layout.Add(new byte[4 * setup.SubSetups.Count]);
                layout.Pointers.Add((setups, at + 0xC, names, 0));
                for (int i = 0; i < setup.SubSetups.Count; i++) stringPointers.Add((names, 4 * i, setup.SubSetups[i]));
            }
        }

        if (pool.Length > 0) stringBlock = layout.Add(pool.ToArray());
        foreach (var (block, at, text) in stringPointers) layout.Pointers.Add((block, at, stringBlock, strings[text]));
        foreach (var (block, at, target) in elementPointers)
        {
            if (!elementPlace.TryGetValue(target, out var place)) throw new InvalidDataException("a branch leads to an element no script holds");
            layout.Pointers.Add((block, at, place.Block, place.At));
        }
        // Every variable block is kept, used or not: its slots are the level's variables.
        foreach (var block in Variables) variableBlocks[block] = layout.Add(block.Data);
        foreach (var setup in Setups)
            foreach (var element in setup.Elements)
                foreach (var (field, pointer) in element.Pointers)
                    if (pointer is ScriptVariablePointer v)
                    {
                        if (variableBlocks[v.Block] < 0) throw new InvalidDataException("a variable outside the file's variable blocks");
                        if (v.Offset < 0 || v.Offset + 4 > v.Block.Data.Length) throw new InvalidDataException("a variable outside its block");
                        var place = elementPlace[element];
                        layout.Pointers.Add((place.Block, place.At + field, variableBlocks[v.Block], v.Offset));
                    }
        foreach (var block in Unreferenced) layout.Add(block);
        int root = layout.Add(new byte[8], RootSection);
        layout.Pointers.Add((root, 0, setups, 0));
        Put(layout.Blocks[root].Data, 4, (uint)Setups.Count);
        return Assemble(layout);
    }

    private static byte[] Assemble(Layout layout)
    {
        int count = layout.Blocks.Count;
        var offsets = new int[count];
        int position = 0x10 + 16 * count;
        for (int i = 0; i < count; i++)
        {
            position = Align(position);
            offsets[i] = position;
            position += layout.Blocks[i].Data.Length;
        }
        int fixupAt = Align(position);
        var fixups = layout.Pointers.Select(p => offsets[p.Block] + p.At).Order().ToList();
        if (fixups.Distinct().Count() != fixups.Count) throw new InvalidDataException("two pointers in one word");
        var d = new byte[Align(fixupAt + 4 + 4 * fixups.Count)];
        Put(d, 0, Magic);
        Put(d, 4, Version);
        Put(d, 8, (uint)count);
        for (int i = 0; i < count; i++)
        {
            int entry = 0x10 + 16 * i;
            Put(d, entry, layout.Blocks[i].Id);
            Put(d, entry + 4, (uint)offsets[i]);
            Put(d, entry + 8, (uint)layout.Blocks[i].Data.Length);
            layout.Blocks[i].Data.CopyTo(d, offsets[i]);
            int end = offsets[i] + layout.Blocks[i].Data.Length;
            Pad(d.AsSpan(end, (i + 1 < count ? offsets[i + 1] : fixupAt) - end));
        }
        foreach (var (block, at, target, targetAt) in layout.Pointers)
            Put(d, offsets[block] + at, (uint)(offsets[target] + targetAt));
        Put(d, fixupAt, (uint)fixups.Count);
        for (int i = 0; i < fixups.Count; i++) Put(d, fixupAt + 4 + 4 * i, (uint)fixups[i]);
        return d;
    }

    /// <summary>The gap before a block, filled as the builder did: "PAD0PAD1..." cut where the gap ends.</summary>
    private static void Pad(Span<byte> gap)
    {
        for (int i = 0; i < gap.Length; i++)
            gap[i] = (i % 4) switch { 0 => (byte)'P', 1 => (byte)'A', 2 => (byte)'D', _ => (byte)"0123456789ABCDEF"[i / 4 % 16] };
    }

    private static int Align(int value) => (value + 15) & ~15;

    private static void Put(byte[] d, int o, uint value) => BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(o), value);

    // ---------------------------------------------------------------- checks

    /// <summary>A description of everything the game reads, for comparing two files: equal text, same behaviour.</summary>
    public string Fingerprint()
    {
        var text = new StringBuilder();
        var variableIndex = Variables.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
        foreach (var setup in Setups)
        {
            text.Append($"setup {setup.Name} {setup.Reserved:X} [{string.Join('|', setup.SubSetups)}]\n");
            foreach (var script in setup.Scripts)
            {
                text.Append($" script {script.Name}\n");
                var index = script.Elements.Select((e, i) => (e, i)).ToDictionary(x => x.e, x => x.i);
                foreach (var element in script.Elements)
                {
                    text.Append("  ").Append(Convert.ToHexString(element.Data));
                    foreach (var (field, pointer) in element.Pointers.OrderBy(p => p.Key))
                        text.Append($" +{field:X}=").Append(pointer switch
                        {
                            ScriptElementPointer e => $"e{index[e.Element]}",
                            ScriptVariablePointer v => $"v{variableIndex[v.Block]}:{v.Offset:X}",
                            ScriptStringPointer s => $"s'{s.Text}'",
                            _ => "?",
                        });
                    text.Append('\n');
                }
            }
        }
        foreach (var v in Variables) text.Append("vars ").Append(Convert.ToHexString(v.Data)).Append('\n');
        foreach (var u in Unreferenced) text.Append("kept ").Append(Convert.ToHexString(u)).Append('\n');
        return text.ToString();
    }

    /// <summary>The bytes to save, checked: read back, they must describe the same scripts.</summary>
    public byte[] ToCheckedBytes()
    {
        byte[] bytes = ToBytes();
        if (Parse(bytes).Fingerprint() != Fingerprint()) throw new InvalidDataException("the written file reads back differently");
        return bytes;
    }
}
