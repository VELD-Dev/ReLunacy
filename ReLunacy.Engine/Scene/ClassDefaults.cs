using ReLunacy.Engine.Assets.Geometry;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Assets.Levels;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// A region's class-default settings: the block gp_prius keeps once per (kind, class) in 0x25030 and shares between
/// every instance of the class that was not edited away from it. A new Clue, controller or Moby starts from it, so it
/// does not carry another instance's values. Read once at load, from the instances whose settings block lies in that
/// section (<see cref="PlacedInstance{TAsset}.SharedSettings"/>, <see cref="VolumeClue.SharedSettings"/>,
/// <see cref="GameplayController.SharedSettings"/>), and kept as copies: editing an instance does not change them.
/// A class with no shared block in the region has no default (see <see cref="Moby"/>, <see cref="Clue"/>,
/// <see cref="Controller"/>), and a new instance of it falls back to a clone of another instance with its
/// references cleared. A Moby's class is its asset id; a Clue's and a controller's the class word of its settings.
/// </summary>
public sealed class ClassDefaults
{
    /// <param name="Settings">The class struct, as an instance's settings hold it.</param>
    /// <param name="References">The words of it naming instances (none in a retail default).</param>
    public sealed record Default(byte[] Settings, IReadOnlyList<ClueReference> References)
    {
        /// <summary>A copy for a new instance to own.</summary>
        public byte[] CloneSettings() => (byte[])Settings.Clone();
    }

    public static readonly ClassDefaults None = new([], [], []);

    private readonly Dictionary<ulong, Default> _mobys;
    private readonly Dictionary<uint, Default> _clues;
    private readonly Dictionary<uint, Default> _controllers;

    private ClassDefaults(Dictionary<ulong, Default> mobys, Dictionary<uint, Default> clues, Dictionary<uint, Default> controllers)
    {
        _mobys = mobys;
        _clues = clues;
        _controllers = controllers;
    }

    public static ClassDefaults From(Region region)
    {
        var mobys = new Dictionary<ulong, Default>();
        var clues = new Dictionary<uint, Default>();
        var controllers = new Dictionary<uint, Default>();
        foreach (var moby in region.MobyInstances.OfType<PlacedInstance<IMoby>>())
            if (moby.SharedSettings && moby.Settings.Length > 0)
                mobys.TryAdd(moby.Asset.Id, Copy(moby.Settings, moby.References));
        foreach (var clue in region.Volumes.Select(v => v.Clue))
            if (clue is { SharedSettings: true, Settings.Length: > 0 })
                clues.TryAdd(clue.Class, Copy(clue.Settings, clue.References));
        foreach (var controller in region.Controllers)
            if (controller is { SharedSettings: true, Settings.Length: > 0 })
                controllers.TryAdd(controller.Class, Copy(controller.Settings, controller.References));
        return new ClassDefaults(mobys, clues, controllers);
    }

    private static Default Copy(byte[] settings, IReadOnlyList<ClueReference> references) =>
        new((byte[])settings.Clone(), [.. references.Where(r => !r.InList)]);

    /// <summary>The default of the Moby class with this asset id, or null.</summary>
    public Default? Moby(ulong assetId) => _mobys.GetValueOrDefault(assetId);
    public Default? Clue(uint cls) => _clues.GetValueOrDefault(cls);
    public Default? Controller(uint cls) => _controllers.GetValueOrDefault(cls);

    public IReadOnlyCollection<ulong> MobyClassIds => _mobys.Keys;
    public IReadOnlyCollection<uint> ClueClassIds => _clues.Keys;
    public IReadOnlyCollection<uint> ControllerClassIds => _controllers.Keys;
}
