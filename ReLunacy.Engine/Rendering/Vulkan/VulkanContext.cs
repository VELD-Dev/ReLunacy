using Veldrith;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>Stage 0 of the from-scratch raw-Vulkan renderer (see Docs/NewRenderer.md).
///
/// The new renderer does NOT create its own Vulkan instance/device - during the staged migration it
/// SHARES Veldrith's, so the window, swapchain, ImGui and present all keep working while only the
/// scene pass moves to raw Vulkan. Veldrith exposes the handles via
/// <c>GraphicsDevice.GetVulkanInfo(out BackendInfoVulkan)</c> as raw <see langword="nint"/>s; this
/// wraps them into the Vortice.Vulkan handle types (pinned to Veldrith's exact Vortice version so
/// they are the same structs). From here later stages build their own command pool, pipelines,
/// descriptor sets and SIMULTANEOUS_USE command buffers to get record-once/replay - the thing
/// Veldrith's ONE_TIME_SUBMIT command lists cannot do.</summary>
public sealed class VulkanContext
{
    public VkInstance Instance { get; }
    public VkPhysicalDevice PhysicalDevice { get; }
    public VkDevice Device { get; }
    public VkQueue GraphicsQueue { get; }
    public uint GraphicsQueueFamilyIndex { get; }

    /// <summary>Vortice's per-instance and per-device function tables. Veldrith's own
    /// (VkGraphicsDevice.DeviceApi) is on a private type we can't reach, so we build our own bound to
    /// the SAME shared handles. The base loader is already initialised by Veldrith, so
    /// Vulkan.GetApi/new VkDeviceApi just resolve entry points against these handles.</summary>
    public VkInstanceApi InstanceApi { get; }
    public VkDeviceApi DeviceApi { get; }

    /// <summary>Kept so the renderer can call <c>GetVkImage(Veldrith.Texture)</c> - the only supported
    /// way to reach a Veldrith-owned texture's raw VkImage, which is how the scene renders into an
    /// image ImGui already displays.</summary>
    public BackendInfoVulkan BackendInfo { get; }

    public VulkanContext(GraphicsDevice graphicsDevice)
    {
        if (!graphicsDevice.GetVulkanInfo(out BackendInfoVulkan info))
            throw new InvalidOperationException(
                "The new renderer requires the Vulkan backend; GraphicsDevice.GetVulkanInfo failed.");
        BackendInfo = info;

        Instance = new VkInstance(info.Instance);
        PhysicalDevice = new VkPhysicalDevice(info.PhysicalDevice);
        Device = new VkDevice(info.Device);
        GraphicsQueue = new VkQueue(info.GraphicsQueue);
        GraphicsQueueFamilyIndex = info.GraphicsQueueFamilyIndex;

        InstanceApi = Vortice.Vulkan.Vulkan.GetApi(Instance);
        DeviceApi = new VkDeviceApi(InstanceApi, Device);
    }
}
