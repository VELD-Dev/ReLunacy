using System.Buffers.Binary;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Assets.Audio;

/// <summary>
/// A level's Scream bank (SBlk version 3; docs/reverse/acit_audio.md): sounds, each a list of grains (the program
/// <see cref="ScreamSound"/> runs), and the tones' samples, read from resident_sound.dat and decoded on first use.
/// The block is a private copy: like the game, random grains keep their last picks in it.
/// </summary>
public sealed class ScreamBank
{
    private readonly byte[] _block;
    private readonly int _soundTable, _grainTable, _grainData;
    private readonly long _sampleData;
    private readonly (string Archive, string Entry) _file;
    private readonly Dictionary<(uint Offset, ushort Flags), ToneSample?> _samples = [];

    /// <summary>Taken while a sound of the bank runs: grains and samples are shared.</summary>
    internal readonly object Sync = new();

    /// <summary>Scream's global registers (negative register numbers), shared by every sound.</summary>
    internal readonly sbyte[] GlobalRegisters = new sbyte[64];

    public int SoundCount { get; }

    public ScreamBank(byte[] block, long sampleData, (string Archive, string Entry) file)
    {
        _block = block;
        _sampleData = sampleData;
        _file = file;
        SoundCount = BinaryPrimitives.ReadUInt16BigEndian(block.AsSpan(0x16));
        _soundTable = (int)BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(0x1C));
        _grainTable = (int)BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(0x20));
        _grainData = (int)BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(0x34));
        int fit = (block.Length - _soundTable) / 12;
        if (SoundCount > fit) SoundCount = Math.Max(0, fit);
    }

    // ---------------------------------------------------------------- records

    /// <summary>A sound (12 bytes): +0 volume 0..127, +1 volume group, +2 s16 pan in degrees, +4 grain count,
    /// +6 u16 flags, +8 offset of its first grain in the grain table.</summary>
    internal readonly record struct Sound(int Volume, int Group, int Pan, int GrainCount, int FirstGrain);

    internal Sound GetSound(int index)
    {
        int e = _soundTable + index * 12;
        int first = _grainTable + (int)BinaryPrimitives.ReadUInt32BigEndian(_block.AsSpan(e + 8));
        int count = _block[e + 4];
        if (first < 0 || first + count * 8 > _block.Length) count = 0;
        return new Sound(_block[e], (sbyte)_block[e + 1], BinaryPrimitives.ReadInt16BigEndian(_block.AsSpan(e + 2)), count, first);
    }

    /// <summary>A grain (8 bytes): the opcode word (type in the top byte, then three argument bytes, or the offset
    /// of its data in the grain data) and the ticks (1/240 s) to wait before it runs.</summary>
    internal int GrainType(in Sound sound, int grain) => _block[sound.FirstGrain + grain * 8];

    internal int GrainDelay(in Sound sound, int grain) => BinaryPrimitives.ReadInt32BigEndian(_block.AsSpan(sound.FirstGrain + grain * 8 + 4));

    /// <summary>Argument byte 1..3 of the opcode word.</summary>
    internal ref byte GrainArg(in Sound sound, int grain, int n) => ref _block[sound.FirstGrain + grain * 8 + n];

    /// <summary>The low 24 bits of the opcode word.</summary>
    internal int GrainValue(in Sound sound, int grain) => (int)(BinaryPrimitives.ReadUInt32BigEndian(_block.AsSpan(sound.FirstGrain + grain * 8)) & 0xFFFFFF);

    /// <summary>The grain's data in the grain data area (tones, child sounds, LFOs, random play history...).</summary>
    internal Span<byte> GrainData(in Sound sound, int grain, int length)
    {
        int at = _grainData + GrainValue(sound, grain);
        return at >= 0 && at + length <= _block.Length ? _block.AsSpan(at, length) : Span<byte>.Empty;
    }

    /// <summary>The first tone grain's sample of every sound (what the bank lists for a sound without running it).</summary>
    internal IEnumerable<(int Sound, ResidentSample Sample)> FirstTones()
    {
        for (int i = 0; i < SoundCount; i++)
        {
            var sound = GetSound(i);
            for (int g = 0; g < sound.GrainCount; g++)
            {
                if (GrainType(sound, g) is not (1 or 9)) continue;
                var tone = GrainData(sound, g, 0x18);
                if (tone.IsEmpty) break;
                yield return (i, new ResidentSample(_sampleData + BinaryPrimitives.ReadUInt32BigEndian(tone[0x10..]),
                    (int)BinaryPrimitives.ReadUInt32BigEndian(tone[0x14..]), (int)Math.Round(ToneRate((sbyte)tone[2], tone[3])),
                    BinaryPrimitives.ReadUInt16BigEndian(tone[0xE..])));
                break;
            }
        }
    }

    // ---------------------------------------------------------------- summary

    /// <summary>A grain type's name as the EBOOT spells it (docs/reverse/acit_audio.md section 6).</summary>
    public static string GrainName(int type) => type switch
    {
        0 or 20 => "Null", 1 or 9 => "Start Tone", 2 => "XREF ID", 3 => "XREF Num", 4 => "LFO",
        5 => "Start Child Sound", 6 => "Stop Child Sound", 7 => "Plugin", 8 => "Branch To Child Sound",
        10 => "Ducker", 11 => "Reverb", 21 => "Loop Start", 22 => "Loop End", 23 => "Loop Continue", 24 => "Stop Sound",
        25 => "Random Play", 26 => "Random Delay", 27 => "Random Pitchbend", 28 => "Set Pitchbend", 29 => "Add Pitchbend",
        30 => "Set Register", 31 => "Set Register Random", 32 => "Increment Register", 33 => "Decrement Register",
        34 => "Test Register", 35 => "Marker", 36 => "Goto Marker", 37 => "Goto Random Marker", 38 => "Wait For Voices",
        39 => "Play Cycle", 40 => "Add Register", 41 => "Key Off Voices", 42 => "Silence Voices", 43 => "On Stop Marker",
        44 => "Copy Register", 45 => "Stream", 46 => "Goto Random Marker Ex", 47 => "Delay", 48 => "Random Play Ex",
        49 or 50 => "Stream MultiTrack Volumes", 51 => "Wait For Sync Event",
        _ => $"Type {type}",
    };

    /// <summary>A pick among runs of grains: Random Play (25), Play Cycle (39) or Random Play Ex (48, Shuffle when
    /// it never repeats one of the last picks).</summary>
    public readonly record struct GrainChoice(int Type, int Options, int Each, bool Shuffle);

    /// <summary>What a sound's grains are, without running them: its volume and pan, how many grains of each type,
    /// its random picks, and the other sounds it starts or becomes.</summary>
    public sealed record SoundSummary(int Volume, int Pan, int Grains, IReadOnlyList<(int Type, int Count)> Types,
        IReadOnlyList<GrainChoice> Choices, IReadOnlyList<int> Children);

    public SoundSummary? Describe(int index)
    {
        if (index < 0 || index >= SoundCount) return null;
        lock (Sync)
        {
            var sound = GetSound(index);
            var types = new SortedDictionary<int, int>();
            var choices = new List<GrainChoice>();
            var children = new List<int>();
            for (int g = 0; g < sound.GrainCount; g++)
            {
                int type = GrainType(sound, g);
                // Types that share a name count as one.
                int shown = type switch { 9 => 1, 20 => 0, 50 => 49, _ => type };
                types[shown] = types.GetValueOrDefault(shown) + 1;
                switch (type)
                {
                    case 25 or 39:
                        choices.Add(new GrainChoice(type, GrainArg(sound, g, 1), GrainArg(sound, g, 2), false));
                        break;
                    case 48 when GrainData(sound, g, 4) is { IsEmpty: false } d:
                        choices.Add(new GrainChoice(type, (sbyte)d[0], (sbyte)d[1], d[2] == 1 && d[3] > 0));
                        break;
                    case 5 or 8 when GrainData(sound, g, 0x10) is { IsEmpty: false } d:
                        int child = BinaryPrimitives.ReadInt32BigEndian(d[0xC..]);
                        if (child >= 0 && child < SoundCount && !children.Contains(child)) children.Add(child);
                        break;
                }
            }
            return new SoundSummary(sound.Volume, sound.Pan, sound.GrainCount, [.. types.Select(t => (t.Key, t.Value))], choices, children);
        }
    }

    // ---------------------------------------------------------------- samples

    /// <summary>A tone's sample, decoded: 16-bit mono, and the sample it loops back to (-1: plays once).</summary>
    internal sealed record ToneSample(short[] Pcm, int LoopStart);

    /// <summary>
    /// The sample of a tone, read and decoded once. Tone flags (as the EBOOT's 0x979318): 0x80 = 16-bit big-endian
    /// PCM after a 16-byte header (+0 loop start, +4 sample count), else PS-ADPCM; 0x40 = loops (ADPCM: from the
    /// trailing 16-byte block -1, loop start sample, ... when there is one, else from the frame flagged 6);
    /// 0x2000 = loops from the start. The last 16 bytes are never audio.
    /// </summary>
    internal ToneSample? Sample(uint offset, int size, ushort flags)
    {
        var key = (offset, (ushort)(flags & 0x20C0));
        if (_samples.TryGetValue(key, out var cached)) return cached;
        var sample = ReadSample(_file, _sampleData + offset, size, flags);
        _samples[key] = sample;
        return sample;
    }

    internal static ToneSample? ReadSample((string Archive, string Entry) file, long offset, int size, ushort flags)
    {
        if (size is <= 16 or >= 32 << 20 || PsarcRanges.Read(file.Archive, file.Entry, offset, size) is not { } data ||
            data.Length != size) return null;
        return (flags & 0x80) != 0 ? Pcm16(data, flags) : Adpcm(data, flags);
    }

    private static ToneSample Pcm16(byte[] data, ushort flags)
    {
        int count = (data.Length - 16) / 2, loop = -1;
        if ((flags & 0x40) != 0)
        {
            loop = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
            count = Math.Min(count, (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)));
        }
        else if ((flags & 0x2000) != 0) loop = 0;
        var pcm = new short[Math.Max(0, count)];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(16 + i * 2));
        return new ToneSample(pcm, loop >= 0 && loop < pcm.Length ? loop : -1);
    }

    private static ToneSample Adpcm(byte[] data, ushort flags)
    {
        int end = data.Length - 16, loopFrame = -1;
        if ((flags & 0x40) != 0)
        {
            if (BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(end)) == -1)
                loopFrame = (int)(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(end + 4)) / 28);
            else
                for (int f = 0; f * 16 < end; f++)
                    if ((data[f * 16 + 1] & 6) == 6) { loopFrame = f; break; }
        }
        else if ((flags & 0x2000) != 0) loopFrame = 0;
        var pcm = PsAdpcm.Decode(data.AsSpan(0, end));
        int loop = loopFrame * 28;
        return new ToneSample(pcm, loopFrame >= 0 && loop < pcm.Length ? loop : -1);
    }

    /// <summary>The rate a tone plays at (EBOOT 0x9790F8): 48 kHz * 2^((60 - |note| + fine/128) / 12) for the signed
    /// center note and fine tune; fine below 127 counts from the semitone under.</summary>
    internal static double ToneRate(sbyte note, int fine)
    {
        int semitones = 60 - Math.Abs((int)note);
        if (fine < 127) semitones--;
        else fine -= 127;
        return 48000.0 * (69450.0 / 65536.0) * Math.Pow(2.0, (semitones + (fine & 0x7F) / 128.0) / 12.0);
    }
}
