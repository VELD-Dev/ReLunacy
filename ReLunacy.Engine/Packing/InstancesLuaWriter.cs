using System.Text;
using System.Text.RegularExpressions;
using System.Buffers.Binary;

namespace ReLunacy.Engine.Packing;

/// <summary>What <see cref="InstancesLuaWriter"/> made: the files to write (by name within the level) and what changed.</summary>
public sealed record InstancesLuaResult(Dictionary<string, byte[]> Files, List<string> Changes, List<string> Problems);

/// <summary>
/// Keeps a region's instances.lua / instances.lc (and the .trimmed pair) in step with its gp_prius. A line binds a name to a
/// handle: <c>name = path_handle:new( layer, index ); -- 0xTUID</c>, (layer, index in layer) as <see cref="GpPriusDocument.Handle"/>.
/// The file's line order is the build's and is kept: lines are changed in place (new handle, new name), removed when their
/// instance is deleted, and new instances are added after the last line of their layer. A renamed instance whose old name a
/// level script uses also keeps the old name as an alias line, so the scripts keep working. The .lc is compiled from the text.
/// </summary>
public static class InstancesLuaWriter
{
    private static readonly Regex Line = new(@"^(\S+) = (\w+)_handle:new\( (\d+), (\d+) \); -- 0x([0-9A-Fa-f]+)$", RegexOptions.Compiled);
    private static readonly string[] Families = ["moby", "path", "volume", "clue", "controller", "scent", "area", "pod"];
    private static readonly HashSet<string> Keywords = ["and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"];

    private sealed record Entry(string Text, int Kind, int Layer, int Index, ulong Tuid, string Name, bool Foreign);

    public static readonly string[] FileNames = ["instances.lua", "instances.lua.trimmed"];

