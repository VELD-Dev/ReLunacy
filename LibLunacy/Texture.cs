namespace LibLunacy
{
	public class CTexture
	{
		[FileStructure(0x20)]
		public struct OldTextureReference
		{
			[FileOffset(0x00)] public uint offset;				//offsets into textures.dat
			[FileOffset(0x04)] public ushort mipmapCount;

			//Bits (0 based, from left to right):
			//  shift  |  bits  |  desc
			// --------|--------|------------------------------------------------
			//    2    |    1   |  if 1 then unswizzled, ignored on DXT formats
			//    4    |    4   |  format, see TexFormat enum
			[FileOffset(0x06)] public ushort formatBitField;
			[FileOffset(0x18)] public ushort width;
			[FileOffset(0x1A)] public ushort height;
		}
		[FileStructure(0x10)]
		public struct OldTexstreamReference
		{
			[FileOffset(0x00)] public uint offset;				//offsets into textures.dat
			[FileOffset(0x06)] public ushort index;
		}
		[FileStructure(0x04)]
		public struct NewTexMeta
		{
			[FileOffset(0x00)] public byte format;
			[FileOffset(0x01)] public byte mipmapCount;
			[FileOffset(0x02)] public byte widthPow;
			[FileOffset(0x03)] public byte heightPow;
		}

		public byte[] data;
		public TexFormat format;
		public int width;
		public int height;
		public int mipmapCount;
		public uint id;
		public string name;

		public enum TexFormat
		{
			R5G6B5 = 0x03,
			A8R8G8B8 = 0x05,
			DXT1 = 0x06,
			DXT3 = 0x07,
			DXT5 = 0x08,
		}

		public uint HighmipSize
		{
			get
			{
				switch(format)
				{
					case TexFormat.DXT1:
						return (uint)(Math.Max(1, (width+3)/4) * Math.Max(1, (height+3)/4)) * 8;
					case TexFormat.DXT3:
					case TexFormat.DXT5:
						return (uint)(Math.Max(1, (width+3)/4) * Math.Max(1, (height+3)/4)) * 16;
					case TexFormat.A8R8G8B8:
						return (uint)(width * height * 4u);
					case TexFormat.R5G6B5:
						return (uint)(width * height * 2u);
					default:
						return 0;
				}
			}
		}

		public CTexture(FileManager fm, int index)
		{
			if(fm.isOld)
			{
				IGFile main = fm.igfiles["main.dat"];
				Stream textures = fm.rawfiles["textures.dat"];
				Stream? texstream = fm.rawfiles["texstream.dat"];

				IGFile.SectionHeader texrefs = main.QuerySection(0x5200);
				IGFile.SectionHeader texstrrefs = main.QuerySection(0x9800);

				main.sh.Seek(texrefs.offset + index * 0x20);
				OldTextureReference otr = FileUtils.ReadStructure<OldTextureReference>(main.sh);
				main.sh.Seek(texstrrefs.offset);
				OldTexstreamReference[] ots = FileUtils.ReadStructureArray<OldTexstreamReference>(main.sh, texstrrefs.count);

				width = otr.width;
				height = otr.height;
				mipmapCount = otr.mipmapCount;
				format = (TexFormat)((otr.formatBitField >> 8) & 0xF);

				if(texstream != null && ots.Any(x => x.index == index))
				{
					width *= 2;
					height *= 2;
					mipmapCount += 1;
				}

				data = new byte[HighmipSize];

				if(HighmipSize == 0) return;

				if(texstream != null && ots.Any(x => x.index == index))
				{
					texstream.Seek(ots.First(x => x.index == index).offset, SeekOrigin.Begin);
					if((format == TexFormat.DXT1 || format == TexFormat.DXT3 || format == TexFormat.DXT5) /*&& (otr.formatBitField & 0x2000) == 0*/)
					{
						texstream.Read(data);
					}
					else
					{
						//width = 0;
						//height = 0;
						Console.WriteLine($"Unswizzling {((uint)(texrefs.offset + index * 0x20)).ToString("X08")}");
						Unswizzle(texstream, ref data, width, height, format);
					}
				}
				else
				{
					textures.Seek(otr.offset, SeekOrigin.Begin);
					if((format == TexFormat.DXT1 || format == TexFormat.DXT3 || format == TexFormat.DXT5) /*&& (otr.formatBitField & 0x2000) == 0*/)
					{
						textures.Read(data);
					}
					else
					{
						//width = 0;
						//height = 0;
						Console.WriteLine($"Unswizzling {((uint)(texrefs.offset + index * 0x20)).ToString("X08")}");
						Unswizzle(textures, ref data, width, height, format);
					}
				}

				id = (uint)(texrefs.offset + index * 0x20);
				name = $"Texture_{index}";
			}
			else
			{
				IGFile assetlookup = fm.igfiles["assetlookup.dat"];
				Stream textures = fm.rawfiles["textures.dat"];
				Stream highmips = fm.rawfiles["highmips.dat"];
				IGFile.SectionHeader highmipPtrs = assetlookup.QuerySection(0x1D1C0);
				IGFile.SectionHeader textureMetas = assetlookup.QuerySection(0x1D140);

				assetlookup.sh.Seek(highmipPtrs.offset + index * 0x10);
				AssetLoader.AssetPointer hmipPtr = FileUtils.ReadStructure<AssetLoader.AssetPointer>(assetlookup.sh);
				assetlookup.sh.Seek(textureMetas.offset + index * 0x04);
				NewTexMeta meta = FileUtils.ReadStructure<NewTexMeta>(assetlookup.sh);

				width = 1 << meta.widthPow;
				height = 1 << meta.heightPow;
				mipmapCount = 1;//meta.mipmapCount;
				if(meta.format == 6 || meta.format == 7 || meta.format == 8 || meta.format == 5 || meta.format == 0x0B)
				{
					format = (TexFormat)meta.format;
				}
				else
				{
					Console.Error.WriteLine($"WARNING: TEXTURE {hmipPtr.tuid.ToString("X016")} HAS UNKNOWN FORMAT {meta.format.ToString("X02")}, SKIPPING...");
					goto errorcleanup;
				}

				if(hmipPtr.length == 0)
				{
					Console.Error.WriteLine($"WARNING: HMIP {hmipPtr.tuid.ToString("X016")} HAS SIZE 0, SKIPPING...");
					goto errorcleanup;
				}

				data = new byte[hmipPtr.length];
				highmips.Seek(hmipPtr.offset, SeekOrigin.Begin);

				if(format == TexFormat.DXT1 || format == TexFormat.DXT3 || format == TexFormat.DXT5)
				{
					highmips.Read(data);
				}
				else if (format == TexFormat.A8R8G8B8 || format == TexFormat.R5G6B5)
				{
					Console.WriteLine($"Unswizzling {hmipPtr.tuid.ToString("X016")}");
					Unswizzle(highmips, ref data, width, height, format);
				}

				goto finish;

				errorcleanup:
				width = 0;
				height = 0;

				finish:
				id = (uint)hmipPtr.tuid;
			}
		}

		private void Unswizzle(Stream s, ref byte[] b, int width, int height, TexFormat format)
		{
			if(format == TexFormat.DXT1 || format == TexFormat.DXT3 || format == TexFormat.DXT5) throw new InvalidOperationException("DXT textures aren't swizzled");
			if(b.Length == 0) throw new ArgumentException("Array too small");
			//if(b.Length == 0) return;

			long ogPos = s.Position;

			int pixelSize = 0;
			     if(format == TexFormat.R5G6B5)   pixelSize = 2;
			else if(format == TexFormat.A8R8G8B8) pixelSize = 4;

			byte[] pixel = new byte[pixelSize];

			for(int t = 0; t < width * height; t++)
			{
				int index = Morton(t, width, height);
				s.Read(pixel);
				if(format == TexFormat.A8R8G8B8)
				{
					Array.Reverse(pixel, 1, 3);	// ABGR -> ARGB
					Array.Reverse(pixel, 0, 4); // ARGB -> RGBA
				}
				Array.Copy(pixel, 0, b, index * pixelSize, pixelSize);
			}
		}

		//Stolen from RawTex
		private int Morton(int t, int x, int y)
		{
			int num2;
			int num = num2 = 1;
			int num3 = t;
			int num4 = x;
			int num5 = y;
			int num6 = 0;
			int num7 = 0;
			while (num4 > 1 || num5 > 1)
			{
				if (num4 > 1)
				{
					num6 += num2 * (num3 & 1);
					num3 >>= 1;
					num2 *= 2;
					num4 >>= 1;
				}
				if (num5 > 1)
				{
					num7 += num * (num3 & 1);
					num3 >>= 1;
					num *= 2;
					num5 >>= 1;
				}
			}
			return num7 * x + num6;
		}

		private static readonly byte[] ddsHeader = new byte[0x80]
		{
			0x44, 0x44, 0x53, 0x20,		// "DDS "
			0x7C, 0x00, 0x00, 0x00,		// Version Info
			0x07, 0x10, 0x0A, 0x00,		// More Version Info
			0x00, 0x00, 0x00, 0x00,		// Height
			0x00, 0x00, 0x00, 0x00,		// Width
			0x00, 0x00, 0x00, 0x00,		// Size
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// Mipmaps 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x20, 0x00, 0x00, 0x00,		// 
			0x04, 0x00, 0x00, 0x00,		// 
			0x44, 0x58, 0x54, 0x30,		// "DXT0"
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x08, 0x10, 0x40, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00,		// 
			0x00, 0x00, 0x00, 0x00 		// 
		};
		public void ExportToDDS(Stream dst, bool leaveOpen = true)
		{
			dst.Write(ddsHeader, 0x00, 0x80);
			dst.Seek(0x0C, SeekOrigin.Begin);
			dst.Write(BitConverter.GetBytes((uint)height), 0x00, 0x04);
			dst.Write(BitConverter.GetBytes((uint)width), 0x00, 0x04);
			dst.Write(BitConverter.GetBytes(HighmipSize), 0x00, 0x04);
			dst.Seek(0x1C, SeekOrigin.Begin);
			dst.Write(BitConverter.GetBytes((uint)1), 0x00, 0x04);
			dst.Seek(0x57, SeekOrigin.Begin);
			switch(format)
			{
				case TexFormat.DXT1:
					dst.Write(BitConverter.GetBytes((byte)0x31), 0x00, 0x01);
					break;
				case TexFormat.DXT3:
					dst.Write(BitConverter.GetBytes((byte)0x33), 0x00, 0x01);
					break;
				case TexFormat.DXT5:
					dst.Write(BitConverter.GetBytes((byte)0x35), 0x00, 0x01);
					break;
				case TexFormat.A8R8G8B8:
					dst.Seek(0x4C, SeekOrigin.Begin);
					dst.Write(new byte[]
					{
						0x20, 0x00, 0x00, 0x00,
						0x41, 0x00, 0x00, 0x00,
						0x00, 0x00, 0x00, 0x00,
						0x20, 0x00, 0x00, 0x00,
						0x00, 0x00, 0x00, 0xFF,
						0x00, 0x00, 0xFF, 0x00,
						0x00, 0xFF, 0x00, 0x00,
						0xFF, 0x00, 0x00, 0x00
					}, 0x00, 0x20);
					break;
				default:
					Console.WriteLine($"WARNING: TEXTURE {id.ToString("X08")} aka {name} has unsupported texture format {format.ToString()}");
					break;
			}
			dst.Seek(0x80, SeekOrigin.Begin);
			dst.Write(data, 0x00, (int)HighmipSize);
			dst.Flush();
			if(!leaveOpen)
			{
				dst.Close();
			}
		}
	}
}