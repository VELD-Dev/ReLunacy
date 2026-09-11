using ReLunacy.Engine.Assets.Animations;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Animation catalog reader shared by both engine revisions. Storage parsing stays revision-specific;
/// callers receive the same AnimationSet/AnimationClip model either way.
/// </summary>
public sealed class AnimationReader
{
    private const uint NewAnimsetPointerSection = 0x1D700;

    private readonly FileManager _fileManager;
    private readonly List<AnimationClip> _allClips = [];
    private readonly List<AnimationClip> _oldGlobalClips = [];
    private readonly Dictionary<ulong, AnimationSet> _newAnimationSets = [];
    private IGFile? _oldMain;
    private uint _oldF000Offset;
    private bool _loaded;

    public IReadOnlyList<AnimationClip> AllClips => _allClips;
    public IReadOnlyDictionary<ulong, AnimationSet> AnimationSets => _newAnimationSets;

    public AnimationReader(FileManager fileManager) =>
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));

    public IReadOnlyList<AnimationClip> ReadAll()
    {
        if (_loaded) return _allClips;
        _loaded = true;

        if (_fileManager.isOld) ReadOldEngine();
        else ReadNewEngine();
        return _allClips;
    }

    /// <summary>Resolves the animation set attached to one already-read Moby prototype.</summary>
    public AnimationSet? ResolveForMoby(Objects.Moby moby, ulong mobyId)
    {
        if (!_loaded) ReadAll();

        if (moby.MobyObj is OldMoby oldMoby)
        {
            if (_oldMain is null || oldMoby.animationCount == 0 || oldMoby.animationListPointer == 0)
                return null;

            var indices = MobyAnimationResolver.Resolve(
                _oldMain.sh,
                oldMoby.animationCount,
                oldMoby.animationListPointer,
                _oldF000Offset);
            if (indices.Count == 0) return null;

            var clips = new List<AnimationClip>(indices.Count);
            foreach (int index in indices)
            {
                if ((uint)index < (uint)_oldGlobalClips.Count)
                    clips.Add(_oldGlobalClips[index]);
            }
            return clips.Count == 0 ? null : new AnimationSet(mobyId, clips, isSynthetic: true);
        }

        if (moby.MobyObj is NewMoby newMoby && newMoby.animsetTuid != 0 &&
            _newAnimationSets.TryGetValue(newMoby.animsetTuid, out var set))
            return set;

        return null;
    }

    private void ReadOldEngine()
    {
        if (!_fileManager.igfiles.TryGetValue("main.dat", out var main) || main is null) return;
        _oldMain = main;

        var f000 = main.QuerySection(AnimationMetadataOld.ID);
        if (f000.id != AnimationMetadataOld.ID || f000.count == 0) return;
        if (f000.length != AnimationMetadataOld.Size)
            throw new InvalidDataException($"Old F000 stride is 0x{f000.length:X}, expected 0x{AnimationMetadataOld.Size:X}.");
        _oldF000Offset = f000.offset;

        var f400 = main.QuerySection(AnimationAuxMetadataOld.ID);
        var f500 = main.QuerySection(AnimationFrameAuxOld.ID);
        bool hasParallelF400 = f400.id == AnimationAuxMetadataOld.ID &&
                               f400.length == AnimationAuxMetadataOld.Size &&
                               f400.count == f000.count;

        for (uint i = 0; i < f000.count; i++)
        {
            var header = AnimationMetadataOld.Read(main.sh, f000.offset + AnimationMetadataOld.Size * i);
            ValidateOldHeaderStorage(header, i);

            AnimationAuxMetadataOld? aux = null;
            IReadOnlyList<AnimationFrameAuxOld> frameAux = [];
            if (hasParallelF400)
            {
                aux = AnimationAuxMetadataOld.Read(main.sh, f400.offset + AnimationAuxMetadataOld.Size * i);
                if (aux.Value.frameAuxCount != 0)
                    TryReadOldFrameAux(main.sh, f500, aux.Value, out frameAux);
            }

            var storage = new OldAnimationClipStorage(main.sh, header, aux, frameAux);
            var clip = new AnimationClip(
                header.Name,
                header.localAnimationIndex,
                header.flags,
                header.numFrames,
                header.numBones,
                header.frameRate,
                header.num16BitTracks,
                header.num8BitTracks,
                header.numReferenceValues,
                header.controlByteSize,
                header.linearSpeed,
                0,
                storage);
            _oldGlobalClips.Add(clip);
            _allClips.Add(clip);
        }
    }

    private void ReadNewEngine()
    {
        if (!_fileManager.igfiles.TryGetValue("assetlookup.dat", out var assetLookup) || assetLookup is null)
            return;
        if (!_fileManager.rawfiles.TryGetValue("animsets.dat", out var animsetsStream) || animsetsStream is null)
            return;

        var pointerSection = assetLookup.QuerySection(NewAnimsetPointerSection);
        if (pointerSection.id != NewAnimsetPointerSection || pointerSection.length < AssetPointer.Size)
            return;

        assetLookup.sh.Seek(pointerSection.offset);
        var pointers = AssetPointer.ReadArray(assetLookup.sh, pointerSection.length / AssetPointer.Size);
        foreach (var pointer in pointers)
        {
            if (pointer.length == 0) continue;
            byte[] data = ReadRawAsset(animsetsStream, pointer.offset, pointer.length);
            var clips = ReadNewAnimationSet(data);
            var set = new AnimationSet(pointer.TUID, clips);
            _newAnimationSets[pointer.TUID] = set;
            _allClips.AddRange(clips);
        }
    }

    private static IReadOnlyList<AnimationClip> ReadNewAnimationSet(byte[] data)
    {
        using var memory = new MemoryStream(data, writable: false);
        var ig = new IGFile(memory);
        try
        {
            var f000 = ig.QuerySection(AnimationMetadataNew.ID);
            if (f000.id != AnimationMetadataNew.ID || f000.count == 0) return [];
            if (f000.length != AnimationMetadataNew.Size)
                throw new InvalidDataException($"New F000 stride is 0x{f000.length:X}, expected 0x{AnimationMetadataNew.Size:X}.");

            var f400 = ig.QuerySection(AnimationAuxMetadataNew.ID);
            var f990 = ig.QuerySection(AnimationAuxBlockNew.ID);
            bool hasF400 = f400.id == AnimationAuxMetadataNew.ID &&
                           f400.length == AnimationAuxMetadataNew.Size &&
                           f400.count == f000.count;
            bool hasF990 = f990.id == AnimationAuxBlockNew.ID &&
                           f990.length == AnimationAuxBlockNew.Size &&
                           f990.count == f000.count;

            var clips = new List<AnimationClip>((int)f000.count);
            for (uint i = 0; i < f000.count; i++)
            {
                uint f000Pointer = f000.offset + AnimationMetadataNew.Size * i;
                var header = AnimationMetadataNew.Read(ig.sh, f000Pointer);
                ValidateNewHeaderStorage(header);

                AnimationAuxMetadataNew? aux = hasF400
                    ? AnimationAuxMetadataNew.Read(ig.sh, f400.offset + AnimationAuxMetadataNew.Size * i)
                    : null;
                AnimationAuxBlockNew? auxBlock = hasF990
                    ? AnimationAuxBlockNew.Read(ig.sh, f990.offset + AnimationAuxBlockNew.Size * i)
                    : null;

                if (auxBlock is { } block)
                {
                    if (block.f000Pointer != f000Pointer)
                        throw new InvalidDataException($"F990[{i}] does not point to its parallel F000 record.");
                    if (hasF400 && block.f400Pointer != f400.offset + AnimationAuxMetadataNew.Size * i)
                        throw new InvalidDataException($"F990[{i}] does not point to its parallel F400 record.");
                }

                var storage = new NewAnimationClipStorage(data, header, aux, auxBlock);
                clips.Add(new AnimationClip(
                    header.Name,
                    header.localAnimationIndex,
                    header.flags,
                    header.numFrames,
                    header.numBones,
                    header.frameRate,
                    header.num16BitTracks,
                    header.num8BitTracks,
                    header.numReferenceValues,
                    header.controlByteSize,
                    header.linearSpeed,
                    header.auxiliarySampleCount,
                    storage));
            }
            return clips;
        }
        finally
        {
            ig.Dispose();
        }
    }

    private static byte[] ReadRawAsset(Stream stream, uint offset, uint length)
    {
        var data = new byte[checked((int)length)];
        stream.Seek(offset, SeekOrigin.Begin);
        int read = 0;
        while (read < data.Length)
        {
            int n = stream.Read(data, read, data.Length - read);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
        return data;
    }

    private static void ValidateOldHeaderStorage(in AnimationMetadataOld header, uint index)
    {
        string name = string.IsNullOrEmpty(header.Name) ? $"F000[{index}]" : header.Name;
        if (header.IsPacked)
        {
            uint expected = Align16(Align16(header.num16BitTracks * 2u) + header.num8BitTracks);
            if (header.frameStride != expected)
                throw new InvalidDataException($"Animation '{name}' has an invalid packed payload size.");
        }
        if (header.controlPtr != 0 && header.framesPtr != 0 &&
            (ulong)header.controlPtr + Align128(header.controlByteSize) != header.framesPtr)
            throw new InvalidDataException($"Animation '{name}' has an invalid control/frame boundary.");
        if (header.framesPtr != 0 && header.rootTransformPtr != 0)
        {
            uint storedFrames = header.numFrames + (header.IsLooping ? 1u : 0u);
            ulong end = (ulong)header.framesPtr + (ulong)storedFrames * OldAnimationClipStorage.PhysicalFrameStride(header);
            if (end != header.rootTransformPtr)
                throw new InvalidDataException($"Animation '{name}' has an invalid stored-frame range.");
        }
    }

    private static void ValidateNewHeaderStorage(in AnimationMetadataNew header)
    {
        uint expectedStride = Align16(Align16(header.num16BitTracks * 2u) + header.num8BitTracks);
        if (header.frameStride != expectedStride)
            throw new InvalidDataException($"Animation '{header.Name}' has an invalid new-engine frame stride.");
        if (header.framesPtr == 0 && (header.num16BitTracks != 0 || header.num8BitTracks != 0))
            throw new InvalidDataException($"Animation '{header.Name}' has frame tracks but no frame-data pointer.");
    }

    private static bool TryReadOldFrameAux(
        StreamHelper sh,
        in IGFile.SectionHeader f500,
        in AnimationAuxMetadataOld aux,
        out IReadOnlyList<AnimationFrameAuxOld> records)
    {
        records = [];
        if (f500.id != AnimationFrameAuxOld.ID || f500.length != AnimationFrameAuxOld.Size || aux.frameAuxPointer < f500.offset)
            return false;

        uint delta = aux.frameAuxPointer - f500.offset;
        if (delta % AnimationFrameAuxOld.Size != 0) return false;
        ulong first = delta / AnimationFrameAuxOld.Size;
        if (first + aux.frameAuxCount > f500.count) return false;

        var list = new List<AnimationFrameAuxOld>(checked((int)aux.frameAuxCount));
        for (uint i = 0; i < aux.frameAuxCount; i++)
            list.Add(AnimationFrameAuxOld.Read(sh, aux.frameAuxPointer + i * AnimationFrameAuxOld.Size));
        records = list;
        return true;
    }

    private static uint Align16(uint value) => (value + 0x0Fu) & ~0x0Fu;
    private static uint Align128(uint value) => (value + 0x7Fu) & ~0x7Fu;
}
