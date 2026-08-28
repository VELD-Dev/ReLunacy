namespace ReLunacy.Utility;

/// <summary>Font Awesome 6 Free Solid glyphs, merged into the default ImGui font by ImGuiController.
/// Use them anywhere ImGui takes text - a label, a button, a menu item - either directly
/// (<c>ImGui.Text(Icons.Folder)</c>) or combined with text via <see cref="ImGuiPlus.Label"/> /
/// <see cref="ImGuiPlus.IconButton"/>.
///
/// Stored as <c>\uXXXX</c> escapes (all in the Private Use Area the loader whitelists,
/// 0xE000-0xF8FF) so the source stays ASCII. Every codepoint was verified present in
/// Assets/Fonts/fa-solid-900.ttf (Font Awesome 6.7.2). To add more: look the icon up on
/// fontawesome.com (Free + Solid), take its Unicode value, confirm it's in that range.</summary>
public static class Icons
{
    // General / app
    public const string Home = "\uf015";
    public const string Gear = "\uf013";
    public const string Gears = "\uf085";
    public const string Wrench = "\uf0ad";
    public const string Save = "\uf0c7";  // floppy-disk
    public const string Search = "\uf002";  // magnifying-glass
    public const string Close = "\uf00d";  // xmark
    public const string Check = "\uf00c";
    public const string Plus = "\uf067";
    public const string Minus = "\uf068";
    public const string Trash = "\uf1f8";
    public const string Download = "\uf019";
    public const string Upload = "\uf093";
    public const string Refresh = "\uf021";  // arrows-rotate
    public const string Info = "\uf05a";  // circle-info
    public const string Warning = "\uf071";  // triangle-exclamation

    // Files / assets
    public const string Folder = "\uf07b";
    public const string FolderOpen = "\uf07c";
    public const string File = "\uf15b";

    // View / visibility
    public const string Eye = "\uf06e";
    public const string EyeSlash = "\uf070";
    public const string Camera = "\uf030";
    public const string Play = "\uf04b";
    public const string Pause = "\uf04c";

    // Level assets (the editor's domain)
    public const string Cube = "\uf1b2";  // mobys
    public const string Cubes = "\uf1b3";  // ties
    public const string Image = "\uf03e";  // textures
    public const string Palette = "\uf53f";  // shaders / materials
    public const string VectorSquare = "\uf5cb";  // volumes
    public const string Map = "\uf279";  // level
    public const string MountainSun = "\ue52f";  // terrain / ufrags
    public const string Tree = "\uf1bb";  // foliage
    public const string Droplet = "\uf043";
    public const string LayerGroup = "\uf5fd";

    // Lighting
    public const string Lightbulb = "\uf0eb";
    public const string Sun = "\uf185";

    // Debug / dev
    public const string Bug = "\uf188";
    public const string Code = "\uf121";
    public const string Terminal = "\uf120";
    public const string Gamepad = "\uf11b";
}
