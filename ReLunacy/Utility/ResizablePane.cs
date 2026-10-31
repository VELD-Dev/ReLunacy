using System.Numerics;

namespace ReLunacy.Utility;

/// <summary>
/// A side-by-side pane whose width can be dragged, and which stays where it is put.
///
/// ImGui's ResizeX puts the resize grip on the border and records the new width in the child
/// window - but the width also has to be passed in on every call, since the starting width is a
/// choice this code makes rather than something ImGui can know. So the width is read back after
/// the child opens and handed in again next frame; a drag then sticks regardless of which of the
/// two values ImGui decided to keep.
///
/// The width is clamped against the room actually available, which matters when the frame is
/// resized or undocked: a pane remembered from a wide window would otherwise leave its neighbour
/// with nothing, or be pushed off the edge with its own grip out of reach.
/// </summary>
/// <param name="width">Its starting width, or zero to take half of whatever room there is.</param>
public sealed class ResizablePane(float width, float minimum = 140f)
{
    /// <summary>The width as it stands, before clamping to what is available.</summary>
    public float Width { get; private set; } = width;

    /// <summary>Opens the pane. Call <see cref="End"/> afterwards whatever this returns, as with
    /// BeginChild itself.</summary>
    public bool Begin(string id)
    {
        float asked = Resolve(Width, ImGui.GetContentRegionAvail().X,
                              ImGui.GetStyle().ItemSpacing.X, minimum);

        bool open = ImGui.BeginChild(id, new Vector2(asked, 0f),
                                     ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX);
        if (open)
        {
            // Only a width DIFFERENT from the one asked for means the person dragged the border.
            // Writing the asked-for value back would overwrite a remembered width with whatever
            // the clamp produced, so narrowing the window once would forget it permanently.
            float actual = ImGui.GetWindowSize().X;
            if (MathF.Abs(actual - asked) > 0.5f) Width = actual;
        }
        return open;
    }

    /// <summary>
    /// The width to ask for, given the room there is.
    ///
    /// Separated from the drawing because it is the part that misbehaves out of sight: a pane
    /// remembered from a wide window has to give its neighbour room when the frame shrinks,
    /// without shrinking so far that its own resize grip cannot be reached again.
    /// </summary>
    /// <param name="width">What is wanted, or zero to take half.</param>
    public static float Resolve(float width, float available, float spacing, float minimum)
    {
        // Half and half is the sensible start for a side-by-side split, and it cannot be chosen
        // before there is a frame to measure - so zero means "decide on the first draw".
        if (width <= 0f) width = (available - spacing) * 0.5f;

        // Leave at least the minimum for whatever sits beside it, but never ask for less than the
        // minimum either, even when that overruns a frame too narrow to hold both.
        float room = MathF.Max(minimum, available - minimum - spacing);
        return Math.Clamp(width, minimum, room);
    }

    public static void End() => ImGui.EndChild();
}
