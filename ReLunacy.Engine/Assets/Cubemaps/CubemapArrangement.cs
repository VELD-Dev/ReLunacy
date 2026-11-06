namespace ReLunacy.Engine.Assets.Cubemaps;

/// <summary>
/// How a stored cubemap is turned into the cube the GPU samples. Two INDEPENDENT parts, which were
/// previously tangled into one set of per-direction face combos and three global flip checkboxes:
///
///   1. <see cref="Storage"/> - how a single face's texels are laid out, the same for all six faces.
///      Named in the convention's own notation: "(v, u)" means the texel the GL/RSX convention calls
///      (u, v) is stored at (v, u). MEASURED, see below.
///   2. <see cref="Orientation"/> - how the assembled cube sits in the world: a signed axis
///      permutation, 48 of them, which is exactly the cube's symmetry group.
///
/// Separating them matters because only part 2 can be a re-orientation. The old model applied ONE
/// flip set to all six faces, and a cube rotation does not work that way: yawing 90 degrees about Y
/// permutes the four side faces without rotating them while rotating the top and bottom 90 degrees.
/// So most of what the old model could express was not a cube at all - a face permutation with no
/// matching per-face rotation leaves the six faces not joining, which MEASURED (dev/tools/cubeprobe
/// --verify, 2026-11-06) at 11x to 60x the seam discontinuity of a cube that does join. Here the
/// per-face transform is DERIVED from the orientation, so every one of the 48 is a real cube.
///
/// The storage transform is settled: of the eight, only "(v, u)" - a plain transpose - makes the six
/// faces join, on every one of the 24 cubes measured across all four new-engine games (A4O
/// luminopolis/crumbler/polar_sea, ITN haunted_city, QForce jungle_ruins_pre, ACiT axiom_city), by a
/// margin of 16x to 60x. CubemapReader already applies that transpose when it loads a new-engine
/// cube, which is why the default here is the one that leaves its bytes alone. "(u, v)" and
/// "(1-v, u)" are both ruled out: a per-face row flip cannot be hiding in the storage layout,
/// because a row flip would have broken the seams.
///
/// The orientation is NOT settled, and a seam metric cannot settle it: re-orienting a cube that
/// joins leaves it joining, so all 48 score identically (measured spread 0.03-0.19 against the 16x+
/// that separates the storage transforms). It needs a landmark - a direction in the level whose
/// appearance in the cube is known, such as the sun - not a smoothness score and not a look at the
/// cube on its own.
///
/// Mutating an instance bumps <see cref="Revision"/>, which is how a caller tells that the cubes it
/// uploaded are stale (see AssetManager.RequestEnvironmentCubemapRebuild).
/// </summary>
public sealed class CubemapArrangement
{
    public const int OrientationCount = 48;
    public const int StorageCount = 8;

    /// <summary>Where the convention's (u, v) actually sits in the file, as the notation reads.</summary>
    public static readonly string[] StorageNames =
        ["(u, v)", "(1-u, v)", "(u, 1-v)", "(1-u, 1-v)", "(v, u)", "(1-v, u)", "(v, 1-u)", "(1-v, 1-u)"];

    /// <summary>The storage transform CubemapReader's transpose already applies, so selecting it
    /// leaves the loaded bytes alone. Measured as the only one whose faces join.</summary>
    public const int TransposedStorage = 4;

    /// <summary>The six axis orders, identity first.</summary>
    private static readonly int[][] AxisOrders =
        [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];

    // A signed axis is coded axis * 2 + (negative ? 1 : 0), so the codes 0..5 ARE the face indices
    // (+X, -X, +Y, -Y, +Z, -Z) and face f's outward normal is simply code f. The tangent frames are
    // the GL/RSX ones the GPU's cube sampler uses, read straight off the face selection: for face f,
    // sc = d . S[f] and tc = d . T[f].
    private static readonly int[] FaceS = [5, 4, 0, 0, 0, 1]; // -Z, +Z, +X, +X, +X, -X
    private static readonly int[] FaceT = [3, 3, 4, 5, 3, 3]; // -Y, -Y, +Z, -Z, -Y, -Y

