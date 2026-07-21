using Veldrith;
using Vortice.Vulkan;

namespace ReLunacy.Utility;

/// <summary>
/// Bliss/Veldrith expose no cross-backend GPU memory query, so this reads used VRAM directly
/// through Vortice.Vulkan (the same binding library Veldrith's own Vulkan backend is built on,
/// already loaded in-process) via VK_EXT_memory_budget. Vulkan-only — D3D11/D3D12/Metal/OpenGL
/// would each need their own native query and aren't implemented; other backends always read 0.
/// </summary>
internal static unsafe class VramUsageQuery
{
    private const string ProcName = "vkGetPhysicalDeviceMemoryProperties2";
    private const string ProcNameKhr = "vkGetPhysicalDeviceMemoryProperties2KHR";
    private const string BudgetExtension = "VK_EXT_memory_budget";

    private static bool _resolved;
    private static delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties2*, void> _getMemoryProperties2;
    private static VkPhysicalDevice _physicalDevice;

    public static ulong GetUsedVramBytes(GraphicsDevice graphicsDevice)
    {
        if (graphicsDevice.BackendType != GraphicsBackend.Vulkan)
            return 0;

        if (!_resolved)
            Resolve(graphicsDevice);

        if (_getMemoryProperties2 == null)
            return 0;

        try
        {
            var budget = new VkPhysicalDeviceMemoryBudgetPropertiesEXT();
            var props2 = new VkPhysicalDeviceMemoryProperties2 { pNext = &budget };
            _getMemoryProperties2(_physicalDevice, &props2);

            ulong used = 0;
            for (int i = 0; i < props2.memoryProperties.memoryHeapCount; i++)
            {
                if ((props2.memoryProperties.memoryHeaps[i].flags & VkMemoryHeapFlags.DeviceLocal) != 0)
                    used += budget.heapUsage[i];
            }
            return used;
        }
        catch
        {
            // Physical-device query only — safe to keep retrying on transient failure, but
            // never let an optional stat readout take the editor down with it.
            _getMemoryProperties2 = null;
            return 0;
        }
    }

    private static void Resolve(GraphicsDevice graphicsDevice)
    {
        _resolved = true;

        if (!graphicsDevice.GetVulkanInfo(out var info))
            return;

        bool hasBudgetExtension = false;
        foreach (var ext in info.AvailableDeviceExtensions)
        {
            if (ext.Name == BudgetExtension)
            {
                hasBudgetExtension = true;
                break;
            }
        }
        if (!hasBudgetExtension)
            return;

        var instance = new VkInstance(info.Instance);
        var proc = Vulkan.vkGetInstanceProcAddr(instance, ProcName);
        if (proc.Value == null)
            proc = Vulkan.vkGetInstanceProcAddr(instance, ProcNameKhr);
        if (proc.Value == null)
            return;

        _physicalDevice = new VkPhysicalDevice(info.PhysicalDevice);
        _getMemoryProperties2 = (delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties2*, void>)proc.Value;
    }
}
