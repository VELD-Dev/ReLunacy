using System.Buffers.Binary;

namespace ReLunacy.Engine.Assets.Audio;

/// <summary>
/// One Scream sound playing, the way A Crack in Time's Scream runs it (EBOOT grain dispatcher 0x979510, handlers
/// in the table at 0xDE3BFC; docs/reverse/acit_audio.md): the sound's grains run at 240 ticks a second, starting
/// tones (voices), child sounds, random picks, delays, loops, pitch bends, registers and LFOs. Voices are mixed to
/// 48 kHz stereo with their ADSR envelopes, pitch and pan. Plays until its grains and voices end; a looping sound
/// plays until <see cref="Stop"/>.
/// </summary>
public sealed class ScreamSound : ISoundSource
{
    public const int Rate = 48000;
    private const int TickSamples = Rate / 240;
    private const int MaxVoices = 96;

    private readonly ScreamBank _bank;
    private readonly Random _random;
    private readonly List<Handler> _handlers = [];
    private readonly Handler? _root;
    private float[] _mix = [];
    private int _tickLeft, _gotos;
    private bool _stopped;

    public int Channels => 2;
    public int SampleRate => Rate;

    /// <summary>True once every grain, voice and child sound has ended.</summary>
    public bool Finished => _handlers.Count == 0;

    public ScreamSound(ScreamBank bank, int sound, Random? random = null)
    {
        _bank = bank;
        _random = random ?? Random.Shared;
        if (sound < 0 || sound >= bank.SoundCount) return;
        lock (bank.Sync)
        {
            var record = bank.GetSound(sound);
            _root = Start(sound, record.Volume / 127f, Mod360(record.Pan), null);
        }
    }

    // ---------------------------------------------------------------- state

    /// <summary>A running sound (Scream's block sound handler): its grain position and what grains set.</summary>
    private sealed class Handler
    {
        public int Sound;
        public ScreamBank.Sound Record;
        public int Next, Countdown, ToPlay, ToSkip;
        public bool Skipping, Stopping, LoopedThisTick, ToneThisTick;
        public float Volume, Bend, LfoVolume = 1f, LfoBend;
        public int Pan, LfoPan, LfoPitch;
        public readonly sbyte[] Registers = new sbyte[4];
        public readonly Lfo?[] Lfos = new Lfo?[4];
        public readonly List<Voice> Voices = [];
        public readonly List<Handler> Children = [];
        public Handler? Parent;

        public bool Done => Next < 0 && Voices.Count == 0 && Children.Count == 0;
    }

    /// <summary>A playing tone.</summary>
    private sealed class Voice
    {
        public required ScreamBank.ToneSample Sample;
        public double Position, BaseRate;
        public int BendDown, BendUp, Pan;
        public float Volume;
        public Envelope Envelope;
        public bool Ended;
    }

    /// <summary>An LFO slot of a handler (EBOOT 0x97D640 sets it, 0x97DFE8 applies it).</summary>
    private sealed class Lfo
    {
        public int Target, Other, Shape, Flags, Depth, BaseDepth, Duty, Value, State;
        public uint Phase, Step, BaseStep;
    }

    // ---------------------------------------------------------------- output

    public int Read(Span<short> buffer)
    {
        lock (_bank.Sync)
        {
            int frames = buffer.Length / 2, written = 0;
            while (written < frames && _handlers.Count > 0)
            {
                if (_tickLeft == 0)
                {
                    Tick();
                    _tickLeft = TickSamples;
                    if (_handlers.Count == 0) break;
                }
                int n = Math.Min(_tickLeft, frames - written);
                Mix(buffer.Slice(written * 2, n * 2), n);
                written += n;
                _tickLeft -= n;
            }
            return written * 2;
        }
    }

    /// <summary>Stops as the game does (EBOOT 0x9828B8, 0x9779B8): child sounds stop, grains after an On Stop Marker
    /// run, voices are keyed off into their release. A second call silences everything.</summary>
    public void Stop()
    {
        lock (_bank.Sync)
        {
            if (_stopped)
            {
                _handlers.Clear();
                return;
            }
            _stopped = true;
            if (_root != null) StopHandler(_root);
        }
    }

