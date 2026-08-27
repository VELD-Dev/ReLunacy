using System.Numerics;
using System.Runtime.InteropServices;
using SDL3;

namespace ReLunacy.Utility;

/// <summary>Keyboard, mouse and text input for the frame currently being built.
///
/// Static because there is exactly one window and one cursor. <see cref="EditorWindow.PumpEvents"/>
/// feeds it; <see cref="Begin"/> and <see cref="End"/> bracket the frame so the edge-triggered queries
/// (IsKeyPressed, IsMouseButtonPressed) can compare against the previous frame.</summary>
public static class Input
{
    // SDL's scancode space. The array is indexed by scancode directly, which is why KeyboardKey's
    // values are scancodes.
    private const int KeyCount = 512;

    private static nint _window;

    private static readonly bool[] _keysDown = new bool[KeyCount];
    private static readonly bool[] _keysDownLast = new bool[KeyCount];
    private static readonly bool[] _mouseDown = new bool[8];
    private static readonly bool[] _mouseDownLast = new bool[8];

    private static Vector2 _mousePosition;
    private static Vector2 _mouseDelta;
    private static Vector2 _scrollDelta;
    private static string _typedText = string.Empty;
    private static bool _relativeMouseMode;

    public static void Init(EditorWindow window) => _window = window.Handle;

    public static void Destroy()
    {
        _window = nint.Zero;
        ClearState();
    }

    /// <summary>Drops every held key and button. Used on focus loss, where the OS stops sending the
    /// matching key-up events.</summary>
    public static void ClearState()
    {
        Array.Clear(_keysDown);
        Array.Clear(_keysDownLast);
        Array.Clear(_mouseDown);
        Array.Clear(_mouseDownLast);
        _mouseDelta = Vector2.Zero;
        _scrollDelta = Vector2.Zero;
    }

    /// <summary>Snapshots the previous frame's key/button state, before this frame's events land.
    /// Everything edge-triggered is a comparison against that snapshot.</summary>
    public static void Begin()
    {
        Array.Copy(_keysDown, _keysDownLast, KeyCount);
        Array.Copy(_mouseDown, _mouseDownLast, _mouseDown.Length);
    }

    /// <summary>Clears the per-frame accumulators. Held state survives; deltas and typed text do not.</summary>
    public static void End()
    {
        _mouseDelta = Vector2.Zero;
        _scrollDelta = Vector2.Zero;
        _typedText = string.Empty;
    }

    internal static void ProcessEvent(SDL.Event e)
    {
        switch ((SDL.EventType)e.Type)
        {
            case SDL.EventType.KeyDown:
                // Auto-repeat is deliberately not recorded: a repeat is not a fresh press, and letting
                // it through would make IsKeyPressed fire over and over while a key is simply held.
                if (!e.Key.Repeat) SetKey(e.Key.Scancode, true);
                break;

            case SDL.EventType.KeyUp:
                SetKey(e.Key.Scancode, false);
                break;

            case SDL.EventType.MouseButtonDown:
            case SDL.EventType.MouseButtonUp:
                SetMouseButton(e.Button.Button, e.Button.Down);
                // The button event carries its own position. Taking it means a click is registered at
                // where the click happened, even if no motion event preceded it that frame.
                _mousePosition = new Vector2(e.Button.X, e.Button.Y);
                break;

            case SDL.EventType.MouseMotion:
                _mousePosition = new Vector2(e.Motion.X, e.Motion.Y);
                // Accumulated, not assigned: several motion events can arrive in one frame, and in
                // relative mouse mode the deltas are the only thing that carries the movement at all.
                _mouseDelta += new Vector2(e.Motion.XRel, e.Motion.YRel);
                break;

            case SDL.EventType.MouseWheel:
                // Flipped is SDL telling us the platform already applied natural scrolling to the
                // values; undoing it keeps a wheel-up here meaning the same thing everywhere.
                float sign = e.Wheel.Direction == SDL.MouseWheelDirection.Flipped ? -1f : 1f;
                _scrollDelta += new Vector2(e.Wheel.X, e.Wheel.Y) * sign;
                break;

            case SDL.EventType.TextInput:
                _typedText += ReadUtf8(e.Text.Text);
                break;
        }
    }

    private static void SetKey(SDL.Scancode scancode, bool down)
    {
        int index = (int)scancode;
        if ((uint)index < KeyCount) _keysDown[index] = down;
    }

    private static void SetMouseButton(byte button, bool down)
    {
        if (button < _mouseDown.Length) _mouseDown[button] = down;
    }

    private static string ReadUtf8(nint utf8) =>
        utf8 == nint.Zero ? string.Empty : Marshal.PtrToStringUTF8(utf8) ?? string.Empty;

    public static bool IsKeyDown(KeyboardKey key) => _keysDown[(int)key];
    public static bool IsKeyUp(KeyboardKey key) => !_keysDown[(int)key];
    public static bool IsKeyPressed(KeyboardKey key) => _keysDown[(int)key] && !_keysDownLast[(int)key];
    public static bool IsKeyReleased(KeyboardKey key) => !_keysDown[(int)key] && _keysDownLast[(int)key];

    public static bool IsMouseButtonDown(MouseButton button) => _mouseDown[(int)button];
    public static bool IsMouseButtonUp(MouseButton button) => !_mouseDown[(int)button];
    public static bool IsMouseButtonPressed(MouseButton button) => _mouseDown[(int)button] && !_mouseDownLast[(int)button];
    public static bool IsMouseButtonReleased(MouseButton button) => !_mouseDown[(int)button] && _mouseDownLast[(int)button];

    /// <summary>Cursor position in window coordinates.</summary>
    public static Vector2 GetMousePosition() => _mousePosition;

    /// <summary>Movement since the last frame. This is the ONLY meaningful reading during a
    /// relative-mouse-mode drag, where the cursor itself does not move.</summary>
    public static Vector2 GetMouseDelta() => _mouseDelta;

    public static bool IsMouseScrolling(out Vector2 delta)
    {
        delta = _scrollDelta;
        return delta != Vector2.Zero;
    }

    public static void SetMousePosition(Vector2 position)
    {
        SDL.WarpMouseInWindow(_window, position.X, position.Y);
        _mousePosition = position;
    }

    /// <summary>Hides the cursor and locks it in place, reporting movement as deltas only. What a
    /// look-around or orbit drag runs in, so the pointer cannot escape the window mid-drag.</summary>
    public static void EnableRelativeMouseMode()
    {
        if (_relativeMouseMode) return;
        _relativeMouseMode = true;
        SDL.SetWindowRelativeMouseMode(_window, true);
    }

    public static void DisableRelativeMouseMode()
    {
        if (!_relativeMouseMode) return;
        _relativeMouseMode = false;
        SDL.SetWindowRelativeMouseMode(_window, false);
    }

    public static bool IsRelativeMouseModeEnabled() => _relativeMouseMode;

    /// <summary>Text typed this frame, already composed (so an IME or a dead key produces the final
    /// character rather than the keystrokes that built it). False when nothing was typed.</summary>
    public static bool GetTypedText(out string text)
    {
        text = _typedText;
        return text.Length > 0;
    }

    public static bool IsTextInputActive() => SDL.TextInputActive(_window);
    public static void EnableTextInput() => SDL.StartTextInput(_window);
    public static void DisableTextInput() => SDL.StopTextInput(_window);

    public static string GetClipboardText() => SDL.GetClipboardText() ?? string.Empty;
    public static void SetClipboardText(string text) => SDL.SetClipboardText(text);
}
