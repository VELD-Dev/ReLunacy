using Newtonsoft.Json;

namespace ReLunacy.Utility.Localization;

[JsonObject(Description = "Language file", Id = "Language")]
public class Language
{
    public string LangName;

    [JsonIgnore]
    public string LangCode;
    [JsonIgnore]
    public string Filepath { get; set; }

    public readonly SortedDictionary<string, string> strings = new(new Dictionary<string, string>(), StringComparer.Ordinal);

    [JsonConstructor]
    public Language()
    {
        LangName = "fallback";
        LangCode = "en-fb";
        Filepath = "fallback.json";
    }

    public static Language? LoadFromFile(string filepath)
    {
        if (!File.Exists(filepath))
            throw new FileNotFoundException($"Language file {filepath} could not be found.");

        var res = JsonConvert.DeserializeObject<Language>(File.ReadAllText(filepath), new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented,
        });

        if (res == null) return null;

        res.Filepath = filepath;
        res.LangCode = Path.GetFileNameWithoutExtension(filepath);

        return res;
    }

    public void Save()
    {
        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(Filepath, json);
    }
}
