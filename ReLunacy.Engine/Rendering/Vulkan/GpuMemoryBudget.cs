using NeoVeldrid;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// How much device-local memory the editor may count on, read from the GPU: the largest device-local
/// heap, and with <c>VK_EXT_memory_budget</c> the driver's budget for this process on it (what other
/// applications leave, which on a 4 GB card is the number that matters). A level whose textures would
/// not fit gets them reduced before upload (<see cref="AssetManager.PrepareTextures(Loading.Readers.LevelData,
/// Loading.LevelLoadProgress?, ulong, out TextureBudgetReport)"/>) instead of the driver failing inside
/// NeoVeldrid's allocator, which on some Windows drivers is an access violation rather than an error.
/// </summary>
/// <param name="HeapBytes">Size of the largest device-local heap.</param>
/// <param name="BudgetBytes">The driver's budget for this process on that heap, or the heap size
/// without the extension.</param>
/// <param name="FromBudgetExtension">Whether <see cref="BudgetBytes"/> came from VK_EXT_memory_budget.</param>
/// <param name="UsageBytes">What this process holds on that heap now, with VK_EXT_memory_budget; else 0.</param>
public readonly record struct GpuMemoryBudget(string DeviceName, ulong HeapBytes, ulong BudgetBytes, bool FromBudgetExtension, ulong UsageBytes = 0)
{
    private const ulong MiB = 1024 * 1024;

    /// <summary>Debugging / testing: RELUNACY_TEXTURE_BUDGET_MIB=&lt;n&gt; replaces the budget, e.g. 3584 to
    /// see what a 4 GB card gets.</summary>
    private static ulong? OverrideBudget =>
        ulong.TryParse(Environment.GetEnvironmentVariable("RELUNACY_TEXTURE_BUDGET_MIB"), out ulong mib) && mib > 0 ? mib * MiB : null;

    /// <summary>Heap bytes a texture costs beyond its own pixels. NeoVeldrid rounds every sub-allocation up
    /// to the next multiple of the device's buffer-image granularity (64 KiB on the RX 7600 measured), on
    /// average half of it - which on a level of 29,262 mostly small textures is 914 MiB on top of 1,119.
    /// Measured with VK_EXT_memory_budget's heap usage (dev/tools/stagingprobe): krell_canyon 2,203 MiB of
    /// textures + 17,509 x 32 KiB predicts 2,750 MiB, the heap took 2,819; creature_collector 1,119 MiB +
    /// 29,262 x 32 KiB predicts 2,033, the heap took 2,051.</summary>
    public const ulong PerTextureOverhead = 32 * 1024;

    /// <summary>The heap bytes the level's textures may take, each counted as its pixels plus
    /// <see cref="PerTextureOverhead"/> (AssetManager.PrepareTextures). What is not a texture is kept back
    /// as 512 MiB: the geometry (141 and 225 MiB on the two levels above), the scene, bloom and swapchain
    /// targets, NeoVeldrid's 64 MiB staging chunks and the unfilled tail of its last 256 MiB chunk.
    /// Without VK_EXT_memory_budget only the heap's size is known, and on a desktop part of it is already
    /// the compositor's and other applications': 85 % of it is counted.</summary>
    public ulong TextureAllowance
    {
        get
        {
            ulong usable = FromBudgetExtension ? BudgetBytes : BudgetBytes / 20 * 17;
            return usable > 704 * MiB ? usable - 512 * MiB : 192 * MiB;
        }
    }

    /// <summary>The device's budget, or null when the device is not Vulkan.</summary>
    public static unsafe GpuMemoryBudget? Query(GraphicsDevice graphicsDevice)
    {
        VulkanContext ctx;
        try { ctx = new VulkanContext(graphicsDevice); }
        catch (InvalidOperationException) { return null; }

        // Physical-device-level structures of a device extension may be queried whenever the device
        // supports the extension, enabled or not.
        bool hasBudget = false;
        uint count = 0;
        ctx.InstanceApi.vkEnumerateDeviceExtensionProperties(ctx.PhysicalDevice, null, &count, null);
        if (count > 0)
        {
            var extensions = new VkExtensionProperties[count];
            fixed (VkExtensionProperties* p = extensions)
            {
                ctx.InstanceApi.vkEnumerateDeviceExtensionProperties(ctx.PhysicalDevice, null, &count, p);
                for (uint i = 0; i < count && !hasBudget; i++)
                    hasBudget = new VkUtf8String(p[i].extensionName).ToString() == "VK_EXT_memory_budget";
            }
        }

        VkPhysicalDeviceMemoryBudgetPropertiesEXT budget = new();
        VkPhysicalDeviceMemoryProperties2 props2 = new();
        if (hasBudget) props2.pNext = &budget;
        ctx.InstanceApi.vkGetPhysicalDeviceMemoryProperties2(ctx.PhysicalDevice, &props2);
        VkPhysicalDeviceMemoryProperties props = props2.memoryProperties;

        int heap = -1;
        for (int i = 0; i < (int)props.memoryHeapCount; i++)
            if ((props.memoryHeaps[i].flags & VkMemoryHeapFlags.DeviceLocal) != 0 &&
                (heap < 0 || props.memoryHeaps[i].size > props.memoryHeaps[heap].size))
                heap = i;
        if (heap < 0) return null;

        ulong heapBytes = props.memoryHeaps[heap].size;
        ulong budgetBytes = hasBudget && budget.heapBudget[heap] > 0 ? Math.Min(budget.heapBudget[heap], heapBytes) : heapBytes;
        bool fromExtension = hasBudget && budget.heapBudget[heap] > 0;
        if (OverrideBudget is { } forced)
        {
            budgetBytes = forced;
            fromExtension = false;
        }
        ulong usage = hasBudget ? budget.heapUsage[heap] : 0;
        return new GpuMemoryBudget(graphicsDevice.DeviceName, heapBytes, budgetBytes, fromExtension, usage);
    }
}

/// <summary>What fitting a level's textures to the GPU's memory did (AssetManager.PrepareTextures).</summary>
/// <param name="FullBytes">Heap bytes every texture would take at its own resolution.</param>
/// <param name="FittedBytes">After the caps.</param>
/// <param name="MinDimension">The largest side every texture may keep; 0 when nothing was capped.</param>
/// <param name="MaxDimension">The largest side any reduced texture kept: MinDimension, or twice it for the
/// textures the remaining memory let keep one more level.</param>
/// <param name="ReducedCount">Textures that lost resolution.</param>
public readonly record struct TextureBudgetReport(GpuMemoryBudget Budget, ulong FullBytes, ulong FittedBytes,
    uint MinDimension, uint MaxDimension, int ReducedCount, int TextureCount)
{
    public bool Reduced => ReducedCount > 0;
}
