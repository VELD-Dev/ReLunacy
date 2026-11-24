using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// The editor's icons for controller classes (Assets/Icons), drawn as camera-facing sprites at the controllers
/// (VulkanRenderer.ControllerIcons). A class names its icon in its game's .fields ("icon Name;", see
/// <see cref="ClassFields"/>); a class with none keeps the wire diamond.
/// </summary>
public static class ControllerIcons
{
    /// <summary>The icon names (file names without .png), in layer order: an icon's index is its layer in the
    /// renderer's texture. Set by <see cref="Load"/>.</summary>
    public static string[] Files { get; private set; } = [];

    /// <summary>The decoded icons, by layer; null where the file could not be read. Set by <see cref="Load"/>.</summary>
    public static Image?[]? Images { get; private set; }

    /// <summary>Reads every icon from <paramref name="folder"/>; one that fails is left out (its classes keep
    /// the diamond) and named in the returned list.</summary>
    public static List<string> Load(string folder)
    {
        var failed = new List<string>();
        Files = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal).ToArray()
            : [];
        var images = new Image?[Files.Length];
        for (int i = 0; i < Files.Length; i++)
        {
            try { images[i] = new Image(File.ReadAllBytes(Path.Combine(folder, Files[i] + ".png"))); }
            catch (Exception ex) { failed.Add($"{Files[i]}: {ex.Message}"); }
        }
        Images = images;
        return failed;
    }

    /// <summary>The icon layer of a controller class in <paramref name="game"/>, or -1 when it has none.</summary>
    public static int IconOf(GameId? game, uint controllerClass)
    {
        if (Images == null || ClassFields.IconName(game, controllerClass) is not { } name) return -1;
        int layer = Array.FindIndex(Files, f => f.Equals(name, StringComparison.OrdinalIgnoreCase));
        return layer >= 0 && Images[layer] != null ? layer : -1;
    }
}
