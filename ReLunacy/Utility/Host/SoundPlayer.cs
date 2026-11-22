using SDL3;
using ReLunacy.Engine.Assets.Audio;

namespace ReLunacy.Utility;

/// <summary>
/// Plays one sound at a time on the default output (SDL3 audio, opened on first use), produced while it plays: a
/// thread keeps a fraction of a second queued. Starting a sound stops the one playing.
/// </summary>
public static class SoundPlayer
{
    private const double QueuedSeconds = 0.15, ChunkSeconds = 0.05;

    private static readonly object Lock = new();
    private static IntPtr _stream;
    private static ISoundSource? _source;
    private static object? _current;
    private static bool _initialised, _failed, _ended, _stopping;
    private static int _generation;

    /// <summary>What is playing (any key the caller chose), or null; null too while a stopped sound fades out.</summary>
    public static object? Current
    {
        get
        {
            lock (Lock)
            {
                if (_stream == IntPtr.Zero) return null;
                if (!_ended || SDL.GetAudioStreamQueued(_stream) > 0) return _stopping ? null : _current;
                StopLocked();
                return null;
            }
        }
    }

    /// <summary>What is playing, stopped sounds still fading out included, and the frames queued ahead of what is
    /// heard (to place a playback cursor).</summary>
    public static (object? Key, int QueuedFrames) Status
    {
        get
        {
            lock (Lock)
            {
                if (_stream == IntPtr.Zero || _source == null) return (null, 0);
                int queued = SDL.GetAudioStreamQueued(_stream);
                if (_ended && queued <= 0) { StopLocked(); return (null, 0); }
                return (_current, Math.Max(0, queued) / (Math.Max(1, _source.Channels) * 2));
            }
        }
    }

    public static bool Play(PcmSound sound, object key) => Play(new PcmSource(sound), key);

    public static bool Play(ISoundSource source, object key)
    {
        lock (Lock)
        {
            StopLocked();
            if (_failed) return false;
            if (!_initialised)
            {
                if (!SDL.InitSubSystem(SDL.InitFlags.Audio))
                {
                    Console.WriteLine($"Audio: {SDL.GetError()}");
                    _failed = true;
                    return false;
                }
                _initialised = true;
            }
            var spec = new SDL.AudioSpec { Format = SDL.AudioFormat.AudioS16LE, Channels = source.Channels, Freq = source.SampleRate };
            _stream = SDL.OpenAudioDeviceStream(SDL.AudioDeviceDefaultPlayback, in spec, null, IntPtr.Zero);
            if (_stream == IntPtr.Zero)
            {
                Console.WriteLine($"Audio: {SDL.GetError()}");
                return false;
            }
            _source = source;
            _current = key;
            _ended = _stopping = false;
            int generation = ++_generation;
            Feed(generation);
            SDL.ResumeAudioStreamDevice(_stream);
            StartFeeder(generation);
            return true;
        }
    }

    /// <summary>Jumps to a buffered frame, starting playback if this take is idle.</summary>
    public static bool TrySeek(SoundTake take, long frame)
    {
        lock (Lock)
        {
            if (!take.TrySeek(frame)) return false;
            if (!ReferenceEquals(_source, take)) return Play(take, take);

            // The queue still contains PCM from the old position, and the feeder may already
            // have reached the end of the source while that PCM is playing.
            if (!SDL.ClearAudioStream(_stream))
            {
                StopLocked();
                return false;
            }
            bool restartFeeder = _ended;
            int generation = restartFeeder ? ++_generation : _generation;
            _ended = _stopping = false;
            if (Feed(generation) && restartFeeder) StartFeeder(generation);
            return true;
        }
    }

    /// <summary>Stops the sound as the game would (its release and stop grains play); a second call cuts it.</summary>
    public static void Stop()
    {
        lock (Lock)
        {
            if (_source == null || _stopping) StopLocked();
            else
            {
                _source.Stop();
                _stopping = true;
            }
        }
    }

    private static void StartFeeder(int generation) =>
        new Thread(() => Run(generation)) { IsBackground = true, Name = "Sound" }.Start();

    private static void Run(int generation)
    {
        while (true)
        {
            Thread.Sleep(10);
            lock (Lock)
                if (generation != _generation || !Feed(generation)) return;
        }
    }

    // Tops the queue up; false once the source has ended (or another sound replaced it).
    private static bool Feed(int generation)
    {
        if (generation != _generation || _stream == IntPtr.Zero || _source is not { } source) return false;
        if (_ended) return false;
        int frameBytes = source.Channels * 2;
        int want = (int)(source.SampleRate * QueuedSeconds) * frameBytes;
        var chunk = new short[(int)(source.SampleRate * ChunkSeconds) * source.Channels];
        while (SDL.GetAudioStreamQueued(_stream) < want)
        {
            int n = source.Read(chunk);
            if (n <= 0)
            {
                _ended = true;
                SDL.FlushAudioStream(_stream);
                return false;
            }
            var bytes = new byte[n * 2];
            Buffer.BlockCopy(chunk, 0, bytes, 0, bytes.Length);
            if (!BitConverter.IsLittleEndian)
                for (int i = 0; i < bytes.Length; i += 2) (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
            SDL.PutAudioStreamData(_stream, bytes, bytes.Length);
        }
        return true;
    }

    private static void StopLocked()
    {
        if (_stream != IntPtr.Zero) SDL.DestroyAudioStream(_stream);
        _stream = IntPtr.Zero;
        _source = null;
        _current = null;
        _ended = _stopping = false;
        _generation++;
    }
}
