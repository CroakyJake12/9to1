using System;
namespace Avalonia.X11.Glx;
unsafe partial class GlxInterface
{
    delegate* unmanaged[Stdcall]<nint,nint,nint,nint,int>_addr_MakeContextCurrent;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate int __wasmDummyMakeContextCurrent(nint a0,nint a1,nint a2,nint a3);
    public  partial bool MakeContextCurrent(nint @display, nint @draw, nint @read, nint @context)
    {
        return _addr_MakeContextCurrent(@display, @draw, @read, @context) != 0;
    }
    delegate* unmanaged[Stdcall]<nint>_addr_GetCurrentContext;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint __wasmDummyGetCurrentContext();
    public  partial nint GetCurrentContext()
    {
        return _addr_GetCurrentContext();
    }
    delegate* unmanaged[Stdcall]<nint>_addr_GetCurrentDisplay;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint __wasmDummyGetCurrentDisplay();
    public  partial nint GetCurrentDisplay()
    {
        return _addr_GetCurrentDisplay();
    }
    delegate* unmanaged[Stdcall]<nint>_addr_GetCurrentDrawable;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint __wasmDummyGetCurrentDrawable();
    public  partial nint GetCurrentDrawable()
    {
        return _addr_GetCurrentDrawable();
    }
    delegate* unmanaged[Stdcall]<nint>_addr_GetCurrentReadDrawable;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint __wasmDummyGetCurrentReadDrawable();
    public  partial nint GetCurrentReadDrawable()
    {
        return _addr_GetCurrentReadDrawable();
    }
    delegate* unmanaged[Stdcall]<nint,nint,int*,nint>_addr_CreatePbuffer;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint __wasmDummyCreatePbuffer(nint a0,nint a1,int* a2);
    public  partial nint CreatePbuffer(nint @dpy, nint @fbc, int[] @attrib_list)
    {
        fixed(int* @__p_attrib_list = attrib_list)
        return _addr_CreatePbuffer(@dpy, @fbc, @__p_attrib_list);
    }
    delegate* unmanaged[Stdcall]<nint,nint,nint>_addr_DestroyPbuffer;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint __wasmDummyDestroyPbuffer(nint a0,nint a1);
    public  partial nint DestroyPbuffer(nint @dpy, nint @fb)
    {
        return _addr_DestroyPbuffer(@dpy, @fb);
    }
    delegate* unmanaged[Stdcall]<nint,int,int*,global::Avalonia.X11.XVisualInfo*>_addr_ChooseVisual;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.X11.XVisualInfo* __wasmDummyChooseVisual(nint a0,int a1,int* a2);
    public  partial global::Avalonia.X11.XVisualInfo* ChooseVisual(nint @dpy, int @screen, int[] @attribList)
    {
        fixed(int* @__p_attribList = attribList)
        return _addr_ChooseVisual(@dpy, @screen, @__p_attribList);
    }
    delegate* unmanaged[Stdcall]<nint,global::Avalonia.X11.XVisualInfo*,nint,int,nint>_addr_CreateContext;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint __wasmDummyCreateContext(nint a0,global::Avalonia.X11.XVisualInfo* a1,nint a2,int a3);
    public  partial nint CreateContext(nint @dpy, global::Avalonia.X11.XVisualInfo* @vis, nint @shareList, bool @direct)
    {
        return _addr_CreateContext(@dpy, @vis, @shareList, @direct ? 1 : 0);
    }
    delegate* unmanaged[Stdcall]<nint,nint,nint,int,int*,nint>_addr_CreateContextAttribsARB;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint __wasmDummyCreateContextAttribsARB(nint a0,nint a1,nint a2,int a3,int* a4);
    public  partial nint CreateContextAttribsARB(nint @dpy, nint @fbconfig, nint @shareList, bool @direct, int[] @attribs)
    {
        fixed(int* @__p_attribs = attribs)
        return _addr_CreateContextAttribsARB(@dpy, @fbconfig, @shareList, @direct ? 1 : 0, @__p_attribs);
    }
    delegate* unmanaged[Stdcall]<nint,nint,void>_addr_DestroyContext;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyDestroyContext(nint a0,nint a1);
    public  partial void DestroyContext(nint @dpy, nint @ctx)
    {
        _addr_DestroyContext(@dpy, @ctx);
    }
    delegate* unmanaged[Stdcall]<nint,int,int*,int*,nint*>_addr_ChooseFBConfig;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint* __wasmDummyChooseFBConfig(nint a0,int a1,int* a2,int* a3);
    public  partial nint* ChooseFBConfig(nint @dpy, int @screen, int[] @attrib_list, out int @nelements)
    {
        fixed(int* @__p_attrib_list = attrib_list)
        fixed(int* @__p_nelements = &nelements)
        return _addr_ChooseFBConfig(@dpy, @screen, @__p_attrib_list, @__p_nelements);
    }
    delegate* unmanaged[Stdcall]<nint,nint,global::Avalonia.X11.XVisualInfo*>_addr_GetVisualFromFBConfig;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate global::Avalonia.X11.XVisualInfo* __wasmDummyGetVisualFromFBConfig(nint a0,nint a1);
    public  partial global::Avalonia.X11.XVisualInfo* GetVisualFromFBConfig(nint @dpy, nint @config)
    {
        return _addr_GetVisualFromFBConfig(@dpy, @config);
    }
    delegate* unmanaged[Stdcall]<nint,nint,int,int*,int>_addr_GetFBConfigAttrib;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate int __wasmDummyGetFBConfigAttrib(nint a0,nint a1,int a2,int* a3);
    public  partial int GetFBConfigAttrib(nint @dpy, nint @config, int @attribute, out int @value)
    {
        fixed(int* @__p_value = &value)
        return _addr_GetFBConfigAttrib(@dpy, @config, @attribute, @__p_value);
    }
    delegate* unmanaged[Stdcall]<nint,nint,void>_addr_SwapBuffers;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummySwapBuffers(nint a0,nint a1);
    public  partial void SwapBuffers(nint @dpy, nint @drawable)
    {
        _addr_SwapBuffers(@dpy, @drawable);
    }
    delegate* unmanaged[Stdcall]<void>_addr_WaitX;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyWaitX();
    public  partial void WaitX()
    {
        _addr_WaitX();
    }
    delegate* unmanaged[Stdcall]<void>_addr_WaitGL;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate void __wasmDummyWaitGL();
    public  partial void WaitGL()
    {
        _addr_WaitGL();
    }
    delegate* unmanaged[Stdcall]<int>_addr_GlGetError;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate int __wasmDummyGlGetError();
    public  partial int GlGetError()
    {
        return _addr_GlGetError();
    }
    delegate* unmanaged[Stdcall]<nint,int,nint>_addr_QueryExtensionsString;
    [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(global::System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate nint __wasmDummyQueryExtensionsString(nint a0,int a1);
    public  partial nint QueryExtensionsString(nint @display, int @screen)
    {
        return _addr_QueryExtensionsString(@display, @screen);
    }
    void Initialize(Func<string, IntPtr> getProcAddress)
    {
        var addr = IntPtr.Zero;
        // Initializing MakeContextCurrent
        addr = IntPtr.Zero;
        addr = getProcAddress("glXMakeContextCurrent");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_MakeContextCurrent");
        _addr_MakeContextCurrent = (delegate* unmanaged[Stdcall]<nint,nint,nint,nint,int>)addr;
        // Initializing GetCurrentContext
        addr = IntPtr.Zero;
        addr = getProcAddress("glXGetCurrentContext");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetCurrentContext");
        _addr_GetCurrentContext = (delegate* unmanaged[Stdcall]<nint>)addr;
        // Initializing GetCurrentDisplay
        addr = IntPtr.Zero;
        addr = getProcAddress("glXGetCurrentDisplay");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetCurrentDisplay");
        _addr_GetCurrentDisplay = (delegate* unmanaged[Stdcall]<nint>)addr;
        // Initializing GetCurrentDrawable
        addr = IntPtr.Zero;
        addr = getProcAddress("glXGetCurrentDrawable");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetCurrentDrawable");
        _addr_GetCurrentDrawable = (delegate* unmanaged[Stdcall]<nint>)addr;
        // Initializing GetCurrentReadDrawable
        addr = IntPtr.Zero;
        addr = getProcAddress("glXGetCurrentReadDrawable");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetCurrentReadDrawable");
        _addr_GetCurrentReadDrawable = (delegate* unmanaged[Stdcall]<nint>)addr;
        // Initializing CreatePbuffer
        addr = IntPtr.Zero;
        addr = getProcAddress("glXCreatePbuffer");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreatePbuffer");
        _addr_CreatePbuffer = (delegate* unmanaged[Stdcall]<nint,nint,int*,nint>)addr;
        // Initializing DestroyPbuffer
        addr = IntPtr.Zero;
        addr = getProcAddress("glXDestroyPbuffer");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DestroyPbuffer");
        _addr_DestroyPbuffer = (delegate* unmanaged[Stdcall]<nint,nint,nint>)addr;
        // Initializing ChooseVisual
        addr = IntPtr.Zero;
        addr = getProcAddress("glXChooseVisual");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_ChooseVisual");
        _addr_ChooseVisual = (delegate* unmanaged[Stdcall]<nint,int,int*,global::Avalonia.X11.XVisualInfo*>)addr;
        // Initializing CreateContext
        addr = IntPtr.Zero;
        addr = getProcAddress("glXCreateContext");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreateContext");
        _addr_CreateContext = (delegate* unmanaged[Stdcall]<nint,global::Avalonia.X11.XVisualInfo*,nint,int,nint>)addr;
        // Initializing CreateContextAttribsARB
        addr = IntPtr.Zero;
        addr = getProcAddress("glXCreateContextAttribsARB");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_CreateContextAttribsARB");
        _addr_CreateContextAttribsARB = (delegate* unmanaged[Stdcall]<nint,nint,nint,int,int*,nint>)addr;
        // Initializing DestroyContext
        addr = IntPtr.Zero;
        addr = getProcAddress("glXDestroyContext");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_DestroyContext");
        _addr_DestroyContext = (delegate* unmanaged[Stdcall]<nint,nint,void>)addr;
        // Initializing ChooseFBConfig
        addr = IntPtr.Zero;
        addr = getProcAddress("glXChooseFBConfig");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_ChooseFBConfig");
        _addr_ChooseFBConfig = (delegate* unmanaged[Stdcall]<nint,int,int*,int*,nint*>)addr;
        // Initializing GetVisualFromFBConfig
        addr = IntPtr.Zero;
        addr = getProcAddress("glXGetVisualFromFBConfig");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetVisualFromFBConfig");
        _addr_GetVisualFromFBConfig = (delegate* unmanaged[Stdcall]<nint,nint,global::Avalonia.X11.XVisualInfo*>)addr;
        // Initializing GetFBConfigAttrib
        addr = IntPtr.Zero;
        addr = getProcAddress("glXGetFBConfigAttrib");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GetFBConfigAttrib");
        _addr_GetFBConfigAttrib = (delegate* unmanaged[Stdcall]<nint,nint,int,int*,int>)addr;
        // Initializing SwapBuffers
        addr = IntPtr.Zero;
        addr = getProcAddress("glXSwapBuffers");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_SwapBuffers");
        _addr_SwapBuffers = (delegate* unmanaged[Stdcall]<nint,nint,void>)addr;
        // Initializing WaitX
        addr = IntPtr.Zero;
        addr = getProcAddress("glXWaitX");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_WaitX");
        _addr_WaitX = (delegate* unmanaged[Stdcall]<void>)addr;
        // Initializing WaitGL
        addr = IntPtr.Zero;
        addr = getProcAddress("glXWaitGL");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_WaitGL");
        _addr_WaitGL = (delegate* unmanaged[Stdcall]<void>)addr;
        // Initializing GlGetError
        addr = IntPtr.Zero;
        addr = getProcAddress("glGetError");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_GlGetError");
        _addr_GlGetError = (delegate* unmanaged[Stdcall]<int>)addr;
        // Initializing QueryExtensionsString
        addr = IntPtr.Zero;
        addr = getProcAddress("glXQueryExtensionsString");
        if (addr == IntPtr.Zero) throw new System.EntryPointNotFoundException("_addr_QueryExtensionsString");
        _addr_QueryExtensionsString = (delegate* unmanaged[Stdcall]<nint,int,nint>)addr;
}
}