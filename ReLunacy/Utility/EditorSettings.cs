using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Vortice.Mathematics;

[JsonObject]
public class EditorSettings
{
    public bool DebugMode;
    public float RenderDistance;
    public float CamMoveSpeed;
    public float CamMaxSpeed;
    public float CamFOV;
    public float CamSensivity;
    public bool FrustrumCulling;
    public uint MSAA_Level; // 0 = no MSAA, 1 = 2x, 2 = 4x, 3 = 8x
    public bool VSync;
    public int TargetFPS;
    public double FrametimeCap;
    public string Language;
    public bool UseFallbackLanguage;
    public bool OverlayFramerate;
    public bool OverlayLevelStats;
    public bool OverlayProfiler;
    public bool OverlayCamInfo;
    public int ProfilerRefreshRate;
    public int ProfilerFrameSampleSize;
    public float OverlayOpacity;
    public Vector2 OverlayPadding;
    public int OverlayPos;
    public float ToolsGizmoSize;
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
        FrustrumCulling = true;
        VSync = false;
        TargetFPS = 60;
        FrametimeCap = 1.0 / 60.0;
        MSAA_Level = 0; // 0 = no MSAA, 1 = 2x, 2 = 4x, 3 = 8x
        Language = "en";
        UseFallbackLanguage = true;
        OverlayFramerate = true;
        OverlayLevelStats = false;
        OverlayProfiler = false;
        OverlayCamInfo = true;
        ProfilerRefreshRate = 250;
        ProfilerFrameSampleSize = 10;
        OverlayOpacity = 0.35f;
        OverlayPadding = new(10f, 10f);
        OverlayPos = 0;
        ToolsGizmoSize = 0.06f;
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
        if (File.Exists(path))
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
        if (File.Exists(path))
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
        if (TryLoadFromFile(path, out EditorSettings settings))
        {
            return settings;
        }

        settings = new EditorSettings() { SettingsFilePath = path };
        settings.SaveSettingsToFile();
        return settings;
    }
}
