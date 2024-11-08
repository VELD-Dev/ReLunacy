using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Utils;

public abstract class EnhancedEnum<T> where T : EnhancedEnum<T>
{
    protected static readonly List<T> ENUM_VALUES = [];
    protected static readonly HashSet<int> KEYS_SET = [];

    public readonly int KEY;
    public readonly string HUMAN_NAME;

    public EnhancedEnum(int key, string humanName)
    {
        if (KEYS_SET.Contains(key))
            throw new ArgumentException($"Enum already contains a key {key}");
        KEYS_SET.Add(key);

        KEY = key;
        HUMAN_NAME = humanName;
        ENUM_VALUES.Add((T)this);
    }

    public static T operator ++(EnhancedEnum<T> a)
    {
        int newIdx = (ENUM_VALUES.IndexOf((T)a) + 1) % ENUM_VALUES.Count;
        return ENUM_VALUES[newIdx];
    }

    public static T operator --(EnhancedEnum<T> a)
    {
        int newIdx = ENUM_VALUES.IndexOf((T)a) - 1;
        // Negative modulus is weird, that's why I'm not using it here
        if (newIdx == -1)
            newIdx = ENUM_VALUES.Count - 1;
        return ENUM_VALUES[newIdx];
    }

    public static ReadOnlyCollection<T> GetValues()
    {
        return ENUM_VALUES.AsReadOnly();
    }

    public static T? GetByKey(int key)
    {
        foreach (T t in ENUM_VALUES)
            if (t.KEY == key)
                return t;
        return null;
    }

    public override string ToString()
    {
        return HUMAN_NAME;
    }
}
