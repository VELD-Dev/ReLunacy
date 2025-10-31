using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Core.IO;

/// <summary>
/// Interface for reading assets from binary data
/// </summary>
/// <typeparam name="TAsset">Type of asset to read</typeparam>
public interface IAssetReader<out TAsset> where TAsset : IAsset
{
    /// <summary>
    /// Reads an asset from the given reader
    /// </summary>
    TAsset Read(BinaryDataReader reader);
}

/// <summary>
/// Interface for readers that can read multiple assets
/// </summary>
public interface IBatchAssetReader<out TAsset> where TAsset : IAsset
{
    /// <summary>
    /// Reads multiple assets
    /// </summary>
    IEnumerable<TAsset> ReadAll(BinaryDataReader reader);
}
