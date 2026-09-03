using System.Numerics;
using Newtonsoft.Json;
using ReLunacy.Utility;
using NeoVeldrid;

public enum UpdateChannel
{
    Stable,
    Nightly,
}

/// <summary>One saved dockspace layout: the raw ImGui ini blob (dock node tree + whichever windows
/// were already open when it was saved), plus which Frame TYPES were open and which dock node each
/// one was docked into. The ini blob alone isn't enough to restore a frame that's currently CLOSED:
/// ImGui only auto-places a window when it calls Begin() with the same identity string the ini
/// recorded, and that string embeds a "###{frameId}" suffix assigned by a per-session counter -
/// reopening a closed frame gives it a brand new id that will essentially never match, no matter
/// how faithfully the ini itself is reproduced. FrameDockIds is what lets DockspaceLayoutManager
/// reopen a missing frame and explicitly place it, instead of it appearing correctly docked only by
/// accident (or not at all).</summary>
public class SavedLayout
{
    public string Ini = string.Empty;
    // Frame.GetType().Name -> the live DockId (ImGuiWindowPtr.DockId) that window had when saved.
    // Only frames that were actually DOCKED somewhere (not floating) get an entry here - a floating
    // window has nothing meaningful to force a reopened frame into. See DockspaceLayoutManager.
    public Dictionary<string, uint> FrameDockIds = [];
}

[JsonObject]
public class EditorSettings
{
    public bool DebugMode;
    // Windowed-mode geometry (desktop coordinates - see EditorWindow.GetWindowSize), and whether the
    // editor was maximized on exit. Only WindowMaximized is ever true-to-life while maximized -
    // WindowWidth/Height deliberately keep whatever they were the last time the window was NOT
    // maximized (see LunaWindow.OnClose), since SDL reports the maximized/screen-filling size while
    // maximized, not a size worth restoring to. Defaults to maximized on a fresh install rather than
    // a fixed 1280x720, matching every other editor-style app's expected first-launch experience.
    public int WindowWidth;
    public int WindowHeight;
    public bool WindowMaximized;
    // Render menu's per-type visibility toggles + Moby distance culling - mirrors of
    // EntityManager.Singleton's own render{Mobys,Ties,...}/MobyDistanceCullingEnabled fields.
    // EntityManager (ReLunacy.Engine) can't read these directly - it has no reference to this
    // app-layer settings object (same reason VolumeWireThickness/VolumeColor are synced in from
    // View3D every frame instead) - so RenderMenuDraw writes through to both on every toggle
    // instead, and Window.Init copies these into EntityManager.Singleton once at startup so the
    // very first frame already reflects last session's choices, not the engine's own hardcoded
    // defaults. Not level-specific state (unlike per-zone visibility, which resets with each level
    // load) - these are "what kinds of things do I want to see" preferences that should carry over
    // regardless of which level is open.
    public bool RenderMobys;
    public bool RenderTies;
    public bool RenderUFrags;
    public bool RenderFoliage;
    public bool RenderVolumes;
    public bool RenderBoundingSpheres;
    public bool MobyDistanceCullingEnabled;
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
    // Dockspace layouts saved via View > Layout > Save Current Layout As... - each value is an ImGui
    // ini-settings blob (ImGui.SaveIniSettingsToMemoryS/LoadIniSettingsFromMemory), which captures
    // every dock node split and window dock assignment in one string. ActiveLayoutName is which of
    // these (if any) is currently in effect; it's re-saved on exit (DockspaceLayoutManager.
    // SaveActiveLayout) so in-session tweaks persist, and reloaded on next startup (Window.Init) so
    // the editor reopens exactly as it was left.
    //
    // null/empty means "no saved layout selected" - deliberately NOT defaulted to "Default": that
    // string is also the hardcoded DockBuilder preset's name (DockspacePreset.Default, and the
    // literal MenuItem in View > Layout), so if a real SavedLayouts["Default"] entry existed too,
    // the Layout menu would render both a MenuItem and a BeginMenu with the same ID ("Default"),
    // which is exactly the "PopID"/duplicate-ID assertion this caused. It also meant TryLoadLayout
    // succeeded on every launch after the first (since SaveActiveLayout unconditionally wrote to
    // whatever ActiveLayoutName was, which used to default to "Default"), permanently short-circuiting
    // ApplyDefaultLayout via _layoutApplied and replaying whatever the last (possibly undocked) ini
    // blob was instead - nothing ever looked "pre-docked" again. See EditorSettings.TryLoadFromFile
    // for the one-time migration that clears an already-poisoned "Default" entry on existing installs.
    public Dictionary<string, SavedLayout> SavedLayouts = [];
    public string? ActiveLayoutName;
    // Last USRDIR path scanned in the Game Browser frame (File > Open Game Browser) - persisted
    // across both frame reopens and app restarts so the browser doesn't start empty every time.
    // See GameBrowserFrame's constructor, which auto re-scans this path (if set) as soon as the
    // frame is opened, instead of making the user re-Browse/Paste/Scan it by hand every session.
    public string GameBrowserRootPath = string.Empty;
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
        WindowWidth = 1280;
        WindowHeight = 720;
        WindowMaximized = true;
        RenderMobys = true;
        RenderTies = true;
        RenderUFrags = true;
        RenderFoliage = true;
        RenderVolumes = true;
        RenderBoundingSpheres = false;
        MobyDistanceCullingEnabled = true;
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
        SavedLayouts = [];
        ActiveLayoutName = null;
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
            try
            {
                settings = JsonConvert.DeserializeObject<EditorSettings>(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                // SavedLayouts changed shape (plain ini string -> SavedLayout object) after this
                // field already shipped once - an old file's SavedLayouts entries won't deserialize
                // into the new type, and Newtonsoft aborts the whole object on a field failure like
                // this rather than skipping just that field. Treating that as "no file" (LoadOrCreate
                // then makes a fresh one, rewriting the file) loses the rest of the settings too, but
                // that's still better than crashing on launch - this is pre-release software still
                // under active layout-feature development, not a shipped format to migrate carefully.
                settings = null;
            }
            if (settings != null)
            {
                settings.SettingsFilePath = path;
                // One-time cleanup for installs that saved a settings file before ActiveLayoutName
                // stopped defaulting to "Default" - see the field's doc comment for why a
                // SavedLayouts["Default"] entry is never legitimate. Silent and self-healing: strips
                // it once, and it can't come back since nothing writes that key anymore.
                if (settings.SavedLayouts.Remove("Default") | (settings.ActiveLayoutName == "Default"))
                    settings.ActiveLayoutName = null;
            }
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
