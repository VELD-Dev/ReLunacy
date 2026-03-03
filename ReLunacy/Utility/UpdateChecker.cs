using System.Globalization;
using Newtonsoft.Json.Linq;
using ReLunacy.Frames;

namespace ReLunacy.Utility;

public class UpdateChecker
{
    [JsonObject]
    public sealed record class VersionsCompare()
    {
        public string url = "";
        public string html_url = "";
        public string permalink_url = "";
        public string diff_url = "";
        public string patch_url = "";
        public object base_commit = new object();
        public object merge_base_commit = new object();
        public string status = "same";
        public int ahead_by = 0;
        public int behind_by = 0;
        public int total_commits = 0;
        public JObject[] commits = [];
        public JObject[] files = [];
    }
    
    public static async Task<VersionsCompare> GetVersionsCompare(string newVers)
    {
        if (newVers == ProgramInfo.Version || Version.Parse(newVers) < Version.Parse(ProgramInfo.Version))
        {
            return new VersionsCompare();
        }

        try
        {
            using var handler = new HttpClientHandler();
            handler.UseDefaultCredentials = true;

            using var client = new HttpClient(handler);
            HttpRequestMessage requestMessage = new(HttpMethod.Get,
                $"https://api.github/com/repos/VELD-Dev/ReLunacy/compare/{ProgramInfo.Version}...{newVers}");
            requestMessage.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:47.0) Gecko/20100101 Firefox/47.0");

            HttpResponseMessage response = await client.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<VersionsCompare>(content);
        }
        catch (Exception e)
        {
            LunaLog.LogWarn($"Failed to get versions compare. {e}");
            return new VersionsCompare() {status = "error"};
        }
    }
    
    public static async void CheckUpdates()
    {
        try
        {
            using var handler = new HttpClientHandler();
            handler.UseDefaultCredentials = true;

            using var client = new HttpClient(handler);
            HttpRequestMessage requestMessage = new(HttpMethod.Get, "https://api.github.com/repos/VELD-Dev/ReLunacy/releases/latest");
            requestMessage.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:47.0) Gecko/20100101 Firefox/47.0");

            HttpResponseMessage response = await client.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync();

            JObject data = (JObject?)JsonConvert.DeserializeObject(content) ?? throw new Exception("Request failed to be read. Response content is not readable.");

            string? time = (string?)data["created_at"];
            string? url  = (string?)data["html_url"];
            string? newReleaseTag = (string?)data["tag_name"];

            if (newReleaseTag == null) return;
            if (time == null) return;
            if (url == null) return;
            var parsedReleaseTag = new Version(newReleaseTag);
            if (parsedReleaseTag > new Version(ProgramInfo.Version))
            {
                var timeParsed = DateTime.ParseExact(time, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                LunaLog.LogInfo($"An update is available: v{parsedReleaseTag} ({(DateTime.Now - timeParsed)} ago)");
                var updateFrame = new UpdateInfoFrame(url, newReleaseTag, timeParsed);
                LunaWindow.Instance.AddFrame(updateFrame);
            }
            else if(parsedReleaseTag < new Version(ProgramInfo.Version))
            {
                LunaLog.LogInfo($"You see to be on a very private and beta channel... Or you're a developer ? Tough times huh ?");
            }
            else
            {
                LunaLog.LogInfo("No update available.");
            }
        }
        catch(Exception e)
        {
            LunaLog.LogWarn($"Failed to check for updates. {e}");
        }
    }
}