using ReLunacy.Engine.Loading;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Utility;

public class LoadingProgress(string status, uint max, bool percentage)
{
    public string status = status;
    public uint current = 0;
    public uint max = max;
    public bool isPercentage = percentage;

    /// <summary>
    /// The second line under the bar: what the current phase is counting through ("1,024 / 3,211").
    ///
    /// Separate from <see cref="status"/>, which names the phase, because the two change at
    /// different rates - the label once per phase, this one constantly - and because the status bar
    /// has room for one of them and the loading window for both.
    /// </summary>
    public string? detail;

    public float Progress => current / (float)max;

    public float GetPercents() => current / (float)max * 100;
    public void SetStatus(string newStatus) => status = newStatus;
    public void SetProgress(uint prog) => current = prog;
    public void SetTotal(uint total)
    {
        if (total < 1) total = 1;
        if (total < current) total = current;
        max = total;
    }

    /// <summary>
    /// Applies one report in place, rather than handing the frame a replacement progress.
    ///
    /// Reporting used to allocate a new LoadingProgress per update and swap it in through
    /// LoadingFrame.UpdateProgress, which de-registered the old one and registered the new one with
    /// the task registry on every single report - a lock pair and a new status-bar sequence number
    /// per percent of a level load. Mutating is also what lets the caller hold one reference for the
    /// whole load and keep reporting into it from whichever thread it is on.
    /// </summary>
    public void Report(string newStatus, float fraction, string? newDetail = null)
    {
        status = newStatus;
        detail = newDetail;
        current = (uint)Math.Clamp(MathF.Round(fraction * max), 0f, max);
    }
}

/// <summary>
/// The localized name of each phase of a job, resolved once up front.
///
/// Up front because <see cref="LM.Get"/> inserts a blank entry for a key it has never seen, so
/// calling it from a worker - which is where every report comes from - would mutate the language
/// dictionary while the UI thread is reading it. Everything the workers need is a plain string by
/// the time they have it.
///
/// Generic over the phase enum because every long job in the editor reports the same way: a level
/// load (<see cref="LoadPhase"/>), a level export, an archive save. The keys are
/// <c>GUI_&lt;prefix&gt;_&lt;phase name&gt;</c>.
/// </summary>
public sealed class PhaseLabels<TPhase> where TPhase : struct, Enum
{
    private readonly Dictionary<TPhase, string> _labels;
    private readonly string _itemFormat;

    private PhaseLabels(Dictionary<TPhase, string> labels, string itemFormat)
    {
        _labels = labels;
        _itemFormat = itemFormat;
    }

    /// <summary>Call on the main thread, before the work starts.</summary>
    public static PhaseLabels<TPhase> Snapshot(string keyPrefix)
    {
        var labels = new Dictionary<TPhase, string>();
        foreach (TPhase phase in Enum.GetValues<TPhase>())
            labels[phase] = LM.Get($"GUI_{keyPrefix}_{phase}");
        return new PhaseLabels<TPhase>(labels, LM.Get("GUI_LoadPhase_Items"));
    }

    public string Label(TPhase phase) => _labels.TryGetValue(phase, out string? label) ? label : phase.ToString();

    /// <summary>The count line, or null for a phase that counts nothing (one indivisible step).</summary>
    public string? Items(long done, long total) =>
        total > 0 ? string.Format(_itemFormat, done, total) : null;

    /// <summary>Writes one report onto the bar. Called from worker threads.</summary>
    public void Apply(LoadingProgress bar, TPhase phase, float overall, long done = 0, long total = 0) =>
        bar.Report(Label(phase), overall, Items(done, total));
}
