namespace ReLunacy.Engine.Assets.LevelElements;

/// <summary>
/// How the old engine's (Tools of Destruction, Quest for Booty) class fields name other instances: which fields do is the
/// game's .fields file (ClassFields); this decodes the word.
/// </summary>
public static class OldClassReferences
{
    /// <summary>The index a settings field's word names, or -1 for none: a Moby is <c>((i + 1) &lt;&lt; 16) | i</c> (0 none),
    /// every other kind the raw index (0xFFFFFFFF none).</summary>
    public static int Decode(string kind, uint word) => kind == "Moby"
        ? word != 0 && word >> 16 == (word & 0xFFFF) + 1 ? (int)(word & 0xFFFF) : -1
        : word == 0xFFFFFFFF ? -1 : (int)word;

    /// <summary>The Clue a Scent member names: <c>0x10000 | i</c>.</summary>
    public static int DecodeScentMember(uint word) => word >> 16 == 1 ? (int)(word & 0xFFFF) : -1;
}
