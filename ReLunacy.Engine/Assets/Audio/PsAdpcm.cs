namespace ReLunacy.Engine.Assets.Audio;

/// <summary>
/// Sony's PS-ADPCM (VAG): 16-byte frames of a shift / filter byte, a flag byte and 28 four-bit samples. Bit 0 of the
/// flags ends the sound; a frame whose flags are 7 holds no audio.
/// </summary>
public static class PsAdpcm
{
    private static readonly int[] F0 = [0, 60, 115, 98, 122];
    private static readonly int[] F1 = [0, 0, -52, -55, -60];

    /// <summary>Decodes <paramref name="channels"/> channels that alternate every <paramref name="interleave"/>
    /// bytes (one channel: the frames in order) into interleaved 16-bit samples.</summary>
    public static short[] Decode(ReadOnlySpan<byte> data, int channels = 1, int interleave = 0x10)
    {
        channels = Math.Max(1, channels);
        if (channels == 1) interleave = data.Length;
        int stride = interleave * channels;
        int framesPerBlock = interleave / 16;
        var output = new List<short>(data.Length / 16 * 28);
        var history = new (int S1, int S2)[channels];
        var frame = new short[channels][];
        for (int c = 0; c < channels; c++) frame[c] = new short[28];
        bool ended = false;
        for (int block = 0; !ended && block * stride < data.Length; block++)
            for (int f = 0; !ended && f < framesPerBlock; f++)
            {
                bool any = false;
                for (int c = 0; c < channels; c++)
                {
                    int at = block * stride + c * interleave + f * 16;
                    Array.Clear(frame[c]);
                    if (at + 16 > data.Length) { ended = true; continue; }
                    var bytes = data.Slice(at, 16);
                    int flags = bytes[1];
                    if (flags == 7) { ended = true; continue; }
                    DecodeFrame(bytes, frame[c], ref history[c]);
                    any = true;
                    if ((flags & 1) != 0) ended = true;
                }
                if (!any) break;
                for (int i = 0; i < 28; i++)
                    for (int c = 0; c < channels; c++) output.Add(frame[c][i]);
            }
        return [.. output];
    }

    private static void DecodeFrame(ReadOnlySpan<byte> bytes, short[] samples, ref (int S1, int S2) history)
    {
        int shift = bytes[0] & 0xF, filter = Math.Min(bytes[0] >> 4, 4);
        if (shift > 12) shift = 9;
        int s1 = history.S1, s2 = history.S2;
        for (int i = 0; i < 28; i++)
        {
            int b = bytes[2 + i / 2];
            int nibble = (i & 1) == 0 ? b & 0xF : b >> 4;
            int value = (short)(nibble << 12) >> shift;
            value += (s1 * F0[filter] + s2 * F1[filter] + 32) >> 6;
            value = Math.Clamp(value, short.MinValue, short.MaxValue);
            samples[i] = (short)value;
            s2 = s1;
            s1 = value;
        }
        history = (s1, s2);
    }
}