    private void StopHandler(Handler h)
    {
        foreach (var child in h.Children.ToArray()) StopHandler(child);
        int marker = -1;
        for (int g = 0; g < h.Record.GrainCount; g++)
            if (_bank.GrainType(h.Record, g) == 43) { marker = g; break; }
        if (h.Stopping) return;
        if (marker >= 0)
        {
            h.Next = marker + 1 < h.Record.GrainCount ? marker + 1 : -1;
            h.Countdown = 1;
            h.Skipping = false;
        }
        else h.Next = -1;
        h.Stopping = true;
        foreach (var v in h.Voices) v.Envelope.KeyOff();
    }

    private void Mix(Span<short> output, int frames)
    {
        if (_mix.Length < frames * 2) _mix = new float[frames * 2];
        var mix = _mix.AsSpan(0, frames * 2);
        mix.Clear();
        foreach (var h in _handlers)
        {
            float bend = Math.Clamp(h.Bend + h.LfoBend, -1f, 1f);
            for (int vi = h.Voices.Count - 1; vi >= 0; vi--)
            {
                var v = h.Voices[vi];
                // Pitch (EBOOT 0x985EF8): the bend times the tone's range down or up, plus the LFO's pitch, in
                // 1/128 semitones, clamped to five octaves.
                double cents = Math.Clamp(bend * (bend < 0 ? v.BendDown : v.BendUp) + h.LfoPitch, -7680.0, 7680.0);
                double step = v.BaseRate * Math.Pow(2.0, cents / 1536.0) / Rate;
                float gain = v.Volume * h.Volume * h.LfoVolume;
                double angle = Mod360(v.Pan + h.Pan + h.LfoPan) * Math.PI / 180.0;
                double side = (Math.Sin(angle) + 1.0) * Math.PI / 4.0;
                float left = gain * (float)Math.Cos(side), right = gain * (float)Math.Sin(side);
                var pcm = v.Sample.Pcm;
                for (int i = 0; i < frames && !v.Ended; i++)
                {
                    int level = v.Envelope.Step();
                    if (v.Envelope.Off) { v.Ended = true; break; }
                    int at = (int)v.Position;
                    double frac = v.Position - at;
                    int next = at + 1 < pcm.Length ? at + 1 : v.Sample.LoopStart >= 0 ? v.Sample.LoopStart : at;
                    float s = (float)(pcm[at] + (pcm[next] - pcm[at]) * frac) * level / 32768f;
                    mix[i * 2] += s * left;
                    mix[i * 2 + 1] += s * right;
                    v.Position += step;
                    if (v.Position >= pcm.Length)
                    {
                        if (v.Sample.LoopStart >= 0 && pcm.Length > v.Sample.LoopStart)
                            v.Position = v.Sample.LoopStart + (v.Position - pcm.Length) % (pcm.Length - v.Sample.LoopStart);
                        else v.Ended = true;
                    }
                }
                if (v.Ended) h.Voices.RemoveAt(vi);
            }
        }
        for (int i = 0; i < mix.Length; i++) output[i] = (short)Math.Clamp(mix[i], short.MinValue, short.MaxValue);
    }

    // ---------------------------------------------------------------- ticks

    private Handler Start(int sound, float volume, int pan, Handler? parent)
    {
        var record = _bank.GetSound(sound);
        var h = new Handler { Sound = sound, Record = record, Volume = volume, Pan = pan, Parent = parent };
        // As EBOOT 0x978C38: the first grain waits its delay after the first tick.
        h.Next = record.GrainCount > 0 ? 0 : -1;
        h.Countdown = record.GrainCount > 0 ? _bank.GrainDelay(record, 0) + 1 : 0;
        parent?.Children.Add(h);
        _handlers.Add(h);
        return h;
    }

