namespace ReLunacy.Utility;

/// <summary>A physical key, by USB HID scancode.
///
/// Values ARE SDL3 scancodes, so <see cref="Input.IsKeyDown"/> can index SDL's keyboard state array
/// with one directly. Scancodes describe key POSITION, not the character it produces, which is what
/// makes WASD stay under the left hand on an AZERTY layout.</summary>
public enum KeyboardKey
{
    Unknown = 0,

    A = 4, B = 5, C = 6, D = 7, E = 8, F = 9, G = 10, H = 11, I = 12,
    J = 13, K = 14, L = 15, M = 16, N = 17, O = 18, P = 19, Q = 20, R = 21,
    S = 22, T = 23, U = 24, V = 25, W = 26, X = 27, Y = 28, Z = 29,

    // The number ROW. Scancode order runs 1..9 then 0, not 0..9.
    Number1 = 30, Number2 = 31, Number3 = 32, Number4 = 33, Number5 = 34,
    Number6 = 35, Number7 = 36, Number8 = 37, Number9 = 38, Number0 = 39,

    Enter = 40, Escape = 41, BackSpace = 42, Tab = 43, Space = 44,
    Minus = 45, Equal = 46, BracketLeft = 47, BracketRight = 48, BackSlash = 49,
    Semicolon = 51, Apostrophe = 52, Grave = 53, Comma = 54, Period = 55, Slash = 56,
    CapsLock = 57,

    F1 = 58, F2 = 59, F3 = 60, F4 = 61, F5 = 62, F6 = 63,
    F7 = 64, F8 = 65, F9 = 66, F10 = 67, F11 = 68, F12 = 69,

    PrintScreen = 70, ScrollLock = 71, Pause = 72,
    Insert = 73, Home = 74, PageUp = 75, Delete = 76, End = 77, PageDown = 78,
    Right = 79, Left = 80, Down = 81, Up = 82,

    NumLock = 83,
    KeypadDivide = 84, KeypadMultiply = 85, KeypadMinus = 86, KeypadPlus = 87, KeypadEnter = 88,
    Keypad1 = 89, Keypad2 = 90, Keypad3 = 91, Keypad4 = 92, Keypad5 = 93,
    Keypad6 = 94, Keypad7 = 95, Keypad8 = 96, Keypad9 = 97, Keypad0 = 98,
    KeypadDecimal = 99,

    Menu = 118,

    ControlLeft = 224, ShiftLeft = 225, AltLeft = 226, WinLeft = 227,
    ControlRight = 228, ShiftRight = 229, AltRight = 230, WinRight = 231,
}

/// <summary>Values are SDL3's 1-based button indices, which is what its button events report.</summary>
public enum MouseButton
{
    Left = 1,
    Middle = 2,
    Right = 3,
    X1 = 4,
    X2 = 5,
}
