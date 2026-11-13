using System.Buffers.Binary;
using System.Text;

namespace ReLunacy.Engine.Text;

/// <summary>
/// A game's text in one language: core_text_tags.&lt;language&gt;.pkg (packed/game/global_cached.psarc on A Crack in
/// Time), every string the game shows by number - prompts, names, menus, mission text. A settings field of the
/// "text id" kind (a moon's name, a prompt) is an index into it.
///
/// The file is an old-style IGHW (version 0.2, four sections, pointer fix-ups at the end), read on A Crack in Time:
///
///   0x50  header (section 0x26000, 0x1C bytes): 0, pointers to the three blocks below, the two counts, the
///         language number (0 us, 1 gb, 2 fr, 3 de, 4 it, 6 nl, 8 es)
///   A     one u32 per text id, in id order: the 24-bit hash of the text's tag (id 0 holds 0: no text)
///   B     12 bytes per string, sorted by hash: hash, 0xFFFFFFFF, pointer to the string
///   C     the strings, UTF-8, zero-terminated, in B's order. Button glyphs are private-use code points (U+E004...)
///   end   the count of pointers in the file, then where each one is (three in the header, one per B entry)
///
/// Gaps between blocks are filled with the bytes "PAD0PAD1PAD2..." up to the next 16-byte boundary, and the file
/// ends with eight zero bytes. <see cref="ToBytes"/> writes exactly that: all seven retail languages come back
/// byte for byte (dev/tools/textprobe).
/// </summary>
public sealed class TextTable
{
    public sealed class Entry
    {
        /// <summary>The 24-bit hash table A holds for the id (the game's hash of the tag name).</summary>
        public uint Hash;
        /// <summary>The string, or null when the id has none (id 0).</summary>
        public string? Text;
    }

    /// <summary>The language number in the header.</summary>
    public uint Language { get; private set; }
    /// <summary>The strings by text id.</summary>
    public List<Entry> Entries { get; } = [];

    private const uint HeaderSection = 0x26000;

