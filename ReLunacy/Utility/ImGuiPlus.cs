using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace ReLunacy.Utility;

public static class ImGuiPlus
{
    public static void HelpMarker(string text)
    {
        ImGui.TextDisabled("(?)");
        if(ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort) && ImGui.BeginTooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    public static void RequiredMarker()
    {
        ImGui.TextColored(new(228f / 255f, 48f / 255f, 48f / 255, 1), ['*']);
    }

    public static void RequiredMarker(string text, ImGuiHoveredFlags flags = ImGuiHoveredFlags.DelayShort)
    {
        ImGui.TextColored(new(228f / 255f, 48f / 255f, 48 / 255f, 1), ['*']);
        if(ImGui.IsItemHovered(flags) && ImGui.BeginTooltip())
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
}
