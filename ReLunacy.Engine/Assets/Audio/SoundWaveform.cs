using System.Buffers.Binary;

namespace ReLunacy.Engine.Assets.Audio;

/// <summary>
/// A sound's waveform for display: per bucket, the lowest and highest sample of any channel (-1..1), over the first
/// <see cref="Frames"/> frames of a take. <see cref="Complete"/> is false when the sound went on past what was rendered
/// (a loop): the waveform is then of its start.
/// </summary>
public sealed class SoundWaveform
{
    public const int DefaultBuckets = 1024;
    private static ReadOnlySpan<byte> Magic => "RLWF"u8;
    private const int Version = 1;

    public required float[] Min { get; init; }
    public required float[] Max { get; init; }
    public required long Frames { get; init; }
    public required int SampleRate { get; init; }
    public required int Channels { get; init; }
    public required bool Complete { get; init; }

    public int Buckets => Min.Length;
    public double Seconds => Frames / (double)Math.Max(1, SampleRate);

    /// <summary>Lowest and highest over the buckets that fall in [<paramref name="from"/>, <paramref name="to"/>) of
    /// 0..1 of the waveform, for drawing it at another width.</summary>
    public (float Min, float Max) Range(float from, float to)
    {
        int a = Math.Clamp((int)(from * Buckets), 0, Buckets - 1), b = Math.Clamp((int)MathF.Ceiling(to * Buckets), a + 1, Buckets);
        float min = 0f, max = 0f;
        for (int i = a; i < b; i++)
        {
            min = MathF.Min(min, Min[i]);
            max = MathF.Max(max, Max[i]);
        }
        return (min, max);
    }

    /// <summary>Renders <paramref name="source"/> for at most <paramref name="maxSeconds"/> and reduces it to
    /// <paramref name="buckets"/> peaks. Null when it gives nothing.</summary>
    public static SoundWaveform? Render(ISoundSource source, double maxSeconds, int buckets = DefaultBuckets)
    {
        int channels = Math.Max(1, source.Channels), rate = Math.Max(1, source.SampleRate);
        long limit = (long)(maxSeconds * rate) * channels;
        var samples = new List<short>();
        var chunk = new short[4096 * channels];
        bool ended = false;
        while (samples.Count < limit)
        {
            int n = source.Read(chunk);
            if (n <= 0) { ended = true; break; }
            samples.AddRange(chunk.AsSpan(0, (int)Math.Min(n, limit - samples.Count)));
        }
        source.Stop();
        if (samples.Count < channels) return null;
        return FromSamples(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(samples), channels, rate, ended, buckets);
    }

    /// <summary>The waveform of interleaved 16-bit samples.</summary>
    public static SoundWaveform FromSamples(ReadOnlySpan<short> samples, int channels, int sampleRate, bool complete, int buckets = DefaultBuckets)
    {
        long frames = samples.Length / channels;
        buckets = (int)Math.Max(1, Math.Min(buckets, frames));
        var min = new float[buckets];
        var max = new float[buckets];
        for (int b = 0; b < buckets; b++)
        {
            long from = frames * b / buckets, to = Math.Max(from + 1, frames * (b + 1) / buckets);
            short lo = 0, hi = 0;
            foreach (short s in samples.Slice((int)(from * channels), (int)((to - from) * channels)))
            {
                if (s < lo) lo = s;
                if (s > hi) hi = s;
            }
            min[b] = lo / 32768f;
            max[b] = hi / 32767f;
        }
        return new SoundWaveform { Min = min, Max = max, Frames = frames, SampleRate = sampleRate, Channels = channels, Complete = complete };
    }

    /// <summary>Compact bytes for a disk cache: peaks quantised to a signed byte.</summary>
    public byte[] Encode()
    {
        var bytes = new byte[4 + 4 + 8 + 4 + 4 + 4 + 4 + Buckets * 2];
        Magic.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), Version);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), Frames);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), Channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), Complete ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), Buckets);
        for (int i = 0; i < Buckets; i++)
        {
            bytes[32 + i * 2] = (byte)(sbyte)Math.Clamp(MathF.Round(Min[i] * 127f), -127f, 127f);
            bytes[33 + i * 2] = (byte)(sbyte)Math.Clamp(MathF.Round(Max[i] * 127f), -127f, 127f);
        }
        return bytes;
    }

    /// <summary>The waveform <see cref="Encode"/> wrote, or null.</summary>
    public static SoundWaveform? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 32 || !bytes[..4].SequenceEqual(Magic) || BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]) != Version) return null;
        int buckets = BinaryPrimitives.ReadInt32LittleEndian(bytes[28..]);
        if (buckets <= 0 || bytes.Length < 32 + buckets * 2) return null;
        var min = new float[buckets];
        var max = new float[buckets];
        for (int i = 0; i < buckets; i++)
        {
            min[i] = (sbyte)bytes[32 + i * 2] / 127f;
            max[i] = (sbyte)bytes[33 + i * 2] / 127f;
        }
        return new SoundWaveform
        {
            Min = min, Max = max,
            Frames = BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]),
            SampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]),
            Channels = BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]),
            Complete = BinaryPrimitives.ReadInt32LittleEndian(bytes[24..]) != 0,
        };
    }
}
