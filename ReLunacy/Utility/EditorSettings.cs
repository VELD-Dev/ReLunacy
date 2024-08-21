using Vector2 = System.Numerics.Vector2;

namespace ReLunacy.Utility;

public class EditorSettings
{
    public bool DebugMode;
    public float RenderDistance;
    public float CamMoveSpeed;
    public float CamMaxSpeed;
    public float CamFOV;
    public float CamSensivity;
    public uint MSAA_Level;
    public VSyncMode VSyncMode;
    public bool OverlayFramerate;
    public bool OverlayLevelStats;
    public bool OverlayProfiler;
    public bool OverlayCamInfo;
    public int ProfilerRefreshRate;
    public int ProfilerFrameSampleSize;
    public float OverlayOpacity;
    public Vector2 OverlayPadding;
    internal LunaLog.LogLevel LogLevel;
    public Dictionary<string, string> CustomShaders;
    public bool LegacyRenderingMode;

    [JsonIgnore]
    public float CamFOVRad { get => CamFOV * (MathHelper.Pi / 180f); }

    [JsonIgnore]
    public string SettingsFilePath { get; private set; }

    [JsonConstructor]
    public EditorSettings()
    {
        DebugMode = false;
        RenderDistance = 3000f;
        CamMoveSpeed = 15f;
        CamMaxSpeed = 25f;
        CamFOV = 82.4f;
        CamSensivity = 1f;
        VSyncMode = VSyncMode.Off;
        OverlayFramerate = true;
        OverlayLevelStats = false;
        OverlayProfiler = false;
        OverlayCamInfo = false;
        ProfilerRefreshRate = 250;
        ProfilerFrameSampleSize = 10;
        OverlayOpacity = 0.35f;
        OverlayPadding = new(10f, 10f);
        LegacyRenderingMode = false;
#if DEBUG
        LogLevel = LunaLog.LogLevel.Debug;
#else
        LogLevel = LunaLog.LogLevel.Info;
#endif

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
