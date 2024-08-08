namespace ReLunacy.Utility;

public class EditorSettings
{
    public bool DebugMode;
    public float RenderDistance;
    public float MoveSpeed;
    public float MaxSpeed;
    public uint MSAA_Level;
    public VSyncMode VSyncMode;
    public bool OverlayFramerate;
    public bool OverlayLevelStats;
    public bool OverlayProfiler;
    public float OverlayOpacity;
    public Dictionary<string, string> CustomShaders;

    [JsonIgnore]
    public string SettingsFilePath { get; private set; }

    [JsonConstructor]
    public EditorSettings()
    {
        DebugMode = false;
        RenderDistance = 300f;
        MoveSpeed = 5f;
        MaxSpeed = MoveSpeed * (4f / 3f);
        VSyncMode = VSyncMode.Off;
        OverlayFramerate = true;
        OverlayLevelStats = false;
        OverlayProfiler = false;
        OverlayOpacity = 0.35f;
        
        CustomShaders = [];
    }

    public static EditorSettings? LoadFromFile(string path)
    {
        EditorSettings? settingsToLoad;
        if(File.Exists(path))
        {
            settingsToLoad = JsonConvert.DeserializeObject<EditorSettings>(path);
            settingsToLoad.SettingsFilePath = path;
        }
        else
        {
            settingsToLoad = null;
        }
        return settingsToLoad;
    }

    public void SaveSettingsToFile()
    {
        string output = JsonConvert.SerializeObject(this, Formatting.Indented);

        if (SettingsFilePath == null)
            throw new IOException("The settings file path does not exist! This shouldn't happen.");

        File.WriteAllText(SettingsFilePath, output);
    }

    public void ReloadSettings()
    {
        JsonConvert.PopulateObject(File.ReadAllText(SettingsFilePath), this);
    }

    public static bool TryLoadFromFile(string path, out EditorSettings settings)
    {
        if(File.Exists(path))
        {
            settings = JsonConvert.DeserializeObject<EditorSettings>(File.ReadAllText(path));
            settings.SettingsFilePath = path;
            return true;
        }
        else
        {
            settings = null;
            return false;
        }
    }

    public static EditorSettings LoadOrCreate(string path)
    {
        if(TryLoadFromFile(path, out EditorSettings settings))
        {
            return settings;
        }
        
        settings = new EditorSettings() { SettingsFilePath = path };
        settings.SaveSettingsToFile();
        return settings;
    }
}
