using NeoVeldrid;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>Stage 0 of the from-scratch raw-Vulkan renderer (see Docs/NewRenderer.md).
///
/// The new renderer does NOT create its own Vulkan instance/device - during the staged migration it
/// SHARES NeoVeldrid's, so the window, swapchain, ImGui and present all keep working while only the
/// scene pass moves to raw Vulkan. NeoVeldrid exposes the handles via
/// <c>GraphicsDevice.GetVulkanInfo(out BackendInfoVulkan)</c> as raw <see langword="nint"/>s/
/// <see langword="ulong"/>s - a binding-agnostic escape hatch, not tied to whatever Vulkan binding
/// NeoVeldrid itself uses internally (Silk.NET.Vulkan) - so this wraps them into the Vortice.Vulkan
/// handle types this project's own raw-Vulkan code uses, with no version-matching needed either way.
/// From here later stages build their own command pool, pipelines, descriptor sets and
/// SIMULTANEOUS_USE command buffers to get record-once/replay - the thing NeoVeldrid's ONE_TIME_SUBMIT
/// command lists cannot do.</summary>
public sealed class VulkanContext
{
    public VkInstance Instance { get; }
    public VkPhysicalDevice PhysicalDevice { get; }
    public VkDevice Device { get; }
    public VkQueue GraphicsQueue { get; }
    public uint GraphicsQueueFamilyIndex { get; }

    /// <summary>Vortice's per-instance and per-device function tables. NeoVeldrid's own internal API
    /// table is on a private type we can't reach, so we build our own bound to the SAME shared
    /// handles.</summary>
    public VkInstanceApi InstanceApi { get; }
    public VkDeviceApi DeviceApi { get; }

    /// <summary>Kept so the renderer can call <c>GetVkImage(NeoVeldrid.Texture)</c> - the only
    /// supported way to reach a NeoVeldrid-owned texture's raw VkImage, which is how the scene
    /// renders into an image ImGui already displays.</summary>
    public BackendInfoVulkan BackendInfo { get; }

    /// <summary>Vortice.Vulkan keeps its own static <c>vkGetInstanceProcAddr</c> function pointer,
    /// populated only by an explicit <see cref="Vortice.Vulkan.Vulkan.vkInitialize"/> call (which loads
    /// libvulkan itself and resolves it) - <see cref="Vortice.Vulkan.Vulkan.GetApi(VkInstance)"/> calls
    /// through that pointer to build the instance/device tables, so it must run first. Under the old
    /// Veldrith fork this happened to already be populated (Veldrith used Vortice.Vulkan internally too,
    /// as a side effect of building its own device), which is why this was never called explicitly here.
    /// NeoVeldrid uses Silk.NET.Vulkan instead and never touches Vortice.Vulkan's static state, so
    /// without this the pointer stays null and GetApi segfaults dereferencing it - confirmed by
    /// decompiling Vortice.Vulkan.dll and matching the crash site to right here. vkInitialize() just
    /// takes another handle to the already-loaded libvulkan.so.1/vulkan-1.dll, so this is safe to call
    /// even though NeoVeldrid loaded it first.</summary>
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