    /// <summary>Per direction: which stored face feeds it, and where that face's (u, v) comes from.
    /// <c>uFromV</c>/<c>vFromU</c> say the two coordinates are exchanged; the negate flags mirror.</summary>
    private readonly struct FaceMap
    {
        public FaceMap(int source, bool uFromV, bool negateU, bool vFromU, bool negateV)
        { Source = source; UFromV = uFromV; NegateU = negateU; VFromU = vFromU; NegateV = negateV; }
        public int Source { get; }
        public bool UFromV { get; }
        public bool NegateU { get; }
        public bool VFromU { get; }
        public bool NegateV { get; }
        public bool IsIdentity => !UFromV && !NegateU && !VFromU && !NegateV;
    }

    private readonly bool storedTransposed;
    private int orientation;
    private int storage;
    private FaceMap[] maps = [];

    /// <param name="storedTransposed">True when the reader has already transposed the faces it hands
    /// over, as CubemapReader does for new-engine cubes and does not for old-engine ones. The storage
    /// transform is named in FILE space either way, so this is what keeps the names honest.</param>
    public CubemapArrangement(bool storedTransposed, int orientation = 0, int storage = 0)
    {
        this.storedTransposed = storedTransposed;
        this.orientation = Math.Clamp(orientation, 0, OrientationCount - 1);
        this.storage = Math.Clamp(storage, 0, StorageCount - 1);
        Rebuild();
    }

    /// <summary>
    /// What the new-engine cubes are uploaded with: the measured storage transform, and the cube
    /// turned upside down.
    ///
    /// The Y negation is VELD's finding of 2026-11-06, "flip V and permutate Y+ with Y-", which under
    /// the old model was two separate settings. It is one thing: negating Y swaps the +Y and -Y faces
    /// AND flips V on all six, so the two halves were never independent, and either half on its own
    /// scores 26x-47x on seam discontinuity while the pair scores exactly what the unrotated cube
    /// does. That is what identifies it as a real symmetry rather than a lucky-looking scramble - but
    /// also why no seam measurement can confirm it is the RIGHT symmetry. Still a finding, judged by
    /// eye against the retail game.
    /// </summary>
    public static CubemapArrangement NewEngine { get; } =
        new(storedTransposed: true, orientation: OrientationOf([0, 1, 2], negX: false, negY: true, negZ: false), storage: TransposedStorage);

    /// <summary>Identity: every audited retail ToD cube matches the documented face order, and the
    /// old-engine reader hands its faces over untransposed.</summary>
    public static CubemapArrangement OldEngine { get; } = new(storedTransposed: false);

    public static CubemapArrangement For(bool oldEngine) => oldEngine ? OldEngine : NewEngine;

    /// <summary>Bumped by every change, so a stale upload can be detected without comparing fields.</summary>
    public int Revision { get; private set; }

    /// <summary>Which of the 48 signed axis permutations the cube is sampled through.</summary>
    public int Orientation
    {
        get => orientation;
        set
        {
            int v = ((value % OrientationCount) + OrientationCount) % OrientationCount;
            if (orientation == v) return;
            orientation = v;
            Rebuild();
            Revision++;
        }
    }

    /// <summary>Which of the eight per-face texel layouts the stored faces are read under, indexing
    /// <see cref="StorageNames"/>.</summary>
    public int Storage
    {
        get => storage;
        set
        {
            int v = Math.Clamp(value, 0, StorageCount - 1);
            if (storage == v) return;
            storage = v;
            Rebuild();
            Revision++;
        }
    }

    /// <summary>The index in the 48 of the orientation that maps each axis to the named axis with the
    /// named sign. <paramref name="axes"/> is the axis order, so [0,1,2] keeps every axis in place.</summary>
    public static int OrientationOf(ReadOnlySpan<int> axes, bool negX, bool negY, bool negZ)
    {
        int order = 0;
        for (int i = 0; i < AxisOrders.Length; i++)
            if (AxisOrders[i][0] == axes[0] && AxisOrders[i][1] == axes[1] && AxisOrders[i][2] == axes[2])
            { order = i; break; }
        return order * 8 + (negX ? 1 : 0) + (negY ? 2 : 0) + (negZ ? 4 : 0);
    }

