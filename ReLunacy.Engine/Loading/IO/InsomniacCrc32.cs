using System.Text;

namespace ReLunacy.Engine.Loading.IO;

/// <summary>
/// The CRC-32 the new-engine runtime hashes texture source paths with (QForce EBOOT 0x007A0A9C): the
/// standard reflected table (polynomial 0xEDB88320, the EBOOT's copy at 0x00C78B54), initial value
/// 0xFFFFFFFF and NO final inversion. A new-engine texture's 64-bit TUID is
/// (this CRC of its source path) &lt;&lt; 32 | its id - PROVEN on 16,231 / 17,256 QForce shader slots whose
/// texture the level holds under that exact key (the others share their pixels with another path or
/// live under another id, and the game finds them by the rules in TextureShaderLoader).
/// </summary>
public static class InsomniacCrc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    public static uint Compute(string text) => Compute(Encoding.UTF8.GetBytes(text));

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
