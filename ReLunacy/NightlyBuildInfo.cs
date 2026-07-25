namespace ReLunacy;

// Rewritten by .github/workflows/nightly.yml right before a nightly build compiles, stamping in
// the short commit hash and build date baked into that build's release asset filename (see
// UpdateChecker.CheckNightly, which extracts the same info back out of the filename to compare).
// Left null here for every other build (local dev, stable releases) — nightly-update comparisons
// only make sense when the running binary actually knows which nightly build it is.
public static class NightlyBuildInfo
{
    public const string? CommitHash = null;
    public const string? BuildDate = null;
}
