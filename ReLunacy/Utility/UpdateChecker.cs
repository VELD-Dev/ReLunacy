using System.Globalization;
using System.Net;
using Newtonsoft.Json.Linq;
using ReLunacy.Core;
using ReLunacy.Core.Frames.Modals;

namespace ReLunacy.Utility;

// Recreated after the LibLunacy/Bliss merge deleted the old implementation (see git history for
// the pre-rewrite version this is loosely based on) — now channel-aware per EditorSettings.
//
// Stable checks GitHub's normal "latest release" and compares its tag as a Version against
// ProgramInfo.Version, same as before.
//
// Nightly is a different shape entirely: .github/workflows/nightly.yml keeps a single rolling
// release under the "nightly" tag, and (per its allowUpdates/replacesArtifacts settings)
// accumulates every nightly build's artifacts there rather than replacing them — so a nightly tag
// has no single meaningful version number, just a growing list of dated, commit-stamped assets.
// Comparison instead extracts the commit hash baked into the newest asset's filename for this
// platform and compares it against NightlyBuildInfo.CommitHash (this build's own identity, which
// is null unless this binary is itself a nightly build the workflow stamped).
public static class UpdateChecker
{
    private const string RepoApiBase = "https://api.github.com/repos/VELD-Dev/ReLunacy";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true });
        client.DefaultRequestHeaders.Add("User-Agent", "ReLunacy-UpdateChecker");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        return client;
    }

    public static async void CheckUpdates(UpdateChannel channel)
    {
        try
        {
            if (channel == UpdateChannel.Nightly)
                await CheckNightly();
            else
                await CheckStable();
        }
        catch (Exception e)
        {
            LunaLog.LogWarn($"Failed to check for updates: {e}");
        }
    }

    private static async Task CheckStable()
    {
        using var client = CreateClient();
        var response = await client.GetAsync($"{RepoApiBase}/releases/latest");
        response.EnsureSuccessStatusCode();
        var data = JObject.Parse(await response.Content.ReadAsStringAsync());

        string? tag = (string?)data["tag_name"];
        string? url = (string?)data["html_url"];
        string? publishedAt = (string?)data["published_at"];
        if (tag == null || url == null || publishedAt == null) return;

        if (!TryParseVersion(tag, out var newVersion) || !TryParseVersion(ProgramInfo.Version, out var currentVersion))
        {
            LunaLog.LogWarn($"Could not compare release tag '{tag}' against current version '{ProgramInfo.Version}'.");
            return;
        }

        if (newVersion > currentVersion)
        {
            LunaLog.LogInfo($"A stable update is available: v{tag}");
            LunaWindow.Instance.AddFrame(new UpdateInfoFrame(url, tag, DateTime.Parse(publishedAt, CultureInfo.InvariantCulture)));
        }
        else
        {
            LunaLog.LogInfo("No stable update available.");
        }
    }

    private static async Task CheckNightly()
    {
        using var client = CreateClient();
        var response = await client.GetAsync($"{RepoApiBase}/releases/tags/nightly");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            LunaLog.LogInfo("No nightly release exists yet.");
            return;
        }
        response.EnsureSuccessStatusCode();
        var data = JObject.Parse(await response.Content.ReadAsStringAsync());

        string? url = (string?)data["html_url"];
        var assets = data["assets"] as JArray;
        if (url == null || assets == null || assets.Count == 0) return;

        // Filenames are "ReLunacy-nightly-{yyyy-MM-dd}.{shortCommit}.{platformRid}.{ext}" (see the
        // nightly workflow) — pick this platform's newest by filename, which sorts lexicographically
        // the same as chronologically thanks to the leading yyyy-MM-dd.
        string platformRid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        var latestForPlatform = assets
            .Where(a => ((string?)a["name"])?.Contains(platformRid) == true)
            .OrderByDescending(a => (string?)a["name"], StringComparer.Ordinal)
            .FirstOrDefault();
        if (latestForPlatform == null)
        {
            LunaLog.LogInfo($"No nightly build published for this platform ({platformRid}) yet.");
            return;
        }

        string assetName = (string)latestForPlatform["name"]!;
        string? remoteCommit = ExtractCommitHash(assetName);
        string? publishedAt = (string?)latestForPlatform["created_at"] ?? (string?)data["published_at"];
        if (remoteCommit == null) return;

        if (NightlyBuildInfo.CommitHash != null && remoteCommit == NightlyBuildInfo.CommitHash)
        {
            LunaLog.LogInfo("You're already on the latest nightly build.");
            return;
        }

        LunaLog.LogInfo($"A nightly update is available: {assetName}");
        LunaWindow.Instance.AddFrame(new UpdateInfoFrame(
            url, assetName,
            publishedAt != null ? DateTime.Parse(publishedAt, CultureInfo.InvariantCulture) : DateTime.Now,
            isNightly: true));
    }

    private static string? ExtractCommitHash(string assetName)
    {
        // ReLunacy-nightly-2026-07-25.abcdef1.win-x64.zip -> "abcdef1"
        var parts = assetName.Split('.');
        return parts.Length >= 2 ? parts[1] : null;
    }

    private static bool TryParseVersion(string raw, out Version version)
    {
        string cleaned = raw.TrimStart('v', 'V');
        bool ok = Version.TryParse(cleaned, out var parsed);
        version = parsed ?? new Version(0, 0);
        return ok;
    }
}
