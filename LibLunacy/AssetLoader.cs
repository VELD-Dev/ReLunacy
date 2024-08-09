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

		public void LoadAssets()
		{
			LoadTextures();
			LoadShaders();
			LoadMobys();
			LoadTies();
			LoadZones();
		}
		public void LoadMobys()
		{
			if(fm.isOld) LoadMobysOld();
			else         LoadMobysNew();
		}
		private void LoadMobysOld()
		{
			IGFile main = fm.igfiles["main.dat"];
			IGFile.SectionHeader mobySection = main.QuerySection(0xD100);
			for(int i = 0; i < mobySection.count; i++)
			{
				mobys.Add((ulong)i, new CMoby(main, this, (uint)i));
			}
		}
		private void LoadMobysNew()
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
			}
		}

		public void LoadTies()
		{
			if(fm.isOld) LoadTiesOld();
			else         LoadTiesNew();
		}
		private void LoadTiesOld()
		{
			IGFile main = fm.igfiles["main.dat"];
			IGFile.SectionHeader tieSection = main.QuerySection(0x3400);
			for(int i = 0; i < tieSection.count; i++)
			{
				CTie tie = new CTie(main, this, (uint)i);
				ties.Add(tie.id, tie);
			}
		}
		private void LoadTiesNew()
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
			}
		}

		public void LoadShaders()
		{
			if(fm.isOld) LoadShadersOld();
			else         LoadShadersNew();
		}

		private void LoadShadersOld()
		{
			IGFile main = fm.igfiles["main.dat"];
			IGFile.SectionHeader shaderSection = main.QuerySection(0x5000);
			for(int i = 0; i < shaderSection.count; i++)
			{
				shaderDB.Add(new CShader(main, this, (uint)i));
				shaders.Add((ulong)i, shaderDB[i]);
			}
		}
		private void LoadShadersNew()
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
			}
		}

		public void LoadTextures()
		{
			if(fm.isOld) LoadTexturesOld();
			else         LoadTexturesNew();
		}

		private void LoadTexturesOld()
		{
			IGFile main = fm.igfiles["main.dat"];
			IGFile.SectionHeader textureSection = main.QuerySection(0x5200);
			for(int i = 0; i < textureSection.count; i++)
			{
				textures.Add((uint)(textureSection.offset + i * 0x20), new CTexture(fm, i));
			}
		}
		private void LoadTexturesNew()
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

			for(int i = 0; i < highmips.Length; i++)
			{
				textures.Add((uint)highmips[i].tuid, new CTexture(fm, i));
			}
		}

		public void LoadZones()
		{
			if(fm.isOld) LoadZonesOld();
			else         LoadZonesNew();
		}

		private void LoadZonesOld()
		{
			IGFile main = fm.igfiles["main.dat"];
			IGFile.SectionHeader zoneSection = main.QuerySection(0x5000);
			for (int i = 0; i < zoneSection.count; i++)
			{
				CZone zone = new(main, this);
				zone.index = i;

				Console.WriteLine("[0x{0:X}] Zone {1} ({2}) has {3} ufrags", "unk", zone.name, i, zone.ufrags?.Length ?? 0);
				zones.Add((ulong)i, zone);

				ufrags.Add((ulong)zone.index, new());
				var locUfrags = ufrags[(ulong)zone.index];
                for (int j = 0; j < zone.ufrags.Length; j++)
                {
                    locUfrags.Add(zone.ufrags[j].GetTuid(), zone.ufrags[j]);
                }
            }
		}
		private void LoadZonesNew()
		{			if (!fm.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup))
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
			Stream zoneStream = fm.rawfiles["zones.dat"];
			for(int i = 0; i < zonePtrs.Length; i++)
			{
				byte[] zonedat = new byte[zonePtrs[i].length];
				zoneStream.Seek(zonePtrs[i].offset, SeekOrigin.Begin);
				zoneStream.Read(zonedat, 0x00, (int)zonePtrs[i].length);
				MemoryStream zonems = new MemoryStream(zonedat);
				IGFile igzone = new(zonems);
				CZone zone = new(igzone, this) { index = i };
				Console.WriteLine($"[0x{zonePtrs[i].offset:X}] Zone {zone.index} {zone.name} (0x{zonePtrs[i].tuid:X}) has {zone.ufrags.Length} ufrags.");
				zones.Add(zonePtrs[i].tuid, zone);
				
				var localUfrags = new Dictionary<ulong, CZone.UFrag>();
				for(int j = 0; j < zone.ufrags.Length; j++)
				{
					localUfrags.TryAdd(zone.ufrags[j].GetTuid(), zone.ufrags[j]);
				}
				ufrags.Add((ulong)zone.index, localUfrags);

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
