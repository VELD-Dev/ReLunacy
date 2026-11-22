namespace ReLunacy.Engine.Assets.Audio;

/// <summary>
/// A sound as it plays, kept to be drawn: wraps a source, renders up to a head of seconds at once (all of a sound
/// that ends sooner), then reads on as it is played. What was rendered stays readable by frame (the whole take when
/// it fit in the head, else the last seconds of a ring), with the min / max of every block of
/// <see cref="PeakBlock"/> frames for a waveform.
/// </summary>
public sealed class SoundTake : ISoundSource
{
    public const int PeakBlock = 256;

    private readonly ISoundSource _source;
    private readonly object _sync = new();
    private readonly short[] _ring;
    private readonly float[] _peakMin, _peakMax;
    private readonly int _capacity;
    private long _written, _served;
    private bool _sourceEnded, _cut;

    public int Channels { get; }
    public int SampleRate { get; }
    public IReadOnlyList<SoundPart> Parts { get; }

    /// <summary>True when the source ended within the head: <see cref="Written"/> frames are the whole take.</summary>
    public bool Complete { get; }

    /// <param name="headSeconds">Rendered at once; a sound that ends within it is kept whole.</param>
    /// <param name="ringSeconds">Kept of a longer sound, the newest frames.</param>
    public SoundTake(ISoundSource source, double headSeconds, double ringSeconds)
    {
        _source = source;
        Parts = source is PcmSource pcm ? pcm.Parts : [];
        Channels = Math.Max(1, source.Channels);
        SampleRate = Math.Max(1, source.SampleRate);
        long headFrames = (long)(headSeconds * SampleRate);
        var head = new List<short>();
        var chunk = new short[4096 * Channels];
        while (head.Count / Channels < headFrames)
        {
            int n = source.Read(chunk);
            if (n <= 0) { _sourceEnded = true; break; }
            head.AddRange(chunk.AsSpan(0, n));
        }
        Complete = _sourceEnded;
        long frames = head.Count / Channels;
        long capacity = Complete ? frames : Math.Max(frames, (long)(ringSeconds * SampleRate));
        _capacity = (int)Math.Max(PeakBlock, (capacity + PeakBlock - 1) / PeakBlock * PeakBlock);
        _ring = new short[_capacity * Channels];
        _peakMin = new float[_capacity / PeakBlock];
        _peakMax = new float[_capacity / PeakBlock];
        Append(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(head));
    }

    /// <summary>Frames rendered so far.</summary>
    public long Written { get { lock (_sync) return _written; } }

    /// <summary>Frames handed to the player so far.</summary>
    public long Served { get { lock (_sync) return _served; } }

    /// <summary>The first frame still kept.</summary>
    public long Oldest { get { lock (_sync) return Math.Max(0, _written - _capacity); } }

    public int Read(Span<short> buffer)
    {
        lock (_sync)
        {
            if (_cut) return 0;
            int frames = buffer.Length / Channels, done = 0;
            while (done < frames)
            {
                if (_served < _written)
                {
                    int at = (int)(_served % _capacity);
                    int n = (int)Math.Min(Math.Min(_written - _served, frames - done), _capacity - at);
                    _ring.AsSpan(at * Channels, n * Channels).CopyTo(buffer.Slice(done * Channels));
                    _served += n;
                    done += n;
                    continue;
                }
                if (_sourceEnded) break;
                int want = Math.Min(frames - done, _capacity / 2);
                int got = _source.Read(buffer.Slice(done * Channels, want * Channels)) / Channels;
                if (got <= 0) { _sourceEnded = true; break; }
                Append(buffer.Slice(done * Channels, got * Channels));
                _served += got;
                done += got;
            }
            return done * Channels;
        }
    }

    /// <summary>Reuses a fully buffered take for playback from the beginning.</summary>
    public bool Rewind() => Complete && TrySeek(0);

    /// <summary>Moves playback within the frames still buffered by this take.</summary>
    public bool TrySeek(long frame)
    {
        lock (_sync)
        {
            if (frame < Math.Max(0, _written - _capacity) || frame > _written) return false;
            _served = frame;
            _cut = false;
            return true;
        }
    }