    private void Tick()
    {
        _gotos = 0;
        for (int i = 0; i < _handlers.Count; i++)
        {
            var h = _handlers[i];
            if (h.Next >= 0)
            {
                // EBOOT 0x977B10: count down; at zero run grains until one sets a delay. Stopping runs them at once.
                h.LoopedThisTick = h.ToneThisTick = false;
                h.Countdown--;
                for (int guard = 0; h.Next >= 0 && (h.Countdown <= 0 || h.Stopping) && guard < 256; guard++)
                    RunGrain(h);
            }
            UpdateLfos(h);
        }
        for (int i = _handlers.Count - 1; i >= 0; i--)
        {
            var h = _handlers[i];
            if (!h.Done) continue;
            h.Parent?.Children.Remove(h);
            _handlers.RemoveAt(i);
        }
    }

    /// <summary>EBOOT 0x979510: runs the next grain, then waits the following grain's delay plus what it returned.</summary>
    private void RunGrain(Handler h)
    {
        if (h.Next >= h.Record.GrainCount) { h.Next = -1; return; }
        int wait = Execute(h, h.Next);
        if (wait < 0) { h.Next = -1; return; }
        if (h.Skipping && --h.ToPlay == 0)
        {
            h.Skipping = false;
            h.Next += h.ToSkip;
        }
        h.Next++;
        if (h.Next < 0 || h.Next >= h.Record.GrainCount) { h.Next = -1; return; }
        h.Countdown = _bank.GrainDelay(h.Record, h.Next) + wait;
    }