    /// <summary>"+X,-Y,+Z" - the axis each sampled component is taken from, which is the orientation
    /// read out as what it does to a direction.</summary>
    public static string DescribeOrientation(int index)
    {
        index = ((index % OrientationCount) + OrientationCount) % OrientationCount;
        int[] axes = AxisOrders[index / 8];
        int signs = index % 8;
        string[] names = ["X", "Y", "Z"];
        var parts = new string[3];
        for (int i = 0; i < 3; i++)
            parts[i] = ((signs & (1 << i)) != 0 ? "-" : "+") + names[axes[i]];
        return string.Join(",", parts);
    }

    /// <summary>The stored face that feeds cube direction <paramref name="direction"/>.</summary>
    public int Source(int direction) => maps[Math.Clamp(direction, 0, 5)].Source;

    /// <summary>True when every direction takes its own face and reads it untransformed, i.e. the
    /// upload is the loaded bytes as they are.</summary>
    public bool IsIdentity
    {
        get
        {
            for (int f = 0; f < 6; f++)
                if (maps[f].Source != f || !maps[f].IsIdentity) return false;
            return true;
        }
    }

    public bool SameAs(CubemapArrangement other) =>
        ReferenceEquals(this, other) ||
        (orientation == other.orientation && storage == other.storage && storedTransposed == other.storedTransposed);

    public void CopyFrom(CubemapArrangement other)
    {
        if (orientation == other.orientation && storage == other.storage) return;
        orientation = other.orientation;
        storage = other.storage;
        Rebuild();
        Revision++;
    }

    /// <summary>Back to the loaded bytes as they are: no re-orientation, and the storage transform
    /// that the reader has already applied.</summary>
    public void Reset()
    {
        int wanted = storedTransposed ? TransposedStorage : 0;
        if (orientation == 0 && storage == wanted) return;
        orientation = 0;
        storage = wanted;
        Rebuild();
        Revision++;
    }

    /// <summary>One line for the upload log, so which arrangement produced a screenshot is recoverable.</summary>
    public string Describe() =>
        $"stored {StorageNames[storage]}, oriented {DescribeOrientation(orientation)}" +
        (IsIdentity ? " (as loaded)" : "");

    /// <summary>Where direction <paramref name="direction"/>'s (u, v) is read from on its stored face,
    /// in continuous coordinates - for a filtered CPU sample, where the integer form's n-1-x would be
    /// half a texel out.</summary>
    public (float u, float v) Map(int direction, float u, float v)
    {
        var m = maps[Math.Clamp(direction, 0, 5)];
        float su = m.UFromV ? v : u;
        float sv = m.VFromU ? u : v;
        return (m.NegateU ? 1f - su : su, m.NegateV ? 1f - sv : sv);
    }

