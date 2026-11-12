using System.Numerics;

namespace ReLunacy.Engine.Assets.LevelElements;

/// <summary>
/// What a path is for, to tell paths apart (<see cref="GameplayPath.Role"/>). A path has no class of its own, but
/// what uses it does: a Clue (GrindRail +0x14, LedgeGrab +0x14, SuperSoakerFillUp +0x18, Enemy +0x78...) or a
/// controller (a camera's path +0x64 or look-at path +0x68, CarSpawner, ZgruteWaveAttackController...), whose
/// settings name it. That covers the paths the level's data links (luminopolis 71 of 189); the rest are used
/// by the level scripts (krell_canyon: 391 of 416), and take a role from their name where it says one -
/// "patrol...", "cam...", "...grind...", "...ledge...".
/// </summary>
public static class PathRoles
{
    public const string Camera = "Camera";
    public const string CameraLookAt = "Camera look-at";
    public const string Patrol = "Patrol";
    public const string Unassigned = "Unassigned";

    /// <summary>Gives every path its role. Clues come first, then cameras, then other controllers, then the name.</summary>
    public static void Assign(IReadOnlyList<GameplayPath> paths, IReadOnlyList<Volume> volumes, IReadOnlyList<GameplayController> controllers,
        IEnumerable<(string Name, string Asset, IReadOnlyList<ClueReference> References)>? mobys = null)
    {
        // References name their target; several paths can share a name, and all of them get the role.
        var found = new Dictionary<string, (int Rank, string Role, string Source)>();
        void Note(string target, int rank, string role, string source)
        {
            if (!found.TryGetValue(target, out var current) || rank < current.Rank) found[target] = (rank, role, source);
        }
        foreach (var volume in volumes)
            if (volume.Clue is { } clue)
                foreach (var reference in clue.References)
                    if (reference.Kind == "Path") Note(reference.Name, 0, clue.ClassName, $"Clue {clue.Name} ({clue.ClassName} +0x{reference.Offset:X})");
        foreach (var controller in controllers)
            foreach (var reference in controller.References)
            {
                if (reference.Kind != "Path") continue;
                string cls = controller.ClassName ?? $"0x{controller.Class:X}";
                if (controller.IsCamera)
                    // CamArgFollowPath: +0x64 the camera path, +0x68 the look-at path (Fianshoo's NEW_ENGINE_CAMERAS).
                    Note(reference.Name, 1, reference.Offset == 0x68 ? CameraLookAt : Camera, $"{controller.Name} ({cls} +0x{reference.Offset:X})");
                else
                    Note(reference.Name, 3, cls, $"{controller.Name} ({cls} +0x{reference.Offset:X})");
            }
        // A Moby's settings naming a path: an enemy's patrol (dust_creeper +0x808), its role the Moby's asset.
        foreach (var (name, asset, references) in mobys ?? [])
            foreach (var reference in references)
                if (reference.Kind == "Path") Note(reference.Name, 2, asset, $"{name} ({asset} +0x{reference.Offset:X})");

        foreach (var path in paths)
        {
            if (found.TryGetValue(path.Name, out var hit)) { path.Role = hit.Role; path.RoleSource = hit.Source; continue; }
            path.Role = FromName(path.Name);
            path.RoleSource = null;
        }
    }

    private static string FromName(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("grind")) return "GrindRail";
        if (n.Contains("ledge")) return "LedgeGrab";
        if (n.Contains("lookat") || n.Contains("look_at")) return CameraLookAt;
        if (n.StartsWith("cam") || n.Contains("_cam") || n.Contains("camera")) return Camera;
        if (n.Contains("patrol")) return Patrol;
        return Unassigned;
    }

    /// <summary>A role's colour: fixed for the common ones, else a stable colour from its name.</summary>
    public static Vector3 Colour(string role) => role switch
    {
        "GrindRail" => new(1f, 0.55f, 0.1f),
        "LedgeGrab" => new(0.35f, 0.95f, 0.35f),
        Camera => new(1f, 0.85f, 0.2f),
        CameraLookAt => new(1f, 1f, 0.65f),
        Patrol or "Enemy" => new(1f, 0.3f, 0.3f),
        Unassigned => new(0.15f, 0.85f, 1f),
        _ => ClueClasses.ColourOf(role),
    };
}