    private int Execute(Handler h, int g)
    {
        var s = h.Record;
        switch (_bank.GrainType(s, g))
        {
            case 1 or 9: Tone(h, g); return 0;
            case 4: StartLfo(h, g); return 0;
            case 5: StartChild(h, g); return 0;
            case 6:
            {
                var d = _bank.GrainData(s, g, 0x10);
                if (d.IsEmpty) return 0;
                int index = BinaryPrimitives.ReadInt32BigEndian(d[0xC..]);
                foreach (var child in h.Children.ToArray())
                    if (child.Sound == index) StopHandler(child);
                return 0;
            }
            case 8: return Branch(h, g);
            case 22:
            {
                // Loop End: back to the Loop Start before it; a second loop in one tick waits a tick.
                int start = g - 1;
                while (start >= 0 && _bank.GrainType(s, start) != 21) start--;
                if (start < 0) return -1;
                h.Next = start - 1;
                if (h.LoopedThisTick) return 1;
                h.LoopedThisTick = true;
                return 0;
            }
            case 23:
            {
                // Loop Continue: leaves the loop (past its Loop End).
                int end = g + 1;
                while (end < s.GrainCount && _bank.GrainType(s, end) != 22) end++;
                if (end >= s.GrainCount) return -1;
                h.Next = end;
                return 0;
            }
            case 24: return -1;
            case 25:
            {
                // Random Play: one of `options` runs of `count` grains, never the last pick twice.
                if (h.Skipping) return -1;
                int options = _bank.GrainArg(s, g, 1), count = _bank.GrainArg(s, g, 2);
                if (options == 0 || count == 0) return 0;
                ref byte previous = ref _bank.GrainArg(s, g, 3);
                int pick = _random.Next(0x10000) % options;
                if (pick == (sbyte)previous && ++pick == options) pick = 0;
                previous = (byte)pick;
                Choose(h, pick, options, count);
                return 0;
            }
            case 26: return _random.Next(0x10000) % (_bank.GrainValue(s, g) + 1);
            case 27:
            {
                float r = Math.Clamp(_random.Next(0x10000) * 2f / 65535f - 1f, -1f, 1f);
                h.Bend = r * ((sbyte)_bank.GrainArg(s, g, 1) / 100f);
                return 0;
            }
            case 28: h.Bend = Math.Clamp(BendValue((sbyte)_bank.GrainArg(s, g, 1)), -1f, 1f); return 0;
            case 29: h.Bend = Math.Clamp(h.Bend + BendValue((sbyte)_bank.GrainArg(s, g, 1)), -1f, 1f); return 0;
            case 30: Register(h, (sbyte)_bank.GrainArg(s, g, 1)) = (sbyte)_bank.GrainArg(s, g, 2); return 0;
            case 31:
            {
                int low = (sbyte)_bank.GrainArg(s, g, 2), high = (sbyte)_bank.GrainArg(s, g, 3);
                int span = Math.Max(1, high - low + 1);
                Register(h, (sbyte)_bank.GrainArg(s, g, 1)) = (sbyte)(_random.Next(0x10000) % span + low);
                return 0;
            }
            case 32: { ref sbyte r = ref Register(h, (sbyte)_bank.GrainArg(s, g, 1)); r = (sbyte)Math.Min(r + 1, 127); return 0; }
            case 33: { ref sbyte r = ref Register(h, (sbyte)_bank.GrainArg(s, g, 1)); r = (sbyte)Math.Max(r - 1, -128); return 0; }
            case 34:
            {
                // Test Register: the next grain runs only if the register is <, ==, > or != the value.
                int r = Register(h, (sbyte)_bank.GrainArg(s, g, 1)), value = (sbyte)_bank.GrainArg(s, g, 3);
                bool run = _bank.GrainArg(s, g, 2) switch { 0 => r < value, 1 => r == value, 2 => r > value, 3 => r != value, _ => true };
                if (!run) h.Next++;
                return 0;
            }
            case 36: return GotoMarker(h, (sbyte)_bank.GrainArg(s, g, 1));
            case 37:
            {
                int low = (sbyte)_bank.GrainArg(s, g, 1), high = (sbyte)_bank.GrainArg(s, g, 2);
                return GotoMarker(h, _random.Next(0x10000) % Math.Max(1, high - low + 1) + low);
            }
            case 46:
            {
                var d = _bank.GrainData(s, g, 2);
                if (d.IsEmpty) return 0;
                int low = (sbyte)d[0], high = (sbyte)d[1];
                return GotoMarker(h, _random.Next(0x10000) % Math.Max(1, high - low + 1) + low);
            }
            case 38:
                // Wait For Voices: until this sound's voices and child sounds have ended.
                if (h.Voices.Count == 0 && h.Children.Count == 0 && !h.ToneThisTick) return 0;
                h.Next--;
                return 1;
            case 39:
            {
                // Play Cycle: the runs in turn.
                if (h.Skipping) return -1;
                int options = _bank.GrainArg(s, g, 1), count = _bank.GrainArg(s, g, 2);
                if (options == 0 || count == 0) return 0;
                ref byte current = ref _bank.GrainArg(s, g, 3);
                int pick = current % options;
                current = (byte)(pick + 1 == options ? 0 : pick + 1);
                Choose(h, pick, options, count);
                return 0;
            }
            case 40:
            {
                ref sbyte r = ref Register(h, (sbyte)_bank.GrainArg(s, g, 2));
                r = (sbyte)Math.Clamp(r + (sbyte)_bank.GrainArg(s, g, 1), -128, 127);
                return 0;
            }
            case 41: foreach (var v in h.Voices) v.Envelope.KeyOff(); return 0;
            case 42: h.Voices.Clear(); return 0;
            case 43: h.Next = s.GrainCount - 1; return 0;
            case 44: Register(h, (sbyte)_bank.GrainArg(s, g, 2)) = Register(h, (sbyte)_bank.GrainArg(s, g, 1)); return 0;
            case 47: return Delay(h, g);
            case 48: return RandomPlayEx(h, g);
            default: return 0;
        }
    }

    private void Choose(Handler h, int pick, int options, int count)
    {
        h.Next += pick * count;
        h.ToPlay = count + 1;
        h.ToSkip = (options - pick - 1) * count;
        h.Skipping = true;
    }

    private static float BendValue(int v) => v < 0 ? v / 128f : v / 127f;

    private ref sbyte Register(Handler h, int r) => ref r >= 0 ? ref h.Registers[r & 3] : ref _bank.GlobalRegisters[~r & 63];

