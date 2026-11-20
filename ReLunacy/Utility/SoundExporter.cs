using System.Runtime.InteropServices;
using NAudio.Lame;
using NAudio.SoundFile;
using NAudio.Wave;
using ReLunacy.Engine.Assets.Audio;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Loading.Readers;

namespace ReLunacy.Utility;

public static class SoundExporter
{
    public static IWaveProvider? GetWaveFromSourceAudio(LevelSounds sounds, BankSound sound, IProgress<float> progress)
    {
        var pcm = SoundReader.Decode(sounds, sound.Bank, sound.BankIndex);
        if (pcm is null)
        {
            progress.Report(1);
            return null;
        }
        
        // samplerate * channels * bits per sample / 8 (bits per byte)
        var avgbps = pcm.SampleRate * pcm.Channels * 16 / 8;
        
        var wav = new BufferedWaveProvider(WaveFormat.CreateCustomFormat(
            WaveFormatEncoding.Pcm,
            pcm.SampleRate,
            pcm.Channels, 
            avgbps, 
            2 * pcm.Channels, 
            16
        ));
        
        wav.AddSamples(MemoryMarshal.Cast<short, byte>(pcm.Samples.AsSpan()));
        wav.CompleteAdding();
        progress.Report(1);
        return wav;
    }

    public static void ExportSoundAsFormat(LevelSounds sounds, BankSound sound, SoundFileMajorFormat format,
        IProgress<float> progress, Level level, bool isSoundtrack, double compressionLevel = 0)
    {
        var wave = GetWaveFromSourceAudio(sounds, sound, progress);
        if (wave is null) return;
        
        SoundFileWriter.CreateSoundFile(sound.Name, wave, format, new()
        {
            CompressionLevel = compressionLevel,
            Clipping = false,
            Subtype = SoundFileSubtype.Default,
            Tags = isSoundtrack ? new()
            {
                Artist = level.Game.Id is GameId.ToolsOfDestruction or GameId.QuestForBooty or GameId.RFallOfMan  ? "David Bergeaud"
                    : level.Game.Id is GameId.ACrackInTime ? "Boris Salchow"
                    : level.Game.Id is GameId.All4One or GameId.IntoTheNexus or GameId.FullFrontalAssault ? "Michael Bross"
                    : "Unknown",
                Title = sound.Name,
                Album = level.Game.DisplayName + " OST",
                Copyright = "Sony Computer Entertainment Inc."
            } : null,
            VbrQuality = 1.0
        });
    }
}