using ReLunacy.Core.Undo;
using ReLunacy.Engine.Packing;

namespace ReLunacy.Core.Project;

internal static class ShaderSwapEdits
{
    private static readonly Dictionary<ShaderSwapSlot, ShaderSwapEdit> Pending = [];

    public static int Revision { get; private set; }
    public static ShaderSwapEdit? Get(ShaderSwapSlot slot) => Pending.GetValueOrDefault(slot);
    public static Dictionary<ShaderSwapSlot, ShaderSwapEdit> Snapshot() => new(Pending);

    public static void Set(ShaderSwapSlot slot, ShaderSwapEdit? edit, string label)
    {
        if (edit?.Replacement == slot.Original) edit = null;
        var previous = Get(slot);
        if (previous?.Replacement == edit?.Replacement) return;
        Apply(edit);
        UndoManager.Singleton.Push(label, () => Apply(previous), () => Apply(edit));

        void Apply(ShaderSwapEdit? value)
        {
            if (value != null) Pending[slot] = value;
            else Pending.Remove(slot);
            Revision++;
        }
    }

    public static void Clear()
    {
        Pending.Clear();
        Revision++;
    }
}
