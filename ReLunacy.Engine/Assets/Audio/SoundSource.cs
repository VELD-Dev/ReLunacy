namespace ReLunacy.Engine.Assets.Audio;

/// <summary>Audio produced while it plays: 16-bit samples, channels interleaved.</summary>
public interface ISoundSource
{
    int Channels { get; }
    int SampleRate { get; }

    /// <summary>Fills <paramref name="buffer"/> (whole frames); returns the samples written, 0 once it has ended.</summary>
    int Read(Span<short> buffer);

    /// <summary>Asks it to end as the game stops a sound: releases and stop grains may still play.</summary>
    void Stop();
}

/// <summary>A decoded sound played once.</summary>
public sealed class PcmSource(PcmSound sound) : ISoundSource
{
    private int _at;

    public int Channels => sound.Channels;
    public int SampleRate => sound.SampleRate;
    internal IReadOnlyList<SoundPart> Parts => sound.Parts;

    public int Read(Span<short> buffer)
    {
        int n = Math.Min(buffer.Length / Channels * Channels, sound.Samples.Length - _at);
        if (n <= 0) return 0;
        sound.Samples.AsSpan(_at, n).CopyTo(buffer);
        _at += n;
        return n;
    }

    public void Stop() => _at = sound.Samples.Length;
}
