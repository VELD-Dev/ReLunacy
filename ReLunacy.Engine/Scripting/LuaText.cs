using System.Text;

namespace ReLunacy.Engine.Scripting;

/// <summary>
/// Converts between Lua source bytes and the text the editor works with.
///
/// Lua strings are byte strings, not text in any encoding, and the shipped scripts prove it: 28 of
/// the 524 use <c>0x85</c> as a line terminator, and one has a bare <c>0x85</c> inside a string
/// literal - <c>"the name of this\x85energy creature"</c>. Decoding that as UTF-8 is lossy, because
/// <c>0x85</c> is not valid UTF-8 on its own: it becomes U+FFFD, which encodes back to THREE bytes,
/// so recompiling the file silently lengthened a string constant and shifted everything after it.
///
/// Latin-1 is used because it is the one encoding that maps 0x00-0xFF onto U+0000-U+00FF exactly,
/// making the round-trip byte-transparent. That is a deliberate choice of fidelity over appearance:
/// a byte the game meant as something else may draw as an odd glyph, but it survives being loaded,
/// edited and written back, which is what matters for files that get recompiled into the archive.
/// </summary>
public static class LuaText
{
    public static string FromBytes(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    public static byte[] ToBytes(string text) => Encoding.Latin1.GetBytes(text);

    /// <summary>Reads a Lua source file without reinterpreting its bytes.</summary>
    public static string ReadFile(string path) => FromBytes(File.ReadAllBytes(path));
}
