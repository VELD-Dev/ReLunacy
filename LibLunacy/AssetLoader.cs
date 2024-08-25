using System.Numerics;

namespace LibLunacy
{
	//Assets are managed here and whenever an asset needs to reference another, that is done here
	public class AssetLoader
	{
		public FileManager fm;

		public Dictionary<ulong, CMoby> mobys = new Dictionary<ulong, CMoby>();
		public Dictionary<ulong, CTie> ties = new Dictionary<ulong, CTie>();
		public Dictionary<ulong, CShader> shaders = new Dictionary<ulong, CShader>();
		public Dictionary<uint, CTexture> textures = new Dictionary<uint, CTexture>();
		public Dictionary<ulong, CZone> zones = new Dictionary<ulong, CZone>();
		public Dictionary<ulong, Dictionary<ulong, CZone.UFrag>> ufrags = new();

		public List<CShader> shaderDB = new List<CShader>();
		//Copy of shaders except it's only used on old engine, should probably find a better way to do this

		public AssetLoader(FileManager fileManager)
		{
			fm = fileManager;
		}

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
			status = "Loading Zones...";
			totalProgress = 4;
			LoadZones(ref progress);
			totalProgress = 5;
		}
		public void LoadMobys(ref Vector2 progress)
		{
			if(fm.isOld) LoadMobysOld(ref progress);
			else         LoadMobysNew(ref progress);
		}
		private void LoadMobysOld(ref Vector2 progress)
		{
			IGFile main = fm.igfiles["main.dat"];
			IGFile.SectionHeader mobySection = main.QuerySection(0xD100);
			progress.X = 0;
			progress.Y = mobySection.count;
			for(int i = 0; i < mobySection.count; i++)
			{
				mobys.Add((ulong)i, new CMoby(main, this, (uint)i));
				progress.X = i + 1;
			}
		}
		private void LoadMobysNew(ref Vector2 progress)
		{
			if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup))
			{
				Console.WriteLine("Cannot find assetlookup.dat.");
				return;
			}
			if (assetlookup is null)
			{
				Console.WriteLine("Assetlookup is null.");
				return;
			}
			IGFile.SectionHeader mobySection = assetlookup.QuerySection(0x1D600);
			assetlookup.sh.Seek(mobySection.offset);
			AssetPointer[] mobyPtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, mobySection.length / 0x10);
			progress.X = 0;
			progress.Y = mobyPtrs.Length;
			Stream mobyStream = fm.rawfiles["mobys.dat"];
			for(int i = 0; i < mobyPtrs.Length; i++)
			{
				byte[] mobydat = new byte[mobyPtrs[i].length];
				mobyStream.Seek(mobyPtrs[i].offset, SeekOrigin.Begin);
				mobyStream.Read(mobydat, 0x00, (int)mobyPtrs[i].length);
				MemoryStream mobyms = new MemoryStream(mobydat);

				IGFile igmoby = new IGFile(mobyms);
				CMoby moby = new CMoby(igmoby, this);
				Console.WriteLine($"Moby {i.ToString("X04")} is {moby.name}");
				mobys.Add(mobyPtrs[i].tuid, moby);
				progress.X = i + 1;
			}
		}

		public void LoadTies(ref Vector2 progress)
		{
			if(fm.isOld) LoadTiesOld(ref progress);
			else         LoadTiesNew(ref progress);
		}
		private void LoadTiesOld(ref Vector2 progress)
		{
			IGFile main = fm.igfiles["main.dat"];
			IGFile.SectionHeader tieSection = main.QuerySection(0x3400);
			progress.X = 0;
			progress.Y = tieSection.count;
			for(int i = 0; i < tieSection.count; i++)
			{
				CTie tie = new CTie(main, this, (uint)i);
				ties.Add(tie.id, tie);
				progress.X = i + 1;
			}
		}
		private void LoadTiesNew(ref Vector2 progress)
        {
            if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup))
            {
                Console.WriteLine("Cannot find assetlookup.dat.");
                return;
            }
            if (assetlookup is null)
            {
                Console.WriteLine("Assetlookup is null.");
                return;
            }
            IGFile.SectionHeader tieSection = assetlookup.QuerySection(0x1D300);
			assetlookup.sh.Seek(tieSection.offset);
			AssetPointer[] tiePtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, tieSection.length / 0x10);
			progress.X = 0;
			progress.Y = tiePtrs.Length;
			Stream tieStream = fm.rawfiles["ties.dat"];
			for(int i = 0; i < tiePtrs.Length; i++)
			{
				byte[] tiedat = new byte[tiePtrs[i].length];
				tieStream.Seek(tiePtrs[i].offset, SeekOrigin.Begin);
				tieStream.Read(tiedat, 0x00, (int)tiePtrs[i].length);
				MemoryStream tiems = new MemoryStream(tiedat);

				IGFile igtie = new IGFile(tiems);
				CTie tie = new CTie(igtie, this);
				Console.WriteLine($"tie {i.ToString("X04")} is {tie.name}");
				ties.Add(tiePtrs[i].tuid, tie);
				progress.X = i + 1;
			}
		}

		public void LoadShaders(ref Vector2 progress)
		{
			if(fm.isOld) LoadShadersOld(ref progress);
			else         LoadShadersNew(ref progress);
		}

		private void LoadShadersOld(ref Vector2 progress)
		{
			IGFile main = fm.igfiles["main.dat"];
			IGFile.SectionHeader shaderSection = main.QuerySection(0x5000);
			progress.X = 0;
			progress.Y = shaderSection.count;
			for(int i = 0; i < shaderSection.count; i++)
			{
				shaderDB.Add(new CShader(main, this, (uint)i));
				shaders.Add((ulong)i, shaderDB[i]);
				progress.X = i + 1;
			}
		}
		private void LoadShadersNew(ref Vector2 progress)
        {
            if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup))
            {
                Console.WriteLine("Cannot find assetlookup.dat.");
                return;
            }
            if (assetlookup is null)
            {
                Console.WriteLine("Assetlookup is null.");
                return;
            }
            IGFile.SectionHeader shaderSection = assetlookup.QuerySection(0x1D100);
			assetlookup.sh.Seek(shaderSection.offset);
			AssetPointer[] shaderPtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, shaderSection.length / 0x10);
			progress.X = 0;
			progress.Y = shaderPtrs.Length;
			Stream shaderStream = fm.rawfiles["shaders.dat"];
			for(int i = 0; i < shaderPtrs.Length; i++)
			{
				byte[] shaderdat = new byte[shaderPtrs[i].length];
				shaderStream.Seek(shaderPtrs[i].offset, SeekOrigin.Begin);
				shaderStream.Read(shaderdat, 0x00, (int)shaderPtrs[i].length);
				MemoryStream shaderms = new MemoryStream(shaderdat);
				IGFile igshader = new IGFile(shaderms);
				CShader shader = new CShader(igshader, this);
				shaders.Add(shaderPtrs[i].tuid, shader);
				progress.X = i + 1;
			}
		}

		public void LoadTextures(ref Vector2 progress)
		{
			if(fm.isOld) LoadTexturesOld(ref progress);
			else         LoadTexturesNew(ref progress);
		}

		private void LoadTexturesOld(ref Vector2 progress)
		{
			IGFile main = fm.igfiles["main.dat"];
			IGFile.SectionHeader textureSection = main.QuerySection(0x5200);
			progress.X = 0;
			progress.Y = textureSection.count;
			for(int i = 0; i < textureSection.count; i++)
			{
				textures.Add((uint)(textureSection.offset + i * 0x20), new CTexture(fm, i));
				progress.X = i + 1;
			}
		}
		private void LoadTexturesNew(ref Vector2 progress)
        {
            if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup))
            {
                Console.WriteLine("Cannot find assetlookup.dat.");
                return;
            }
            if (assetlookup is null)
            {
                Console.WriteLine("Assetlookup is null.");
                return;
            }
            IGFile.SectionHeader highmipSection = assetlookup.QuerySection(0x1D1C0);
			assetlookup.sh.Seek(highmipSection.offset);
			AssetPointer[] highmips = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, highmipSection.length / 0x10);

			progress.X = 0;
			progress.Y = highmips.Length;

			for(int i = 0; i < highmips.Length; i++)
			{
				textures.Add((uint)highmips[i].tuid, new CTexture(fm, i));
				progress.X = i + 1;
			}
		}

		public void LoadZones(ref Vector2 progress)
		{
			if(fm.isOld) LoadZonesOld(ref progress);
			else         LoadZonesNew(ref progress);
		}

		private void LoadZonesOld(ref Vector2 progress)
		{
			IGFile main = fm.igfiles["main.dat"];
			IGFile.SectionHeader zoneSection = main.QuerySection(0x5000);
			progress.X = 0;
			progress.Y = zoneSection.count;
			Console.WriteLine($"{zoneSection.count} zones detected (0x{zoneSection.length:X} bytes long)");
			for (int i = 0; i < zoneSection.count; i++)
			{
				CZone zone = new(main, this, i);

                Console.WriteLine("[0x{0:X}] Zone {1} ({2}/{3}) has {4} ufrags", "unk", zone.name, i+1, zoneSection.count, zone.ufrags.Length);
				zones.Add((ulong)i, zone);

				ufrags.Add((ulong)zone.index, new());
				var locUfrags = ufrags[(ulong)zone.index];

				if (zone.ufrags is null) continue;
                for (int j = 0; j < zone.ufrags.Length; j++)
                {
                    locUfrags.TryAdd(zone.ufrags[j].GetTuid(), zone.ufrags[j]);
                }
				progress.X = i + 1;
            }
		}
		private void LoadZonesNew(ref Vector2 progress)
		{			
			if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup))
			{
				Console.WriteLine("Cannot find assetlookup.dat.");
				return;
			}
			if (assetlookup is null)
			{
				Console.WriteLine("Assetlookup is null.");
				return;
			}
			IGFile.SectionHeader zoneSection = assetlookup.QuerySection(0x1DA00);
			assetlookup.sh.Seek(zoneSection.offset);
			AssetPointer[] zonePtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, zoneSection.length / 0x10);
            progress.X = 0;
            progress.Y = zonePtrs.Length;
            Stream zoneStream = fm.rawfiles["zones.dat"];
			for(int i = 0; i < zonePtrs.Length; i++)
			{
				byte[] zonedat = new byte[zonePtrs[i].length];
				zoneStream.Seek(zonePtrs[i].offset, SeekOrigin.Begin);
				zoneStream.Read(zonedat, 0x00, (int)zonePtrs[i].length);
				MemoryStream zonems = new MemoryStream(zonedat);
				IGFile igzone = new(zonems);
				CZone zone = new(igzone, this, i);
				Console.WriteLine($"[0x{zonePtrs[i].offset:X}] Zone {zone.index} {zone.name} (0x{zonePtrs[i].tuid:X}) has {zone.ufrags.Length} ufrags and {zone.tieInstances.Count} ties.");
				zones.Add(zonePtrs[i].tuid, zone);
				
				var localUfrags = new Dictionary<ulong, CZone.UFrag>();
				for(int j = 0; j < zone.ufrags.Length; j++)
				{
					localUfrags.TryAdd(zone.ufrags[j].GetTuid(), zone.ufrags[j]);
				}
				ufrags.Add((ulong)zone.index, localUfrags);
				progress.X = i + 1;
            }
		}

		public void Dispose()
		{
			if(!fm.isOld)
			{
				foreach(KeyValuePair<ulong, CMoby> moby in mobys)
				{
					moby.Value.Dispose();
				}
			}
		}

		[FileStructure(0x10)]
		public struct AssetPointer
		{
			[FileOffset(0x00)] public ulong tuid;
			[FileOffset(0x08)] public uint offset;
			[FileOffset(0x0C)] public uint length;
		}
	}
}
