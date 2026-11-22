using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

internal static class FallOfManLightmaps
{
    private const uint DescriptorSection = 0x5500;
    internal const uint ResidentPixelsSection = 0x9950;

    public static IReadOnlyList<ITexture?> Read(IGFile main, Stream localPixels)
    {
        var descriptors = main.QuerySection(DescriptorSection);
        if (descriptors.id != DescriptorSection || descriptors.length != 0x20) return [];

        var resident = main.QuerySection(ResidentPixelsSection);
        var maps = new ITexture?[descriptors.count];
        int failed = 0;
        for (uint i = 0; i < descriptors.count; i++)
        {
            var descriptor = NewEngineZoneLightingReader.ReadDescriptor(main.sh, descriptors.offset + i * descriptors.length);
            string name = $"Fall of Man lightmap {i}";
            ulong id = 0x5500_0000_0000_0000UL | i;
            try
            {
                // NV4097 location 2 addresses 0x9950, shared with the level's cubemaps.
                // Location 1 addresses the raw ps3leveltexs.dat stream.
                maps[i] = (descriptor.Flags & 3) switch
                {
                    1 => NewEngineZoneLightingReader.DecodeTexture(localPixels, descriptor, 0, localPixels.Length, id, name),
                    2 when resident.id == ResidentPixelsSection =>
                        NewEngineZoneLightingReader.DecodeTexture(main.sh.BaseStream, descriptor, resident.offset,
                            resident.offset + (long)resident.length, id, name),
                    _ => null,
                };
            }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or OverflowException)
            {
                if (failed == 0) Console.WriteLine($"{name}: {e.Message}");
            }
            if (maps[i] == null) failed++;
        }
        Console.WriteLine($"Fall of Man: {maps.Length - failed}/{maps.Length} baked lightmaps decoded.");
        return maps;
    }
}
