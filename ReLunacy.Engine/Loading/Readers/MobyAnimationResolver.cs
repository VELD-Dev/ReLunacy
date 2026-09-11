using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Resolves an old-engine Moby's own animation list from D100 +0x16/+0x24 into global
/// indices in main.dat section 0xF000. This is pointer arithmetic only: no name matching and no
/// bone-count heuristic.</summary>
public static class MobyAnimationResolver
{
    public static List<int> Resolve(StreamHelper sh, ushort animationCount, uint animationListPointer, uint f000SectionOffset)
    {
        var indices = new List<int>(animationCount);
        if (animationCount == 0 || animationListPointer == 0)
            return indices;

        for (int i = 0; i < animationCount; i++)
        {
            uint clipPtr = sh.ReadUInt32(animationListPointer + (uint)i * 4);
            if (clipPtr < f000SectionOffset)
                continue;

            uint delta = clipPtr - f000SectionOffset;
            if (delta % AnimationMetadataOld.Size != 0)
                continue;

            indices.Add((int)(delta / AnimationMetadataOld.Size));
        }

        return indices;
    }
}
