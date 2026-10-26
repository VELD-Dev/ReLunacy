namespace ReLunacy.Engine.Assets.Animations;

/// <summary>
/// Engine-independent animation set attached to a Moby. Old-engine Mobys synthesize a set from
/// their ordered D100 animation pointer list; new-engine Mobys resolve a real animset asset by TUID.
/// </summary>
public sealed class AnimationSet
{
    public ulong Id { get; }
    public IReadOnlyList<AnimationClip> Clips { get; }
    public bool IsSynthetic { get; }

    public AnimationSet(ulong id, IReadOnlyList<AnimationClip>? clips, bool isSynthetic = false)
    {
        Id = id;
        Clips = clips ?? [];
        IsSynthetic = isSynthetic;
    }
}
