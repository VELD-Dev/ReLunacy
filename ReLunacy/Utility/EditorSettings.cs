using System.Numerics;
using Newtonsoft.Json;
using ReLunacy.Utility;
using NeoVeldrid;

public enum UpdateChannel
{
    Stable,
    Nightly,
}

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
    public float VolumeWireThickness;
    public Vector4 VolumeColor;
    public Vector4 VolumeSelectedColor;
    public Vector4 SelectionOutlineColor;
    internal LunaLog.LogLevel LogLevel;
    public Dictionary<string, string> CustomShaders = [];
    // Opt-in only: the outline's history documents that both winding-based and
    // normal-based backface techniques were tried for the selection outline and both broke -
    // triangle winding in these source assets isn't reliably consistent (sometimes not even within
    // a single mesh), which is why AssetManager hardcodes CULL_NONE by default. This flag exists so
    // culling can be flipped on live, per-session, to see how bad it actually is on real data rather
    // than assuming - not a confirmed-safe rendering mode.
    public bool BackfaceCulling;
    // See UpdateChecker: Stable checks GitHub's normal "latest release"; Nightly checks the
    // rolling "nightly" tag release .github/workflows/nightly.yml keeps updated on every push to
    // the nightly branch. Independent of which build the user is actually running - someone on a
    // stable build can still opt into nightly update notifications and vice versa.
    public UpdateChannel UpdateChannel;
    // First real lighting pass for the live renderer (see LitModelShaderSource) - everything else
    // is unlit. Opt-in default off, same "experimental until proven" reasoning as BackfaceCulling
    // above, since this is genuinely new/unverified rendering code, not a rebuild of something
    // already trusted.
    public bool EnableLighting;
    // Scene-wide default texture filtering for the 3D view (AssetManager also supports per-texture
    // overrides for future use - see AssetManager.SetTextureFiltering(textureId, filtering)).
    public ReLunacy.Engine.Rendering.TextureFiltering TextureFiltering;

    // Far clip for the ASSET VIEWER's preview camera only (the 3D view has its own, RenderDistance).
    // Assets are previewed at wildly different scales - a UFrag is drawn at 1/256 while a moby is
    // unit-ish - so one hardcoded far plane clipped some of them; this is adjustable from the overlay
    // toolbar over the preview itself.
    public float AssetViewerFarPlane;

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
        VolumeWireThickness = 0.1f;
        VolumeColor = new Vector4(1f, 1f, 0f, 1f);
        VolumeSelectedColor = new Vector4(1f, 1f, 1f, 1f);
        SelectionOutlineColor = new Vector4(1f, 0.65f, 0f, 1f);
        BackfaceCulling = false;
        UpdateChannel = UpdateChannel.Stable;
        EnableLighting = false;
        AssetViewerFarPlane = 100f;
        // Bilinear by default: it's what the game itself does on PS3, and the reason this
        // setting exists at all - Point remains selectable for pixel-peeping raw texel data.
        TextureFiltering = ReLunacy.Engine.Rendering.TextureFiltering.Bilinear;
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
