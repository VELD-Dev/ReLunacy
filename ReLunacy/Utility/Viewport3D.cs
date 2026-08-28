using System.Numerics;
using Hexa.NET.ImGui;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Utility;

/// <summary>Hosts a 3D viewport inside an ImGui window: the rendered image, the toolbar over it, the
/// gizmo, and the arbitration that decides which of those gets the mouse.
///
/// Every frame that shows a 3D view used to carry its own copy of this plumbing (mouse position
/// relative to the image, a hover test, a "a click happened" latch consumed somewhere further down,
/// and the relative-mouse-mode flag), which meant the rules for who wins a click existed once per
/// frame and had already drifted between them. They live here now, in one order, stated once:
///
///   overlay UI  >  gizmo  >  picking  >  camera
///
/// The order is expressed by the order the calls are made in, so it is visible at the call site
/// rather than encoded as a chain of boolean guards:
///
/// <code>
/// _viewport.Begin("view3d");
/// // ... camera control, gated on AllowCameraInput, reporting drags via SetMouseCaptured
/// _viewport.DrawImage(binding);        // image, then the overlay opens over it
/// _viewport.Overlay.ToggleButton(...); // claims the click if it was hovered
/// _viewport.Gizmo(gizmoController, camera, selected);   // claims it next
/// if (_viewport.TryConsumeClick()) Pick();              // only what nothing above took
/// _viewport.End();
/// </code>
///
/// Nothing here renders the scene or moves a camera: those differ per viewport (the level view flies,
/// the asset preview orbits) and stay with the frame that owns them.</summary>
public sealed class Viewport3D
{
    /// <summary>The toolbar drawn over the image. Opened by <see cref="DrawImage"/>/<see cref="DrawEmpty"/>
    /// and closed by <see cref="End"/>, so callers only add buttons to it.</summary>
    public ViewportOverlay Overlay { get; } = new();

    /// <summary>Top-left of the image in SCREEN space.</summary>
    public Vector2 ScreenPos { get; private set; }

