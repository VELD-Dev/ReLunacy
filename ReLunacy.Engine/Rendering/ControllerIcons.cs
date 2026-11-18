using ReLunacy.Engine.Games;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// The editor's icons for controller classes (Assets/Icons), drawn as camera-facing sprites at the controllers
/// (VulkanRenderer.ControllerIcons). A class with no icon keeps the wire diamond.
/// </summary>
public static class ControllerIcons
{
    /// <summary>The icon file of each class, by game. Add a row to give a class an icon.</summary>
    private static readonly (GameId Game, uint[] Classes, string File)[] Table =
    [
        (GameId.ACrackInTime, [0x35E], "CarSpawner.png"),
        (GameId.ACrackInTime, [0x79E], "Dialogue.png"),
        (GameId.ACrackInTime, [0x8B8], "DisguiseController.png"),
        (GameId.ACrackInTime, [0x171], "EnemyBattlefieldController.png"),
        (GameId.ACrackInTime, [0x173], "EnemyFirepointController.png"),
        (GameId.ACrackInTime, [0x371], "EnemyGroupController.png"),
        (GameId.ACrackInTime, [0x3D0], "EffectController.png"),
        (GameId.ACrackInTime, [0x8A9], "InfoController.png"),
        (GameId.ACrackInTime, [0x211], "MoveGameObjectController.png"),
        (GameId.ACrackInTime, [0x369], "SoundEmitter.png"),
        (GameId.ACrackInTime, [0x202], "SoundEnvironment.png"),
        (GameId.ACrackInTime, [0x6E3], "SpaceAsteroidField.png"),
        (GameId.ACrackInTime, [0x75E, 0x75D, 0x81D], "SpaceAttackController.png"),
    ];

    /// <summary>The icon files, in layer order: an icon's index is its layer in the renderer's texture.</summary>
    public static readonly string[] Files = Table.Select(row => row.File).Distinct().ToArray();

    /// <summary>The decoded icons, by layer; null where the file could not be read. Set by <see cref="Load"/>.</summary>
    public static Image?[]? Images { get; private set; }

    /// <summary>Reads every icon from <paramref name="folder"/>; one that fails is left out (its classes keep
    /// the diamond) and named in the returned list.</summary>
    public static List<string> Load(string folder)
    {
        var failed = new List<string>();
        var images = new Image?[Files.Length];
        for (int i = 0; i < Files.Length; i++)
        {
            try { images[i] = new Image(File.ReadAllBytes(Path.Combine(folder, Files[i]))); }
            catch (Exception ex) { failed.Add($"{Files[i]}: {ex.Message}"); }
        }
        Images = images;
        return failed;
    }

    /// <summary>The icon layer of a controller class in <paramref name="game"/>, or -1 when it has none.</summary>
    public static int IconOf(GameId? game, uint controllerClass)
    {
        if (game == null || Images == null) return -1;
        foreach (var (rowGame, classes, file) in Table)
            if (rowGame == game && classes.Contains(controllerClass))
            {
                int layer = Array.IndexOf(Files, file);
                return Images[layer] != null ? layer : -1;
            }
        return -1;
    }
}
