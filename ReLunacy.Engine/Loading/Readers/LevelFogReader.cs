using System.Numerics;
using ReLunacy.Engine.Assets.Lighting;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Reads the old-engine level fog configuration from main.dat section 0x7100.</summary>
public sealed class LevelFogReader
{
    private readonly FileManager _fileManager;

    public LevelFogReader(FileManager fileManager)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
    }

    public LevelFog? Read()
    {
        if (!_fileManager.isOld) return null;
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null) return null;

        IGFile.SectionHeader section;
        try
        {
            section = main.QuerySection(LevelFog.SectionId);
        }
        catch
        {
            return null;
        }

        if (section.id != LevelFog.SectionId || section.count == 0 || section.length < 0x58)
            return null;

        if (section.count != 1)
            Console.WriteLine($"Level fog 0x{LevelFog.SectionId:X4}: expected one record, found {section.count}; reading the first.");

        if (section.length != LevelFog.ConfirmedOldStride)
            Console.WriteLine($"Level fog 0x{LevelFog.SectionId:X4}: stride is 0x{section.length:X}, confirmed fixture stride is 0x{LevelFog.ConfirmedOldStride:X}; known fields remain inside the validated span.");

        uint b = section.offset;
        Vector3 colour = new(
            ReadFloat(main, b + 0x40),
            ReadFloat(main, b + 0x44),
            ReadFloat(main, b + 0x48));
        float nearDistance = ReadFloat(main, b + 0x4C);
        float farDistance = ReadFloat(main, b + 0x50);
        float amount = ReadFloat(main, b + 0x54);
        byte[] raw = main.sh.ReadFromOffset(checked((int)section.length), b);
        BloomParameters? bloom = ReadBloom(raw);

        if (!IsFinite(colour) || !float.IsFinite(nearDistance) || !float.IsFinite(farDistance) || !float.IsFinite(amount))
        {
            Console.WriteLine("Level fog 0x7100 contains non-finite known fields; retaining raw data but refusing to manufacture coefficients.");
            return new LevelFog
            {
                Colour = colour,
                NearDistance = nearDistance,
                FarDistance = farDistance,
                Amount = amount,
                SourceOffset = b,
                RawRecord = raw,
                Bloom = bloom,
            };
        }

        float linearScale = 0f;
        float linearBias = 0f;
        if (farDistance > nearDistance)
        {
            // Mirrors the retail CPU setter 0x572774. The final ToD FOGC source is deliberately not
            // selected here; this is only the exact coefficient preparation performed by the game.
            linearScale = amount / (farDistance - nearDistance);
            linearBias = -nearDistance * linearScale;
        }
        else
        {
            Console.WriteLine($"Level fog 0x7100 has invalid linear range near={nearDistance:G9}, far={farDistance:G9}; coefficients left neutral.");
        }

        var fog = new LevelFog
        {
            Colour = colour,
            NearDistance = nearDistance,
            FarDistance = farDistance,
            Amount = amount,
            LinearScale = linearScale,
            LinearBias = linearBias,
            SourceOffset = b,
            RawRecord = raw,
            Bloom = bloom,
        };

        Console.WriteLine($"Level fog (0x7100): colour={colour}, near={nearDistance:G6}, far={farDistance:G6}, amount={amount:G6}, scale={linearScale:G9}, bias={linearBias:G9}.");
        return fog;
    }

    /// <summary>0x7100 +0x70..+0x8C, as 0x004E2E00 copies them (see <see cref="LevelFog.Bloom"/>).</summary>
    private static BloomParameters? ReadBloom(byte[] raw)
    {
        if (raw.Length < 0x90)
            return null;
        static uint U(byte[] r, int o) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(r.AsSpan(o, 4));
        static float F(byte[] r, int o) => BitConverter.UInt32BitsToSingle(U(r, o));
        if (U(raw, 0x70) == 0)
            return null;
        float mip = F(raw, 0x74), history = F(raw, 0x78), scale = F(raw, 0x80), exponent = F(raw, 0x84), intensity = F(raw, 0x88);
        if (!float.IsFinite(mip) || !float.IsFinite(history) || !float.IsFinite(scale) || !float.IsFinite(exponent) || !float.IsFinite(intensity))
            return null;
        return new BloomParameters(scale, exponent, mip, history, intensity,
            ColourExtract: U(raw, 0x8C) != 0, DestinationAlpha: true);
    }

    private static float ReadFloat(IGFile file, uint offset)
    {
        file.sh.Seek(offset);
        return file.sh.ReadSingle();
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
