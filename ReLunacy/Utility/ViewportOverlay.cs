using System.Numerics;
using Hexa.NET.ImGui;

namespace ReLunacy.Utility;

/// <summary>A compact toolbar drawn on top of a viewport image, plus optional drop-down panels
/// hanging off its buttons. Call <see cref="Begin"/> right after drawing the image, add buttons,
/// open panels for the toggled-on ones, then <see cref="End"/>.</summary>
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

    /// <summary>True when the cursor is over one of this overlay's controls, so the click must not
    /// also reach the gizmo or picker underneath. Reset by <see cref="Begin"/>.</summary>
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
        // Translucent so the viewport stays readable underneath, brighter on hover/press.
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
        // AutoResizeY when no explicit height was given, so the panel fits its widgets.
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
        // AllowWhenBlockedByActiveItem keeps a dragged slider counted as ours even off-panel.
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            WantsMouse = true;
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    public void End()
    {
        if (!_active) return;

        ImGui.PopStyleColor(3);
        ImGui.PopID();
        _active = false;

        // Restores the caller's layout cursor. The zero-size Dummy is required: without an item
        // submitted at the restore point, ImGui asserts about the cursor extending parent bounds.
        ImGui.SetCursorScreenPos(_restoreCursor);
        ImGui.Dummy(Vector2.Zero);
        ImGui.SetCursorScreenPos(_restoreCursor);
    }
}
