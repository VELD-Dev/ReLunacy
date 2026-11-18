using System.Text;

namespace ReLunacy.Engine.Packing;

public sealed partial class GpPriusDocument
{
    /// <summary>The longest name any instance of the three new-engine games has (ACiT 64, All 4 One 56, Into the Nexus 48).</summary>
    public const int MaxNameLength = 64;

    /// <summary>Why <paramref name="name"/> can not name an instance, or null. The 263,000 shipped names are made of letters,
    /// digits and underscores only (instances.lua turns a name into a Lua variable) and at most <see cref="MaxNameLength"/>
    /// long; the editor's own generated names also hold a '.' (an asset's name), so any printable ASCII but a space and '/'
    /// (the path separator the editor strips) is accepted.</summary>
    public static string? NameProblem(string name)
    {
        if (string.IsNullOrEmpty(name)) return "empty";
        if (name.Length > MaxNameLength) return $"up to {MaxNameLength} characters";
        foreach (char c in name)
            if (c <= ' ' || c > '~' || c == '/') return "ASCII, no spaces or /";
        return null;
    }

    /// <summary>
    /// Gives an instance another name. The string is rewritten in place in 0x11300 (a string is NUL terminated and only
    /// the instance's metadata entry points at it, as in every shipped file), so the pool changes length by the
    /// difference and every pointer behind it moves (<see cref="Splice"/>). A string that other entries share (a
    /// repeated name that a writer pointed several entries at) is left to them and the instance gets a string of its
    /// own at the end of the pool. Nothing else names an instance by its name: references go by (layer, index) or TUID.
    /// </summary>
    public GpEditResult Rename(GpInstance instance, string name)
    {
        if (NameProblem(name) is { } bad) return GpEditResult.Refuse($"{instance.Kind} name: {bad}");
        var meta = MetadataOf(instance.Kind);
        var names = Section(Names);
        if (meta == null || names == null) return GpEditResult.Refuse($"this region has no {instance.Kind} names");
        if (instance.Index < 0 || instance.Index >= meta.Count) return GpEditResult.Refuse($"{instance.Kind} {instance.Index} does not exist");
        int field = instance.Index * 0x10 + 8;
        var pointer = PointerAt(meta, field);
        if (pointer == null || pointer.Target != names) return GpEditResult.Refuse($"{instance.Kind} {instance.Index}'s name is not in 0x11300");

        string old = String(pointer);
        var result = new GpEditResult();
        byte[] bytes = [.. Encoding.ASCII.GetBytes(name), 0];
        if (old == name) { result.Applied = true; return result; }

        int at = pointer.TargetAt;
        int oldLength = Array.IndexOf(names.Data, (byte)0, at) - at + 1;
        bool shared = oldLength <= 0 || _pointers.Any(p => !ReferenceEquals(p, pointer) && p.Target == names && p.TargetAt >= at && p.TargetAt < at + oldLength);
        if (shared)
        {
            int end = names.Data.Length;
            Splice(names, end, 0, bytes);
            SetPointer(meta, field, names, end);
        }
        else Splice(names, at, oldLength, bytes, pointer);
        result.Applied = true;
        result.Changes.Add($"{instance.Kind} {old} renamed {name}");
        return result;
    }
}