    private int GotoMarker(Handler h, int marker)
    {
        if (++_gotos > 8)
        {
            h.Next--;
            return 1;
        }
        for (int g = 0; g < h.Record.GrainCount; g++)
            if (_bank.GrainType(h.Record, g) == 35 && (sbyte)_bank.GrainArg(h.Record, g, 1) == marker)
            {
                h.Next = g - 1;
                return 0;
            }
        return -1;
    }

    /// <summary>Delay (EBOOT 0x97CA48): data +0 mode, +1 register, +2 u16 range.</summary>
    private int Delay(Handler h, int g)
    {
        var d = _bank.GrainData(h.Record, g, 4);
        if (d.IsEmpty || d[0] == 1) return 0;
        if ((d[0] & 2) != 0)
        {
            int range = (d[0] & 4) == 0 ? BinaryPrimitives.ReadUInt16BigEndian(d[2..]) : (ushort)Register(h, (sbyte)d[1]);
            return range == 0 ? 0 : _random.Next(0x10000) % range;
        }
        return d[0] == 4 ? Register(h, (sbyte)d[1]) : 0;
    }

    /// <summary>Random Play Ex (EBOOT 0x97CCD8): data +0 options, +1 grains each, +2 mode (1 = no pick among the
    /// last +3 picks), +4 history index, +5..+20 history, +21 state.</summary>
    private int RandomPlayEx(Handler h, int g)
    {
        if (h.Skipping) return -1;
        var d = _bank.GrainData(h.Record, g, 24);
        if (d.IsEmpty) return 0;
        int options = (sbyte)d[0], count = (sbyte)d[1];
        if (options <= 0 || count <= 0) return 0;
        int pick = _random.Next(0x10000) % options;
        if (d[2] == 1)
        {
            int depth = Math.Min(Math.Min((int)d[3], 16), options - 1);
            if (d[21] == 2)
            {
                d.Slice(5, 16).Fill(0xFF);
                d[4] = 0;
                d[21] = 0;
            }
            else if (depth > 0)
            {
                var recent = new List<int>(depth);
                for (int k = 0; k < depth; k++)
                {
                    byte past = d[5 + ((d[4] - 1 - k) & 15)];
                    if (past != 0xFF) recent.Add(past);
                }
                if (depth == options - 1 && recent.Count == depth)
                {
                    for (pick = 0; pick < options && recent.Contains(pick); pick++) { }
                    pick %= options;
                    d[21] = 2;
                }
                else
                    for (int tries = 0; tries < 32 && recent.Contains(pick); tries++) pick = _random.Next(0x10000) % options;
            }
        }
        d[5 + (d[4] & 15)] = (byte)pick;
        d[4] = (byte)((d[4] + 1) & 15);
        Choose(h, pick, options, count);
        return 0;
    }

    // ---------------------------------------------------------------- grains that start things

    /// <summary>Start tone grain (EBOOT 0x97D820). Tone, 0x18 bytes: +0 priority, +1 s8 volume, +2 s8 center note,
    /// +3 fine, +4 s16 pan, +8 bend range down, +9 up (semitones), +A ADSR1, +C ADSR2, +E flags, +10 sample
    /// offset, +14 size.</summary>
    private void Tone(Handler h, int g)
    {
        var t = _bank.GrainData(h.Record, g, 0x18);
        if (t.IsEmpty) return;
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(t[0xE..]);
        if ((flags & 8) != 0) return;
        var sample = _bank.Sample(BinaryPrimitives.ReadUInt32BigEndian(t[0x10..]), (int)BinaryPrimitives.ReadUInt32BigEndian(t[0x14..]), flags);
        if (sample is not { Pcm.Length: > 0 }) return;
        if (_handlers.Sum(x => x.Voices.Count) >= MaxVoices) return;
        h.Voices.Add(new Voice
        {
            Sample = sample,
            BaseRate = ScreamBank.ToneRate((sbyte)t[2], t[3]),
            BendDown = (sbyte)t[8] * 128,
            BendUp = (sbyte)t[9] * 128,
            Volume = Level(h, (sbyte)t[1]) / 127f,
            Pan = TonePan(h, BinaryPrimitives.ReadInt16BigEndian(t[4..]), flags),
            Envelope = new Envelope(BinaryPrimitives.ReadUInt16BigEndian(t[0xA..]), BinaryPrimitives.ReadUInt16BigEndian(t[0xC..])),
        });
        h.ToneThisTick = true;
    }

