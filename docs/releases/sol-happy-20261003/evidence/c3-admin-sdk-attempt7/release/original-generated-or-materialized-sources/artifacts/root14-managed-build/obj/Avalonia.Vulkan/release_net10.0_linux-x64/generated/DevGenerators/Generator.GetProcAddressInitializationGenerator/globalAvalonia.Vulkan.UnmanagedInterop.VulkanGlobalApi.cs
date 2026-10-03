using System;
namespace Avalonia.Vulkan.UnmanagedInterop;
unsafe partial class VulkanGlobalApi
{
    delegate* unmanaged[Stdcall]<uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkLayerProperties*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_EnumerateInstanceLayerProperties;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyEnumerateInstanceLayerProperties(uint* a0,global::Avalonia.Vulkan.UnmanagedInterop.VkLayerProperties* a1);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult EnumerateInstanceLayerProperties(ref uint @pPropertyCount, global::Avalonia.Vulkan.UnmanagedInterop.VkLayerProperties* @pProperties)
    {
        fixed(uint* @__p_pPropertyCount = &pPropertyCount)
        return _addr_EnumerateInstanceLayerProperties(@__p_pPropertyCount, @pProperties);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkInstanceCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkInstance*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_vkCreateInstance;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyvkCreateInstance(global::Avalonia.Vulkan.UnmanagedInterop.VkInstanceCreateInfo* a0,nint a1,global::Avalonia.Vulkan.UnmanagedInterop.VkInstance* a2);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult vkCreateInstance(ref global::Avalonia.Vulkan.UnmanagedInterop.VkInstanceCreateInfo @pCreateInfo, nint @pAllocator, out global::Avalonia.Vulkan.UnmanagedInterop.VkInstance @pInstance)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkInstanceCreateInfo* @__p_pCreateInfo = &pCreateInfo)
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkInstance* @__p_pInstance = &pInstance)
        return _addr_vkCreateInstance(@__p_pCreateInfo, @pAllocator, @__p_pInstance);
    }
    delegate* unmanaged[Stdcall]<nint,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkExtensionProperties*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_vkEnumerateInstanceExtensionProperties;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyvkEnumerateInstanceExtensionProperties(nint a0,uint* a1,global::Avalonia.Vulkan.UnmanagedInterop.VkExtensionProperties* a2);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult vkEnumerateInstanceExtensionProperties(nint @pLayerName, uint* @pPropertyCount, global::Avalonia.Vulkan.UnmanagedInterop.VkExtensionProperties* @pProperties)
    {
        return _addr_vkEnumerateInstanceExtensionProperties(@pLayerName, @pPropertyCount, @pProperties);
    }
    void Initialize(Func<string, IntPtr> getProcAddress)
    {
        var addr = IntPtr.Zero;
        // Initializing EnumerateInstanceLayerProperties
        addr = IntPtr.Zero;
        addr = getProcAddress("vkEnumerateInstanceLayerProperties");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_EnumerateInstanceLayerProperties");
        _addr_EnumerateInstanceLayerProperties = (delegate* unmanaged[Stdcall]<uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkLayerProperties*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing vkCreateInstance
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateInstance");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_vkCreateInstance");
        _addr_vkCreateInstance = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkInstanceCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkInstance*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing vkEnumerateInstanceExtensionProperties
        addr = IntPtr.Zero;
        addr = getProcAddress("vkEnumerateInstanceExtensionProperties");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_vkEnumerateInstanceExtensionProperties");
        _addr_vkEnumerateInstanceExtensionProperties = (delegate* unmanaged[Stdcall]<nint,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkExtensionProperties*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
}
}