using System.Numerics;

namespace LibLunacy
{
	public class Gameplay
	{
		IGFile file;
		public Region[] regions;

		public Gameplay(AssetLoader al)
		{
			Console.WriteLine("Reading Gameplay.dat.");
			file = al.fm.igfiles["gameplay.dat"];

			if(al.fm.isOld)
			{
				regions = new Region[1];
				regions[0] = new Region(file, al);
			}
			else
			{
				//Loading regions

				IGFile.SectionHeader stringTableSection = file.QuerySection(0x25000);

				//gameplay.dat is a weird file in this version of the engine, the count field of section headers is the length and length field of section headers is 0

				file.sh.Seek(stringTableSection.offset + stringTableSection.count - 0x10);
				regions = new Region[file.sh.ReadUInt32()];

				uint regionTableOffset = file.sh.ReadUInt32();

				for(int i = 0; i < regions.Length; i++)
				{
					file.sh.Seek(regionTableOffset + 0x04 * i);
					
					string regionName = file.sh.ReadString(file.sh.ReadUInt32());
					Console.WriteLine($"Region {i}: {regionName}");
					regions[i] = new Region(al, regionName);
				}
			}
		}
	}

	public class Region
	{
		public string name = "default";

		public Dictionary<ulong, CMobyInstance> mobyInstances = new Dictionary<ulong, CMobyInstance>();
		public Dictionary<ulong, CVolumeInstance> volumeInstances = new Dictionary<ulong, CVolumeInstance>();
		public CZone[] zones;

		public class CVolumeInstance
		{
			public Vector3 position;
			public Quaternion rotation;
			public Vector3 scale;
			public string name;
			public ulong id;
			public CVolumeInstance(NewVolumeInstance nvolume, NewInstance ni)
			{
				Matrix4x4.Decompose(nvolume.transform, out scale, out rotation, out position);
				name = ni.name;
				id = ni.tuid;
			}
		}
		public class CMobyInstance
		{
			public Vector3 position;
			public Vector3 rotation;
			public float scale;
			public CMoby moby;
			public string name;

			public CMobyInstance(OldMobyInstance omi, AssetLoader al)
			{
				position = omi.position;
				rotation = omi.rotation;
				scale = omi.scale;
				moby = al.mobys[omi.mobyIndex];
			}
			public CMobyInstance(NewMobyInstance nmi, NewInstance ni, AssetLoader al, IGFile region)
			{
				position = nmi.position;
				rotation = nmi.rotation;
				scale = nmi.scale;

				region.sh.Seek(region.QuerySection(0x1C600).offset + 0x08 * nmi.mobyIndex);

				moby = al.mobys[region.sh.ReadUInt64()];
			}

		}


		[FileStructure(0x48)]
		public struct OldMobyInstance
		{
			[FileOffset(0x18)] public Vector3 position;
			[FileOffset(0x24)] public Vector3 rotation;		//ZYX euler in radians
			[FileOffset(0x30)] public float scale;
			[FileOffset(0x3C)] public ushort mobyIndex;
		}

		[FileStructure(0x50)]
		public struct NewMobyInstance
		{
			[FileOffset(0x00)] public ushort mobyIndex;
			[FileOffset(0x02)] public ushort groupIndex;
			[FileOffset(0x14)] public Vector3 position;
			[FileOffset(0x20)] public Vector3 rotation;		//ZYX euler in radians
			[FileOffset(0x2C)] public float scale;
		}
		[FileStructure(0x40)]
		public struct NewVolumeInstance
		{
			[FileOffset(0x00)] public Matrix4x4 transform;
		}
		[FileStructure(0x10)]
		public struct NewInstance
		{
			[FileOffset(0x00)] public ulong tuid;
			[FileOffset(0x08), Reference] public string name;
			[FileOffset(0x0C)] public ushort group;
		}

		public Region(IGFile file, AssetLoader al)
		{
			IGFile.SectionHeader mobyInstSections = file.QuerySection(0x7340);
			file.sh.Seek(mobyInstSections.offset);
			OldMobyInstance[] mobys = FileUtils.ReadStructureArray<OldMobyInstance>(file.sh, mobyInstSections.count);

			zones = new CZone[1];
			zones[0] = new CZone(al.fm.igfiles["main.dat"], al);
			zones[0].name = "art";

			DebugFile.DebugInstanceName[] names = null;
			if(al.fm.debug != null)
			{
				names = al.fm.debug.GetMobyInstanceNames();
			}

			for(int i = 0; i < mobys.Length; i++)
			{
				mobyInstances.Add((ulong)i, new CMobyInstance(mobys[i], al));
				if(names != null)
				{
					mobyInstances.Last().Value.name = names[i].name;
				}
				else
				{
					mobyInstances.Last().Value.name = $"Moby_{mobys[i].mobyIndex.ToString("X04")}_Instance_{i}";
				}
			}
		}
		public Region(AssetLoader al, string regionName)
		{
			name = regionName;
			IGFile prius = (IGFile)al.fm.LoadFile($"{name}/gp_prius.dat", false);
			IGFile region = (IGFile)al.fm.LoadFile($"{name}/region.dat", false);

			IGFile.SectionHeader mobyInstSection = prius.QuerySection(0x25048);
			prius.sh.Seek(mobyInstSection.offset);
			NewMobyInstance[] mobys = FileUtils.ReadStructureArray<NewMobyInstance>(prius.sh, mobyInstSection.count);

			IGFile.SectionHeader mobyNamesSection = prius.QuerySection(0x2504C);
			prius.sh.Seek(mobyNamesSection.offset);
			NewInstance[] mobyNames = FileUtils.ReadStructureArray<NewInstance>(prius.sh, mobyInstSection.count);

			for(int i = 0; i < mobys.Length; i++)
			{
				mobyInstances.Add(mobyNames[i].tuid, new CMobyInstance(mobys[i], mobyNames[i], al, region));
				mobyInstances.Last().Value.name = mobyNames[i].name;
			}

			IGFile.SectionHeader volumeInstSection = prius.QuerySection(0x2505C);
			prius.sh.Seek(volumeInstSection.offset);
			NewVolumeInstance[] volumes = FileUtils.ReadStructureArray<NewVolumeInstance>(prius.sh, volumeInstSection.count);

			IGFile.SectionHeader volumeNamesSection = prius.QuerySection(0x25060);
			prius.sh.Seek(volumeNamesSection.offset);
			NewInstance[] volumeNames = FileUtils.ReadStructureArray<NewInstance>(prius.sh, volumeInstSection.count);

			for(int i = 0; i < volumes.Length; i++)
			{
				//volumeInstances.Add(volumeNames[i].tuid, new CVolumeInstance(volumes[i], volumeNames[i]));
			}

			IGFile.SectionHeader zoneNames = region.QuerySection(0x1C000);
			IGFile.SectionHeader zoneRefs = region.QuerySection(0x1C010);

			zones = new CZone[zoneRefs.count];

			for(int i = 0; i < zoneRefs.count; i++)
			{
				region.sh.Seek(zoneRefs.offset + i * 8);
				zones[i] = al.zones[region.sh.ReadUInt64()];
				region.sh.Seek(zoneNames.offset + i * 4);
				zones[i].name = region.sh.ReadString(region.sh.ReadUInt32());
			}
		}
	}
}