    /// <summary>A tone volume (EBOOT 0x979A28): 0..127, or -1..-4 a register, -6.. a global register, -5 random.</summary>
    private int Level(Handler h, int v)
    {
        if (v >= 0) return v;
        int n = -v;
        int r = n < 5 ? h.Registers[n - 1] : n - 6 is >= 0 and < 64 ? _bank.GlobalRegisters[n - 6] : _random.Next(0x10000) & 0x7F;
        return Math.Max(0, r);
    }

    /// <summary>A tone pan (EBOOT 0x9797A0): degrees, or a register scaled to 360, -5 random; speaker pans (-20007..
    /// -20000, flag 0x400) count as the front.</summary>
    private int TonePan(Handler h, int p, ushort flags)
    {
        if ((flags & 0x400) != 0 || p is >= -20007 and <= -20000) return 0;
        if (p >= 0) return p % 360;
        int n = -p;
        if (n < 5) return Mod360(h.Registers[n - 1] * 360 / 127);
        if (n - 6 is >= 0 and < 64) return Mod360(_bank.GlobalRegisters[n - 6] * 360 / 127);
        return _random.Next(0x10000) % 360;
    }

    /// <summary>Start Child Sound (EBOOT 0x97A5A0). Data: +0 s32 volume, +4 s32 pan, +8 flags (low nibble: volume
    /// times this sound's / the child's / both / neither; bits 4-6 the same for pan), +C s32 sound index.</summary>
    private void StartChild(Handler h, int g)
    {
        var d = _bank.GrainData(h.Record, g, 0x10);
        if (d.IsEmpty) return;
        int volumeMode = d[8] & 0xF, panMode = (d[8] >> 4) & 7;
        if (h.Volume == 0f && volumeMode is 0 or 2) return;
        int index = BinaryPrimitives.ReadInt32BigEndian(d[0xC..]);
        if (index < 0 || index >= _bank.SoundCount) return;
        var child = _bank.GetSound(index);
        float volume = Math.Clamp(ChildValue(h, (sbyte)d[3], true), 0, 127) / 127f;
        volume *= volumeMode switch { 0 => h.Volume, 1 => child.Volume / 127f, 2 => child.Volume / 127f * h.Volume, _ => 1f };
        int pan = Mod360(ChildValue(h, BinaryPrimitives.ReadInt16BigEndian(d[6..]), false));
        pan += panMode switch { 0 => h.Pan, 1 => child.Pan, 2 => child.Pan + h.Pan, _ => 0 };
        Start(index, volume, Mod360(pan), h);
    }

    private int ChildValue(Handler h, int v, bool volume)
    {
        if (v >= 0) return v;
        if (v >= -4) return h.Registers[-v - 1];
        if (v == -5) return volume ? _random.Next(0x10000) & 0x7F : _random.Next(0x10000) % 360;
        return -6 - v < 64 ? _bank.GlobalRegisters[-6 - v] : 0;
    }

    /// <summary>Branch To Child Sound (EBOOT 0x97AE00): this sound becomes another one. Data: +9 keep the voices,
    /// +C s32 sound index.</summary>
    private int Branch(Handler h, int g)
    {
        var d = _bank.GrainData(h.Record, g, 0x10);
        if (d.IsEmpty) return -1;
        int index = BinaryPrimitives.ReadInt32BigEndian(d[0xC..]);
        if (index < 0 || index >= _bank.SoundCount) return -1;
        var record = _bank.GetSound(index);
        if (record.GrainCount == 0) return -1;
        if (d[9] == 0) h.Voices.Clear();
        h.Sound = index;
        h.Record = record;
        h.Volume = record.Volume / 127f;
        h.Pan = Mod360(record.Pan);
        h.Next = -1;
        h.Skipping = false;
        h.ToPlay = h.ToSkip = 0;
        return 0;
    }

