using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Texture streaming's policy. A level loads every texture at no more than <see cref="BaseDimension"/>
/// (decoded from its own small mips, so loading skips most of the decoding), and the textures the
/// camera needs larger are then decoded again at the size it needs, on worker threads, and swapped in.
/// Under the GPU memory allowance, textures nothing has shown for a while go back down to make room,
/// least recently seen first; with room to spare nothing is ever demoted, so moving around does not
/// churn.
///
/// The size a texture needs comes from the renderer: the largest radius on screen, in pixels, of any
/// instance drawing a material that samples it. A texture as many texels across as that object is
/// pixels across, rounded up to a power of two, is what the sampler's mip selection would pick at most
/// for a texture mapped once over the object - generous for tiling textures, about right for atlases.
///
/// This class only decides and decodes. AssetManager uploads what it hands back and swaps it in.
/// </summary>
internal sealed class TextureStreamer
{
    /// <summary>The largest side a texture loads with. 128 px with its mips is 85 KiB.</summary>
    public const uint BaseDimension = 128;

    /// <summary>Debugging: RELUNACY_TEXTURE_STREAMING=0 loads every texture at its own resolution again
    /// (still fitted to the GPU's memory), as before streaming.</summary>
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("RELUNACY_TEXTURE_STREAMING") != "0";

    /// <summary>Seconds a texture keeps the largest size it was needed at after the need drops.</summary>
    private const double HoldSeconds = 5;

    /// <summary>Decodes running at once. Half the cores, so the editor itself stays responsive.</summary>
    private static readonly int MaxDecodes = Math.Max(1, Environment.ProcessorCount / 2);

    public sealed class Entry(ITexture source, GpuTexture gpu, bool generateMips, uint resident)
    {
        public readonly ITexture Source = source;
        public readonly GpuTexture Gpu = gpu;
        /// <summary>Generate mips when the texture has none of its own (false for atlases).</summary>
        public readonly bool GenerateMips = generateMips;
        public readonly bool Mipped = generateMips || source is Assets.Textures.Texture { HasAuthoredMipChain: true };
        public readonly uint Full = Math.Max(source.Width, source.Height);

        /// <summary>The largest side the GPU holds now (a cap: the texture is that or just under).</summary>
        public uint Resident = resident;
        /// <summary>The largest side asked for this frame, and the hold on the largest recent one.</summary>
        public uint Need;
        public uint Wanted;
        public double WantedAt;
        public double LastSeen = double.NegativeInfinity;
        /// <summary>Kept at full resolution, whatever is on screen (the sky, the texture explorer).</summary>
        public bool Pinned;
        /// <summary>Pinned, but its full resolution could not be had: it stays as it is.</summary>
        public bool PinAbandoned;

        /// <summary>The size being decoded or uploaded; 0 when idle.</summary>
        public uint Changing;
        public Task<TextureLevels?>? Decode;
        /// <summary>When a change last failed, so it is not retried every frame.</summary>
        public double FailedAt = double.NegativeInfinity;

        public uint Target => Pinned ? Full : Math.Min(Full, Math.Max(Wanted, BaseDimension));
    }

    private readonly Dictionary<GpuTexture, Entry> _entries = new(ReferenceEqualityComparer.Instance);

    public int Count => _entries.Count;
    public bool TryGet(GpuTexture gpu, out Entry entry) => _entries.TryGetValue(gpu, out entry!);
    public IEnumerable<Entry> Entries => _entries.Values;

    public void Add(Entry entry) => _entries[entry.Gpu] = entry;

    /// <summary>Heap bytes the streamed textures take at their current sizes, plus what changes in progress
    /// have allocated beside them.</summary>
    public ulong CommittedBytes { get; private set; }

    /// <summary>Pinned textures not yet at full resolution: whoever pinned them is waiting.</summary>
    public int PinnedPending { get; private set; }

    public void Pin(Entry entry)
    {
        if (entry.Pinned) return;
        entry.Pinned = true;
        if (entry.Resident < entry.Full) PinnedPending++;
    }

    public void Remove(GpuTexture gpu)
    {
        if (_entries.Remove(gpu, out var entry) && entry.Pinned && entry.Resident < entry.Full) PinnedPending--;
    }

    /// <summary>The largest side the screen needs for an object of this radius in pixels.</summary>
    public static uint NeedFor(float screenRadius)
    {
        if (screenRadius <= 0f) return 0;
        float diameter = MathF.Min(screenRadius * 2f, 16384f);
        return Math.Max(1u, System.Numerics.BitOperations.RoundUpToPowerOf2((uint)MathF.Ceiling(diameter)));
    }

    /// <summary>Records that <paramref name="entry"/> is needed at <paramref name="dimension"/> this frame.</summary>
    public static void Demand(Entry entry, uint dimension, double now)
    {
        if (dimension == 0) return;
        if (dimension > entry.Need) entry.Need = dimension;
        entry.LastSeen = now;
    }

