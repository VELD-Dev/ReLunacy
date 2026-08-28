using System.Numerics;
using Hexa.NET.ImGui;

namespace ReLunacy.Utility;

/// <summary>A compact toolbar drawn ON TOP of a viewport image, plus optional drop-down panels hanging
/// off its buttons.
///
/// Any frame that renders a 3D viewport can use this: call <see cref="Begin"/> straight after the
/// <c>ImGui.Image</c>, add buttons, open panels for the ones that are toggled on, then <see cref="End"/>.
/// It positions everything in screen space over the image and restores the caller's layout cursor
/// afterwards, so the surrounding frame layout is untouched.
///
/// Buttons drawn after the image land on top of it: same ImGui window, later draw order.
///
/// <code>
/// _overlay.Begin("viewport", imagePos, imageSize);
/// _overlay.ToggleButton("C", ref showClip, "Clip distance");
/// if (showClip &amp;&amp; _overlay.BeginPanel("clip", new Vector2(260f, 0f)))
/// {
///     ImGui.SliderFloat("Far", ref far, 1f, 10000f);
///     _overlay.EndPanel();
/// }
/// _overlay.End();
/// </code></summary>
public sealed class ViewportOverlay
{
    private const float Margin = 8f;
    private const float Spacing = 4f;
    private const float ButtonSize = 24f;

    private Vector2 _origin;
    private Vector2 _size;
    private Vector2 _cursor;
    private Vector2 _restoreCursor;
    private float _panelTop;
    private int _panelColumn;
    private bool _active;

    /// <summary>True while a viewport is small enough that the toolbar would cover most of it, in
    /// which case everything below is skipped. Callers do not need to check this - the add methods
    /// are all no-ops when inactive.</summary>
    public bool IsActive => _active;

    /// <summary>True when the cursor is over one of this overlay's controls, so a click on it belongs to
    /// the overlay and must not also reach the gizmo or the picker underneath. Reset by <see cref="Begin"/>
    /// and accumulated as the controls are added; <see cref="Viewport3D"/> reads it when resolving who
    /// gets the click.</summary>
    public bool WantsMouse { get; private set; }

    /// <param name="id">Unique per viewport; keeps ImGui ids from colliding between frames that both
    /// use an overlay.</param>
    /// <param name="viewportPos">Top-left of the image in SCREEN space (what ImGui.GetCursorScreenPos
    /// returned just before the image was drawn).</param>
    public void Begin(string id, Vector2 viewportPos, Vector2 viewportSize)
    {
        _restoreCursor = ImGui.GetCursorScreenPos();
        WantsMouse = false;
        // Hidden rather than squeezed: a toolbar over a tiny viewport is worse than no toolbar.
        _active = viewportSize.X >= ButtonSize * 3f && viewportSize.Y >= ButtonSize * 3f;
        if (!_active) return;

        _origin = viewportPos;
        _size = viewportSize;
        _cursor = viewportPos + new Vector2(Margin, Margin);
        _panelTop = _cursor.Y + ButtonSize + Spacing;
        _panelColumn = 0;

        ImGui.PushID(id);
        // Translucent so the viewport stays readable underneath, and brighter on hover/press so the
        // buttons still feel like buttons rather than a watermark.
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.10f, 0.10f, 0.12f, 0.65f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.30f, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.35f, 0.42f, 0.95f));
    }

    /// <summary>A button that latches. Returns true on the frame it was clicked.</summary>
    public bool ToggleButton(string label, ref bool state, string? tooltip = null)
    {
        if (!_active) return false;

        bool clicked;
        if (state)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.45f, 0.75f, 0.90f));
            clicked = Button(label, tooltip);
            ImGui.PopStyleColor();
        }
        else
        {
            clicked = Button(label, tooltip);
        }

        if (clicked) state = !state;
        return clicked;
    }

    /// <summary>A momentary button. Returns true on the frame it was clicked.</summary>
    public bool Button(string label, string? tooltip = null)
    {
        if (!_active) return false;

        ImGui.SetCursorScreenPos(_cursor);
        bool clicked = ImGui.Button(label, new Vector2(ButtonSize, ButtonSize));
        if (ImGui.IsItemHovered())
        {
            WantsMouse = true;
            if (tooltip != null) ImGui.SetTooltip(tooltip);
        }

        // Remember where a panel opened from this button should hang.
        _panelColumn = (int)((_cursor.X - _origin.X - Margin) / (ButtonSize + Spacing));
        _cursor.X += ButtonSize + Spacing;
        return clicked;
    }

    /// <summary>Opens a panel under the toolbar, aligned to the button that was added last. Returns
    /// false if there is no room, in which case do NOT call <see cref="EndPanel"/>.</summary>
    public bool BeginPanel(string id, Vector2 size)
    {
        if (!_active) return false;

        float left = _origin.X + Margin + _panelColumn * (ButtonSize + Spacing);
        // Keep the panel inside the viewport, sliding it left if it would overhang the right edge.
        float maxWidth = Math.Max(80f, _size.X - Margin * 2f);
        float width = size.X <= 0f ? maxWidth : Math.Min(size.X, maxWidth);
        left = Math.Min(left, _origin.X + _size.X - Margin - width);
        float maxHeight = Math.Max(40f, _size.Y - (_panelTop - _origin.Y) - Margin);
        float height = size.Y <= 0f ? 0f : Math.Min(size.Y, maxHeight);

        ImGui.SetCursorScreenPos(new Vector2(left, _panelTop));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 0.92f));
        // AutoResizeY when no explicit height was asked for, so a panel is exactly as tall as the
        // widgets in it rather than a guessed constant that has to be maintained by hand.
        var flags = ImGuiChildFlags.Borders | (height <= 0f ? ImGuiChildFlags.AutoResizeY : ImGuiChildFlags.None);
        bool open = ImGui.BeginChild(id, new Vector2(width, height), flags);
        if (!open)
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
        return open;
    }

    public void EndPanel()
    {
        if (!_active) return;
        // Asked while still inside the child, so it answers for the panel rather than the frame's window.
        // AllowWhenBlockedByActiveItem keeps a slider being dragged counted as ours even on the frames
        // the cursor has wandered off the panel.
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            WantsMouse = true;
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    public void End()
    {
        // Nothing was drawn and the cursor was never moved, so there is nothing to put back. Restoring
        // anyway would submit the Dummy below for no reason and nudge the parent's content extent.
        if (!_active) return;

        ImGui.PopStyleColor(3);
        ImGui.PopID();
        _active = false;

        // Put the layout cursor back where the caller left it, so the overlay cannot disturb whatever
        // the frame lays out after the viewport.
        //
        // The Dummy is required, not decorative. ImGui flags "SetCursorPos used to extend parent
        // boundaries" when a window ends with the cursor past CursorMaxPos and no item submitted
        // since. The cursor after an Image sits exactly one ItemSpacing.y below CursorMaxPos, so simply
        // restoring it trips that assert whenever nothing else follows the viewport (which is the norm:
        // gizmos draw through draw lists, not items). Submitting a zero-size item at the restore point
        // pulls CursorMaxPos down to it and clears the flag; setting the position again afterwards then
        // leaves the cursor exactly where the caller had it, with CursorPos == CursorMaxPos, which the
        // check passes.
        ImGui.SetCursorScreenPos(_restoreCursor);
        ImGui.Dummy(Vector2.Zero);
        ImGui.SetCursorScreenPos(_restoreCursor);
    }
}
