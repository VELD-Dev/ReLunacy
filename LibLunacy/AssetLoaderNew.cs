using System.Numerics;
using LibLunacy.Legacy;
using LibLunacy.Objects;
using LibLunacy.Shaders;
using LibLunacy.Textures;

namespace LibLunacy
{
    /// <summary>
    /// Modern asset loader using new LibLunacy object model.
    /// Replaces Legacy/AssetLoader with proper architecture.
    /// </summary>
    public class AssetLoaderNew : IDisposable
    {
        public FileManager fm;
        private readonly AssetBuilder? builder;
        private readonly bool cacheOriginalData;

        // Use NEW LibLunacy types (Objects/, Shaders/, Textures/)
        public Dictionary<ulong, Moby> mobys = new();
        public Dictionary<ulong, Tie> ties = new();
        public Dictionary<ulong, Shader> shaders = new();
        public Dictionary<ulong, Texture> textures = new();
        public Dictionary<ulong, Zone> zones = new();

        // Track disposable resources
        private readonly List<IDisposable> disposables = new();

        public AssetLoaderNew(FileManager fileManager, bool enableRebuilding = true)
        {
            fm = fileManager;
            cacheOriginalData = enableRebuilding;

            if (enableRebuilding && !fm.isOld)
                builder = new AssetBuilder(fm);
        }

        #region Loading Methods

        public void LoadAssets(ref Vector2 progress, ref float totalProgress, ref string status)
        {
            status = "Loading textures...";
            totalProgress = 0;
            LoadTextures(ref progress);
            status = "Loading shaders...";
            totalProgress = 1;
            LoadShaders(ref progress);
            status = "Loading mobys...";
            totalProgress = 2;
            LoadMobys(ref progress);
            status = "Loading ties...";
            totalProgress = 3;
            LoadTies(ref progress);
            status = "Loading zones...";
            totalProgress = 4;
            LoadZones(ref progress);
            totalProgress = 5;
        }

        public void LoadMobys(ref Vector2 progress)
        {
            if (fm.isOld) LoadMobysOld(ref progress);
            else LoadMobysNew(ref progress);
        }

        private void LoadMobysOld(ref Vector2 progress)
        {
            IGFile main = fm.igfiles["main.dat"];
            IGFile.SectionHeader mobySection = main.QuerySection(0xD100);
            progress.X = 0;
            progress.Y = mobySection.count;

            for (int i = 0; i < mobySection.count; i++)
            {
                var moby = new Moby(main.sh, i);
                mobys.Add((ulong)i, moby);
                disposables.Add(moby);
                progress.X = i + 1;
            }
        }

        private void LoadMobysNew(ref Vector2 progress)
        {
            if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
            {
                Console.WriteLine("Cannot find assetlookup.dat.");
                return;
            }

            IGFile.SectionHeader mobySection = assetlookup.QuerySection(0x1D600);
            assetlookup.sh.Seek(mobySection.offset);
            AssetPointer[] mobyPtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, mobySection.length / 0x10);
            progress.X = 0;
            progress.Y = mobyPtrs.Length;
            Stream mobyStream = fm.rawfiles["mobys.dat"];

            for (int i = 0; i < mobyPtrs.Length; i++)
            {
                byte[] mobydat = new byte[mobyPtrs[i].length];
                mobyStream.Seek(mobyPtrs[i].offset, SeekOrigin.Begin);
                mobyStream.Read(mobydat, 0x00, (int)mobyPtrs[i].length);

                // Cache original data for rebuilding
                if (cacheOriginalData && builder != null)
                    builder.CacheOriginalData(mobyPtrs[i].TUID, mobydat, AssetBuilder.AssetType.Moby);

                MemoryStream mobyms = new MemoryStream(mobydat);
                StreamHelper streamHelper = new StreamHelper(mobyms, StreamHelper.Endianness.Big);

                disposables.Add(mobyms);
                disposables.Add(streamHelper);

                Moby moby = new Moby(streamHelper);
                mobys.Add(mobyPtrs[i].TUID, moby);
                disposables.Add(moby);
                progress.X = i + 1;
            }
        }

        public void LoadTies(ref Vector2 progress)
        {
            if (fm.isOld) LoadTiesOld(ref progress);
            else LoadTiesNew(ref progress);
        }