    public static TextTable Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x70 || BinaryPrimitives.ReadUInt32BigEndian(data) != 0x49474857)
            throw new InvalidDataException("Not a text table: no IGHW header.");
        if (BinaryPrimitives.ReadUInt32BigEndian(data[0x10..]) != HeaderSection)
            throw new InvalidDataException("Not a text table: its first section is not 0x26000.");
        int header = (int)BinaryPrimitives.ReadUInt32BigEndian(data[0x14..]);
        uint U(int at) => BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
        int a = (int)U(header + 4), b = (int)U(header + 8);
        int ids = (int)U(header + 0x10), strings = (int)U(header + 0x14);
        if (a + ids * 4L > data.Length || b + strings * 12L > data.Length)
            throw new InvalidDataException("Text table: its tables run past the end of the file.");

        var byHash = new Dictionary<uint, string>(strings);
        for (int i = 0; i < strings; i++)
        {
            uint hash = U(b + i * 12);
            int at = (int)U(b + i * 12 + 8);
            if (at >= data.Length) throw new InvalidDataException($"Text table: string {i} lies outside the file.");
            int end = data[at..].IndexOf((byte)0);
            byHash[hash] = Encoding.UTF8.GetString(data.Slice(at, end < 0 ? data.Length - at : end));
        }
        var table = new TextTable { Language = U(header + 0x18) };
        for (int i = 0; i < ids; i++)
        {
            uint hash = U(a + i * 4);
            table.Entries.Add(new Entry { Hash = hash, Text = byHash.GetValueOrDefault(hash) });
        }
        return table;
    }

    public byte[] ToBytes()
    {
        var strings = Entries.Where(e => e.Text != null).OrderBy(e => e.Hash).ToList();
        for (int i = 1; i < strings.Count; i++)
            if (strings[i].Hash == strings[i - 1].Hash)
                throw new InvalidDataException($"Two strings share the hash 0x{strings[i].Hash:X6}.");
        int a = 0x70;
        int b = Align(a + Entries.Count * 4);
        int c = Align(b + strings.Count * 12);
        var encoded = strings.Select(s => Encoding.UTF8.GetBytes(s.Text!)).ToList();
        int textSize = encoded.Sum(s => s.Length + 1);

        using var stream = new MemoryStream();
        var word = new byte[4];
        void W(uint value) { BinaryPrimitives.WriteUInt32BigEndian(word, value); stream.Write(word); }
        void Pad(int to)
        {
            // "PAD0PAD1PAD2...", cut where the boundary falls.
            for (int i = 0; stream.Length < to; i++)
                stream.WriteByte((byte)((i & 3) switch { 0 => 'P', 1 => 'A', 2 => 'D', _ => '0' + i / 4 % 10 }));
        }

        W(0x49474857); W(2); W(4); W(0);
        W(HeaderSection); W(0x50); W(0x1C); W(0);
        W(0xFFFFFFFF); W((uint)a); W((uint)(Entries.Count * 4)); W(0);
        W(0xFFFFFFFF); W((uint)b); W((uint)(strings.Count * 12)); W(0);
        W(0xFFFFFFFF); W((uint)c); W((uint)textSize); W(0);
        W(0); W((uint)a); W((uint)b); W((uint)c); W((uint)Entries.Count); W((uint)strings.Count); W(Language);
        Pad(a);
        foreach (var entry in Entries) W(entry.Hash);
        Pad(b);
        int at = c;
        for (int i = 0; i < strings.Count; i++)
        {
            W(strings[i].Hash); W(0xFFFFFFFF); W((uint)at);
            at += encoded[i].Length + 1;
        }
        Pad(c);
        foreach (var text in encoded) { stream.Write(text); stream.WriteByte(0); }
        Pad(Align((int)stream.Length));
        // Where the file's pointers are: the header's three, then each string's.
        W((uint)(3 + strings.Count));
        W(0x54); W(0x58); W(0x5C);
        for (int i = 0; i < strings.Count; i++) W((uint)(b + i * 12 + 8));
        W(0); W(0);
        return stream.ToArray();
    }

    private static int Align(int value) => (value + 15) & ~15;

    /// <summary>A hash for a new string that no entry uses: the 24 low bits of an FNV-1a of <paramref name="tag"/>,
    /// moved up until free. The game's own hash of a tag name is not known; any unique value finds its string.</summary>
    public uint FreeHash(string tag)
    {
        uint hash = 2166136261;
        foreach (char ch in tag) hash = (hash ^ ch) * 16777619;
        hash &= 0xFFFFFF;
        var used = Entries.Select(e => e.Hash).ToHashSet();
        while (hash == 0 || used.Contains(hash)) hash = (hash + 1) & 0xFFFFFF;
        return hash;
    }

    /// <summary>Private-use characters (the button glyphs, U+E000..U+F8FF) and control characters as {E004}, so a
    /// string can be read and typed in an editor whose font has none of them.</summary>
    public static string Escape(string text)
    {
        StringBuilder? builder = null;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            bool special = ch is >= '' and <= '' || (ch < ' ' && ch != '\n') || ch is '{' or '}';
            if (special && builder == null) builder = new StringBuilder(text, 0, i, text.Length + 16);
            if (special) builder!.Append('{').Append(((int)ch).ToString("X4")).Append('}');
            else builder?.Append(ch);
        }
        return builder?.ToString() ?? text;
    }

    /// <summary>The reverse of <see cref="Escape"/>; a brace group that is not four hex digits stays as typed.</summary>
    public static string Unescape(string text)
    {
        if (!text.Contains('{')) return text;
        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{' && i + 5 < text.Length && text[i + 5] == '}'
                && int.TryParse(text.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out int code))
            {
                builder.Append((char)code);
                i += 5;
            }
            else builder.Append(text[i]);
        }
        return builder.ToString();
    }
}
