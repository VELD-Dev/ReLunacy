using System.Globalization;
using System.Net;
using Newtonsoft.Json.Linq;
using ReLunacy.Core;
using ReLunacy.Core.Frames.Modals;

namespace ReLunacy.Utility;

/// <summary>One commit in the range between the running build and the available update, as listed
/// by GitHub's compare API. <see cref="Message"/> is only the summary line; <see cref="ShortSha"/>
/// links to the full commit at <see cref="Url"/>.</summary>
public readonly record struct CommitInfo(string ShortSha, string Message, string Url);

// Channel-aware per EditorSettings. Stable picks the highest release tag by SemVersion and compares
// it against ProgramInfo.Version. Nightly has no single version number (the "nightly" tag
// accumulates every build's dated, commit-stamped assets), so comparison extracts the commit
// hash from the newest asset's filename and compares it against NightlyBuildInfo.CommitHash.
//
// Prereleases are part of the stable channel's ladder: 0.05 -> 0.06-alpha.1 -> 0.06-beta.1 -> 0.06.
// That falls out of taking the highest version rather than being special-cased - SemVersion already
// ranks 0.06-beta.1 above 0.05 and below 0.06, so a prerelease is only ever offered while nothing
// higher exists, and the final release supersedes it the moment it is published. To keep
// prereleases away from stable users instead, filter the list below to !v.IsPrerelease unless the
// running build is itself a prerelease.
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
        if (!SemVersion.TryParse(ProgramInfo.Version, out var currentVersion))
        {
            LunaLog.LogWarn($"Could not read the running version '{ProgramInfo.Version}' as a version.");
            return;
        }

        // The whole list, not /releases/latest: GitHub defines "latest" as the newest release that
        // is NOT a prerelease, so an alpha or beta is invisible there and an alpha build would never
        // be offered anything until the final release shipped.
        var response = await client.GetAsync($"{RepoApiBase}/releases?per_page=30");
        response.EnsureSuccessStatusCode();
        var releases = JArray.Parse(await response.Content.ReadAsStringAsync());

        JObject? best = null;
        var newVersion = default(SemVersion);
        foreach (var entry in releases.OfType<JObject>())
        {
            if ((bool?)entry["draft"] == true) continue;
            // Tags that are not versions are skipped rather than warned about: the nightly channel
            // publishes under the fixed tag "nightly" and shares this list.
            if (!SemVersion.TryParse((string?)entry["tag_name"], out var candidate)) continue;
            if (best == null || candidate > newVersion)
            {
                best = entry;
                newVersion = candidate;
            }
        }

        if (best == null)
        {
            LunaLog.LogWarn("No release tag in the repository could be read as a version.");
            return;
        }

        string? tag = (string?)best["tag_name"];
        string? url = (string?)best["html_url"];
        string? publishedAt = (string?)best["published_at"];
        string? body = (string?)best["body"];
        if (tag == null || url == null || publishedAt == null) return;

        if (newVersion > currentVersion)
        {
            LunaLog.LogInfo($"An update is available: v{tag}" + (newVersion.IsPrerelease ? " (prerelease)" : ""));
            // Commits between the running release's tag and the new one.
            var commits = await FetchCommitsAsync(client, ProgramInfo.Version, tag);
            LunaWindow.Instance.AddFrame(new UpdateInfoFrame(url, tag, DateTime.Parse(publishedAt, CultureInfo.InvariantCulture), changelog: body, commits: commits));
        }
        else
        {
            LunaLog.LogInfo($"No update available; v{ProgramInfo.Version} is current.");
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
        string? body = (string?)data["body"];
        var assets = data["assets"] as JArray;
        if (url == null || assets == null || assets.Count == 0) return;

        // Filenames are "ReLunacy-nightly-{yyyy-MM-dd}.{shortCommit}.{platformRid}.{ext}"; sorting
        // lexicographically also sorts chronologically thanks to the leading yyyy-MM-dd.
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
        // Nightly builds have no changelog body, so the commit list is the "what's new". Compare
        // from this build's own commit when known, otherwise from the latest stable tag.
        string baseRef = NightlyBuildInfo.CommitHash ?? ProgramInfo.Version;
        var commits = await FetchCommitsAsync(client, baseRef, remoteCommit);
        LunaWindow.Instance.AddFrame(new UpdateInfoFrame(
            url, assetName,
            publishedAt != null ? DateTime.Parse(publishedAt, CultureInfo.InvariantCulture) : DateTime.Now,
            isNightly: true, changelog: body, commits: commits));
    }

    /// <summary>Lists the commits in (baseRef, headRef] via GitHub's compare API, newest-first.
    /// Any failure is logged and yields an empty list.</summary>
    private static async Task<List<CommitInfo>> FetchCommitsAsync(HttpClient client, string baseRef, string headRef)
    {
        var result = new List<CommitInfo>();
        if (string.IsNullOrEmpty(baseRef) || string.IsNullOrEmpty(headRef)) return result;

        try
        {
            var response = await client.GetAsync($"{RepoApiBase}/compare/{baseRef}...{headRef}");
            if (!response.IsSuccessStatusCode)
            {
                LunaLog.LogWarn($"Could not list commits {baseRef}...{headRef}: {(int)response.StatusCode} {response.ReasonPhrase}");
                return result;
            }

            var data = JObject.Parse(await response.Content.ReadAsStringAsync());
            if (data["commits"] is not JArray commits) return result;

            foreach (var commit in commits)
            {
                string sha = (string?)commit["sha"] ?? "";
                string message = (string?)commit["commit"]?["message"] ?? "";
                string commitUrl = (string?)commit["html_url"] ?? "";
                // Commit messages are "summary\n\nbody"; only the summary line is wanted here.
                string summary = message.Split('\n', 2)[0].Trim();
                result.Add(new CommitInfo(sha.Length >= 7 ? sha[..7] : sha, summary, commitUrl));
            }

            // GitHub returns the range oldest-first; show the newest commit at the top.
            result.Reverse();
        }
        catch (Exception e)
        {
            LunaLog.LogWarn($"Failed to list commits {baseRef}...{headRef}: {e.Message}");
        }

        return result;
    }

    private static string? ExtractCommitHash(string assetName)
    {
        // ReLunacy-nightly-2026-07-25.abcdef1.win-x64.zip -> "abcdef1"
        var parts = assetName.Split('.');
        return parts.Length >= 2 ? parts[1] : null;
    }

}
