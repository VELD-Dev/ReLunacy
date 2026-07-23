using System.Numerics;
using Newtonsoft.Json;
using ReLunacy.Utility;
using Veldrith;

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
    public GraphicsBackend GraphicsBackend;
    public int TargetFPS;
    public double FrametimeCap;
    public string Language = "en";
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
    public bool GizmoSnapEnabled;
    public float GizmoSnapTranslation;
    public float GizmoSnapRotation;
    public float GizmoSnapScale;
    internal LunaLog.LogLevel LogLevel;
    public Dictionary<string, string> CustomShaders = [];
    public bool LegacyRenderingMode;
    // Global multiplier on IMaterial.DecalOffsetCandidate (ShaderMetadata's file offset 0x48,
    // still an unconfirmed hypothesis) — together they push Decal-render-mode geometry outward
    // along its (computed, since these formats don't carry real per-vertex normals) normal at
    // mesh-build time, so it doesn't Z-fight the opaque surface it's meant to decal. Depth WRITE
    // is already disabled for every AlphaBlend material (DecalAwareForwardRenderer), but depth
    // TEST alone still flickers for genuinely coincident geometry. Default of 1 means "trust the
    // per-material file value as-is" — raise/lower it only if that value turns out to need an
    // extra scale factor once tested against more real data. Baked into the mesh at build time,
    // not applied per-frame — reload the level to see a changed value take effect.
    public float DecalOffset;

    [JsonIgnore]
    public float CamFOVRad => CamFOV * (MathF.PI / 180f);

    [JsonIgnore]
    public string SettingsFilePath { get; private set; } = string.Empty;

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
        GraphicsBackend = GraphicsBackend.Vulkan;
        TargetFPS = 60;
        FrametimeCap = 1.0 / 60.0;
        MSAA_Level = 0;
        Language = "en";
        UseFallbackLanguage = true;
        OverlayFramerate = true;
        OverlayLevelStats = false;
        OverlayProfiler = false;
        OverlayCamInfo = true;
        ProfilerRefreshRate = 250;
        ProfilerFrameSampleSize = 10;
        OverlayOpacity = 0.35f;
        OverlayPadding = new Vector2(10f, 10f);
        OverlayPos = 0;
        ToolsGizmoSize = 0.06f;
        GizmoSnapEnabled = false;
        GizmoSnapTranslation = 1.0f;
        GizmoSnapRotation = 15.0f;
        GizmoSnapScale = 0.25f;
        LegacyRenderingMode = false;
        DecalOffset = 1.0f;
#if DEBUG
        LogLevel = LunaLog.LogLevel.Debug;
#else
        LogLevel = LunaLog.LogLevel.Info;
#endif
        CustomShaders = [];
    }

    public void SaveSettingsToFile()
    {
        string output = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(SettingsFilePath, output);
    }

    public void ReloadSettings() => JsonConvert.PopulateObject(File.ReadAllText(SettingsFilePath), this);

    public static bool TryLoadFromFile(string path, out EditorSettings? settings)
    {
        if (File.Exists(path))
        {
            settings = JsonConvert.DeserializeObject<EditorSettings>(File.ReadAllText(path));
            if (settings != null) settings.SettingsFilePath = path;
            return settings != null;
        }
        settings = null;
        return false;
    }

    public static EditorSettings LoadOrCreate(string path)
    {
        if (TryLoadFromFile(path, out var settings) && settings != null)
            return settings;

        settings = new EditorSettings { SettingsFilePath = path };
        settings.SaveSettingsToFile();
        return settings;
    }
}