    /// <summary>LFO grain (EBOOT 0x97D640). Data: +0 slot, +1 target (0 stops it; 1 volume, 2 pan, 3 pitch, 4 bend,
    /// 5 another LFO's rate, 6 its depth), +2 that LFO, +3 shape (1 sine, 2 square, 3 triangle, 4 saw, 5 random),
    /// +4 u16 duty, +6 u16 depth (1024 = full), +9 flags (1 invert, 2 random phase), +A u16 phase (2048 a cycle),
    /// +C u32 step a tick (0x8000000 a cycle).</summary>
    private void StartLfo(Handler h, int g)
    {
        var d = _bank.GrainData(h.Record, g, 0x10);
        if (d.IsEmpty) return;
        int slot = d[0] & 3;
        if (d[1] == 0)
        {
            h.Lfos[slot] = null;
            return;
        }
        var lfo = new Lfo
        {
            Target = d[1],
            Other = d[2] & 3,
            Shape = d[3],
            Flags = d[9],
            Depth = BinaryPrimitives.ReadUInt16BigEndian(d[6..]),
            Step = BinaryPrimitives.ReadUInt32BigEndian(d[0xC..]),
            Duty = d[3] == 2 ? BinaryPrimitives.ReadUInt16BigEndian(d[4..]) : 0,
        };
        lfo.BaseDepth = lfo.Depth;
        lfo.BaseStep = lfo.Step;
        lfo.Phase = (lfo.Flags & 2) == 0 ? (uint)BinaryPrimitives.ReadUInt16BigEndian(d[0xA..]) << 16 : (uint)(_random.Next(0x10000) & 0x7FF) << 16;
        if (lfo.Shape == 1)
        {
            lfo.Phase += 0x2000000;
            if (lfo.Phase > 0x7FFFFFF) lfo.Phase -= 0x8000000;
        }
        if (lfo.Shape == 5)
        {
            lfo.State = 1;
            lfo.Value = (_random.Next(0x10000) & 0x7FFF) * -(_random.Next(0x10000) & 1);
        }
        h.Lfos[slot] = lfo;
    }

    /// <summary>One tick of every LFO of a handler (EBOOT 0x97DDB0, 0x97DFE8).</summary>
    private void UpdateLfos(Handler h)
    {
        foreach (var lfo in h.Lfos)
        {
            if (lfo == null) continue;
            uint old = lfo.Phase;
            lfo.Phase += lfo.Step;
            if (lfo.Phase > 0x7FFFFFF) lfo.Phase -= 0x8000000;
            int w = Wave(lfo, (int)(old >> 16) & 0x7FF);
            if ((lfo.Flags & 1) != 0) w = -w;
            float amount = w / 32767f * (lfo.Depth / 1024f);
            switch (lfo.Target)
            {
                case 1: h.LfoVolume = Math.Clamp(1f - (w + 32767) / 65534f * (lfo.Depth / 1024f), 0f, 1f); break;
                case 2: h.LfoPan = Mod360((int)(amount * 360f)); break;
                case 3: h.LfoPitch = Math.Clamp((int)(amount * 7680f), -7680, 7680); break;
                case 4: h.LfoBend = Math.Clamp(amount, -1f, 1f); break;
                case 5 when h.Lfos[lfo.Other] is { } other:
                    other.Step = (uint)(other.BaseStep + (((int)((other.BaseStep >> 8) * (uint)lfo.Depth >> 10) * w >> 15) << 8));
                    break;
                case 6 when h.Lfos[lfo.Other] is { } other:
                    other.Depth = (ushort)(other.BaseDepth + ((other.BaseDepth * lfo.Depth >> 10) * w >> 15));
                    break;
            }
        }
    }

