using System;
namespace Avalonia.Win32.Vulkan;
unsafe partial class Win32VulkanInterface
{
    delegate* unmanaged[Stdcall]<nint,global::Avalonia.Win32.Vulkan.VkWin32SurfaceCreateInfoKHR*,nint,ulong*,int>_addr_vkCreateWin32SurfaceKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate int __wasmDummyvkCreateWin32SurfaceKHR(nint a0,global::Avalonia.Win32.Vulkan.VkWin32SurfaceCreateInfoKHR* a1,nint a2,ulong* a3);
    public  partial int vkCreateWin32SurfaceKHR(nint @instance, ref global::Avalonia.Win32.Vulkan.VkWin32SurfaceCreateInfoKHR @pCreateInfo, nint @pAllocator, out ulong @pSurface)
    {
        fixed(global::Avalonia.Win32.Vulkan.VkWin32SurfaceCreateInfoKHR* @__p_pCreateInfo = &pCreateInfo)
        fixed(ulong* @__p_pSurface = &pSurface)
        return _addr_vkCreateWin32SurfaceKHR(@instance, @__p_pCreateInfo, @pAllocator, @__p_pSurface);
    }
    void Initialize(Func<string, IntPtr> getProcAddress)
    {
        var addr = IntPtr.Zero;
        // Initializing vkCreateWin32SurfaceKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateWin32SurfaceKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_vkCreateWin32SurfaceKHR");
        _addr_vkCreateWin32SurfaceKHR = (delegate* unmanaged[Stdcall]<nint,global::Avalonia.Win32.Vulkan.VkWin32SurfaceCreateInfoKHR*,nint,ulong*,int>)addr;
}
}