    /// <summary>Size of the image in whole pixels, from the content region available at <see cref="Begin"/>.</summary>
    public Vector2 Size { get; private set; }

    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }

    /// <summary>True when the region has a drawable size. Everything downstream is a no-op otherwise.</summary>
    public bool HasArea => PixelWidth > 0 && PixelHeight > 0;

    /// <summary>Cursor position relative to the image's top-left corner, which is the space the
    /// renderer's Pick() and any screen-space overlay maths work in.</summary>
    public Vector2 MousePos { get; private set; }

    /// <summary>Cursor is over this image, and this window is the one ImGui considers hovered (so a
    /// panel drawn on top of the viewport blocks it).</summary>
    public bool IsHovered { get; private set; }

    /// <summary>Whether the frame's own camera controls should react to the mouse this frame.</summary>
    public bool AllowCameraInput => IsHovered;

    private string _id = string.Empty;
    private bool _clickPending;
    private bool _claimed;
    private GizmoController? _gizmo;

    // Relative mouse mode is a single global flag but there are several viewports, so ownership is
    // tracked rather than assumed: a viewport only clears the flag if it is the one that set it.
    // Without this, any viewport ticking while another was mid-drag would cancel that drag, which is
    // exactly what used to happen between the level view and the asset preview.
    private static Viewport3D? _captureOwner;

    /// <summary>Measures the region the image will occupy and samples the mouse against it. Call at the
    /// point in the layout where the image goes, before anything else in the viewport.</summary>
    public void Begin(string id)
    {
        _id = id;
        ScreenPos = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        PixelWidth = (int)avail.X;
        PixelHeight = (int)avail.Y;
        // Truncated, not the raw float: the render target is an integer number of pixels, so drawing
        // the image at a fractional size would resample it and blur a view that should be 1:1.
        Size = new Vector2(PixelWidth, PixelHeight);

        MousePos = Input.GetMousePosition() - ScreenPos;
        IsHovered = HasArea
                 && ImGui.IsWindowHovered()
                 && MousePos.X >= 0f && MousePos.Y >= 0f
                 && MousePos.X < Size.X && MousePos.Y < Size.Y;

        _claimed = false;
        _gizmo = null;
        // Latched here and resolved at TryConsumeClick, because who is entitled to the click is not
        // known yet: the overlay and the gizmo only find out whether they were hit when they draw,
        // which is further down the same frame.
        _clickPending = IsHovered && Input.IsMouseButtonPressed(MouseButton.Left);
    }

    /// <summary>Marks the mouse as spoken for this frame, so no click falls through to picking. Camera
    /// drags do this via <see cref="SetMouseCaptured"/>; call it directly for anything else that
    /// swallows input.</summary>
    public void ClaimInput() => _claimed = true;

    /// <summary>Enters or leaves relative mouse mode on this viewport's behalf, and claims the mouse
    /// while captured. Safe to call every frame with the current drag state: only edges do anything.</summary>
    public void SetMouseCaptured(bool captured)
    {
        if (captured) _claimed = true;
        if (captured == (_captureOwner == this)) return;

        ImGuiIOPtr io = ImGui.GetIO();
        if (captured)
        {
            // Another viewport is mid-drag. Leave its flag alone; this one's MouseGrabHandler has the
            // button anyway, so the drag still tracks, it just does not also hide the cursor.
            if (_captureOwner != null) return;
            _captureOwner = this;
            io.ConfigFlags |= ImGuiConfigFlags.NoMouse;
        }
        else
        {
            _captureOwner = null;
            io.ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
        }
    }

    /// <summary>Draws the rendered scene and opens the overlay over it.</summary>
    public void DrawImage(ImTextureRef binding)
    {
        if (!HasArea) return;
        ImGui.SetCursorScreenPos(ScreenPos);
        ImGui.Image(binding, Size, Vector2.Zero, Vector2.One);
        Overlay.Begin(_id, ScreenPos, Size);
    }

    /// <summary>Same as <see cref="DrawImage"/> with nothing to show: reserves the region so the layout
    /// is identical, and still opens the overlay so its controls do not blink out whenever there is no
    /// scene (which is when some of them are most useful).</summary>
    public void DrawEmpty()
    {
        if (!HasArea) return;
        ImGui.SetCursorScreenPos(ScreenPos);
        ImGui.Dummy(Size);
        Overlay.Begin(_id, ScreenPos, Size);
    }

    /// <summary>Runs the transform gizmo over this viewport. Only after this has run does the gizmo know
    /// whether it was hit, which is why <see cref="TryConsumeClick"/> must come after it.</summary>
    public void Gizmo(GizmoController gizmo, EditorCamera camera, Entity? entity)
    {
        if (!HasArea) return;
        // Close the overlay first: it holds pushed style colours and an id scope, and the gizmo is not
        // part of it.
        Overlay.End();
        _gizmo = gizmo;
        gizmo.Render(camera, entity, ScreenPos, Size);
    }

    /// <summary>True on the frame a left click landed on the image and nothing above picking wanted it.
    /// Consumes the click, so it answers true at most once per frame.</summary>
    public bool TryConsumeClick()
    {
        if (!_clickPending) return false;
        _clickPending = false;

        if (_claimed || Overlay.WantsMouse) return false;
        // IsOver alongside IsUsing because IsUsingAny() lags a frame behind the initial click-down (it
        // wants a drag delta first), so the very first click on a handle would otherwise leak through.
        if (_gizmo != null && (_gizmo.IsUsing || _gizmo.IsOver)) return false;
        return true;
    }

    /// <summary>Closes the overlay and restores the caller's layout cursor. No-op if the overlay was
    /// already closed by <see cref="Gizmo"/>.</summary>
    public void End() => Overlay.End();
}