    /// <summary>The LFO shapes (EBOOT table 0xDE40D8) at a phase of 0..2047: -32767..32767.</summary>
    private int Wave(Lfo lfo, int p)
    {
        switch (lfo.Shape)
        {
            // A cosine table; the grain starts it a quarter cycle in.
            case 1: return (int)Math.Round(Math.Cos(p * Math.PI / 1024.0) * 32767.0);
            case 2: return p < lfo.Duty ? 0x7FFF : -0x7FFF;
            case 3:
                if (p < 0x200) return p * 0x7FFF / 0x200;
                if (p < 0x600) return 0x7FFF - (p * 0xFFFE - 0x2000000 + 0x400) / 0x400;
                return (p * 0x7FFF - 0x3000000 + 0x600) / 0x200 - 0x7FFF;
            case 4: return p < 0x400 ? p * 0x7FFF / 0x3FF : (p * 0x7FFF - 0x2000000 + 0x400) / 0x400 - 0x7FFF;
            case 5:
                if (p < 0x400)
                {
                    if (lfo.State == 0)
                    {
                        lfo.State = 1;
                        lfo.Value = (_random.Next(0x10000) & 0x7FFF) * -(_random.Next(0x10000) & 1);
                    }
                }
                else if (lfo.State == 1)
                {
                    lfo.State = 0;
                    lfo.Value = (_random.Next(0x10000) & 0x7FFF) * 2 - 0x7FFE;
                }
                return lfo.Value;
            default: return 0;
        }
    }

    private static int Mod360(int degrees) => (degrees % 360 + 360) % 360;

    // ---------------------------------------------------------------- envelope

    /// <summary>The SPU ADSR envelope a voice plays with (ADSR1 / ADSR2 as the PS2 SPU2; level 0..0x7FFF), run at
    /// 48 kHz.</summary>
    private struct Envelope(ushort adsr1, ushort adsr2)
    {
        private enum Phase { Attack, Decay, Sustain, Release, Off }

        private Phase _phase = Phase.Attack;
        private int _level, _counter;

        public readonly bool Off => _phase == Phase.Off;

        public void KeyOff()
        {
            if (_phase == Phase.Off) return;
            _phase = Phase.Release;
            _counter = 0;
        }

        public int Step()
        {
            switch (_phase)
            {
                case Phase.Attack:
                    Advance((adsr1 >> 8) & 0x7F, false, (adsr1 & 0x8000) != 0);
                    if (_level >= 0x7FFF) { _level = 0x7FFF; _phase = Phase.Decay; }
                    break;
                case Phase.Decay:
                    Advance(((adsr1 >> 4) & 0xF) << 2, true, true);
                    if (_level <= ((adsr1 & 0xF) + 1) * 0x800) _phase = Phase.Sustain;
                    break;
                case Phase.Sustain:
                    Advance((adsr2 >> 6) & 0x7F, (adsr2 & 0x4000) != 0, (adsr2 & 0x8000) != 0);
                    break;
                case Phase.Release:
                    Advance((adsr2 & 0x1F) << 2, true, (adsr2 & 0x20) != 0);
                    if (_level <= 0) { _level = 0; _phase = Phase.Off; }
                    break;
            }
            return _level;
        }

        // A rate of 0..127: a step of 7 - (rate & 3) up, or -8 + (rate & 3) down, every 2^(shift - 11) samples, or a
        // larger step every sample for shifts under 11; exponential slows rises above 0x6000 and scales falls.
        private void Advance(int rate, bool decreasing, bool exponential)
        {
            int shift = rate >> 2;
            int step = decreasing ? -8 + (rate & 3) : 7 - (rate & 3);
            step <<= Math.Max(0, 11 - shift);
            int cycles = 1 << Math.Max(0, shift - 11);
            if (exponential && !decreasing && _level > 0x6000) cycles *= 4;
            if (exponential && decreasing) step = (int)((long)step * _level >> 15);
            if (++_counter < cycles) return;
            _counter = 0;
            _level = Math.Clamp(_level + step, 0, 0x7FFF);
        }
    }
}
