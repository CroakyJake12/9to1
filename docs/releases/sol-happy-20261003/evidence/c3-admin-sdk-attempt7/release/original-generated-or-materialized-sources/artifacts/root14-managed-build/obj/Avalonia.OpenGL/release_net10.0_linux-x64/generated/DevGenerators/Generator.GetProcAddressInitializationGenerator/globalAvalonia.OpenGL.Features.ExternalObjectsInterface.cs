using System;
namespace Avalonia.OpenGL.Features;
unsafe partial class ExternalObjectsInterface
{
    delegate* unmanaged[Stdcall]<uint,ulong,int,int,void>_addr_ImportMemoryFdEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyImportMemoryFdEXT(uint a0,ulong a1,int a2,int a3);
    public  partial void ImportMemoryFdEXT(uint @memory, ulong @size, int @handleType, int @fd)
    {
        if (_addr_ImportMemoryFdEXT == null) throw new System.EntryPointNotFoundException("ImportMemoryFdEXT");
        _addr_ImportMemoryFdEXT(@memory, @size, @handleType, @fd);
    }
    public  bool IsImportMemoryFdEXTAvailable => _addr_ImportMemoryFdEXT != null;
    delegate* unmanaged[Stdcall]<uint,int,int,void>_addr_ImportSemaphoreFdEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyImportSemaphoreFdEXT(uint a0,int a1,int a2);
    public  partial void ImportSemaphoreFdEXT(uint @semaphore, int @handleType, int @fd)
    {
        if (_addr_ImportSemaphoreFdEXT == null) throw new System.EntryPointNotFoundException("ImportSemaphoreFdEXT");
        _addr_ImportSemaphoreFdEXT(@semaphore, @handleType, @fd);
    }
    public  bool IsImportSemaphoreFdEXTAvailable => _addr_ImportSemaphoreFdEXT != null;
    delegate* unmanaged[Stdcall]<int,uint*,void>_addr_CreateMemoryObjectsEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyCreateMemoryObjectsEXT(int a0,uint* a1);
    public  partial void CreateMemoryObjectsEXT(int @n, out uint @memoryObjects)
    {
        fixed(uint* @__p_memoryObjects = &memoryObjects)
        _addr_CreateMemoryObjectsEXT(@n, @__p_memoryObjects);
    }
    delegate* unmanaged[Stdcall]<int,uint*,void>_addr_DeleteMemoryObjectsEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyDeleteMemoryObjectsEXT(int a0,uint* a1);
    public  partial void DeleteMemoryObjectsEXT(int @n, ref uint @objects)
    {
        fixed(uint* @__p_objects = &objects)
        _addr_DeleteMemoryObjectsEXT(@n, @__p_objects);
    }
    delegate* unmanaged[Stdcall]<int,int,int,int,int,uint,ulong,void>_addr_TexStorageMem2DEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyTexStorageMem2DEXT(int a0,int a1,int a2,int a3,int a4,uint a5,ulong a6);
    public  partial void TexStorageMem2DEXT(int @target, int @levels, int @internalFormat, int @width, int @height, uint @memory, ulong @offset)
    {
        _addr_TexStorageMem2DEXT(@target, @levels, @internalFormat, @width, @height, @memory, @offset);
    }
    delegate* unmanaged[Stdcall]<int,uint*,void>_addr_GenSemaphoresEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyGenSemaphoresEXT(int a0,uint* a1);
    public  partial void GenSemaphoresEXT(int @n, out uint @semaphores)
    {
        fixed(uint* @__p_semaphores = &semaphores)
        _addr_GenSemaphoresEXT(@n, @__p_semaphores);
    }
    delegate* unmanaged[Stdcall]<int,uint*,void>_addr_DeleteSemaphoresEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyDeleteSemaphoresEXT(int a0,uint* a1);
    public  partial void DeleteSemaphoresEXT(int @n, ref uint @semaphores)
    {
        fixed(uint* @__p_semaphores = &semaphores)
        _addr_DeleteSemaphoresEXT(@n, @__p_semaphores);
    }
    delegate* unmanaged[Stdcall]<uint,uint,uint*,uint,int*,int*,void>_addr_WaitSemaphoreEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyWaitSemaphoreEXT(uint a0,uint a1,uint* a2,uint a3,int* a4,int* a5);
    public  partial void WaitSemaphoreEXT(uint @semaphore, uint @numBufferBarriers, uint* @buffers, uint @numTextureBarriers, int* @textures, int* @srcLayouts)
    {
        _addr_WaitSemaphoreEXT(@semaphore, @numBufferBarriers, @buffers, @numTextureBarriers, @textures, @srcLayouts);
    }
    delegate* unmanaged[Stdcall]<uint,uint,uint*,uint,int*,int*,void>_addr_SignalSemaphoreEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummySignalSemaphoreEXT(uint a0,uint a1,uint* a2,uint a3,int* a4,int* a5);
    public  partial void SignalSemaphoreEXT(uint @semaphore, uint @numBufferBarriers, uint* @buffers, uint @numTextureBarriers, int* @textures, int* @dstLayouts)
    {
        _addr_SignalSemaphoreEXT(@semaphore, @numBufferBarriers, @buffers, @numTextureBarriers, @textures, @dstLayouts);
    }
    delegate* unmanaged[Stdcall]<int,uint,byte*,void>_addr_GetUnsignedBytei_vEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyGetUnsignedBytei_vEXT(int a0,uint a1,byte* a2);
    public  partial void GetUnsignedBytei_vEXT(int @target, uint @index, byte* @data)
    {
        if (_addr_GetUnsignedBytei_vEXT == null) throw new System.EntryPointNotFoundException("GetUnsignedBytei_vEXT");
        _addr_GetUnsignedBytei_vEXT(@target, @index, @data);
    }
    public  bool IsGetUnsignedBytei_vEXTAvailable => _addr_GetUnsignedBytei_vEXT != null;
    delegate* unmanaged[Stdcall]<int,byte*,void>_addr_GetUnsignedBytevEXT;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyGetUnsignedBytevEXT(int a0,byte* a1);
    public  partial void GetUnsignedBytevEXT(int @target, byte* @data)
    {
        if (_addr_GetUnsignedBytevEXT == null) throw new System.EntryPointNotFoundException("GetUnsignedBytevEXT");
        _addr_GetUnsignedBytevEXT(@target, @data);
    }
    public  bool IsGetUnsignedBytevEXTAvailable => _addr_GetUnsignedBytevEXT != null;
    void Initialize(Func<string, IntPtr> getProcAddress)
    {
        var addr = IntPtr.Zero;
        // Initializing ImportMemoryFdEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glImportMemoryFdEXT");
        _addr_ImportMemoryFdEXT = (delegate* unmanaged[Stdcall]<uint,ulong,int,int,void>)addr;
        // Initializing ImportSemaphoreFdEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glImportSemaphoreFdEXT");
        _addr_ImportSemaphoreFdEXT = (delegate* unmanaged[Stdcall]<uint,int,int,void>)addr;
        // Initializing CreateMemoryObjectsEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glCreateMemoryObjectsEXT");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreateMemoryObjectsEXT");
        _addr_CreateMemoryObjectsEXT = (delegate* unmanaged[Stdcall]<int,uint*,void>)addr;
        // Initializing DeleteMemoryObjectsEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glDeleteMemoryObjectsEXT");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DeleteMemoryObjectsEXT");
        _addr_DeleteMemoryObjectsEXT = (delegate* unmanaged[Stdcall]<int,uint*,void>)addr;
        // Initializing TexStorageMem2DEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glTexStorageMem2DEXT");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_TexStorageMem2DEXT");
        _addr_TexStorageMem2DEXT = (delegate* unmanaged[Stdcall]<int,int,int,int,int,uint,ulong,void>)addr;
        // Initializing GenSemaphoresEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glGenSemaphoresEXT");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GenSemaphoresEXT");
        _addr_GenSemaphoresEXT = (delegate* unmanaged[Stdcall]<int,uint*,void>)addr;
        // Initializing DeleteSemaphoresEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glDeleteSemaphoresEXT");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DeleteSemaphoresEXT");
        _addr_DeleteSemaphoresEXT = (delegate* unmanaged[Stdcall]<int,uint*,void>)addr;
        // Initializing WaitSemaphoreEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glWaitSemaphoreEXT");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_WaitSemaphoreEXT");
        _addr_WaitSemaphoreEXT = (delegate* unmanaged[Stdcall]<uint,uint,uint*,uint,int*,int*,void>)addr;
        // Initializing SignalSemaphoreEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glSignalSemaphoreEXT");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_SignalSemaphoreEXT");
        _addr_SignalSemaphoreEXT = (delegate* unmanaged[Stdcall]<uint,uint,uint*,uint,int*,int*,void>)addr;
        // Initializing GetUnsignedBytei_vEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glGetUnsignedBytei_vEXT");
        _addr_GetUnsignedBytei_vEXT = (delegate* unmanaged[Stdcall]<int,uint,byte*,void>)addr;
        // Initializing GetUnsignedBytevEXT
        addr = IntPtr.Zero;
        addr = getProcAddress("glGetUnsignedBytevEXT");
        _addr_GetUnsignedBytevEXT = (delegate* unmanaged[Stdcall]<int,byte*,void>)addr;
}
}