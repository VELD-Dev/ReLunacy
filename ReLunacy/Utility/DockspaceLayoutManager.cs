using ReLunacy.Core.Frames;

namespace ReLunacy.Utility;

public enum DockspacePreset
{
    Default,
    Compact,
    Wide,
}

public static class DockspaceLayoutManager
{
    private static bool _layoutApplied;

    // The three DockspacePreset names, exactly as ViewMenuDraw.LayoutPresets hardcodes them as
    // literal MenuItem labels. A saved layout can never legitimately use one of these: besides being
    // confusing (which "Default" is this?), the Layout menu would render a MenuItem AND a saved-
    // layout BeginMenu with the same ImGui ID, which is a duplicate-ID assertion - what actually
    // happened when ActiveLayoutName used to default to "Default" and got auto-saved under that name
    // on every exit.
    private static readonly string[] ReservedLayoutNames = ["Default", "Compact", "Wide", LatestLayoutName];

    /// <summary>Auto-save slot, re-written on every close (see SaveActiveLayout) no matter what
    /// ActiveLayoutName is - even a session that never explicitly named/saved a layout still gets
    /// its in-session tweaks captured here, so the very next launch can restore "however I left it"
    /// instead of resetting to the hardcoded default preset. Deliberately excluded from the Layout
    /// menu's saved-layout list (ViewMenuDraw.LayoutPresets filters this name out) - it's an
    /// implementation detail, not something a user names/loads/deletes by hand.</summary>
    public const string LatestLayoutName = "_latest";

    public static void TryApplyLayout(uint dockspaceId, DockspacePreset preset, IReadOnlyList<string> windowNames)
    {
        if (_layoutApplied) return;
        _layoutApplied = true;
        ApplyLayout(dockspaceId, preset, windowNames);
    }

    public static void ForceApplyLayout(uint dockspaceId, DockspacePreset preset, IReadOnlyList<string> windowNames)
    {
        ApplyLayout(dockspaceId, preset, windowNames);
    }

    /// <summary>Reads back the live DockId of every currently open frame (ImGuiWindowPtr.DockId,
    /// found by exact window identity) for SaveLayoutAs/SaveActiveLayout to persist alongside the
    /// ini blob. Skips anything not actually docked (DockId 0, i.e. floating) - there's nothing
    /// meaningful to force a reopened frame into for those.</summary>
    private static unsafe Dictionary<string, uint> CaptureFrameDockIds(IReadOnlyList<Frame> openFrames)
    {
        var result = new Dictionary<string, uint>();
        foreach (var frame in openFrames)
        {
            var win = ImGuiP.FindWindowByName(frame.WindowId);
            if (win.Handle == null) continue;
            uint dockId = win.DockId;
            if (dockId == 0) continue;
            result[frame.GetType().Name] = dockId;
        }
        return result;
    }

