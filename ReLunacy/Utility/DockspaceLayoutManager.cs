using System.Numerics;

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

    private static unsafe void ApplyLayout(uint dockspaceId, DockspacePreset preset, IReadOnlyList<string> windowNames)
    {
        ImGuiP.DockBuilderRemoveNode(dockspaceId);
        ImGuiP.DockBuilderAddNode(dockspaceId);
        ImGuiP.DockBuilderSetNodeSize(dockspaceId, ImGui.GetMainViewport().WorkSize);

        switch (preset)
        {
            case DockspacePreset.Default:
                ApplyDefaultLayout(dockspaceId, windowNames);
                break;
            case DockspacePreset.Compact:
                ApplyCompactLayout(dockspaceId, windowNames);
                break;
            case DockspacePreset.Wide:
                ApplyWideLayout(dockspaceId, windowNames);
                break;
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
        // Default: Center=View3D+AssetViewer+TextureExplorer (tabbed), Right top=Explorer, Right bottom=Inspector, Bottom=Console
        uint centerId, rightId, bottomId;
        uint temp = dockspaceId;

        // Split off bottom (25%)
        ImGuiP.DockBuilderSplitNode(temp, ImGuiDir.Down, 0.25f, &bottomId, &temp);
        // Split off right (25%)
        ImGuiP.DockBuilderSplitNode(temp, ImGuiDir.Right, 0.25f, &rightId, &centerId);
        // Split right into top (explorer) and bottom (inspector)
        uint rightTopId, rightBottomId;
        ImGuiP.DockBuilderSplitNode(rightId, ImGuiDir.Down, 0.5f, &rightBottomId, &rightTopId);

        var explorer = FindWindow(windowNames, "Explorer", "Entity", "Hierarchy");
        var view3d = FindWindow(windowNames, "View3D", "3D", "Viewport");
        var inspector = FindWindow(windowNames, "Inspector", "Properties");
        var console = FindWindow(windowNames, "Console", "Log", "Output");
        var assetViewer = FindWindow(windowNames, "Asset");
        var textureExplorer = FindWindow(windowNames, "Texture");

        // Tabbed together in center
        if (view3d != null) ImGuiP.DockBuilderDockWindow(view3d, centerId);
        if (assetViewer != null) ImGuiP.DockBuilderDockWindow(assetViewer, centerId);
        if (textureExplorer != null) ImGuiP.DockBuilderDockWindow(textureExplorer, centerId);
        // Right column
        if (explorer != null) ImGuiP.DockBuilderDockWindow(explorer, rightTopId);
        if (inspector != null) ImGuiP.DockBuilderDockWindow(inspector, rightBottomId);
        // Bottom
        if (console != null) ImGuiP.DockBuilderDockWindow(console, bottomId);
    }

    private static unsafe void ApplyCompactLayout(uint dockspaceId, IReadOnlyList<string> windowNames)
    {
        // Compact: Left=View3D, Right stacked=Explorer+Inspector
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
        // Wide: Top=View3D, Bottom split=Explorer|Inspector|Assets
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