    /// <summary>Stops the source as the game stops a sound; what was rendered ahead is dropped, so its release
    /// follows at once. A whole take just ends.</summary>
    public void Stop()
    {
        lock (_sync)
        {
            if (Complete || _sourceEnded) { _cut = true; return; }
            _written = _served;
            // The block the cut falls in keeps only the peaks of what was heard.
            long start = _written / PeakBlock * PeakBlock;
            int block = (int)(start % _capacity) / PeakBlock;
            _peakMin[block] = _peakMax[block] = 0f;
            for (long frame = start; frame < _written; frame++)
            {
                float mono = Mono(frame);
                _peakMin[block] = frame == start ? mono : MathF.Min(_peakMin[block], mono);
                _peakMax[block] = frame == start ? mono : MathF.Max(_peakMax[block], mono);
            }
            _source.Stop();
        }
    }

    // Frames into the ring, with each block's peaks.
    private void Append(ReadOnlySpan<short> samples)
    {
        int frames = samples.Length / Channels;
        for (int f = 0; f < frames; f++)
        {
            long frame = _written + f;
            int at = (int)(frame % _capacity);
            float mono = 0f;
            for (int c = 0; c < Channels; c++)
            {
                short s = samples[f * Channels + c];
                _ring[at * Channels + c] = s;
                mono += s;
            }
            mono /= Channels * 32768f;
            int block = at / PeakBlock;
            if (frame % PeakBlock == 0) { _peakMin[block] = mono; _peakMax[block] = mono; }
            else
            {
                _peakMin[block] = MathF.Min(_peakMin[block], mono);
                _peakMax[block] = MathF.Max(_peakMax[block], mono);
            }
        }
        _written += frames;
    }

    // ---------------------------------------------------------------- reading back

    /// <summary>The lowest and highest of the channels' mix over frames [from, to), -1..1; zero outside what is kept.</summary>
    public (float Min, float Max) Range(long from, long to)
    {
        lock (_sync)
        {
            from = Math.Max(from, Math.Max(0, _written - _capacity));
            to = Math.Min(to, _written);
            if (to <= from) return (0f, 0f);
            float min = float.MaxValue, max = float.MinValue;
            long frame = from;
            while (frame < to)
            {
                // Whole blocks from their peaks, the ends sample by sample.
                if (frame % PeakBlock == 0 && frame + PeakBlock <= to)
                {
                    int block = (int)(frame % _capacity) / PeakBlock;
                    min = MathF.Min(min, _peakMin[block]);
                    max = MathF.Max(max, _peakMax[block]);
                    frame += PeakBlock;
                    continue;
                }
                float mono = Mono(frame);
                min = MathF.Min(min, mono);
                max = MathF.Max(max, mono);
                frame++;
            }
            return (min, max);
        }
    }

    /// <summary>One channel's peak and RMS over frames [from, to), 0..1.</summary>
    public (float Peak, float Rms) Level(long from, long to, int channel)
    {
        lock (_sync)
        {
            from = Math.Max(from, Math.Max(0, _written - _capacity));
            to = Math.Min(to, _written);
            if (to <= from || channel >= Channels) return (0f, 0f);
            float peak = 0f;
            double sum = 0;
            for (long frame = from; frame < to; frame++)
            {
                float s = _ring[(int)(frame % _capacity) * Channels + channel] / 32768f;
                peak = MathF.Max(peak, MathF.Abs(s));
                sum += s * s;
            }
            return (peak, (float)Math.Sqrt(sum / (to - from)));
        }
    }

    /// <summary>The channels' mix from <paramref name="from"/> into <paramref name="output"/>; zero where nothing is kept.</summary>
    public void Mix(long from, Span<float> output)
    {
        lock (_sync)
        {
            long oldest = Math.Max(0, _written - _capacity);
            for (int i = 0; i < output.Length; i++)
            {
                long frame = from + i;
                output[i] = frame >= oldest && frame < _written ? Mono(frame) : 0f;
            }
        }
    }

    private float Mono(long frame)
    {
        int at = (int)(frame % _capacity) * Channels;
        float sum = 0f;
        for (int c = 0; c < Channels; c++) sum += _ring[at + c];
        return sum / (Channels * 32768f);
    }
}
