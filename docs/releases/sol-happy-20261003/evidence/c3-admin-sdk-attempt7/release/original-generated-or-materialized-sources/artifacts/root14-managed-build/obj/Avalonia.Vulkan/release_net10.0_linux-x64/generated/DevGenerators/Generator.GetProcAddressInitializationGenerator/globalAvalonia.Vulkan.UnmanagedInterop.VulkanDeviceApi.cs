using System;
namespace Avalonia.Vulkan.UnmanagedInterop;
unsafe partial class VulkanDeviceApi
{
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkFenceCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkFence*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_CreateFence;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyCreateFence(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkFenceCreateInfo* a1,nint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkFence* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult CreateFence(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, ref global::Avalonia.Vulkan.UnmanagedInterop.VkFenceCreateInfo @pCreateInfo, nint @pAllocator, out global::Avalonia.Vulkan.UnmanagedInterop.VkFence @pFence)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkFenceCreateInfo* @__p_pCreateInfo = &pCreateInfo)
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkFence* @__p_pFence = &pFence)
        return _addr_CreateFence(@device, @__p_pCreateInfo, @pAllocator, @__p_pFence);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkFence,nint,void>_addr_DestroyFence;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyDestroyFence(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkFence a1,nint a2);
    public  partial void DestroyFence(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkFence @fence, nint @pAllocator)
    {
        _addr_DestroyFence(@device, @fence, @pAllocator);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPoolCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_CreateCommandPool;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyCreateCommandPool(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPoolCreateInfo* a1,nint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult CreateCommandPool(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, ref global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPoolCreateInfo @pCreateInfo, nint @pAllocator, out global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool @pCommandPool)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPoolCreateInfo* @__p_pCreateInfo = &pCreateInfo)
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool* @__p_pCommandPool = &pCommandPool)
        return _addr_CreateCommandPool(@device, @__p_pCreateInfo, @pAllocator, @__p_pCommandPool);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool,nint,void>_addr_DestroyCommandPool;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyDestroyCommandPool(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool a1,nint a2);
    public  partial void DestroyCommandPool(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool @pool, nint @pAllocator)
    {
        _addr_DestroyCommandPool(@device, @pool, @pAllocator);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBufferAllocateInfo*,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_AllocateCommandBuffers;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyAllocateCommandBuffers(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBufferAllocateInfo* a1,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer* a2);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult AllocateCommandBuffers(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, ref global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBufferAllocateInfo @pAllocateInfo, global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer* @pCommandBuffers)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBufferAllocateInfo* @__p_pAllocateInfo = &pAllocateInfo)
        return _addr_AllocateCommandBuffers(@device, @__p_pAllocateInfo, @pCommandBuffers);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer*,void>_addr_FreeCommandBuffers;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyFreeCommandBuffers(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool a1,uint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer* a3);
    public  partial void FreeCommandBuffers(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool @commandPool, uint @commandBufferCount, global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer* @pCommandBuffers)
    {
        _addr_FreeCommandBuffers(@device, @commandPool, @commandBufferCount, @pCommandBuffers);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkFence*,uint,ulong,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_WaitForFences;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyWaitForFences(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,uint a1,global::Avalonia.Vulkan.UnmanagedInterop.VkFence* a2,uint a3,ulong a4);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult WaitForFences(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, uint @fenceCount, global::Avalonia.Vulkan.UnmanagedInterop.VkFence* @pFences, uint @waitAll, ulong @timeout)
    {
        return _addr_WaitForFences(@device, @fenceCount, @pFences, @waitAll, @timeout);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkFence,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_GetFenceStatus;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyGetFenceStatus(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkFence a1);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult GetFenceStatus(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkFence @fence)
    {
        return _addr_GetFenceStatus(@device, @fence);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBufferBeginInfo*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_BeginCommandBuffer;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyBeginCommandBuffer(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer a0,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBufferBeginInfo* a1);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult BeginCommandBuffer(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer @commandBuffer, ref global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBufferBeginInfo @pBeginInfo)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBufferBeginInfo* @__p_pBeginInfo = &pBeginInfo)
        return _addr_BeginCommandBuffer(@commandBuffer, @__p_pBeginInfo);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_EndCommandBuffer;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyEndCommandBuffer(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer a0);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult EndCommandBuffer(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer @commandBuffer)
    {
        return _addr_EndCommandBuffer(@commandBuffer);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphoreCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_CreateSemaphore;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyCreateSemaphore(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphoreCreateInfo* a1,nint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult CreateSemaphore(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, ref global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphoreCreateInfo @pCreateInfo, nint @pAllocator, out global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore @pSemaphore)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphoreCreateInfo* @__p_pCreateInfo = &pCreateInfo)
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore* @__p_pSemaphore = &pSemaphore)
        return _addr_CreateSemaphore(@device, @__p_pCreateInfo, @pAllocator, @__p_pSemaphore);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore,nint,void>_addr_DestroySemaphore;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyDestroySemaphore(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore a1,nint a2);
    public  partial void DestroySemaphore(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore @semaphore, nint @pAllocator)
    {
        _addr_DestroySemaphore(@device, @semaphore, @pAllocator);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkFence*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_ResetFences;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyResetFences(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,uint a1,global::Avalonia.Vulkan.UnmanagedInterop.VkFence* a2);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult ResetFences(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, uint @fenceCount, global::Avalonia.Vulkan.UnmanagedInterop.VkFence* @pFences)
    {
        return _addr_ResetFences(@device, @fenceCount, @pFences);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkQueue,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkSubmitInfo*,global::Avalonia.Vulkan.UnmanagedInterop.VkFence,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_QueueSubmit;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyQueueSubmit(global::Avalonia.Vulkan.UnmanagedInterop.VkQueue a0,uint a1,global::Avalonia.Vulkan.UnmanagedInterop.VkSubmitInfo* a2,global::Avalonia.Vulkan.UnmanagedInterop.VkFence a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult QueueSubmit(global::Avalonia.Vulkan.UnmanagedInterop.VkQueue @queue, uint @submitCount, global::Avalonia.Vulkan.UnmanagedInterop.VkSubmitInfo* @pSubmits, global::Avalonia.Vulkan.UnmanagedInterop.VkFence @fence)
    {
        return _addr_QueueSubmit(@queue, @submitCount, @pSubmits, @fence);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImageCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkImage*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_CreateImage;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyCreateImage(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkImageCreateInfo* a1,nint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkImage* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult CreateImage(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, ref global::Avalonia.Vulkan.UnmanagedInterop.VkImageCreateInfo @pCreateInfo, nint @pAllocator, out global::Avalonia.Vulkan.UnmanagedInterop.VkImage @pImage)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkImageCreateInfo* @__p_pCreateInfo = &pCreateInfo)
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkImage* @__p_pImage = &pImage)
        return _addr_CreateImage(@device, @__p_pCreateInfo, @pAllocator, @__p_pImage);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImage,nint,void>_addr_DestroyImage;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyDestroyImage(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkImage a1,nint a2);
    public  partial void DestroyImage(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkImage @image, nint @pAllocator)
    {
        _addr_DestroyImage(@device, @image, @pAllocator);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImage,global::Avalonia.Vulkan.UnmanagedInterop.VkMemoryRequirements*,void>_addr_GetImageMemoryRequirements;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyGetImageMemoryRequirements(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkImage a1,global::Avalonia.Vulkan.UnmanagedInterop.VkMemoryRequirements* a2);
    public  partial void GetImageMemoryRequirements(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkImage @image, out global::Avalonia.Vulkan.UnmanagedInterop.VkMemoryRequirements @pMemoryRequirements)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkMemoryRequirements* @__p_pMemoryRequirements = &pMemoryRequirements)
        _addr_GetImageMemoryRequirements(@device, @image, @__p_pMemoryRequirements);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkMemoryAllocateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_AllocateMemory;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyAllocateMemory(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkMemoryAllocateInfo* a1,nint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult AllocateMemory(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, ref global::Avalonia.Vulkan.UnmanagedInterop.VkMemoryAllocateInfo @pAllocateInfo, nint @pAllocator, out global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory @pMemory)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkMemoryAllocateInfo* @__p_pAllocateInfo = &pAllocateInfo)
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory* @__p_pMemory = &pMemory)
        return _addr_AllocateMemory(@device, @__p_pAllocateInfo, @pAllocator, @__p_pMemory);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory,nint,void>_addr_FreeMemory;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyFreeMemory(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory a1,nint a2);
    public  partial void FreeMemory(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory @memory, nint @pAllocator)
    {
        _addr_FreeMemory(@device, @memory, @pAllocator);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImage,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory,ulong,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_BindImageMemory;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyBindImageMemory(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkImage a1,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory a2,ulong a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult BindImageMemory(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkImage @image, global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory @memory, ulong @memoryOffset)
    {
        return _addr_BindImageMemory(@device, @image, @memory, @memoryOffset);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImageViewCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkImageView*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_CreateImageView;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyCreateImageView(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkImageViewCreateInfo* a1,nint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkImageView* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult CreateImageView(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, ref global::Avalonia.Vulkan.UnmanagedInterop.VkImageViewCreateInfo @pCreateInfo, nint @pAllocator, out global::Avalonia.Vulkan.UnmanagedInterop.VkImageView @pView)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkImageViewCreateInfo* @__p_pCreateInfo = &pCreateInfo)
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkImageView* @__p_pView = &pView)
        return _addr_CreateImageView(@device, @__p_pCreateInfo, @pAllocator, @__p_pView);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImageView,nint,void>_addr_DestroyImageView;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyDestroyImageView(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkImageView a1,nint a2);
    public  partial void DestroyImageView(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkImageView @imageView, nint @pAllocator)
    {
        _addr_DestroyImageView(@device, @imageView, @pAllocator);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer,global::Avalonia.Vulkan.UnmanagedInterop.VkPipelineStageFlags,global::Avalonia.Vulkan.UnmanagedInterop.VkPipelineStageFlags,global::Avalonia.Vulkan.UnmanagedInterop.VkDependencyFlags,uint,nint,uint,nint,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkImageMemoryBarrier*,void>_addr_CmdPipelineBarrier;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyCmdPipelineBarrier(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer a0,global::Avalonia.Vulkan.UnmanagedInterop.VkPipelineStageFlags a1,global::Avalonia.Vulkan.UnmanagedInterop.VkPipelineStageFlags a2,global::Avalonia.Vulkan.UnmanagedInterop.VkDependencyFlags a3,uint a4,nint a5,uint a6,nint a7,uint a8,global::Avalonia.Vulkan.UnmanagedInterop.VkImageMemoryBarrier* a9);
    public  partial void CmdPipelineBarrier(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer @commandBuffer, global::Avalonia.Vulkan.UnmanagedInterop.VkPipelineStageFlags @srcStageMask, global::Avalonia.Vulkan.UnmanagedInterop.VkPipelineStageFlags @dstStageMask, global::Avalonia.Vulkan.UnmanagedInterop.VkDependencyFlags @dependencyFlags, uint @memoryBarrierCount, nint @pMemoryBarriers, uint @bufferMemoryBarrierCount, nint @pBufferMemoryBarriers, uint @imageMemoryBarrierCount, global::Avalonia.Vulkan.UnmanagedInterop.VkImageMemoryBarrier* @pImageMemoryBarriers)
    {
        _addr_CmdPipelineBarrier(@commandBuffer, @srcStageMask, @dstStageMask, @dependencyFlags, @memoryBarrierCount, @pMemoryBarriers, @bufferMemoryBarrierCount, @pBufferMemoryBarriers, @imageMemoryBarrierCount, @pImageMemoryBarriers);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainCreateInfoKHR*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_CreateSwapchainKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyCreateSwapchainKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainCreateInfoKHR* a1,nint a2,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult CreateSwapchainKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, ref global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainCreateInfoKHR @pCreateInfo, nint @pAllocator, out global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR @pSwapchain)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainCreateInfoKHR* @__p_pCreateInfo = &pCreateInfo)
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR* @__p_pSwapchain = &pSwapchain)
        return _addr_CreateSwapchainKHR(@device, @__p_pCreateInfo, @pAllocator, @__p_pSwapchain);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR,nint,void>_addr_DestroySwapchainKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyDestroySwapchainKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR a1,nint a2);
    public  partial void DestroySwapchainKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR @swapchain, nint @pAllocator)
    {
        _addr_DestroySwapchainKHR(@device, @swapchain, @pAllocator);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkImage*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_GetSwapchainImagesKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyGetSwapchainImagesKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR a1,uint* a2,global::Avalonia.Vulkan.UnmanagedInterop.VkImage* a3);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult GetSwapchainImagesKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR @swapchain, ref uint @pSwapchainImageCount, global::Avalonia.Vulkan.UnmanagedInterop.VkImage* @pSwapchainImages)
    {
        fixed(uint* @__p_pSwapchainImageCount = &pSwapchainImageCount)
        return _addr_GetSwapchainImagesKHR(@device, @swapchain, @__p_pSwapchainImageCount, @pSwapchainImages);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_DeviceWaitIdle;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyDeviceWaitIdle(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult DeviceWaitIdle(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device)
    {
        return _addr_DeviceWaitIdle(@device);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkQueue,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_QueueWaitIdle;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyQueueWaitIdle(global::Avalonia.Vulkan.UnmanagedInterop.VkQueue a0);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult QueueWaitIdle(global::Avalonia.Vulkan.UnmanagedInterop.VkQueue @queue)
    {
        return _addr_QueueWaitIdle(@queue);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR,ulong,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore,global::Avalonia.Vulkan.UnmanagedInterop.VkFence,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_AcquireNextImageKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyAcquireNextImageKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR a1,ulong a2,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore a3,global::Avalonia.Vulkan.UnmanagedInterop.VkFence a4,uint* a5);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult AcquireNextImageKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR @swapchain, ulong @timeout, global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore @semaphore, global::Avalonia.Vulkan.UnmanagedInterop.VkFence @fence, out uint @pImageIndex)
    {
        fixed(uint* @__p_pImageIndex = &pImageIndex)
        return _addr_AcquireNextImageKHR(@device, @swapchain, @timeout, @semaphore, @fence, @__p_pImageIndex);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer,global::Avalonia.Vulkan.UnmanagedInterop.VkImage,global::Avalonia.Vulkan.UnmanagedInterop.VkImageLayout,global::Avalonia.Vulkan.UnmanagedInterop.VkImage,global::Avalonia.Vulkan.UnmanagedInterop.VkImageLayout,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkImageBlit*,global::Avalonia.Vulkan.UnmanagedInterop.VkFilter,void>_addr_CmdBlitImage;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyCmdBlitImage(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer a0,global::Avalonia.Vulkan.UnmanagedInterop.VkImage a1,global::Avalonia.Vulkan.UnmanagedInterop.VkImageLayout a2,global::Avalonia.Vulkan.UnmanagedInterop.VkImage a3,global::Avalonia.Vulkan.UnmanagedInterop.VkImageLayout a4,uint a5,global::Avalonia.Vulkan.UnmanagedInterop.VkImageBlit* a6,global::Avalonia.Vulkan.UnmanagedInterop.VkFilter a7);
    public  partial void CmdBlitImage(global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer @commandBuffer, global::Avalonia.Vulkan.UnmanagedInterop.VkImage @srcImage, global::Avalonia.Vulkan.UnmanagedInterop.VkImageLayout @srcImageLayout, global::Avalonia.Vulkan.UnmanagedInterop.VkImage @dstImage, global::Avalonia.Vulkan.UnmanagedInterop.VkImageLayout @dstImageLayout, uint @regionCount, global::Avalonia.Vulkan.UnmanagedInterop.VkImageBlit* @pRegions, global::Avalonia.Vulkan.UnmanagedInterop.VkFilter @filter)
    {
        _addr_CmdBlitImage(@commandBuffer, @srcImage, @srcImageLayout, @dstImage, @dstImageLayout, @regionCount, @pRegions, @filter);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkQueue,global::Avalonia.Vulkan.UnmanagedInterop.VkPresentInfoKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_vkQueuePresentKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyvkQueuePresentKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkQueue a0,global::Avalonia.Vulkan.UnmanagedInterop.VkPresentInfoKHR* a1);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult vkQueuePresentKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkQueue @queue, ref global::Avalonia.Vulkan.UnmanagedInterop.VkPresentInfoKHR @pPresentInfo)
    {
        fixed(global::Avalonia.Vulkan.UnmanagedInterop.VkPresentInfoKHR* @__p_pPresentInfo = &pPresentInfo)
        return _addr_vkQueuePresentKHR(@queue, @__p_pPresentInfo);
    }
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImportSemaphoreFdInfoKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_ImportSemaphoreFdKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyImportSemaphoreFdKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkImportSemaphoreFdInfoKHR* a1);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult ImportSemaphoreFdKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkImportSemaphoreFdInfoKHR* @pImportSemaphoreFdInfo)
    {
        if (_addr_ImportSemaphoreFdKHR == null) throw new System.EntryPointNotFoundException("ImportSemaphoreFdKHR");
        return _addr_ImportSemaphoreFdKHR(@device, @pImportSemaphoreFdInfo);
    }
    public  bool IsImportSemaphoreFdKHRAvailable => _addr_ImportSemaphoreFdKHR != null;
    delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImportSemaphoreWin32HandleInfoKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>_addr_ImportSemaphoreWin32HandleKHR;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.Vulkan.UnmanagedInterop.VkResult __wasmDummyImportSemaphoreWin32HandleKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice a0,global::Avalonia.Vulkan.UnmanagedInterop.VkImportSemaphoreWin32HandleInfoKHR* a1);
    public  partial global::Avalonia.Vulkan.UnmanagedInterop.VkResult ImportSemaphoreWin32HandleKHR(global::Avalonia.Vulkan.UnmanagedInterop.VkDevice @device, global::Avalonia.Vulkan.UnmanagedInterop.VkImportSemaphoreWin32HandleInfoKHR* @pImportSemaphoreWin32HandleInfo)
    {
        if (_addr_ImportSemaphoreWin32HandleKHR == null) throw new System.EntryPointNotFoundException("ImportSemaphoreWin32HandleKHR");
        return _addr_ImportSemaphoreWin32HandleKHR(@device, @pImportSemaphoreWin32HandleInfo);
    }
    public  bool IsImportSemaphoreWin32HandleKHRAvailable => _addr_ImportSemaphoreWin32HandleKHR != null;
    void Initialize(Func<string, IntPtr> getProcAddress)
    {
        var addr = IntPtr.Zero;
        // Initializing CreateFence
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateFence");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreateFence");
        _addr_CreateFence = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkFenceCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkFence*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing DestroyFence
        addr = IntPtr.Zero;
        addr = getProcAddress("vkDestroyFence");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DestroyFence");
        _addr_DestroyFence = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkFence,nint,void>)addr;
        // Initializing CreateCommandPool
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateCommandPool");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreateCommandPool");
        _addr_CreateCommandPool = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPoolCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing DestroyCommandPool
        addr = IntPtr.Zero;
        addr = getProcAddress("vkDestroyCommandPool");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DestroyCommandPool");
        _addr_DestroyCommandPool = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool,nint,void>)addr;
        // Initializing AllocateCommandBuffers
        addr = IntPtr.Zero;
        addr = getProcAddress("vkAllocateCommandBuffers");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_AllocateCommandBuffers");
        _addr_AllocateCommandBuffers = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBufferAllocateInfo*,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing FreeCommandBuffers
        addr = IntPtr.Zero;
        addr = getProcAddress("vkFreeCommandBuffers");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_FreeCommandBuffers");
        _addr_FreeCommandBuffers = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandPool,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer*,void>)addr;
        // Initializing WaitForFences
        addr = IntPtr.Zero;
        addr = getProcAddress("vkWaitForFences");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_WaitForFences");
        _addr_WaitForFences = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkFence*,uint,ulong,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing GetFenceStatus
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetFenceStatus");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetFenceStatus");
        _addr_GetFenceStatus = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkFence,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing BeginCommandBuffer
        addr = IntPtr.Zero;
        addr = getProcAddress("vkBeginCommandBuffer");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_BeginCommandBuffer");
        _addr_BeginCommandBuffer = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer,global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBufferBeginInfo*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing EndCommandBuffer
        addr = IntPtr.Zero;
        addr = getProcAddress("vkEndCommandBuffer");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_EndCommandBuffer");
        _addr_EndCommandBuffer = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing CreateSemaphore
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateSemaphore");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreateSemaphore");
        _addr_CreateSemaphore = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphoreCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing DestroySemaphore
        addr = IntPtr.Zero;
        addr = getProcAddress("vkDestroySemaphore");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DestroySemaphore");
        _addr_DestroySemaphore = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore,nint,void>)addr;
        // Initializing ResetFences
        addr = IntPtr.Zero;
        addr = getProcAddress("vkResetFences");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_ResetFences");
        _addr_ResetFences = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkFence*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing QueueSubmit
        addr = IntPtr.Zero;
        addr = getProcAddress("vkQueueSubmit");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_QueueSubmit");
        _addr_QueueSubmit = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkQueue,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkSubmitInfo*,global::Avalonia.Vulkan.UnmanagedInterop.VkFence,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing CreateImage
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateImage");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreateImage");
        _addr_CreateImage = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImageCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkImage*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing DestroyImage
        addr = IntPtr.Zero;
        addr = getProcAddress("vkDestroyImage");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DestroyImage");
        _addr_DestroyImage = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImage,nint,void>)addr;
        // Initializing GetImageMemoryRequirements
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetImageMemoryRequirements");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetImageMemoryRequirements");
        _addr_GetImageMemoryRequirements = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImage,global::Avalonia.Vulkan.UnmanagedInterop.VkMemoryRequirements*,void>)addr;
        // Initializing AllocateMemory
        addr = IntPtr.Zero;
        addr = getProcAddress("vkAllocateMemory");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_AllocateMemory");
        _addr_AllocateMemory = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkMemoryAllocateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing FreeMemory
        addr = IntPtr.Zero;
        addr = getProcAddress("vkFreeMemory");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_FreeMemory");
        _addr_FreeMemory = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory,nint,void>)addr;
        // Initializing BindImageMemory
        addr = IntPtr.Zero;
        addr = getProcAddress("vkBindImageMemory");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_BindImageMemory");
        _addr_BindImageMemory = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImage,global::Avalonia.Vulkan.UnmanagedInterop.VkDeviceMemory,ulong,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing CreateImageView
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateImageView");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreateImageView");
        _addr_CreateImageView = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImageViewCreateInfo*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkImageView*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing DestroyImageView
        addr = IntPtr.Zero;
        addr = getProcAddress("vkDestroyImageView");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DestroyImageView");
        _addr_DestroyImageView = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImageView,nint,void>)addr;
        // Initializing CmdPipelineBarrier
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCmdPipelineBarrier");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CmdPipelineBarrier");
        _addr_CmdPipelineBarrier = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer,global::Avalonia.Vulkan.UnmanagedInterop.VkPipelineStageFlags,global::Avalonia.Vulkan.UnmanagedInterop.VkPipelineStageFlags,global::Avalonia.Vulkan.UnmanagedInterop.VkDependencyFlags,uint,nint,uint,nint,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkImageMemoryBarrier*,void>)addr;
        // Initializing CreateSwapchainKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCreateSwapchainKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreateSwapchainKHR");
        _addr_CreateSwapchainKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainCreateInfoKHR*,nint,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing DestroySwapchainKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkDestroySwapchainKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DestroySwapchainKHR");
        _addr_DestroySwapchainKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR,nint,void>)addr;
        // Initializing GetSwapchainImagesKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkGetSwapchainImagesKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetSwapchainImagesKHR");
        _addr_GetSwapchainImagesKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkImage*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing DeviceWaitIdle
        addr = IntPtr.Zero;
        addr = getProcAddress("vkDeviceWaitIdle");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DeviceWaitIdle");
        _addr_DeviceWaitIdle = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing QueueWaitIdle
        addr = IntPtr.Zero;
        addr = getProcAddress("vkQueueWaitIdle");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_QueueWaitIdle");
        _addr_QueueWaitIdle = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkQueue,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing AcquireNextImageKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkAcquireNextImageKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_AcquireNextImageKHR");
        _addr_AcquireNextImageKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkSwapchainKHR,ulong,global::Avalonia.Vulkan.UnmanagedInterop.VkSemaphore,global::Avalonia.Vulkan.UnmanagedInterop.VkFence,uint*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing CmdBlitImage
        addr = IntPtr.Zero;
        addr = getProcAddress("vkCmdBlitImage");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CmdBlitImage");
        _addr_CmdBlitImage = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkCommandBuffer,global::Avalonia.Vulkan.UnmanagedInterop.VkImage,global::Avalonia.Vulkan.UnmanagedInterop.VkImageLayout,global::Avalonia.Vulkan.UnmanagedInterop.VkImage,global::Avalonia.Vulkan.UnmanagedInterop.VkImageLayout,uint,global::Avalonia.Vulkan.UnmanagedInterop.VkImageBlit*,global::Avalonia.Vulkan.UnmanagedInterop.VkFilter,void>)addr;
        // Initializing vkQueuePresentKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkQueuePresentKHR");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_vkQueuePresentKHR");
        _addr_vkQueuePresentKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkQueue,global::Avalonia.Vulkan.UnmanagedInterop.VkPresentInfoKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing ImportSemaphoreFdKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkImportSemaphoreFdKHR");
        _addr_ImportSemaphoreFdKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImportSemaphoreFdInfoKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
        // Initializing ImportSemaphoreWin32HandleKHR
        addr = IntPtr.Zero;
        addr = getProcAddress("vkImportSemaphoreWin32HandleKHR");
        _addr_ImportSemaphoreWin32HandleKHR = (delegate* unmanaged[Stdcall]<global::Avalonia.Vulkan.UnmanagedInterop.VkDevice,global::Avalonia.Vulkan.UnmanagedInterop.VkImportSemaphoreWin32HandleInfoKHR*,global::Avalonia.Vulkan.UnmanagedInterop.VkResult>)addr;
}
}