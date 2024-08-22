using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Utility;

public class UpdateChecker
{
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
            if (parsedReleaseTag > new Version(Program.Version))
            {
                var timeParsed = DateTime.ParseExact(time, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                LunaLog.LogInfo($"An update is available: v{parsedReleaseTag} ({(DateTime.Now - timeParsed)} ago)");
                var updateFrame = new UpdateInfoFrame(url, newReleaseTag, timeParsed);
                Window.Singleton.AddFrame(updateFrame);
            }
            else if(parsedReleaseTag < new Version(Program.Version))
            {
                LunaLog.LogInfo($"You see to be on a very private and beta channel... Or you're the developer ? Hey me ! How are you doing ?");
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
