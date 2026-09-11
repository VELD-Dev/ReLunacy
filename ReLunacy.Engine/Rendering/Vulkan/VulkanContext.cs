using NeoVeldrid;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>Stage 0 of the from-scratch raw-Vulkan renderer (see Docs/NewRenderer.md).
///
/// Shares NeoVeldrid's Vulkan instance/device rather than creating its own, so the window,
/// swapchain, ImGui and present keep working while only the scene pass moves to raw Vulkan.
/// NeoVeldrid exposes the handles via <c>GraphicsDevice.GetVulkanInfo(out BackendInfoVulkan)</c> as
/// raw <see langword="nint"/>/<see langword="ulong"/> handles, wrapped here into the Vortice.Vulkan
/// types this project's raw-Vulkan code uses.</summary>
public sealed class VulkanContext
{
    public VkInstance Instance { get; }
    public VkPhysicalDevice PhysicalDevice { get; }
    public VkDevice Device { get; }
    public VkQueue GraphicsQueue { get; }
    public uint GraphicsQueueFamilyIndex { get; }

    /// <summary>Vortice's per-instance and per-device function tables, built against NeoVeldrid's
    /// shared handles (NeoVeldrid's own internal API table is on a private type we can't reach).</summary>
    public VkInstanceApi InstanceApi { get; }
    public VkDeviceApi DeviceApi { get; }

    /// <summary>Kept so the renderer can call <c>GetVkImage(NeoVeldrid.Texture)</c>, the only
    /// supported way to reach a NeoVeldrid-owned texture's raw VkImage.</summary>
    public BackendInfoVulkan BackendInfo { get; }

    /// <summary>Whether <see cref="Vortice.Vulkan.Vulkan.vkInitialize"/> has been called. Required
    /// before <see cref="Vortice.Vulkan.Vulkan.GetApi(VkInstance)"/>, which otherwise dereferences a
    /// null function pointer since NeoVeldrid (Silk.NET.Vulkan) never populates Vortice.Vulkan's own
    /// static state.</summary>
    private static bool s_vortriceInitialized;

    public VulkanContext(GraphicsDevice graphicsDevice)
    {
        if (!graphicsDevice.GetVulkanInfo(out BackendInfoVulkan info))
            throw new InvalidOperationException(
                "The new renderer requires the Vulkan backend; GraphicsDevice.GetVulkanInfo failed.");
        BackendInfo = info;

        if (!s_vortriceInitialized)
        {
            Vortice.Vulkan.Vulkan.vkInitialize().CheckResult("Vortice.Vulkan.Vulkan.vkInitialize failed");
            s_vortriceInitialized = true;
        }

        Instance = new VkInstance(info.Instance);
        PhysicalDevice = new VkPhysicalDevice(info.PhysicalDevice);
        Device = new VkDevice(info.Device);
        GraphicsQueue = new VkQueue(info.GraphicsQueue);
        GraphicsQueueFamilyIndex = info.GraphicsQueueFamilyIndex;

        InstanceApi = Vortice.Vulkan.Vulkan.GetApi(Instance);
        DeviceApi = new VkDeviceApi(InstanceApi, Device);
    }
}
