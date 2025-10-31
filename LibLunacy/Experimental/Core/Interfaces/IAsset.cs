namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Base interface for all game assets
/// </summary>
public interface IAsset
{
    /// <summary>
    /// Unique identifier for this asset
    /// </summary>
    ulong Id { get; }

    /// <summary>
    /// Human-readable name (if available)
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Whether this asset has been fully loaded
    /// </summary>
    bool IsLoaded { get; }
}
