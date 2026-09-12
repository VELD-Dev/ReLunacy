using System.Numerics;

namespace ReLunacy.Core.Frames.DockedFrames;

public abstract class DockedFrame : Frame
{
    protected abstract ImGuiCond DockingConditions { get; set; }
    protected abstract Vector2 DefaultPosition { get; set; }

    // Deliberately does NOT call ImGui.SetNextWindowDockID here (it used to, unconditionally, every
    // frame, forcing dockspaceId - the root/passthru node - with DockingConditions, which every
    // subclass sets to ImGuiCond.Appearing). That fired on exactly the frame this window's Begin()
    // first ran, which is exactly when DockspaceLayoutManager's DockBuilderDockWindow had just
    // assigned it a specific split node (centerId/rightTopId/etc.) moments earlier in
    // RenderDockSpace() - SetNextWindowDockID overrides a window's remembered DockId when its
    // condition is satisfied, so this silently discarded every preset/saved-layout placement the
    // instant each window actually opened, on startup and on every "Force Apply" alike. Manual
    // drag-to-dock-zone was unaffected since it never goes through this path, which is why that kept
    // working while nothing docked automatically. DockBuilderDockWindow alone is sufficient to place
    // a window on its first Begin() - this matches dear imgui's own dockspace demo, which never calls
    // SetNextWindowDockID for its individually-placed windows either. A frame the layout system
    // doesn't know about (PSArcExplorer, GameBrowserFrame, LevelDataFrame) just opens floating, same
    // as any ordinary ImGui window with no dock preference - draggable into a zone like any other.
    public override void RenderAsWindow(double deltaTime) => base.RenderAsWindow(deltaTime);
}