    /// <summary>Rebuilds the files for a region whose gp_prius went from <paramref name="oldGp"/> to <paramref name="newGp"/>.
    /// <paramref name="read"/> reads a file of the level by name; <paramref name="scripts"/> gives the level's script sources.</summary>
    public static InstancesLuaResult Build(string region, byte[] oldGp, byte[] newGp, Func<string, byte[]?> read,
        Func<IEnumerable<(string Name, string Text)>> scripts)
    {
        var result = new InstancesLuaResult(new(StringComparer.OrdinalIgnoreCase), [], []);
        GpPriusDocument oldDoc, newDoc;
        try { oldDoc = GpPriusDocument.Parse(oldGp); newDoc = GpPriusDocument.Parse(newGp); }
        catch (Exception e) { result.Problems.Add($"instances.lua not updated, gp_prius does not parse ({e.Message})"); return result; }

        // Old instance -> new instance: the k-th of a TUID in a kind matches the k-th in the other file.
        var map = new Dictionary<(int, int), int>();
        var deleted = new List<(int Kind, int Index)>();
        var created = new List<(int Kind, int Index)>();
        for (int kind = 0; kind <= (int)GpKind.Pod; kind++)
        {
            var k = (GpKind)kind;
            var newByTuid = new Dictionary<ulong, Queue<int>>();
            for (int i = 0; i < newDoc.Count(k); i++)
            {
                ulong t = newDoc.Tuid(new GpInstance(k, i));
                if (!newByTuid.TryGetValue(t, out var q)) newByTuid[t] = q = new();
                q.Enqueue(i);
            }
            var used = new HashSet<int>();
            for (int i = 0; i < oldDoc.Count(k); i++)
            {
                if (newByTuid.TryGetValue(oldDoc.Tuid(new GpInstance(k, i)), out var q) && q.Count > 0) { int n = q.Dequeue(); map[(kind, i)] = n; used.Add(n); }
                else deleted.Add((kind, i));
            }
            for (int i = 0; i < newDoc.Count(k); i++) if (!used.Contains(i)) created.Add((kind, i));
        }

        List<(string Name, string Text)>? scriptCache = null;
        IReadOnlyList<(string Name, string Text)> Scripts() => scriptCache ??= scripts().ToList();

        foreach (string file in FileNames)
        {
            if (read($"{region}/{file}") is not { } bytes) continue;
            var lines = Encoding.Latin1.GetString(bytes).Split("\r\n");
            string header = lines[0];
            bool trailing = lines[^1].Length == 0;
            var entries = new List<Entry>();
            foreach (string text in lines.Skip(1).Where(l => l.Length > 0))
            {
                var m = Line.Match(text);
                int kind = m.Success ? Array.IndexOf(Families, m.Groups[2].Value) : -1;
                entries.Add(kind < 0 ? new Entry(text, -1, 0, 0, 0, "", true)
                    : new Entry(text, kind, int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), Convert.ToUInt64(m.Groups[5].Value, 16), m.Groups[1].Value, false));
            }
            bool trimmed = file.EndsWith(".trimmed");

            // Which old handle each line names (the line's own TUID must agree, or it is left alone).
            var lineOf = new Dictionary<(int, int), int>();
            var output = new List<string>();
            var kept = new List<(string Text, int Layer)>();
            var gone = new List<string>();
            var renamed = new List<string>();
            var aliases = new List<string>();
            int changedLines = 0;
            foreach (var e in entries)
            {
                if (e.Foreign) { kept.Add((e.Text, int.MaxValue)); continue; }
                var (first, count) = oldDoc.LayerRun((GpKind)e.Kind, e.Layer);
                if (e.Index >= count || oldDoc.Tuid(new GpInstance((GpKind)e.Kind, first + e.Index)) != e.Tuid)
                { kept.Add((e.Text, e.Layer)); continue; }
                int oldIndex = first + e.Index;
                if (!map.TryGetValue((e.Kind, oldIndex), out int ni))
                {
                    gone.Add(e.Name);
                    changedLines++;
                    continue;
                }
                var inst = new GpInstance((GpKind)e.Kind, ni);
                var (layer, index) = newDoc.Handle(inst);
                // A line whose name is not the instance's own (an alias a save kept) only follows the handle.
                string name = e.Name == oldDoc.Name(new GpInstance((GpKind)e.Kind, oldIndex)) ? newDoc.Name(inst) : e.Name;
                string text = Format(name, e.Kind, layer, index, e.Tuid);
                if (text != e.Text) changedLines++;
                if (name != e.Name)
                {
                    renamed.Add(e.Name);
                    if (UsedBy(e.Name, Scripts()) is { } script)
                    {
                        kept.Add((text, layer));
                        kept.Add((Format(e.Name, e.Kind, layer, index, e.Tuid), layer));
                        aliases.Add($"{e.Name} (kept for {script})");
                        continue;
                    }
                }
                kept.Add((text, layer));
            }

            // Deleted instances the scripts still name.
            foreach (string name in gone.Distinct())
                if (UsedBy(name, Scripts()) is { } script) result.Problems.Add($"{file}: {name} is deleted but {script} uses it");

            // New instances after the last line of their layer (a new layer after the last lower one).
            int added = 0;
            foreach (var (kind, index) in created)
            {
                var inst = new GpInstance((GpKind)kind, index);
                var (layer, idx) = newDoc.Handle(inst);
                string name = newDoc.Name(inst);
                if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$") || Keywords.Contains(name))
                { result.Problems.Add($"{file}: new {Families[kind]} {name} is not a Lua name, not bound"); continue; }
                int at = kept.FindLastIndex(l => l.Layer <= layer);
                kept.Insert(at + 1, (Format(name, kind, layer, idx, newDoc.Tuid(inst)), layer));
                added++;
            }
            var sb = new StringBuilder(header).Append("\r\n");
            foreach (var l in kept) sb.Append(l.Text).Append("\r\n");
            string textOut = sb.ToString();
            if (!trailing) textOut = textOut[..^2];
            byte[] luaBytes = Encoding.Latin1.GetBytes(textOut);
            if (luaBytes.AsSpan().SequenceEqual(bytes)) continue;

            result.Files[$"{region}/{file}"] = luaBytes;
            string lc = file.Replace(".lua", ".lc");
            if (Compile(kept.Select(l => l.Text)) is { } chunk) result.Files[$"{region}/{lc}"] = chunk;
            else result.Problems.Add($"{lc}: not rebuilt, a line is not a handle binding");
            result.Changes.Add($"{file}: {added} added, {gone.Count} removed, {renamed.Count} renamed" +
                (aliases.Count > 0 ? $", {aliases.Count} old name(s) kept as alias ({string.Join(", ", aliases.Take(3))}{(aliases.Count > 3 ? ", ..." : "")})" : "") +
                $", {changedLines} line(s) changed");
        }
        return result;
    }

    /// <summary>The Lua 5.1 chunk (big-endian, 4-byte numbers, no debug info) of the binding lines, as the game's compiler makes
    /// it: per line GETGLOBAL family, SELF "new", LOADK layer, LOADK index, CALL, SETGLOBAL name; constants in first-use
    /// order (name, family, "new", numbers), then RETURN. Null when a line is not a binding.</summary>
    public static byte[]? Compile(IEnumerable<string> lines)
    {
        var constants = new List<object>();
        int K(object v) { int i = constants.FindIndex(c => c.Equals(v)); if (i >= 0) return i; constants.Add(v); return constants.Count - 1; }
        var code = new List<uint>();
        foreach (string line in lines)
        {
            var m = Line.Match(line);
            if (!m.Success) return null;
            int name = K(m.Groups[1].Value), family = K(m.Groups[2].Value + "_handle"), nw = K("new");
            int layer = K(float.Parse(m.Groups[3].Value)), index = K(float.Parse(m.Groups[4].Value));
            code.Add(5u | (uint)family << 14);                       // GETGLOBAL 0 family
            code.Add(11u | (uint)(256 + nw) << 14);                  // SELF 0 0 K(new)
            code.Add(1u | 2u << 6 | (uint)layer << 14);              // LOADK 2 layer
            code.Add(1u | 3u << 6 | (uint)index << 14);              // LOADK 3 index
            code.Add(28u | 2u << 14 | 4u << 23);                     // CALL 0 4 2
            code.Add(7u | (uint)name << 14);                         // SETGLOBAL 0 name
        }
        code.Add(30u | 1u << 23);                                     // RETURN 0 1
        var o = new List<byte>([0x1B, 0x4C, 0x75, 0x61, 0x51, 0, 0, 4, 4, 4, 4, 0]);
        void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); o.AddRange(b.ToArray()); }
        U32(0); U32(0); U32(0);
        o.AddRange(new byte[] { 0, 0, 2, (byte)(code.Count > 1 ? 4 : 2) });
        U32((uint)code.Count); foreach (uint c in code) U32(c);
        U32((uint)constants.Count);
        foreach (var c in constants)
        {
            if (c is string str) { o.Add(4); U32((uint)str.Length + 1); o.AddRange(Encoding.Latin1.GetBytes(str)); o.Add(0); }
            else { o.Add(3); Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteSingleBigEndian(b, (float)c); o.AddRange(b.ToArray()); }
        }
        U32(0); U32(0); U32(0); U32(0);
        return [.. o];
    }

    public static string Format(string name, int kind, int layer, int index, ulong tuid) =>
        $"{name} = {Families[kind]}_handle:new( {layer}, {index} ); -- 0x{tuid:X16}";

    /// <summary>The first script that names <paramref name="name"/> as a whole word, or null.</summary>
    private static string? UsedBy(string name, IReadOnlyList<(string Name, string Text)> scripts)
    {
        var word = new Regex($@"(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_])");
        foreach (var (n, text) in scripts) if (word.IsMatch(text)) return n;
        return null;
    }

    /// <summary>The Lua sources under the level's scripts folder (/levels/&lt;level&gt;/scripts).</summary>
    public static IEnumerable<(string Name, string Text)> LevelScripts(Loading.IO.FileManager files)
    {
        foreach (string path in files.EntryPaths().Where(p => p.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                     && p.Contains("/scripts/", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("/built", StringComparison.OrdinalIgnoreCase)).ToList())
            if (files.ReadOriginal(path) is { } b) yield return (path, Encoding.Latin1.GetString(b));
    }
}
