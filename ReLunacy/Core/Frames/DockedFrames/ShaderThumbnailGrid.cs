using System.Numerics;
using ReLunacy.Engine.Assets.Browser;

namespace ReLunacy.Core.Frames.DockedFrames;

internal static class ShaderThumbnailGrid
{
    public static AssetEntry? Draw(IReadOnlyList<AssetEntry> entries, ulong? selected, ref bool scrollToSelected)
    {
        const float padding = 6f;
        float width = Math.Clamp(ImGui.GetContentRegionAvail().X, 24f, 112f);
        float imageSize = width - 2f * padding;
        var tile = new Vector2(width, imageSize + ImGui.GetTextLineHeight() + 3f * padding);
        var spacing = ImGui.GetStyle().ItemSpacing;
        int columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing.X) / (tile.X + spacing.X)));
        float rowHeight = tile.Y + spacing.Y;
        if (scrollToSelected)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Id == selected)
                {
                    ImGui.SetScrollY(i / columns * rowHeight);
                    break;
                }
            scrollToSelected = false;
        }

        AssetEntry? clicked = null;
        var clipper = new ImGuiListClipper();
        clipper.Begin((entries.Count + columns - 1) / columns, rowHeight);
        while (clipper.Step())
            for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                for (int column = 0; column < columns && row * columns + column < entries.Count; column++)
                {
                    var entry = entries[row * columns + column];
                    if (column > 0) ImGui.SameLine();
                    ImGui.PushID(entry.Id.ToString("X"));
                    var origin = ImGui.GetCursorScreenPos();
                    if (ImGui.InvisibleButton("##shader", tile, ImGuiButtonFlags.EnableNav))
                        clicked = entry;
                    bool hovered = ImGui.IsItemHovered();
                    var draw = ImGui.GetWindowDrawList();
                    var colour = entry.Id == selected ? ImGuiCol.Header : hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg;
                    draw.AddRectFilled(origin, origin + tile, ImGui.GetColorU32(colour), 4f);
                    var min = origin + new Vector2(padding);
                    var max = min + new Vector2(imageSize);
                    AssetsBrowserFrame.DrawThumbnail(draw, entry, min, max);
                    draw.AddText(new Vector2(min.X, max.Y + padding), ImGui.GetColorU32(ImGuiCol.Text),
                        AssetsBrowserFrame.Fit(entry.Name, imageSize));
                    if (hovered) ImGui.SetTooltip($"{entry.Path}\n0x{entry.Id:X16}");
                    ImGui.PopID();
                }
        clipper.End();
        return clicked;
    }
}
