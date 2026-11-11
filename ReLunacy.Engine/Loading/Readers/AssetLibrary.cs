using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// One family of a level's classes (Mobys, Ties, Shrubs), read from its .dat file by TUID the first
/// time something asks for it rather than all at once. A new-engine level ships every class any of
/// its regions uses, and a region places only part of them: loading one region reads the classes
/// its zones and instances name, and the asset viewer reads any other one when it is picked.
///
/// The old engine keys its classes by index or file offset and has a single region, so it reads
/// them all up front and hands them over through <see cref="FromLoaded"/>.
///
/// Reads may come from the loading task or the main thread, never both at once in practice (a
/// region load wipes the scene first), but they share the level's file streams, so they are
/// serialised all the same.
/// </summary>
public sealed class AssetLibrary<T> where T : class
{
    private readonly Dictionary<ulong, AssetPointer> _pointers;
    private readonly Func<AssetPointer, T?>? _read;
    private readonly Func<AssetPointer, string?>? _readName;
    private readonly Dictionary<ulong, T> _loaded = [];
    private readonly Dictionary<ulong, string> _names = [];
    // Classes that failed to read (or read as empty), so they are not retried on every lookup.
    private readonly HashSet<ulong> _failed = [];
    private readonly object _lock;

    /// <param name="fileLock">Shared by every library of one level: they read the same file streams.</param>
    public AssetLibrary(IEnumerable<AssetPointer> pointers, Func<AssetPointer, T?> read, Func<AssetPointer, string?>? readName, object fileLock)
    {
        _pointers = [];
        foreach (var pointer in pointers)
            _pointers.TryAdd(pointer.TUID, pointer);
        _read = read;
        _readName = readName;
        _lock = fileLock;
    }

    private AssetLibrary(Dictionary<ulong, T> loaded)
    {
        _pointers = [];
        _loaded = loaded;
        _lock = new object();
    }

    /// <summary>Every class already read, with nothing left to read on demand.</summary>
    public static AssetLibrary<T> FromLoaded(Dictionary<ulong, T> loaded) => new(loaded);

    /// <summary>The classes read so far. Grows as <see cref="Get"/> reads more.</summary>
    public IReadOnlyDictionary<ulong, T> Loaded => _loaded;

    /// <summary>Every class the level ships, read or not.</summary>
    public IEnumerable<ulong> Ids => _pointers.Count > 0 ? _pointers.Keys : _loaded.Keys;

    public int Count => _pointers.Count > 0 ? _pointers.Count : _loaded.Count;

    public bool Contains(ulong id) => _pointers.ContainsKey(id) || _loaded.ContainsKey(id);

    /// <summary>The class, read from disk on first use; null when the level has no such class or it
    /// does not read.</summary>
    public T? Get(ulong id)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(id, out var loaded)) return loaded;
            if (_read == null || _failed.Contains(id) || !_pointers.TryGetValue(id, out var pointer)) return null;

            T? value = null;
            try { value = _read(pointer); }
            catch (Exception ex) { Console.WriteLine($"Failed to read {typeof(T).Name} {id:X16}: {ex.Message}"); }
            if (value == null) _failed.Add(id);
            else _loaded[id] = value;
            return value;
        }
    }

    /// <summary>The class's name without reading the class itself: from the loaded class when there is
    /// one, else read off its header. Null when neither gives one.</summary>
    public string? NameOf(ulong id, Func<T, string?> loadedName)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(id, out var loaded)) return loadedName(loaded);
            if (_names.TryGetValue(id, out var cached)) return cached;
            if (_readName == null || !_pointers.TryGetValue(id, out var pointer)) return null;

            string? name = null;
            try { name = _readName(pointer); }
            catch (Exception ex) { Console.WriteLine($"Failed to read the name of {typeof(T).Name} {id:X16}: {ex.Message}"); }
            _names[id] = name ?? string.Empty;
            return name;
        }
    }

    /// <summary>Forgets every class read so far, so a region switch does not keep the previous region's
    /// geometry in memory. Nothing for an old-engine library, which cannot read them back.</summary>
    public void Unload()
    {
        if (_read == null) return;
        lock (_lock)
        {
            _loaded.Clear();
            _failed.Clear();
        }
    }
}
