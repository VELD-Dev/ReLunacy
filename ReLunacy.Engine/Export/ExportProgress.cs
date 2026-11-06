namespace ReLunacy.Engine.Export;

/// <summary>
/// The stages of an export, in the order they run.
///
/// Reported because the bar used to reach 100% as the last instance was gathered and then sit there
/// for as long again: a whole-level .glb spends a serious share of its time inside SharpGLTF's
/// <c>ToGltf2</c> and then writing a file that can be hundreds of megabytes, neither of which said
/// anything.
/// </summary>
public enum ExportPhase
{
    /// <summary>Walking the scene and building each asset's mesh.</summary>
    Meshes,
    /// <summary>Turning the built scene into a glTF model.</summary>
    Serializing,
    /// <summary>Writing the file (plus its .bin and textures, where the format has them).</summary>
    Writing,
}

/// <summary>One report from an export: which stage, how far through the whole job, and the stage's
/// own item counts where it has any.</summary>
public readonly record struct ExportProgressReport(ExportPhase Phase, float Overall, long Done, long Total);

/// <summary>Each export stage's share of the bar, and the arithmetic to map a stage-local fraction
/// onto the whole job. Rough shares rather than measured ones - unlike a level load, an export's
/// balance swings with the format and with how much of the level was ticked.</summary>
public static class ExportPhases
{
    private static (float Start, float End) Range(ExportPhase phase) => phase switch
    {
        ExportPhase.Meshes => (0f, 0.70f),
        ExportPhase.Serializing => (0.70f, 0.85f),
        _ => (0.85f, 1f),
    };

    /// <param name="within">How far through its own stage the work is, 0..1.</param>
    public static ExportProgressReport Report(ExportPhase phase, float within, long done = 0, long total = 0)
    {
        var (start, end) = Range(phase);
        return new ExportProgressReport(phase, start + (end - start) * Math.Clamp(within, 0f, 1f), done, total);
    }
}
