using System;
namespace Avalonia.Vulkan;
unsafe partial class VulkanInstanceApi
{
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkInstance,global::Avalonia.Vulkan.UnmanagedInterop.VkDebugUtilsMessengerCreateInfoEXT*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkDebugUtilsMessengerEXT*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_CreateDebugUtilsMessengerEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyCreateDebugUtilsMessengerEXT(global::Avalonia.Vulkan.UnmanagedInterop.VkInstance a0,global::Avalonia.Vulkan.UnmanagedInterop.VkDebugUtilsMessengerCreateInfoEXT* a1,nint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkDebugUtilsMessengerEXT* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult CreateDebugUtilsMessengerEXT(global::Avalonia.Vulkan.UnmanagedInterop.VkInstance @instance, ref global::Avalonia.Vulkan.UnmanagedInterop.VkDebugUtilsMessengerCreateInfoEXT @pCreateInfo, nint @pAllocator, out global::Avalonia.Vulkan.UnmanagedInterop.VkDebugUtilsMessengerEXT @pMessenger)
    {
        if (_addr_CreateDebugUtilsMessengerEXT == null) throw new System.EntryPointNotFoundException("CreateDebugUtilsMessengerEXT");
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkDebugUtilsMessengerCreateInfoEXT* @__p_pCreateInfo = &pCreateInfo)
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkDebugUtilsMessengerEXT* @__p_pMessenger = &pMessenger)
        return _addr_CreateDebugUtilsMessengerEXT(@instance, @__p_pCreateInfo, @pAllocator, @__p_pMessenger);
    }
    public  bool IsCreateDebugUtilsMessengerEXTAvailable => _addr_CreateDebugUtilsMessengerEXT != null;
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkInstance,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_EnumeratePhysicalDevices;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyEnumeratePhysicalDevices(global::Avalonia.Vulkan.UnmanagedInterop.VkInstance a0,uint* a1,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice* a2);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult EnumeratePhysicalDevices(global::Avalonia.Vulkan.UnmanagedInterop.VkInstance @instance, ref uint @pPhysicalDeviceCount, global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice* @pPhysicalDevices)
    {
        fixed(uint* @__p_pPhysicalDeviceCount = &pPhysicalDeviceCount)
        return _addr_EnumeratePhysicalDevices(@instance, @__p_pPhysicalDeviceCount, @pPhysicalDevices);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceProperties*,void>_addr_GetPhysicalDeviceProperties;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyGetPhysicalDeviceProperties(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceProperties* a1);
    public  partial void GetPhysicalDeviceProperties(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice @physicalDevice, out global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceProperties @pProperties)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceProperties* @__p_pProperties = &pProperties)
        _addr_GetPhysicalDeviceProperties(@physicalDevice, @__p_pProperties);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,byte*,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkExtensionProperties*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_EnumerateDeviceExtensionProperties;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyEnumerateDeviceExtensionProperties(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice a0,byte* a1,uint* a2,global::Avalonia.Vulkan.UnmanagedInterop.VkExtensionProperties* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult EnumerateDeviceExtensionProperties(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice @physicalDevice, byte* @pLayerName, ref uint @pPropertyCount, global::Avalonia.Vulkan.UnmanagedInterop.VkExtensionProperties* @pProperties)
    {
        fixed(uint* @__p_pPropertyCount = &pPropertyCount)
        return _addr_EnumerateDeviceExtensionProperties(@physicalDevice, @pLayerName, @__p_pPropertyCount, @pProperties);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_GetPhysicalDeviceSurfaceSupportKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyGetPhysicalDeviceSurfaceSupportKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice a0,uint a1,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR a2,uint* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult GetPhysicalDeviceSurfaceSupportKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice @physicalDevice, uint @queueFamilyIndex, global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR @surface, out uint @pSupported)
    {
        fixed(uint* @__p_pSupported = &pSupported)
        return _addr_GetPhysicalDeviceSurfaceSupportKHR(@physicalDevice, @queueFamilyIndex, @surface, @__p_pSupported);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkQueueFamilyProperties*,void>_addr_GetPhysicalDeviceQueueFamilyProperties;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyGetPhysicalDeviceQueueFamilyProperties(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice a0,uint* a1,global::Avalonia.Vulkan.UnmanagedInterop.VkQueueFamilyProperties* a2);
    public  partial void GetPhysicalDeviceQueueFamilyProperties(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice @physicalDevice, ref uint @pQueueFamilyPropertyCount, global::Avalonia.Vulkan.UnmanagedInterop.VkQueueFamilyProperties* @pQueueFamilyProperties)
    {
        fixed(uint* @__p_pQueueFamilyPropertyCount = &pQueueFamilyPropertyCount)
        _addr_GetPhysicalDeviceQueueFamilyProperties(@physicalDevice, @__p_pQueueFamilyPropertyCount, @pQueueFamilyProperties);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkDevice*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_CreateDevice;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyCreateDevice(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceCreateInfo* a1,nint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkDevice* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult CreateDevice(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice @physicalDevice, ref global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceCreateInfo @pCreateInfo, nint @pAllocator, out global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @pDevice)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceCreateInfo* @__p_pCreateInfo = &pCreateInfo)
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice* @__p_pDevice = &pDevice)
        return _addr_CreateDevice(@physicalDevice, @__p_pCreateInfo, @pAllocator, @__p_pDevice);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_DestroyDevice;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyDestroyDevice(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,nint a1);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult DestroyDevice(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, nint @pAllocator)
    {
        return _addr_DestroyDevice(@device, @pAllocator);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,uint,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkQueue*,void>_addr_GetDeviceQueue;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyGetDeviceQueue(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,uint a1,uint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkQueue* a3);
    public  partial void GetDeviceQueue(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, uint @queueFamilyIndex, uint @queueIndex, out global::Avalonia.Vulkan.UnmanagedInterop.VkQueue @pQueue)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkQueue* @__p_pQueue = &pQueue)
        _addr_GetDeviceQueue(@device, @queueFamilyIndex, @queueIndex, @__p_pQueue);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,nint,nint>_addr_GetDeviceProcAddr;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint __wasmDummyGetDeviceProcAddr(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,nint a1);
    public  partial nint GetDeviceProcAddr(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, nint @pName)
    {
        return _addr_GetDeviceProcAddr(@device, @pName);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkInstance,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR,nint,void>_addr_DestroySurfaceKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyDestroySurfaceKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkInstance a0,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR a1,nint a2);
    public  partial void DestroySurfaceKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkInstance @instance, global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR @surface, nint @pAllocator)
    {
        _addr_DestroySurfaceKHR(@instance, @surface, @pAllocator);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceFormatKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_GetPhysicalDeviceSurfaceFormatsKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyGetPhysicalDeviceSurfaceFormatsKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR a1,uint* a2,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceFormatKHR* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult GetPhysicalDeviceSurfaceFormatsKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice @physicalDevice, global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR @surface, ref uint @pSurfaceFormatCount, global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceFormatKHR* @pSurfaceFormats)
    {
        fixed(uint* @__p_pSurfaceFormatCount = &pSurfaceFormatCount)
        return _addr_GetPhysicalDeviceSurfaceFormatsKHR(@physicalDevice, @surface, @__p_pSurfaceFormatCount, @pSurfaceFormats);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceMemoryProperties*,void>_addr_GetPhysicalDeviceMemoryProperties;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyGetPhysicalDeviceMemoryProperties(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceMemoryProperties* a1);
    public  partial void GetPhysicalDeviceMemoryProperties(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice @physicalDevice, out global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceMemoryProperties @pMemoryProperties)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceMemoryProperties* @__p_pMemoryProperties = &pMemoryProperties)
        _addr_GetPhysicalDeviceMemoryProperties(@physicalDevice, @__p_pMemoryProperties);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceCapabilitiesKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_GetPhysicalDeviceSurfaceCapabilitiesKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyGetPhysicalDeviceSurfaceCapabilitiesKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR a1,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceCapabilitiesKHR* a2);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult GetPhysicalDeviceSurfaceCapabilitiesKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice @physicalDevice, global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR @surface, out global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceCapabilitiesKHR @pSurfaceCapabilities)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceCapabilitiesKHR* @__p_pSurfaceCapabilities = &pSurfaceCapabilities)
        return _addr_GetPhysicalDeviceSurfaceCapabilitiesKHR(@physicalDevice, @surface, @__p_pSurfaceCapabilities);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkPresentModeKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_GetPhysicalDeviceSurfacePresentModesKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyGetPhysicalDeviceSurfacePresentModesKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR a1,uint* a2,global::Avalonia.Vulkan.UnmanagedInterop.VkPresentModeKHR* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult GetPhysicalDeviceSurfacePresentModesKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice @physicalDevice, global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR @surface, ref uint @pPresentModeCount, global::Avalonia.Vulkan.UnmanagedInterop.VkPresentModeKHR* @pPresentModes)
    {
        fixed(uint* @__p_pPresentModeCount = &pPresentModeCount)
        return _addr_GetPhysicalDeviceSurfacePresentModesKHR(@physicalDevice, @surface, @__p_pPresentModeCount, @pPresentModes);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceProperties2*,void>_addr_GetPhysicalDeviceProperties2;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyGetPhysicalDeviceProperties2(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceProperties2* a1);
    public  partial void GetPhysicalDeviceProperties2(global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice @physicalDevice, global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceProperties2* @pProperties)
    {
        if (_addr_GetPhysicalDeviceProperties2 == null) throw new System.EntryPointNotFoundException("GetPhysicalDeviceProperties2");
        _addr_GetPhysicalDeviceProperties2(@physicalDevice, @pProperties);
    }
    public  bool IsGetPhysicalDeviceProperties2Available => _addr_GetPhysicalDeviceProperties2 != null;
    void Initialize(Func<string, IntPtr> getProcAddress)
    {
        var addr = IntPtr.Zero;
        // Initializing CreateDebugUtilsMessengerEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateDebugUtilsMessengerEXT");
        _addr_CreateDebugUtilsMessengerEXT = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkInstance,global::Avalonia.Vulkan.UnmanagedInterop.VkDebugUtilsMessengerCreateInfoEXT*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkDebugUtilsMessengerEXT*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing EnumeratePhysicalDevices
        addr = IntPtr.Zero;
        addr = getProcAddress("vkEnumeratePhysicalDevices");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_EnumeratePhysicalDevices");
        _addr_EnumeratePhysicalDevices = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkInstance,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing GetPhysicalDeviceProperties
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetPhysicalDeviceProperties");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetPhysicalDeviceProperties");
        _addr_GetPhysicalDeviceProperties = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceProperties*,void>)addr;
        // Initializing EnumerateDeviceExtensionProperties
        addr = IntPtr.Zero;
        addr = getProcAddress("vkEnumerateDeviceExtensionProperties");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_EnumerateDeviceExtensionProperties");
        _addr_EnumerateDeviceExtensionProperties = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,byte*,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkExtensionProperties*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing GetPhysicalDeviceSurfaceSupportKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetPhysicalDeviceSurfaceSupportKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetPhysicalDeviceSurfaceSupportKHR");
        _addr_GetPhysicalDeviceSurfaceSupportKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing GetPhysicalDeviceQueueFamilyProperties
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetPhysicalDeviceQueueFamilyProperties");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetPhysicalDeviceQueueFamilyProperties");
        _addr_GetPhysicalDeviceQueueFamilyProperties = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkQueueFamilyProperties*,void>)addr;
        // Initializing CreateDevice
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateDevice");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreateDevice");
        _addr_CreateDevice = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkDevice*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing DestroyDevice
        addr = IntPtr.Zero;
        addr = getProcAddress("vkDestroyDevice");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DestroyDevice");
        _addr_DestroyDevice = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing GetDeviceQueue
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetDeviceQueue");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetDeviceQueue");
        _addr_GetDeviceQueue = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,uint,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkQueue*,void>)addr;
        // Initializing GetDeviceProcAddr
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetDeviceProcAddr");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetDeviceProcAddr");
        _addr_GetDeviceProcAddr = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,nint,nint>)addr;
        // Initializing DestroySurfaceKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkDestroySurfaceKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DestroySurfaceKHR");
        _addr_DestroySurfaceKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkInstance,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR,nint,void>)addr;
        // Initializing GetPhysicalDeviceSurfaceFormatsKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetPhysicalDeviceSurfaceFormatsKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetPhysicalDeviceSurfaceFormatsKHR");
        _addr_GetPhysicalDeviceSurfaceFormatsKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceFormatKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing GetPhysicalDeviceMemoryProperties
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetPhysicalDeviceMemoryProperties");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetPhysicalDeviceMemoryProperties");
        _addr_GetPhysicalDeviceMemoryProperties = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceMemoryProperties*,void>)addr;
        // Initializing GetPhysicalDeviceSurfaceCapabilitiesKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetPhysicalDeviceSurfaceCapabilitiesKHR");
        _addr_GetPhysicalDeviceSurfaceCapabilitiesKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceCapabilitiesKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing GetPhysicalDeviceSurfacePresentModesKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetPhysicalDeviceSurfacePresentModesKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetPhysicalDeviceSurfacePresentModesKHR");
        _addr_GetPhysicalDeviceSurfacePresentModesKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSurfaceKHR,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkPresentModeKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing GetPhysicalDeviceProperties2
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetPhysicalDeviceProperties2");
        _addr_GetPhysicalDeviceProperties2 = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkPhysicalDeviceProperties2*,void>)addr;
}
}