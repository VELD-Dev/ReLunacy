using LibLunacy.Objects;
using LibLunacy.Shaders;
using LibLunacy.Textures;

namespace LibLunacy
{
    /// <summary>
    /// Rebuilds asset files using new LibLunacy object model with serialization support.
    /// New engine only.
    /// </summary>
    public class AssetBuilder : IDisposable
    {
        private readonly FileManager fm;

        // Cache original asset binary data
        private readonly Dictionary<ulong, byte[]> originalMobyData = new();
        private readonly Dictionary<ulong, byte[]> originalTieData = new();
        private readonly Dictionary<ulong, byte[]> originalShaderData = new();
        private readonly Dictionary<ulong, byte[]> originalZoneData = new();
        private readonly Dictionary<ulong, byte[]> originalTextureData = new();

        public enum AssetType
        {
            Moby,
            Tie,
            Shader,
            Texture,
            Zone
        }

        public AssetBuilder(FileManager fm)
        {
            this.fm = fm;
        }

        /// <summary>
        /// Cache original binary data for an asset
        /// </summary>
        public void CacheOriginalData(ulong tuid, byte[] data, AssetType type)
        {
            var copy = new byte[data.Length];
            Array.Copy(data, copy, data.Length);

            switch (type)
            {
                case AssetType.Moby:
                    originalMobyData[tuid] = copy;
                    break;
                case AssetType.Tie:
                    originalTieData[tuid] = copy;
                    break;
                case AssetType.Shader:
                    originalShaderData[tuid] = copy;
                    break;
                case AssetType.Zone:
                    originalZoneData[tuid] = copy;
                    break;
                case AssetType.Texture:
                    originalTextureData[tuid] = copy;
                    break;
            }
        }

        /// <summary>
        /// Rebuild mobys.dat by serializing modified Moby objects to bytes.
        /// Uses Moby.ToBytes() method to serialize modifications.
        /// </summary>
        public void RebuildMobysFile(Dictionary<ulong, Moby> mobys, string outputPath)
        {
            var assetlookup = fm.igfiles["assetlookup.dat"];
            if (assetlookup == null)
                throw new InvalidOperationException("assetlookup.dat required");

            var mobyptrSection = assetlookup.QuerySection(0x1D600);
            assetlookup.sh.Seek(mobyptrSection.offset);
            var pointers = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, mobyptrSection.length / 0x10);

            using var outputStream = File.Create(outputPath);

            foreach (var ptr in pointers)
            {
                if (mobys.TryGetValue(ptr.TUID, out var moby))
                {
                    // Serialize the modified Moby object to bytes
                    byte[] mobyBytes = moby.ToBytes();
                    outputStream.Write(mobyBytes, 0, mobyBytes.Length);
                }
                else if (originalMobyData.TryGetValue(ptr.TUID, out var originalData))
                {
                    // Fallback: write original data if moby wasn't loaded
                    outputStream.Write(originalData, 0, originalData.Length);
                }
            }
        }

        /// <summary>
        /// Rebuild shaders.dat by serializing Shader objects.
        /// TODO: Implement Shader.ToBytes() method.
        /// </summary>
        public void RebuildShadersFile(Dictionary<ulong, Shader> shaders, string outputPath)
        {
            var assetlookup = fm.igfiles["assetlookup.dat"];
            if (assetlookup == null)
                throw new InvalidOperationException("assetlookup.dat required");

            var shaderptrSection = assetlookup.QuerySection(0x1D100);
            assetlookup.sh.Seek(shaderptrSection.offset);
            var pointers = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, shaderptrSection.length / 0x10);

            using var outputStream = File.Create(outputPath);

            foreach (var ptr in pointers)
            {
                if (originalShaderData.TryGetValue(ptr.TUID, out var originalData))
                {
                    // TODO: Use shader.ToBytes() when implemented
                    outputStream.Write(originalData, 0, originalData.Length);
                }
            }
        }

        /// <summary>
        /// Rebuild ties.dat by serializing modified Tie objects.
        /// Uses Tie.ToBytes() method to serialize modifications.
        /// </summary>
        public void RebuildTiesFile(Dictionary<ulong, Tie> ties, string outputPath)
        {
            var assetlookup = fm.igfiles["assetlookup.dat"];
            if (assetlookup == null)
                throw new InvalidOperationException("assetlookup.dat required");

            var tieptrSection = assetlookup.QuerySection(0x1D300);
            assetlookup.sh.Seek(tieptrSection.offset);
            var pointers = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, tieptrSection.length / 0x10);

            using var outputStream = File.Create(outputPath);

            foreach (var ptr in pointers)
            {
                if (ties.TryGetValue(ptr.TUID, out var tie))
                {
                    // Serialize the modified Tie object to bytes
                    byte[] tieBytes = tie.ToBytes();
                    outputStream.Write(tieBytes, 0, tieBytes.Length);
                }
                else if (originalTieData.TryGetValue(ptr.TUID, out var originalData))
                {
                    // Fallback: write original data if tie wasn't loaded
                    outputStream.Write(originalData, 0, originalData.Length);
                }
            }
        }

        /// <summary>
        /// Rebuild zones.dat by serializing Zone objects.
        /// TODO: Implement Zone.ToBytes() method.
        /// </summary>
        public void RebuildZonesFile(Dictionary<ulong, Zone> zones, string outputPath)
        {
            var assetlookup = fm.igfiles["assetlookup.dat"];
            if (assetlookup == null)
                throw new InvalidOperationException("assetlookup.dat required");

            var zoneptrSection = assetlookup.QuerySection(0x1DA00);
            assetlookup.sh.Seek(zoneptrSection.offset);
            var pointers = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, zoneptrSection.length / 0x10);

            using var outputStream = File.Create(outputPath);

            foreach (var ptr in pointers)
            {
                if (originalZoneData.TryGetValue(ptr.TUID, out var originalData))
                {
                    // TODO: Use zone.ToBytes() when implemented
                    outputStream.Write(originalData, 0, originalData.Length);
                }
            }
        }

        /// <summary>
        /// TODO: Implement proper texture rebuilding.
        /// Textures are more complex - they may be stored in highmip/lowmip files
        /// and require special handling for mipmaps and texture streaming.
        /// </summary>
        public void RebuildTexturesFile(Dictionary<ulong, Texture> textures, string outputPath)
        {
            throw new NotImplementedException("Texture rebuilding not yet implemented. Requires highmip/lowmip file handling.");
        }

        public void Dispose()
        {
            originalMobyData.Clear();
            originalTieData.Clear();
            originalShaderData.Clear();
            originalZoneData.Clear();
            originalTextureData.Clear();
        }
    }
}
