using System.Numerics;

namespace ReLunacy.Utility;

public static class ImGuiPlus
{
    public static void HelpMarker(string text)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort) && ImGui.BeginTooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    public static void RequiredMarker()
    {
        ImGui.TextColored(new Vector4(228f / 255f, 48f / 255f, 48f / 255, 1), "*");
    }

    public static void RequiredMarker(string text, ImGuiHoveredFlags flags = ImGuiHoveredFlags.DelayShort)
    {
        ImGui.TextColored(new Vector4(228f / 255f, 48f / 255f, 48 / 255f, 1), "*");
        if (ImGui.IsItemHovered(flags) && ImGui.BeginTooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    public static void InputVec2(string name, ref Vector2 vector, float step = 1, string field1 = "X", string field2 = "Y", string format = "%0.3f")
    {
        ImGui.BeginGroup();
        ImGui.Text(name);
        ImGui.SameLine();
        ImGui.InputFloat(field1, ref vector.X, step, step, format);
        ImGui.SameLine();
        ImGui.InputFloat(field2, ref vector.Y, step, step, format);
        ImGui.EndGroup();
    }

    public static void InputVec3(string name, ref Vector3 vector, float step = 1, string field1 = "X", string field2 = "Y", string field3 = "Z", string format = "%0.3f")
    {
        ImGui.BeginGroup();
        ImGui.Text(name);
        ImGui.SameLine();
        ImGui.InputFloat(name, ref vector.X, step, step, format);
        ImGui.SameLine();
        ImGui.InputFloat(name, ref vector.Y, step, step, format);
        ImGui.SameLine();
        ImGui.InputFloat(name, ref vector.Z, step, step, format);
        ImGui.EndGroup();
    }

    public static bool CenteredButton(string label, Vector2? size = null, float pivot = 0.5f)
    {
        ImGuiStylePtr style = ImGui.GetStyle();

        float horizontalSize = size != null ? size.Value.X + style.FramePadding.X * 2 : ImGui.CalcTextSize(label).X + style.FramePadding.X * 2f;
        float avail = ImGui.GetContentRegionAvail().X;

        float offset = (avail - horizontalSize) * pivot;
        if (offset > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);

        return size != null ? ImGui.Button(label, size.Value) : ImGui.Button(label);
    }

    public static void CenteredImage(ImTextureRef textureId, Vector2 size, float pivot = 0.5f)
    {
        ImGuiStylePtr style = ImGui.GetStyle();

        float horizontalSize = size.X + style.FramePadding.X * 2;
        float avail = ImGui.GetContentRegionAvail().X;
        float offset = (avail - horizontalSize) * pivot;

        if (offset > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        ImGui.Image(textureId, size);
    }

    /// <summary>A clickable, underlined text link that opens <paramref name="url"/> in the browser
    /// on click and shows a hand cursor + URL tooltip on hover. Behaves as a single inline item, so
    /// SameLine works around it.</summary>
    public static void Hyperlink(string label, string url)
    {
        var color = new Vector4(0.35f, 0.65f, 1f, 1f);
        ImGui.TextColored(color, label);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y - 1f), new Vector2(max.X, max.Y - 1f), ImGui.GetColorU32(color));

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(url);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                ShellUtils.OpenUrl(url);
        }
    }

    /// <summary>Combines a Font Awesome glyph (see <see cref="Icons"/>) and text into one label with
    /// a small gap, for use as a button/menu-item/header label — e.g.
    /// <c>ImGui.MenuItem(ImGuiPlus.Label(Icons.FolderOpen, "Open level"))</c>. The icon is merged
    /// into the default font, so it just renders inline with the text.</summary>
    public static string Label(string icon, string text) => $"{icon}  {text}";

    /// <summary>An icon-only button. <paramref name="id"/> keeps ImGui's label-based identity unique
    /// when several buttons share the same glyph — pass something stable and distinct per button.</summary>
    public static bool IconButton(string icon, string id, Vector2? size = null) =>
        size is { } s ? ImGui.Button($"{icon}##{id}", s) : ImGui.Button($"{icon}##{id}");

    public static void CenteredText(string label, float pivot = 0.5f)
    {
        float horizontalSize = ImGui.CalcTextSize(label).X;
        float avail = ImGui.GetContentRegionAvail().X;
        float offset = (avail - horizontalSize) * pivot;
        if (offset > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        ImGui.Text(label);
    }
}
