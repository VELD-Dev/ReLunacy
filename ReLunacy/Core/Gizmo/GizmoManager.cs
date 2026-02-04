using System.Numerics;
using System.Runtime.CompilerServices;
using Hexa.NET.ImGuizmo;
using Hexa.NET.ImGui;

namespace ReLunacy.Core.Gizmo;

public enum GizmoOperation
{
    Translate,
    Rotate,
    Scale
}

public enum GizmoSpace
{
    Local,
    World
}

public static class GizmoManager
{
    public static GizmoOperation CurrentOperation { get; set; } = GizmoOperation.Translate;
    public static GizmoSpace CurrentSpace { get; set; } = GizmoSpace.World;

    private static bool _isUsing = false;
    public static bool IsUsing => _isUsing;

    private static bool _initialized = false;

    public static void Initialize()
    {
        if (_initialized) return;

        ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
        _initialized = true;
    }

    public static void BeginFrame()
    {
        if (!_initialized) Initialize();
        ImGuizmo.BeginFrame();
    }

    public static unsafe bool Manipulate(
        ref Matrix4x4 worldMatrix,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix,
        Vector2 viewportPos,
        Vector2 viewportSize)
    {
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetDrawlist(ImGui.GetWindowDrawList());
        ImGuizmo.SetRect(viewportPos.X, viewportPos.Y, viewportSize.X, viewportSize.Y);

        var op = CurrentOperation switch
        {
            GizmoOperation.Translate => ImGuizmoOperation.Translate,
            GizmoOperation.Rotate => ImGuizmoOperation.Rotate,
            GizmoOperation.Scale => ImGuizmoOperation.Scale,
            _ => ImGuizmoOperation.Translate
        };

        var mode = CurrentSpace == GizmoSpace.Local
            ? ImGuizmoMode.Local
            : ImGuizmoMode.World;

        float* viewPtr = (float*)Unsafe.AsPointer(ref viewMatrix);
        float* projPtr = (float*)Unsafe.AsPointer(ref projectionMatrix);
        float* worldPtr = (float*)Unsafe.AsPointer(ref worldMatrix);

        bool manipulated = ImGuizmo.Manipulate(viewPtr, projPtr, op, mode, worldPtr);
        _isUsing = ImGuizmo.IsUsing();
        return manipulated;
    }

    public static void SetOperation(GizmoOperation op)
    {
        CurrentOperation = op;
    }

    public static void SetSpace(GizmoSpace space)
    {
        CurrentSpace = space;
    }

    public static void ToggleSpace()
    {
        CurrentSpace = CurrentSpace == GizmoSpace.Local
            ? GizmoSpace.World
            : GizmoSpace.Local;
    }
}
