using System.Globalization;

namespace ReLunacy.Utility;

/// <summary>
/// A version as the release tags actually write them, with prereleases ordered correctly:
///
///     0.05  &lt;  0.06-alpha.1  &lt;  0.06-alpha.2  &lt;  0.06-beta.1  &lt;  0.06
///
/// <see cref="System.Version"/> cannot do this. It refuses to parse anything with a prerelease
/// suffix at all, so the update check used to bail out with a warning the moment either side of the
/// comparison was a tag like "0.06-alpha.1" - which means an alpha build could never be told that
/// the final release had arrived.
///
/// Comparison follows Semantic Versioning 2.0.0 §11: the numeric core first, and when those are
/// equal a version WITH a prerelease is lower than the same version without one (0.06-alpha
/// precedes 0.06). Prerelease identifiers are compared field by field, numeric ones numerically,
/// numeric ranking below alphanumeric, and a shorter run of equal fields ranking lower
/// (0.06-alpha &lt; 0.06-alpha.1). Build metadata after '+' is ignored, as the spec requires.
///
/// Missing components read as 0, so "0.06" and "0.06.0" compare equal - the tags use two-component
/// versions and ProgramInfo.Version matches them.
/// </summary>
public readonly struct SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>The dot-separated identifiers after '-', or empty for a stable release.</summary>
    public string[] Prerelease { get; }

    public bool IsPrerelease => Prerelease.Length > 0;

    private SemVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    /// <summary>
    /// Parses a release tag or version string. Tolerates a leading "v", a two- or three-component
    /// core, a "-prerelease" suffix and "+build" metadata. Returns false when the core is not
    /// numeric, which is what keeps a tag like "nightly" from being read as a version.
    /// </summary>
    public static bool TryParse(string? raw, out SemVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string text = raw.Trim().TrimStart('v', 'V');

        // Build metadata takes no part in precedence (SemVer 2.0.0 §10), so drop it before anything
        // else - including before the prerelease split, since it may contain a '-'.
        int plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];

        string core = text;
        var prerelease = Array.Empty<string>();
        int dash = text.IndexOf('-');
        if (dash >= 0)
        {
            core = text[..dash];
            string tail = text[(dash + 1)..];
            if (tail.Length == 0) return false;
            prerelease = tail.Split('.');
            // "0.06-" or "0.06-alpha..1": an empty identifier is not a version.
            if (prerelease.Any(string.IsNullOrEmpty)) return false;
        }

        string[] parts = core.Split('.');
        if (parts.Length is 0 or > 3) return false;
        var numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;

        version = new SemVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    public int CompareTo(SemVersion other)
    {
        int core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        // Same core: a prerelease is LOWER than the release it leads to. This is the rule that makes
        // an alpha build see the final 0.06 as an update rather than as the same version.
        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;
        if (!other.IsPrerelease) return -1;

        int shared = Math.Min(Prerelease.Length, other.Prerelease.Length);
        for (int i = 0; i < shared; i++)
        {
            int field = CompareIdentifier(Prerelease[i], other.Prerelease[i]);
            if (field != 0) return field;
        }
        // All shared fields equal: fewer fields ranks lower, so alpha < alpha.1.
        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }

    /// <summary>Numeric identifiers compare numerically and rank below alphanumeric ones; two
    /// alphanumerics compare by ASCII order (SemVer 2.0.0 §11).</summary>
    private static int CompareIdentifier(string a, string b)
    {
        bool aNum = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out int an);
        bool bNum = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out int bn);
        if (aNum && bNum) return an.CompareTo(bn);
        if (aNum) return -1;
        if (bNum) return 1;
        return string.CompareOrdinal(a, b);
    }

    public bool Equals(SemVersion other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Prerelease));

    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;
    public static bool operator ==(SemVersion a, SemVersion b) => a.CompareTo(b) == 0;
    public static bool operator !=(SemVersion a, SemVersion b) => a.CompareTo(b) != 0;

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? $"-{string.Join('.', Prerelease)}" : "");
}
