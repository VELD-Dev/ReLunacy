namespace ReLunacy.Engine.Assets.Cubemaps;

/// <summary>
/// Enumerates candidate <see cref="CubemapArrangement"/>s so they can be stepped through instead of
/// dialled in by hand.
///
/// The space used to be a free face permutation times eight global flips - 5760 combinations, which
/// sounded thorough and was mostly worthless. MEASURED 2026-11-06 (dev/tools/cubeprobe --verify): a
/// face permutation whose per-face rotations do not match it leaves the six faces not joining, at
/// 11x to 60x the seam discontinuity of a cube that does join, so the overwhelming majority of those
/// 5760 were broken cubes and looking at them could only waste time. <see cref="CubemapArrangement"/>
/// now derives the per-face rotations from a signed axis permutation, so the space is:
///
///   48 orientations - the cube's symmetry group, every one a consistent cube - times
///    8 storage transforms, which is where the convention's (u, v) sits in the file.
///
/// The storage transform is already settled by measurement at "(v, u)", by a 16x margin on every
/// cube of all four new-engine games, so <see cref="Set.Orientations"/> is the set to sweep. The
/// others are for re-testing that conclusion, not for finding the answer.
///
/// Worth knowing before sweeping: a seam metric is BLIND to all 48 orientations, because re-orienting
/// a cube that joins leaves it joining. Nothing in the viewer or the 3D view can rank them either.
/// Only a landmark can - a direction in the level whose appearance in the cube is known.
/// </summary>
public static class CubemapArrangementSweep
{
    /// <summary>Which candidates to walk.</summary>
    public enum Set
    {
        /// <summary>The 48 cube symmetries under the measured storage transform. Every one is a cube.</summary>
        Orientations,
        /// <summary>The eight per-face texel layouts, unrotated. Measurement says "(v, u)".</summary>
        StorageTransforms,
        /// <summary>Every orientation under every storage transform.</summary>
        All,
    }

    public static int Count(Set set) => set switch
    {
        Set.Orientations => CubemapArrangement.OrientationCount,
        Set.StorageTransforms => CubemapArrangement.StorageCount,
        _ => CubemapArrangement.OrientationCount * CubemapArrangement.StorageCount,
    };

    /// <summary>The candidate at <paramref name="index"/>, wrapped into range so stepping past either
    /// end cycles. New-engine cubes, which is what the viewer lists.</summary>
    public static CubemapArrangement At(Set set, int index)
    {
        int count = Count(set);
        index = ((index % count) + count) % count;
        return set switch
        {
            Set.Orientations => Build(index, CubemapArrangement.TransposedStorage),
            Set.StorageTransforms => Build(0, index),
            _ => Build(index / CubemapArrangement.StorageCount, index % CubemapArrangement.StorageCount),
        };
    }

    /// <summary>Where <paramref name="arrangement"/> sits in <paramref name="set"/>, or -1 when the set
    /// does not contain it.</summary>
    public static int IndexOf(Set set, CubemapArrangement arrangement)
    {
        int count = Count(set);
        for (int i = 0; i < count; i++)
            if (At(set, i).SameAs(arrangement))
                return i;
        return -1;
    }

    /// <summary>The smallest set that contains <paramref name="arrangement"/>, and its index in it.</summary>
    public static (Set Set, int Index) Locate(CubemapArrangement arrangement)
    {
        foreach (var set in (Set[])[Set.Orientations, Set.StorageTransforms, Set.All])
        {
            int at = IndexOf(set, arrangement);
            if (at >= 0) return (set, at);
        }
        // Unreachable: Set.All enumerates every orientation under every storage transform.
        return (Set.All, -1);
    }

    private static CubemapArrangement Build(int orientation, int storage) =>
        new(storedTransposed: true, orientation, storage);
}
