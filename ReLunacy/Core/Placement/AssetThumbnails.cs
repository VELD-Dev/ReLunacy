using ReLunacy.Engine.Assets.Browser;

namespace ReLunacy.Core.Placement;

/// <summary>
/// Moby and Tie class thumbnails, for the frames that place classes (Asset Explorer, the 3D view's drag preview, the moon
/// dialog): <see cref="ThumbnailService"/>'s, by class.
/// </summary>
public sealed class AssetThumbnails
{
    public static AssetThumbnails Singleton { get; } = new();

    public const uint Size = ThumbnailService.Size;

    private static AssetKey Key(bool tie, ulong id) => new(tie ? AssetKind.Tie : AssetKind.Moby, id);

    /// <summary>The thumbnail's image, or null while it is made (it is asked for by this call).</summary>
    public ImTextureRef? Get(bool tie, ulong id) => ThumbnailService.Singleton.Get(Key(tie, id));

    /// <summary>Drops a class's thumbnail (the class was removed).</summary>
    public void Forget(bool tie, ulong id) => ThumbnailService.Singleton.Forget(Key(tie, id));

    /// <summary>The thumbnail was tried and could not be made (nothing to draw).</summary>
    public bool Failed(bool tie, ulong id) => ThumbnailService.Singleton.StateOf(Key(tie, id)) == ThumbnailService.State.Unavailable;

    /// <summary>Moves the thumbnails along; once a frame.</summary>
    public void Pump() => ThumbnailService.Singleton.Pump();

    /// <summary>Thumbnails still waiting.</summary>
    public int Pending => ThumbnailService.Singleton.Pending;
}
