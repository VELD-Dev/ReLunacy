using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Export;

/// <summary>
/// Interface for asset exporters
/// </summary>
public interface IExporter<in TAsset> where TAsset : IAsset
{
    /// <summary>
    /// Exports an asset to a file
    /// </summary>
    void Export(TAsset asset, string outputPath);

    /// <summary>
    /// Gets the file extension for this exporter (e.g., ".obj", ".dds")
    /// </summary>
    string FileExtension { get; }
}