    /// <summary>Captures the live dock/window layout (via ImGui's own ini-settings blob, which
    /// covers dock node splits and every window's dock assignment) into <paramref name="name"/>,
    /// makes it the active layout, and persists settings immediately - not just held in memory -
    /// so a crash right after saving doesn't lose it. No-ops on a reserved name (see
    /// ReservedLayoutNames) rather than silently corrupting the Layout menu's IDs.</summary>
    public static void SaveLayoutAs(EditorSettings settings, string name, IReadOnlyList<Frame> openFrames)
    {
        if (Array.Exists(ReservedLayoutNames, n => n.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        settings.SavedLayouts[name] = new SavedLayout
        {
            Ini = ImGui.SaveIniSettingsToMemoryS(),
            FrameDockIds = CaptureFrameDockIds(openFrames),
        };
        settings.ActiveLayoutName = name;
        settings.SaveSettingsToFile();
    }

    /// <summary>Re-captures the current layout - used on editor exit so session tweaks
    /// (dragged/resized panels) survive without the user having to explicitly re-save. Always
    /// updates the LatestLayoutName auto-save slot regardless of whether a named layout is active,
    /// AND (if one is) re-saves into that name too - so both "whatever I had open, exactly as I
    /// left it" and "my named layout, with this session's tweaks folded in" stay correct after
    /// every exit.</summary>
    public static void SaveActiveLayout(EditorSettings settings, IReadOnlyList<Frame> openFrames)
    {
        var layout = new SavedLayout
        {
            Ini = ImGui.SaveIniSettingsToMemoryS(),
            FrameDockIds = CaptureFrameDockIds(openFrames),
        };
        settings.SavedLayouts[LatestLayoutName] = layout;
        if (!string.IsNullOrEmpty(settings.ActiveLayoutName) && settings.ActiveLayoutName != LatestLayoutName)
            settings.SavedLayouts[settings.ActiveLayoutName] = layout;
        settings.SaveSettingsToFile();
    }

    /// <summary>Restores a previously saved layout by feeding its ini blob back to ImGui. Reliable
    /// for windows that don't have a live dock binding yet (e.g. at startup, before any frame has
    /// called Begin() this run) - ImGui consults the freshly-loaded settings the first time each one
    /// appears. NOT reliable on its own for windows that are ALREADY docked somewhere THIS session
    /// (e.g. loading a different saved layout mid-session): ImGui doesn't tear down an existing dock
    /// binding just because new settings were loaded, so an already-open window keeps whatever node
    /// it already has instead of moving to what the loaded ini says - exactly why loading only ever
    /// seemed to "work for a moment" (the moment being whatever frame happened to already have no
    /// live binding, like an unopened panel) and otherwise silently did nothing. <paramref
    /// name="dockspaceId"/> (pass 0 if none exists yet, e.g. the startup call in Window.Init) fixes
    /// this by tearing the whole tree down first - same DockBuilderRemoveNode/AddNode/Finish
    /// sequence ApplyLayout already uses for the hardcoded presets, which is exactly why those never
    /// had this problem - so every currently-open window loses its old binding and is forced to
    /// pick up the loaded settings on its next Begin(), same as a freshly-opened one would.
    ///
    /// Also reopens any frame the layout had open but <paramref name="openFrames"/> doesn't -
    /// a closed frame has no live window for the reloaded ini to match against until it calls
    /// Begin() for the first time, and by then it's carrying a freshly-assigned "###id" the saved
    /// ini has never heard of, so name-based matching (what the ini itself relies on) can't place
    /// it. The DockId values recorded in SavedLayout.FrameDockIds stay valid across the reload
    /// though - the ini's [Docking][Data] section spells out each node's numeric ID explicitly - so
    /// <paramref name="ensureFrameTypeOpen"/> (construct-or-return-existing, by Frame type name) is
    /// used to open it and DockBuilderDockWindow places it directly, the same forcing mechanism the
    /// hardcoded presets use.
    ///
    /// Marks the DockBuilder default preset as already applied so <see cref="TryApplyLayout"/>
    /// doesn't stomp this with the hardcoded layout on the same startup.</summary>
    public static unsafe bool TryLoadLayout(EditorSettings settings, string? name, uint dockspaceId, IReadOnlyList<Frame> openFrames, Func<string, Frame?> ensureFrameTypeOpen)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!settings.SavedLayouts.TryGetValue(name, out var layout)) return false;

        if (dockspaceId != 0)
        {
            ImGuiP.DockBuilderRemoveNode(dockspaceId);
            ImGuiP.DockBuilderAddNode(dockspaceId, (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.Space);
            ImGuiP.DockBuilderFinish(dockspaceId);
        }

        ImGui.LoadIniSettingsFromMemory(layout.Ini);
        settings.ActiveLayoutName = name;
        _layoutApplied = true;

        foreach (var (typeName, dockId) in layout.FrameDockIds)
        {
            bool alreadyOpen = false;
            foreach (var f in openFrames)
                if (f.GetType().Name == typeName) { alreadyOpen = true; break; }
            if (alreadyOpen) continue;

            var frame = ensureFrameTypeOpen(typeName);
            if (frame != null)
                ImGuiP.DockBuilderDockWindow(frame.WindowId, dockId);
        }

        return true;
    }

    public static void DeleteLayout(EditorSettings settings, string name)
    {
        settings.SavedLayouts.Remove(name);
        if (settings.ActiveLayoutName == name)
            settings.ActiveLayoutName = null;
        settings.SaveSettingsToFile();
    }

    private static unsafe void ApplyLayout(uint dockspaceId, DockspacePreset preset, IReadOnlyList<string> windowNames)
    {
        ImGuiP.DockBuilderRemoveNode(dockspaceId);
        // The ImGuiDockNodeFlagsPrivate.Space bit (dear imgui's internal-only ImGuiDockNodeFlags_
        // DockSpace, 1<<10 - not in the public ImGuiDockNodeFlags enum, which is why it's a separate
        // type here) is required on DockBuilderAddNode, not optional: without it the node this
        // creates is just a floating dock node, not one flagged as belonging to a dockspace. The very
        // next ImGui.DockSpace(dockspaceId, ...) call below (in RenderDockSpace) then finds a node
        // that doesn't match what it expects and rebuilds it as an empty dockspace, silently
        // discarding every DockBuilderDockWindow assignment this method makes afterward - which is
        // exactly why nothing was ever actually docking, on startup or on a forced re-apply, no
        // matter how many times this ran. Confirmed against ocornut/imgui's own docking issue
        // tracker, which documents this exact omission as the standard cause of "DockBuilder layout
        // doesn't stick" reports.
        ImGuiP.DockBuilderAddNode(dockspaceId, (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.Space);
        ImGuiP.DockBuilderSetNodeSize(dockspaceId, ImGui.GetMainViewport().WorkSize);

        switch (preset)
        {
            case DockspacePreset.Default: ApplyDefaultLayout(dockspaceId, windowNames); break;
            case DockspacePreset.Compact: ApplyCompactLayout(dockspaceId, windowNames); break;
            case DockspacePreset.Wide: ApplyWideLayout(dockspaceId, windowNames); break;
        }

        ImGuiP.DockBuilderFinish(dockspaceId);
    }

    private static string? FindWindow(IReadOnlyList<string> names, params string[] candidates)
    {
        foreach (var candidate in candidates)
            foreach (var name in names)
                if (name.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                    return name;
        return null;
    }

    private static unsafe void ApplyDefaultLayout(uint dockspaceId, IReadOnlyList<string> windowNames)
    {
        uint centerId, rightId, bottomId;
        uint temp = dockspaceId;

        ImGuiP.DockBuilderSplitNode(temp, ImGuiDir.Down, 0.25f, &bottomId, &temp);
        ImGuiP.DockBuilderSplitNode(temp, ImGuiDir.Right, 0.25f, &rightId, &centerId);
        uint rightTopId, rightBottomId;
        ImGuiP.DockBuilderSplitNode(rightId, ImGuiDir.Down, 0.5f, &rightBottomId, &rightTopId);

        // "Entity"/"Hierarchy" before the bare "Explorer" fallback: "Textures Explorer" also
        // contains "Explorer", and FindWindow returns the first substring match against whatever's
        // currently open - with Texture/Shader explorers openable independently of the entity one,
        // checking the specific names first keeps this pointed at the entity hierarchy even when
        // the others are open too.
        var explorer = FindWindow(windowNames, "Entity", "Hierarchy", "Explorer");
        var view3d = FindWindow(windowNames, "View3D", "3D", "Viewport");
        var inspector = FindWindow(windowNames, "Inspector", "Properties");
        var console = FindWindow(windowNames, "Console", "Log", "Output");
        var assetViewer = FindWindow(windowNames, "Asset");
        var textureExplorer = FindWindow(windowNames, "Texture");
        var shaderExplorer = FindWindow(windowNames, "Shader");
        var profiler = FindWindow(windowNames, "Profiler");

        // Center: 3D View plus every other asset-browsing frame stacked as tabs alongside it.
        if (view3d != null) ImGuiP.DockBuilderDockWindow(view3d, centerId);
        if (assetViewer != null) ImGuiP.DockBuilderDockWindow(assetViewer, centerId);
        if (textureExplorer != null) ImGuiP.DockBuilderDockWindow(textureExplorer, centerId);
        if (shaderExplorer != null) ImGuiP.DockBuilderDockWindow(shaderExplorer, centerId);
        // Immediate right of 3D View: hierarchy explorer and frame profiler, stacked as tabs.
        if (explorer != null) ImGuiP.DockBuilderDockWindow(explorer, rightTopId);
        if (profiler != null) ImGuiP.DockBuilderDockWindow(profiler, rightTopId);
        // Below that: property inspector.
        if (inspector != null) ImGuiP.DockBuilderDockWindow(inspector, rightBottomId);
        if (console != null) ImGuiP.DockBuilderDockWindow(console, bottomId);
    }

    private static unsafe void ApplyCompactLayout(uint dockspaceId, IReadOnlyList<string> windowNames)
    {
        uint leftId, rightId;
        ImGuiP.DockBuilderSplitNode(dockspaceId, ImGuiDir.Right, 0.3f, &rightId, &leftId);

        var view3d = FindWindow(windowNames, "View3D", "3D", "Viewport");
        var explorer = FindWindow(windowNames, "Explorer", "Entity", "Hierarchy");
        var inspector = FindWindow(windowNames, "Inspector", "Properties");

        if (view3d != null) ImGuiP.DockBuilderDockWindow(view3d, leftId);
        if (explorer != null) ImGuiP.DockBuilderDockWindow(explorer, rightId);
        if (inspector != null) ImGuiP.DockBuilderDockWindow(inspector, rightId);
    }

    private static unsafe void ApplyWideLayout(uint dockspaceId, IReadOnlyList<string> windowNames)
    {
        uint topId, bottomId;
        ImGuiP.DockBuilderSplitNode(dockspaceId, ImGuiDir.Down, 0.35f, &bottomId, &topId);

        uint botLeft, botMid, botRight;
        ImGuiP.DockBuilderSplitNode(bottomId, ImGuiDir.Left, 0.33f, &botLeft, &bottomId);
        ImGuiP.DockBuilderSplitNode(bottomId, ImGuiDir.Left, 0.5f, &botMid, &botRight);

        var view3d = FindWindow(windowNames, "View3D", "3D", "Viewport");
        var explorer = FindWindow(windowNames, "Explorer", "Entity", "Hierarchy");
        var inspector = FindWindow(windowNames, "Inspector", "Properties");
        var assets = FindWindow(windowNames, "Asset");

        if (view3d != null) ImGuiP.DockBuilderDockWindow(view3d, topId);
        if (explorer != null) ImGuiP.DockBuilderDockWindow(explorer, botLeft);
        if (inspector != null) ImGuiP.DockBuilderDockWindow(inspector, botMid);
        if (assets != null) ImGuiP.DockBuilderDockWindow(assets, botRight);
    }
}
