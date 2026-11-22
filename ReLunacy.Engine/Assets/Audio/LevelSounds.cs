namespace ReLunacy.Engine.Assets.Audio;

/// <summary>Where a sound's sample lives: the level's resident bank (resident_sound.dat) or its streaming file
/// (streaming_sound.dat).</summary>
public enum SoundBank { Resident, Streamed }

/// <summary>
/// A sound a level can play by id (the SoundSpecs enum: what SoundEmitter, SoundEnvironment and
/// play_explicit_sound_spec name). The id reaches a sound effect of the level's fxconduit_packed.dat, which names one
/// of resident_sound.dat's sounds; see docs/reverse/acit_audio.md.
/// </summary>
/// <param name="Id">The SoundSpecs id.</param>
/// <param name="SpecName">Its SoundSpecs name (AMB_MUSEUM_Bathroom), or empty when the script has none.</param>
/// <param name="Name">The bank's name for the sound (amb_bathroom_dko).</param>
/// <param name="BankIndex">The Scream sound (resident) or the stream (streamed).</param>
/// <param name="Group">Its SoundGroups id (mixer group).</param>
/// <param name="NameIndex">Its entry of resident_sound.dat's name table (<see cref="LevelSounds.BankSounds"/>).</param>
public sealed record LevelSound(int Id, string SpecName, string Name, SoundBank Bank, int BankIndex, int Group, int NameIndex = -1);

/// <summary>One sound of the level's banks (an entry of resident_sound.dat's name table), whether a SoundSpecs id
/// reaches it or not.</summary>
/// <param name="NameIndex">Its entry of the name table.</param>
/// <param name="Group">The SoundGroups id of the first conduit effect naming it; -1 when none does.</param>
public sealed record BankSound(int NameIndex, string Name, SoundBank Bank, int BankIndex, int Group);

/// <summary>Where a resident sound's first tone is in resident_sound.dat, and its rate.</summary>
public readonly record struct ResidentSample(long Offset, int Size, int SampleRate, ushort Flags = 0);

public enum SoundPartKind { Intro, Loop, Outro, Segment }

/// <summary>A section of a decoded song, measured in sample frames across all channels.</summary>
public readonly record struct SoundPart(SoundPartKind Kind, int StartFrame, int FrameCount);

/// <summary>Decoded audio: 16-bit samples, channels interleaved.</summary>
public sealed record PcmSound(short[] Samples, int Channels, int SampleRate)
{
    public IReadOnlyList<SoundPart> Parts { get; init; } = [];
    public double Seconds => Samples.Length / (double)Math.Max(1, Channels) / Math.Max(1, SampleRate);
}

/// <summary>
/// The sounds an opened level can play, by SoundSpecs id, and where their samples are: read once by
/// <see cref="Loading.Readers.SoundReader"/>, samples decoded on demand.
/// </summary>
public sealed class LevelSounds
{
    private readonly Dictionary<int, LevelSound> _byId;
    private readonly IReadOnlyDictionary<int, string> _specNames;
    private readonly ILookup<int, LevelSound> _byName;

    public LevelSounds(IReadOnlyList<LevelSound> sounds, IReadOnlyDictionary<int, string> specNames,
        ScreamBank? scream, IReadOnlyDictionary<int, ResidentSample> resident, IReadOnlyList<long> streamOffsets,
        (string Archive, string Entry)? residentFile, (string Archive, string Entry)? streamingFile,
        IReadOnlyList<BankSound>? bankSounds = null, IReadOnlyDictionary<int, string>? groupNames = null,
        IReadOnlySet<int>? streamContinuations = null, IReadOnlySet<int>? streamLoops = null)
    {
        Sounds = sounds;
        _byId = sounds.ToDictionary(s => s.Id);
        _byName = sounds.ToLookup(s => s.NameIndex);
        _specNames = specNames;
        BankSounds = bankSounds ?? [];
        GroupNames = groupNames ?? new Dictionary<int, string>();
        Scream = scream;
        Resident = resident;
        StreamOffsets = streamOffsets;
        StreamContinuations = streamContinuations ?? new HashSet<int>();
        StreamLoops = streamLoops ?? new HashSet<int>();
        ResidentFile = residentFile;
        StreamingFile = streamingFile;
    }

    /// <summary>Every sound the level has, by id.</summary>
    public IReadOnlyList<LevelSound> Sounds { get; }

    /// <summary>The resident sounds' Scream bank, or null.</summary>
    public ScreamBank? Scream { get; }

    /// <summary>Each resident sound's first tone.</summary>
    public IReadOnlyDictionary<int, ResidentSample> Resident { get; }
    public IReadOnlyList<long> StreamOffsets { get; }
    internal IReadOnlySet<int> StreamContinuations { get; }
    internal IReadOnlySet<int> StreamLoops { get; }
    public (string Archive, string Entry)? ResidentFile { get; }
    public (string Archive, string Entry)? StreamingFile { get; }

    /// <summary>Every sound of the banks, by name table entry.</summary>
    public IReadOnlyList<BankSound> BankSounds { get; }

    /// <summary>The SoundGroups enum of /scripts/soundgroups.lua: id -> name.</summary>
    public IReadOnlyDictionary<int, string> GroupNames { get; }

    public string GroupLabel(int group, string noGroup = "No group")
    {
        if (group < 0) return noGroup;
        if (!GroupNames.TryGetValue(group, out var name)) return group.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (name.StartsWith("SOUND_GROUP_EX_", StringComparison.Ordinal)) return name["SOUND_GROUP_EX_".Length..];
        if (name.StartsWith("SOUND_GROUP_", StringComparison.Ordinal)) return name["SOUND_GROUP_".Length..];
        return name;
    }

    public LevelSound? Find(int id) => _byId.GetValueOrDefault(id);

    /// <summary>Every SoundSpecs id of the level that plays a bank sound, by id.</summary>
    public IEnumerable<LevelSound> IdsOf(BankSound sound) => _byName[sound.NameIndex];

    /// <summary>The SoundSpecs name of any id, in the level or not.</summary>
    public string? SpecName(int id) => _specNames.GetValueOrDefault(id);
}
