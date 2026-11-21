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
    public sealed class Pcm16WaveProvider(short[] samples, int sampleRate, int channels) : IWaveProvider
    {
        private readonly byte[] _bytes = [.. MemoryMarshal.AsBytes(samples.AsSpan())];
        private int _position;

        public WaveFormat WaveFormat { get; } = new WaveFormat(sampleRate, 16, channels);
        
        public int Read(Span<byte> buffer)
        {
            var remaining = _bytes.Length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var toCopy = Math.Min(remaining, buffer.Length);
            // Align to sample frame size (2 bytes per sample * channels)
            var frameSize = WaveFormat.BlockAlign;
            toCopy -= toCopy % frameSize;
            if (toCopy <= 0)
            {
                return 0;
            }

            _bytes.AsSpan(_position, toCopy).CopyTo(buffer);
            _position += toCopy;
            return toCopy;
        }
    }
    
    public static IWaveProvider? GetWaveFromSourceAudio(LevelSounds sounds, BankSound sound)
    {
        var pcm = SoundReader.Decode(sounds, sound.Bank, sound.BankIndex);
        if (pcm is null)
        {
            return null;
        }
        
        var wav = new Pcm16WaveProvider(pcm.Samples, pcm.SampleRate, pcm.Channels);
        return wav;
    }

    public static void ExportSoundAsFormat(string filepath, LevelSounds sounds, BankSound sound, SoundFileMajorFormat format,
        Level? level, bool isSoundtrack, double compressionLevel = 0)
    {
        var wave = GetWaveFromSourceAudio(sounds, sound);
        if (wave is null) return;
        
        if(!Directory.Exists(Path.GetDirectoryName(filepath)))
            Directory.CreateDirectory(Path.GetDirectoryName(filepath));
        
        LunaLog.LogDebug($"[sound-exporter] Exporting sound as {format}");
        SoundFileWriter.CreateSoundFile(filepath, wave, format, new()
        {
            CompressionLevel = format switch
            {
                SoundFileMajorFormat.Flac => compressionLevel,
                _ => 0
            },
            VbrQuality = format switch
            {
                SoundFileMajorFormat.Mp3
                or SoundFileMajorFormat.OggVorbis
                or SoundFileMajorFormat.Opus => 0.0,
                _ => 0
            },
            Clipping = false,
            Subtype = SoundFileSubtype.Default,
            Tags = isSoundtrack && level is {} l and not { Game: null } ? new()
            {
                Artist = l.Game.Id switch
                {
                    GameId.ToolsOfDestruction or GameId.QuestForBooty or GameId.RFallOfMan => "David Bergeaud",
                    GameId.ACrackInTime => "Boris Salchow",
                    GameId.All4One or GameId.IntoTheNexus or GameId.FullFrontalAssault => "Michael Bross",
                    _ => "Unknown"
                },
                Title = sound.Name,
                Album = l.Game.DisplayName + " OST",
                Copyright = "Sony Computer Entertainment Inc."
            } : null,
        });
    }
}