        private void LoadTiesOld(ref Vector2 progress)
        {
            IGFile main = fm.igfiles["main.dat"];
            IGFile.SectionHeader tieSection = main.QuerySection(0x3400);
            progress.X = 0;
            progress.Y = tieSection.count;

            for (int i = 0; i < tieSection.count; i++)
            {
                var tie = new Tie(main.sh, old: true, index: (uint)i);
                ties.Add(tie.TUID, tie);
                disposables.Add(tie);
                progress.X = i + 1;
            }
        }

        private void LoadTiesNew(ref Vector2 progress)
        {
            if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
            {
                Console.WriteLine("Cannot find assetlookup.dat.");
                return;
            }

            IGFile.SectionHeader tieSection = assetlookup.QuerySection(0x1D300);
            assetlookup.sh.Seek(tieSection.offset);
            AssetPointer[] tiePtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, tieSection.length / 0x10);
            progress.X = 0;
            progress.Y = tiePtrs.Length;
            Stream tieStream = fm.rawfiles["ties.dat"];

            for (int i = 0; i < tiePtrs.Length; i++)
            {
                byte[] tiedat = new byte[tiePtrs[i].length];
                tieStream.Seek(tiePtrs[i].offset, SeekOrigin.Begin);
                tieStream.Read(tiedat, 0x00, (int)tiePtrs[i].length);

                // Cache original data
                if (cacheOriginalData && builder != null)
                    builder.CacheOriginalData(tiePtrs[i].TUID, tiedat, AssetBuilder.AssetType.Tie);

                MemoryStream tiems = new MemoryStream(tiedat);
                StreamHelper streamHelper = new StreamHelper(tiems, StreamHelper.Endianness.Big);

                disposables.Add(tiems);
                disposables.Add(streamHelper);

                Tie tie = new Tie(streamHelper, old: false);
                ties.Add(tiePtrs[i].TUID, tie);
                disposables.Add(tie);
                progress.X = i + 1;
            }
        }

        public void LoadShaders(ref Vector2 progress)
        {
            if (fm.isOld) LoadShadersOld(ref progress);
            else LoadShadersNew(ref progress);
        }

        private void LoadShadersOld(ref Vector2 progress)
        {
            IGFile main = fm.igfiles["main.dat"];
            IGFile.SectionHeader shaderSection = main.QuerySection(0x5000);
            progress.X = 0;
            progress.Y = shaderSection.count;

            for (uint i = 0; i < shaderSection.count; i++)
            {
                var shader = new Shader(main.sh, isOld: true, index: i);
                shaders.Add((ulong)i, shader);
                progress.X = i + 1;
            }
        }

        private void LoadShadersNew(ref Vector2 progress)
        {
            if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
            {
                Console.WriteLine("Cannot find assetlookup.dat.");
                return;
            }

            IGFile.SectionHeader shaderSection = assetlookup.QuerySection(0x1D100);
            assetlookup.sh.Seek(shaderSection.offset);
            AssetPointer[] shaderPtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, shaderSection.length / 0x10);
            progress.X = 0;
            progress.Y = shaderPtrs.Length;
            Stream shaderStream = fm.rawfiles["shaders.dat"];

            for (int i = 0; i < shaderPtrs.Length; i++)
            {
                byte[] shaderdat = new byte[shaderPtrs[i].length];
                shaderStream.Seek(shaderPtrs[i].offset, SeekOrigin.Begin);
                shaderStream.Read(shaderdat, 0x00, (int)shaderPtrs[i].length);

                // Cache original data
                if (cacheOriginalData && builder != null)
                    builder.CacheOriginalData(shaderPtrs[i].TUID, shaderdat, AssetBuilder.AssetType.Shader);

                MemoryStream shaderms = new MemoryStream(shaderdat);
                StreamHelper streamHelper = new StreamHelper(shaderms, StreamHelper.Endianness.Big);

                disposables.Add(shaderms);
                disposables.Add(streamHelper);

                Shader shader = new Shader(streamHelper, isOld: false);
                shaders.Add(shaderPtrs[i].TUID, shader);
                progress.X = i + 1;
            }
        }

        public void LoadTextures(ref Vector2 progress)
        {
            if (fm.isOld) LoadTexturesOld(ref progress);
            else LoadTexturesNew(ref progress);
        }

        private void LoadTexturesOld(ref Vector2 progress)
        {
            IGFile main = fm.igfiles["main.dat"];
            IGFile.SectionHeader textureSection = main.QuerySection(0x5200);
            progress.X = 0;
            progress.Y = textureSection.count;

            for (int i = 0; i < textureSection.count; i++)
            {
                main.sh.Seek(textureSection.offset + i * 0x20);
                var texture = new Texture(main.sh, old: true);
                textures.Add(texture.id, texture);
                progress.X = i + 1;
            }
        }

        private void LoadTexturesNew(ref Vector2 progress)
        {
            if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
            {
                Console.WriteLine("Cannot find assetlookup.dat.");
                return;
            }

            IGFile.SectionHeader highmipSection = assetlookup.QuerySection(0x1D1C0);
            assetlookup.sh.Seek(highmipSection.offset);

            progress.X = 0;
            progress.Y = highmipSection.count;

            for (int i = 0; i < highmipSection.count; i++)
            {
                assetlookup.sh.Seek(highmipSection.offset + i * 0x10);
                var texture = new Texture(assetlookup.sh, old: false);
                texture.ReadHighmipsPtr(assetlookup.sh);
                textures.Add(texture.id, texture);
                progress.X = i + 1;
            }
        }

        public void LoadZones(ref Vector2 progress)
        {
            if (fm.isOld) LoadZonesOld(ref progress);
            else LoadZonesNew(ref progress);
        }

        private void LoadZonesOld(ref Vector2 progress)
        {
            IGFile main = fm.igfiles["main.dat"];
            IGFile.SectionHeader zoneSection = main.QuerySection(0x5000);
            progress.X = 0;
            progress.Y = 1; // Old engine has only ONE zone

            var zone = new Zone(main.sh, old: true);
            zones.Add(0, zone);
            disposables.Add(zone);
            progress.X = 1;
        }

        private void LoadZonesNew(ref Vector2 progress)
        {
            if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
            {
                Console.WriteLine("Cannot find assetlookup.dat.");
                return;
            }

            IGFile.SectionHeader zoneSection = assetlookup.QuerySection(0x1DA00);
            assetlookup.sh.Seek(zoneSection.offset);
            AssetPointer[] zonePtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, zoneSection.length / 0x10);
            progress.X = 0;
            progress.Y = zonePtrs.Length;
            Stream zoneStream = fm.rawfiles["zones.dat"];

            for (int i = 0; i < zonePtrs.Length; i++)
            {
                byte[] zonedat = new byte[zonePtrs[i].length];
                zoneStream.Seek(zonePtrs[i].offset, SeekOrigin.Begin);
                zoneStream.Read(zonedat, 0x00, (int)zonePtrs[i].length);

                // Cache original data
                if (cacheOriginalData && builder != null)
                    builder.CacheOriginalData(zonePtrs[i].TUID, zonedat, AssetBuilder.AssetType.Zone);

                MemoryStream zonems = new MemoryStream(zonedat);
                StreamHelper streamHelper = new StreamHelper(zonems, StreamHelper.Endianness.Big);

                disposables.Add(zonems);
                disposables.Add(streamHelper);

                Zone zone = new Zone(streamHelper, old: false);
                zones.Add(zonePtrs[i].TUID, zone);
                disposables.Add(zone);
                progress.X = i + 1;
            }
        }

        #endregion

        #region Rebuilding Methods

        public void RebuildMobysFile(string outputPath)
        {
            if (builder == null)
                throw new InvalidOperationException("Rebuilding not enabled. Create loader with enableRebuilding=true");
            if (fm.isOld)
                throw new NotSupportedException("Rebuilding only supported for new engine");

            builder.RebuildMobysFile(mobys, outputPath);
        }

        public void RebuildShadersFile(string outputPath)
        {
            if (builder == null)
                throw new InvalidOperationException("Rebuilding not enabled");
            if (fm.isOld)
                throw new NotSupportedException("Rebuilding only supported for new engine");

            builder.RebuildShadersFile(shaders, outputPath);
        }

        public void RebuildTiesFile(string outputPath)
        {
            if (builder == null)
                throw new InvalidOperationException("Rebuilding not enabled");
            if (fm.isOld)
                throw new NotSupportedException("Rebuilding only supported for new engine");

            builder.RebuildTiesFile(ties, outputPath);
        }

        public void RebuildZonesFile(string outputPath)
        {
            if (builder == null)
                throw new InvalidOperationException("Rebuilding not enabled");
            if (fm.isOld)
                throw new NotSupportedException("Rebuilding only supported for new engine");

            builder.RebuildZonesFile(zones, outputPath);
        }

        #endregion

        public void Dispose()
        {
            // Dispose all tracked resources
            foreach (var disposable in disposables)
            {
                disposable?.Dispose();
            }
            disposables.Clear();

            mobys.Clear();
            ties.Clear();
            shaders.Clear();
            textures.Clear();
            zones.Clear();

            builder?.Dispose();
        }
    }
}