    /// <summary>
    /// One face's texels rearranged so that the GPU's own cube sampler, which knows nothing about any
    /// of this, lands where <see cref="Map"/> says it should. Returns <paramref name="face"/> itself
    /// when this direction needs no rearranging, or when the buffer is too short to do it safely.
    /// </summary>
    public byte[] Orient(int direction, byte[] face, int size, int bytesPerTexel)
    {
        var m = maps[Math.Clamp(direction, 0, 5)];
        if (m.IsIdentity) return face;
        int stride = size * bytesPerTexel;
        if (face.Length < size * stride) return face;

        var result = new byte[size * stride];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int sx = m.UFromV ? y : x;
                int sy = m.VFromU ? x : y;
                if (m.NegateU) sx = size - 1 - sx;
                if (m.NegateV) sy = size - 1 - sy;
                Buffer.BlockCopy(face, sy * stride + sx * bytesPerTexel,
                                 result, y * stride + x * bytesPerTexel, bytesPerTexel);
            }
        }
        return result;
    }

    /// <summary>
    /// Derives the per-face source and texel transform from the orientation and the storage
    /// transform, which is the whole point of the two-part model: the caller picks a cube symmetry
    /// and the six per-face rotations that symmetry implies fall out, instead of being guessed.
    ///
    /// For destination face f the sampled direction is M d, so the stored face is the one whose
    /// normal is M N(f), and the coordinates come from projecting M S(f) and M T(f) onto that face's
    /// own tangents - a signed 2x2 permutation, i.e. one of the eight texel transforms. The storage
    /// transform and the reader's transpose are then composed onto it.
    /// </summary>
    private void Rebuild()
    {
        var built = new FaceMap[6];
        for (int f = 0; f < 6; f++)
        {
            int g = Apply(f);
            int a = Dot(Apply(FaceS[f]), FaceS[g]), b = Dot(Apply(FaceT[f]), FaceS[g]);
            int c = Dot(Apply(FaceS[f]), FaceT[g]), e = Dot(Apply(FaceT[f]), FaceT[g]);

            // u comes from the destination's u when a is set and from its v when b is; likewise v.
            bool uFromV = a == 0, vFromU = e == 0;
            bool negU = (uFromV ? b : a) < 0, negV = (vFromU ? c : e) < 0;

            // Compose the storage transform, then the reader's transpose, in that order: the
            // orientation lands on the source face's CONVENTIONAL (u, v), the storage transform says
            // where the file keeps that, and the transpose says what the reader did to the file.
            (uFromV, negU, vFromU, negV) = Compose(uFromV, negU, vFromU, negV, StorageTransform(storage));
            if (storedTransposed)
                (uFromV, negU, vFromU, negV) = Compose(uFromV, negU, vFromU, negV, (true, false, true, false));

            built[f] = new FaceMap(g, uFromV, negU, vFromU, negV);
        }
        maps = built;
    }

    /// <summary>The eight storage transforms as (u exchanged, u negated, v exchanged, v negated),
    /// in the order <see cref="StorageNames"/> names them.</summary>
    private static (bool uFromV, bool negU, bool vFromU, bool negV) StorageTransform(int index) => index switch
    {
        0 => (false, false, false, false), // (u, v)
        1 => (false, true, false, false),  // (1-u, v)
        2 => (false, false, false, true),  // (u, 1-v)
        3 => (false, true, false, true),   // (1-u, 1-v)
        4 => (true, false, true, false),   // (v, u)
        5 => (true, true, true, false),    // (1-v, u)
        6 => (true, false, true, true),    // (v, 1-u)
        _ => (true, true, true, true),     // (1-v, 1-u)
    };

    /// <summary>Applies <paramref name="second"/> after a first texel transform. Both are signed 2x2
    /// permutations, so the result is one too.</summary>
    private static (bool uFromV, bool negU, bool vFromU, bool negV) Compose(
        bool uFromV, bool negU, bool vFromU, bool negV,
        (bool uFromV, bool negU, bool vFromU, bool negV) second)
    {
        // Tracked as "which of the first transform's two outputs does the second one read, and do
        // the mirrors cancel" rather than by multiplying matrices out by hand.
        bool su = second.uFromV, sv = second.vFromU;
        bool outUFromV, outNegU, outVFromU, outNegV;

        // Where the composite's u comes from: second's u picks first's u or v, and the negations add.
        if (!su) { outUFromV = uFromV; outNegU = negU ^ second.negU; }
        else { outUFromV = !vFromU; outNegU = negV ^ second.negU; }

        if (!sv) { outVFromU = vFromU; outNegV = negV ^ second.negV; }
        else { outVFromU = !uFromV; outNegV = negU ^ second.negV; }

        return (outUFromV, outNegU, outVFromU, outNegV);
    }

    /// <summary>The orientation applied to a signed axis code, giving another signed axis code.</summary>
    private int Apply(int code)
    {
        int[] axes = AxisOrders[orientation / 8];
        int signs = orientation % 8;
        int axis = code / 2;
        bool negative = (code & 1) != 0;
        int row = 0;
        for (int i = 0; i < 3; i++) if (axes[i] == axis) { row = i; break; }
        if ((signs & (1 << row)) != 0) negative = !negative;
        return row * 2 + (negative ? 1 : 0);
    }

    /// <summary>+1, -1 or 0 for two signed axis codes - they are unit vectors along the axes.</summary>
    private static int Dot(int a, int b) =>
        a / 2 != b / 2 ? 0 : (a & 1) == (b & 1) ? 1 : -1;
}
