using System.Text.Json;
using ReLunacy.Engine.Packing;

namespace ReLunacy.Engine.Projects;

/// <summary>The original game files preserved by project installs, shared by every project for the game.</summary>
internal sealed class GameInstallBackups
{
    internal static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _root;
    private readonly string _index;
    private readonly Dictionary<string, string> _originals;

    public GameInstallBackups(string root)
    {
        _root = Path.GetFullPath(root);
        _index = Path.Combine(_root, ".relunacy-mods.json");
        _originals = File.Exists(_index)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_index))
                ?? throw new InvalidDataException($"Invalid backup index: {_index}")
            : [];
    }

    public IEnumerable<KeyValuePair<string, string>> Originals => _originals;

    public static string PristinePath(string file) => file + ".pristine";

    public string Relative(string file)
    {
        string relative = Path.GetRelativePath(_root, Path.GetFullPath(file));
        if (relative is "." or ".." || Path.IsPathRooted(relative)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"{file} is outside the game folder");
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public string Absolute(string relative)
    {
        string file = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (Relative(file) != relative) throw new InvalidDataException($"Invalid backup path: {relative}");
        return file;
    }

    public bool Tracks(string file) => _originals.ContainsKey(Relative(file));

    public string Preserve(string file, string original, string hash)
    {
        string relative = Relative(file);
        string pristine = PristinePath(file);
        if (_originals.TryGetValue(relative, out var recorded))
        {
            Verify(pristine, recorded, "pristine copy");
            return pristine;
        }

        if (File.Exists(pristine)) Verify(pristine, hash, "existing pristine copy");
        else
        {
            string temporary = TemporaryFor(pristine);
            try
            {
                File.Copy(original, temporary);
                Verify(temporary, hash, "copy of the original");
                File.Move(temporary, pristine);
            }
            finally { File.Delete(temporary); }
        }

        string pendingIndex = TemporaryFor(_index);
        _originals.Add(relative, hash);
        try
        {
            File.WriteAllText(pendingIndex, JsonSerializer.Serialize(_originals, Json));
            File.Move(pendingIndex, _index, overwrite: true);
        }
        catch
        {
            _originals.Remove(relative);
            throw;
        }
        finally { File.Delete(pendingIndex); }

        return pristine;
    }

    public static string? FindOriginal(string file, string? hash)
    {
        if (hash == null) return null;
        if (File.Exists(file) && LunaProject.HashFile(file) == hash) return file;
        return Backups.Of(file).FirstOrDefault(backup => LunaProject.HashFile(backup.Path) == hash)?.Path;
    }

    public static void Verify(string path, string hash, string description)
    {
        if (!File.Exists(path) || LunaProject.HashFile(path) != hash)
            throw new InvalidDataException($"The {description} is missing or has changed: {path}");
    }

    public static string? Replace(string file, string staged, string? currentHash)
    {
        if (currentHash == null)
        {
            if (File.Exists(file)) throw new IOException($"{file} appeared while preparing the restore");
            File.Move(staged, file);
            return null;
        }

        Verify(file, currentHash, "game file being replaced");
        string backup = Backups.Create(file);
        Verify(backup, currentHash, "backup of the replaced game file");
        File.Move(staged, file, overwrite: true);
        return backup;
    }

    public static string TemporaryFor(string path) => $"{path}.{Guid.NewGuid():N}.tmp";
}