    /// <summary>
    /// One frame of policy: folds this frame's needs into the held sizes, collects finished decodes
    /// (returned, for the caller to upload), and starts new decodes - promotions, largest shortfall
    /// first, and the demotions the allowance requires to make room for them.
    /// </summary>
    public List<(Entry Entry, TextureLevels Levels)> Update(double now, ulong allowance)
    {
        var finished = new List<(Entry, TextureLevels)>();
        ulong committed = 0, wanted = 0;
        int decoding = 0, short_ = 0;
        foreach (var e in _entries.Values)
        {
            if (e.Need >= e.Wanted) { e.Wanted = e.Need; e.WantedAt = now; }
            else if (now - e.WantedAt > HoldSeconds) { e.Wanted = e.Need; e.WantedAt = now; }
            e.Need = 0;

            if (e.Decode is { IsCompleted: true } task)
            {
                e.Decode = null;
                TextureLevels? levels = task.IsCompletedSuccessfully ? task.Result : null;
                if (levels != null) finished.Add((e, levels));
                else Failed(e, now, task.Exception?.GetBaseException().Message ?? "nothing decoded");
            }
            if (e.Decode != null) decoding++;
            committed += Bytes(e, e.Resident) + (e.Changing != 0 ? Bytes(e, e.Changing) : 0);
            wanted += Bytes(e, e.Target);
            if (e.Changing == 0 && e.Target > e.Resident) short_++;
        }
        CommittedBytes = committed;

        if (decoding >= MaxDecodes || short_ == 0) return finished;
        // When everything asked for fits, each texture goes straight to its size. When it does not, one
        // step at a time, so the allowance is shared out - every texture at 256 before any at 1024 -
        // instead of going to whichever came first.
        bool fits = wanted <= allowance;
        var promotions = _entries.Values
            .Where(e => e.Changing == 0 && e.Target > e.Resident && now - e.FailedAt > HoldSeconds && !e.PinAbandoned)
            .OrderByDescending(e => e.Pinned)
            .ThenByDescending(e => (double)e.Target / e.Resident)
            .ThenByDescending(e => e.LastSeen)
            .ToList();
        if (promotions.Count == 0) return finished;

        // Demotion candidates, least recently seen first: only reached for when a promotion does not fit.
        Queue<Entry>? victims = null;
        foreach (var e in promotions)
        {
            if (decoding >= MaxDecodes) break;
            uint size = fits || e.Pinned ? e.Target : Math.Min(e.Target, e.Resident * 2);
            ulong extra = Bytes(e, size);
            while (committed + extra > allowance)
            {
                victims ??= new Queue<Entry>(_entries.Values
                    .Where(v => v.Changing == 0 && !v.Pinned && v.Target < v.Resident)
                    .OrderBy(v => v.LastSeen));
                if (victims.Count == 0 || decoding >= MaxDecodes) break;
                var victim = victims.Dequeue();
                // Freed once its smaller copy is swapped in; that copy is allocated meanwhile.
                committed += Bytes(victim, victim.Target);
                Start(victim, victim.Target);
                decoding++;
                committed -= Math.Min(committed, Bytes(victim, victim.Resident));
            }
            // Too big for what is left; a smaller one further down may still fit.
            if (committed + extra > allowance) continue;
            if (decoding >= MaxDecodes) break;
            committed += extra;
            Start(e, size);
            decoding++;
        }
        return finished;
    }

    private static void Start(Entry e, uint dimension)
    {
        e.Changing = dimension;
        ITexture source = e.Source;
        bool generate = e.GenerateMips;
        e.Decode = Task.Run(() => TextureUploadPreparer.Prepare(source, generate, dimension));
    }

    /// <summary>The change in progress could not be made; the texture keeps what it has.</summary>
    public void Failed(Entry e, double now, string why)
    {
        // A pinned texture's consumer is waiting on it: it gets what there is, and it then stays as it is,
        // since that consumer keeps the device texture.
        if (e.Pinned && !e.PinAbandoned && e.Resident < e.Full)
        {
            e.PinAbandoned = true;
            PinnedPending--;
        }
        e.Changing = 0;
        e.Decode = null;
        e.FailedAt = now;
        Console.WriteLine($"Texture streaming: {e.Source.Id:X16} stays at {e.Resident} px ({why}).");
    }

    /// <summary>The swap is done: the GPU holds the size that was being changed to.</summary>
    public void Completed(Entry e)
    {
        bool waited = e.Pinned && e.Resident < e.Full;
        e.Resident = e.Changing;
        e.Changing = 0;
        if (waited && e.Resident >= e.Full) PinnedPending--;
    }

    public static ulong Bytes(Entry e, uint dimension) => AssetManager.HeapBytes(e.Source, e.Mipped, dimension);
}
