using System;
namespace Avalonia.X11.Vulkan;
unsafe partial class X11VulkanInterface
{
    delegate* unmanaged[Stdcall]<nint,global::Avalonia.X11.Vulkan.VkXlibSurfaceCreateInfoKHR*,nint,ulong*,int>_addr_vkCreateXlibSurfaceKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate int __wasmDummyvkCreateXlibSurfaceKHR(nint a0,global::Avalonia.X11.Vulkan.VkXlibSurfaceCreateInfoKHR* a1,nint a2,ulong* a3);
    public  partial int vkCreateXlibSurfaceKHR(nint @instance, ref global::Avalonia.X11.Vulkan.VkXlibSurfaceCreateInfoKHR @pCreateInfo, nint @pAllocator, out ulong @pSurface)
    {
        fixed(global::Avalonia.X11.Vulkan.VkXlibSurfaceCreateInfoKHR* @__p_pCreateInfo = &pCreateInfo)
        fixed(ulong* @__p_pSurface = &pSurface)
        return _addr_vkCreateXlibSurfaceKHR(@instance, @__p_pCreateInfo, @pAllocator, @__p_pSurface);
    }
    void Initialize(Func<string, IntPtr> getProcAddress)
    {
        var addr = IntPtr.Zero;
        // Initializing vkCreateXlibSurfaceKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateXlibSurfaceKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_vkCreateXlibSurfaceKHR");
        _addr_vkCreateXlibSurfaceKHR = (delegate* unmanaged[Stdcall]<nint,global::Avalonia.X11.Vulkan.VkXlibSurfaceCreateInfoKHR*,nint,ulong*,int>)addr;
}
}