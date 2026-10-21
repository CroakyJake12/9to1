#pragma warning disable 108
// ReSharper disable RedundantUsingDirective
// ReSharper disable JoinDeclarationAndInitializer
// ReSharper disable ArrangeTypeMemberModifiers
// ReSharper disable UnusedType.Local
// ReSharper disable InconsistentNaming
// ReSharper disable RedundantNameQualifier
// ReSharper disable RedundantCast
// ReSharper disable IdentifierTypo
// ReSharper disable PartialTypeWithSinglePart
// ReSharper disable RedundantUnsafeContext
// ReSharper disable RedundantBaseQualifier
// ReSharper disable EmptyStatement
// ReSharper disable RedundantAttributeParentheses
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable FieldCanBeMadeReadOnly.Global
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using MicroCom.Runtime;

namespace Avalonia.Win32.DComposition
{
    internal unsafe partial interface IDCompositionDevice : global::MicroCom.Runtime.IUnknown
    {
        void Commit();
        void WaitForCommitCompletion();
        void GetFrameStatistics(DCOMPOSITION_FRAME_STATISTICS* statistics);
        IDCompositionVisual CreateTargetForHwnd(IntPtr hwnd, bool topmost);
        IDCompositionVisual CreateVisual();
        IDCompositionSurface CreateSurface(uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode);
        IDCompositionVirtualSurface CreateVirtualSurface(uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode);
        IUnknown CreateSurfaceFromHandle(IntPtr handle);
        IUnknown CreateSurfaceFromHwnd(IntPtr hwnd);
        void* CreateTranslateTransform();
        void* CreateScaleTransform();
        void* CreateRotateTransform();
        void* CreateSkewTransform();
        void* CreateMatrixTransform();
        void* CreateTransformGroup(void* transforms, int elements);
        void* CreateTranslateTransform3D();
        void* CreateScaleTransform3D();
        void* CreateRotateTransform3D();
        void* CreateMatrixTransform3D();
        void* CreateTransform3DGroup(void* transforms3D, int elements);
        void* CreateEffectGroup();
        void* CreateRectangleClip();
        void* CreateAnimation();
        int CheckDeviceState();
    }

    internal unsafe partial interface IDCompositionDevice2 : global::MicroCom.Runtime.IUnknown
    {
        void Commit();
        void WaitForCommitCompletion();
        void GetFrameStatistics(DCOMPOSITION_FRAME_STATISTICS* statistics);
        IDCompositionVisual CreateVisual();
        IDCompositionSurfaceFactory CreateSurfaceFactory(IUnknown renderingDevice);
        IDCompositionSurface CreateSurface(uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode);
        IDCompositionVirtualSurface CreateVirtualSurface(uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode);
        void* CreateTranslateTransform();
        void* CreateScaleTransform();
        void* CreateRotateTransform();
        void* CreateSkewTransform();
        void* CreateMatrixTransform();
        void* CreateTransformGroup(void* transforms, int elements);
        void* CreateTranslateTransform3D();
        void* CreateScaleTransform3D();
        void* CreateRotateTransform3D();
        void* CreateMatrixTransform3D();
        void* CreateTransform3DGroup(void* transforms3D, int elements);
        void* CreateEffectGroup();
        void* CreateRectangleClip();
        void* CreateAnimation();
    }

    internal unsafe partial interface IDCompositionDevice3 : IDCompositionDevice2
    {
        void* CreateGaussianBlurEffect();
    }

    internal unsafe partial interface IDCompositionDesktopDevice : IDCompositionDevice2
    {
        IDCompositionVisual CreateTargetForHwnd(IntPtr hwnd, bool topmost);
        IUnknown CreateSurfaceFromHandle(IntPtr handle);
        IUnknown CreateSurfaceFromHwnd(IntPtr hwnd);
    }

    internal unsafe partial interface IDCompositionVisual : global::MicroCom.Runtime.IUnknown
    {
        void SetOffsetX_IDCompositionAnimation(void* animation);
        void SetOffsetX(float offsetX);
        void SetOffsetY_IDCompositionAnimation(void* animation);
        void SetOffsetY(float offsetY);
        void SetTransform_IDCompositionTransform(void* transform);
        void SetTransform(void* matrix);
        void SetTransformParent(IDCompositionVisual visual);
        void SetEffect(void* effect);
        void SetBitmapInterpolationMode(int interpolationMode);
        void SetBorderMode(int borderMode);
        void SetClip_IDCompositionClip(void* clip);
        void SetClip(void* rect);
        void SetContent(IUnknown content);
        void AddVisual(IDCompositionVisual visual, int insertAbove, IDCompositionVisual referenceVisual);
        void RemoveVisual(IDCompositionVisual visual);
        void RemoveAllVisuals();
        void SetCompositeMode(int compositeMode);
    }

    internal unsafe partial interface IDCompositionTarget : global::MicroCom.Runtime.IUnknown
    {
        void SetRoot(IDCompositionVisual visual);
    }

    internal unsafe partial interface IDCompositionSurfaceFactory : global::MicroCom.Runtime.IUnknown
    {
        IDCompositionSurface CreateSurface(uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode);
        IDCompositionVirtualSurface CreateVirtualSurface(uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode);
    }

    internal unsafe partial interface IDCompositionSurface : global::MicroCom.Runtime.IUnknown
    {
        Avalonia.Win32.Interop.UnmanagedMethods.POINT BeginDraw(Avalonia.Win32.Interop.UnmanagedMethods.RECT* updateRect, System.Guid* iid, void** updateObject);
        void EndDraw();
        void SuspendDraw();
        void ResumeDraw();
        void Scroll(Avalonia.Win32.Interop.UnmanagedMethods.RECT* scrollRect, Avalonia.Win32.Interop.UnmanagedMethods.RECT* clipRect, int offsetX, int offsetY);
    }

    internal unsafe partial interface IDCompositionVirtualSurface : IDCompositionSurface
    {
        void Resize(uint width, uint height);
        void Trim(void* rectangles, int count);
    }
}

namespace Avalonia.Win32.DComposition.Impl
{
    internal unsafe partial class __MicroComIDCompositionDeviceProxy : global::MicroCom.Runtime.MicroComProxyBase, IDCompositionDevice
    {
        public void Commit()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Commit failed", __result);
        }

        public void WaitForCommitCompletion()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 1])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("WaitForCommitCompletion failed", __result);
        }

        public void GetFrameStatistics(DCOMPOSITION_FRAME_STATISTICS* statistics)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, statistics);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetFrameStatistics failed", __result);
        }

        public IDCompositionVisual CreateTargetForHwnd(IntPtr hwnd, bool topmost)
        {
            int __result;
            void* __marshal_target = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, bool, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, hwnd, topmost, &__marshal_target);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTargetForHwnd failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVisual>(__marshal_target, true);
        }

        public IDCompositionVisual CreateVisual()
        {
            int __result;
            void* __marshal_visual = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, &__marshal_visual);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateVisual failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVisual>(__marshal_visual, true);
        }

        public IDCompositionSurface CreateSurface(uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode)
        {
            int __result;
            void* __marshal_surface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, width, height, pixelFormat, alphaMode, &__marshal_surface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSurface failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionSurface>(__marshal_surface, true);
        }

        public IDCompositionVirtualSurface CreateVirtualSurface(uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode)
        {
            int __result;
            void* __marshal_virtualSurface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, initialWidth, initialHeight, pixelFormat, alphaMode, &__marshal_virtualSurface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateVirtualSurface failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVirtualSurface>(__marshal_virtualSurface, true);
        }

        public IUnknown CreateSurfaceFromHandle(IntPtr handle)
        {
            int __result;
            void* __marshal_surface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, handle, &__marshal_surface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSurfaceFromHandle failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_surface, true);
        }

        public IUnknown CreateSurfaceFromHwnd(IntPtr hwnd)
        {
            int __result;
            void* __marshal_surface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, hwnd, &__marshal_surface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSurfaceFromHwnd failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_surface, true);
        }

        public void* CreateTranslateTransform()
        {
            int __result;
            void* translateTransform = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 9])(PPV, &translateTransform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTranslateTransform failed", __result);
            return translateTransform;
        }

        public void* CreateScaleTransform()
        {
            int __result;
            void* scaleTransform = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 10])(PPV, &scaleTransform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateScaleTransform failed", __result);
            return scaleTransform;
        }

        public void* CreateRotateTransform()
        {
            int __result;
            void* rotateTransform = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 11])(PPV, &rotateTransform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateRotateTransform failed", __result);
            return rotateTransform;
        }

        public void* CreateSkewTransform()
        {
            int __result;
            void* skewTransform = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 12])(PPV, &skewTransform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSkewTransform failed", __result);
            return skewTransform;
        }

        public void* CreateMatrixTransform()
        {
            int __result;
            void* matrixTransform = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 13])(PPV, &matrixTransform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateMatrixTransform failed", __result);
            return matrixTransform;
        }

        public void* CreateTransformGroup(void* transforms, int elements)
        {
            int __result;
            void* transformGroup = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int, void*, int>)(*PPV)[base.VTableSize + 14])(PPV, transforms, elements, &transformGroup);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTransformGroup failed", __result);
            return transformGroup;
        }

        public void* CreateTranslateTransform3D()
        {
            int __result;
            void* translateTransform3D = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 15])(PPV, &translateTransform3D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTranslateTransform3D failed", __result);
            return translateTransform3D;
        }

        public void* CreateScaleTransform3D()
        {
            int __result;
            void* scaleTransform3D = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 16])(PPV, &scaleTransform3D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateScaleTransform3D failed", __result);
            return scaleTransform3D;
        }

        public void* CreateRotateTransform3D()
        {
            int __result;
            void* rotateTransform3D = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 17])(PPV, &rotateTransform3D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateRotateTransform3D failed", __result);
            return rotateTransform3D;
        }

        public void* CreateMatrixTransform3D()
        {
            int __result;
            void* matrixTransform3D = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 18])(PPV, &matrixTransform3D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateMatrixTransform3D failed", __result);
            return matrixTransform3D;
        }

        public void* CreateTransform3DGroup(void* transforms3D, int elements)
        {
            int __result;
            void* transform3DGroup = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int, void*, int>)(*PPV)[base.VTableSize + 19])(PPV, transforms3D, elements, &transform3DGroup);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTransform3DGroup failed", __result);
            return transform3DGroup;
        }

        public void* CreateEffectGroup()
        {
            int __result;
            void* effectGroup = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 20])(PPV, &effectGroup);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateEffectGroup failed", __result);
            return effectGroup;
        }

        public void* CreateRectangleClip()
        {
            int __result;
            void* clip = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 21])(PPV, &clip);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateRectangleClip failed", __result);
            return clip;
        }

        public void* CreateAnimation()
        {
            int __result;
            void* animation = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 22])(PPV, &animation);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateAnimation failed", __result);
            return animation;
        }

        public int CheckDeviceState()
        {
            int __result;
            int pfValid = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 23])(PPV, &pfValid);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CheckDeviceState failed", __result);
            return pfValid;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDCompositionDevice), new Guid("c37ea93a-e7aa-450d-b16f-9746cb0407f3"), (p, owns) => new __MicroComIDCompositionDeviceProxy(p, owns));
        }

        protected __MicroComIDCompositionDeviceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 24;
    }

    unsafe class __MicroComIDCompositionDeviceVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CommitDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Commit(void* @this)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Commit();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int WaitForCommitCompletionDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int WaitForCommitCompletion(void* @this)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.WaitForCommitCompletion();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetFrameStatisticsDelegate(void* @this, DCOMPOSITION_FRAME_STATISTICS* statistics);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetFrameStatistics(void* @this, DCOMPOSITION_FRAME_STATISTICS* statistics)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GetFrameStatistics(statistics);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTargetForHwndDelegate(void* @this, IntPtr hwnd, bool topmost, void** target);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTargetForHwnd(void* @this, IntPtr hwnd, bool topmost, void** target)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTargetForHwnd(hwnd, topmost);
                        *target = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateVisualDelegate(void* @this, void** visual);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateVisual(void* @this, void** visual)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateVisual();
                        *visual = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSurfaceDelegate(void* @this, uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** surface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSurface(void* @this, uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** surface)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSurface(width, height, pixelFormat, alphaMode);
                        *surface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateVirtualSurfaceDelegate(void* @this, uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** virtualSurface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateVirtualSurface(void* @this, uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** virtualSurface)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateVirtualSurface(initialWidth, initialHeight, pixelFormat, alphaMode);
                        *virtualSurface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSurfaceFromHandleDelegate(void* @this, IntPtr handle, void** surface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSurfaceFromHandle(void* @this, IntPtr handle, void** surface)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSurfaceFromHandle(handle);
                        *surface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSurfaceFromHwndDelegate(void* @this, IntPtr hwnd, void** surface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSurfaceFromHwnd(void* @this, IntPtr hwnd, void** surface)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSurfaceFromHwnd(hwnd);
                        *surface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTranslateTransformDelegate(void* @this, void** translateTransform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTranslateTransform(void* @this, void** translateTransform)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTranslateTransform();
                        *translateTransform = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateScaleTransformDelegate(void* @this, void** scaleTransform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateScaleTransform(void* @this, void** scaleTransform)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateScaleTransform();
                        *scaleTransform = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateRotateTransformDelegate(void* @this, void** rotateTransform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateRotateTransform(void* @this, void** rotateTransform)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateRotateTransform();
                        *rotateTransform = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSkewTransformDelegate(void* @this, void** skewTransform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSkewTransform(void* @this, void** skewTransform)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSkewTransform();
                        *skewTransform = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateMatrixTransformDelegate(void* @this, void** matrixTransform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateMatrixTransform(void* @this, void** matrixTransform)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateMatrixTransform();
                        *matrixTransform = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTransformGroupDelegate(void* @this, void* transforms, int elements, void** transformGroup);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTransformGroup(void* @this, void* transforms, int elements, void** transformGroup)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTransformGroup(transforms, elements);
                        *transformGroup = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTranslateTransform3DDelegate(void* @this, void** translateTransform3D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTranslateTransform3D(void* @this, void** translateTransform3D)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTranslateTransform3D();
                        *translateTransform3D = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateScaleTransform3DDelegate(void* @this, void** scaleTransform3D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateScaleTransform3D(void* @this, void** scaleTransform3D)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateScaleTransform3D();
                        *scaleTransform3D = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateRotateTransform3DDelegate(void* @this, void** rotateTransform3D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateRotateTransform3D(void* @this, void** rotateTransform3D)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateRotateTransform3D();
                        *rotateTransform3D = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateMatrixTransform3DDelegate(void* @this, void** matrixTransform3D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateMatrixTransform3D(void* @this, void** matrixTransform3D)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateMatrixTransform3D();
                        *matrixTransform3D = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTransform3DGroupDelegate(void* @this, void* transforms3D, int elements, void** transform3DGroup);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTransform3DGroup(void* @this, void* transforms3D, int elements, void** transform3DGroup)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTransform3DGroup(transforms3D, elements);
                        *transform3DGroup = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateEffectGroupDelegate(void* @this, void** effectGroup);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateEffectGroup(void* @this, void** effectGroup)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateEffectGroup();
                        *effectGroup = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateRectangleClipDelegate(void* @this, void** clip);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateRectangleClip(void* @this, void** clip)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateRectangleClip();
                        *clip = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateAnimationDelegate(void* @this, void** animation);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateAnimation(void* @this, void** animation)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateAnimation();
                        *animation = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CheckDeviceStateDelegate(void* @this, int* pfValid);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CheckDeviceState(void* @this, int* pfValid)
        {
            IDCompositionDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CheckDeviceState();
                        *pfValid = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIDCompositionDeviceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Commit); 
#else
            base.AddMethod((CommitDelegate)Commit); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&WaitForCommitCompletion); 
#else
            base.AddMethod((WaitForCommitCompletionDelegate)WaitForCommitCompletion); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DCOMPOSITION_FRAME_STATISTICS*, int>)&GetFrameStatistics); 
#else
            base.AddMethod((GetFrameStatisticsDelegate)GetFrameStatistics); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, bool, void**, int>)&CreateTargetForHwnd); 
#else
            base.AddMethod((CreateTargetForHwndDelegate)CreateTargetForHwnd); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateVisual); 
#else
            base.AddMethod((CreateVisualDelegate)CreateVisual); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void**, int>)&CreateSurface); 
#else
            base.AddMethod((CreateSurfaceDelegate)CreateSurface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void**, int>)&CreateVirtualSurface); 
#else
            base.AddMethod((CreateVirtualSurfaceDelegate)CreateVirtualSurface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreateSurfaceFromHandle); 
#else
            base.AddMethod((CreateSurfaceFromHandleDelegate)CreateSurfaceFromHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreateSurfaceFromHwnd); 
#else
            base.AddMethod((CreateSurfaceFromHwndDelegate)CreateSurfaceFromHwnd); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateTranslateTransform); 
#else
            base.AddMethod((CreateTranslateTransformDelegate)CreateTranslateTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateScaleTransform); 
#else
            base.AddMethod((CreateScaleTransformDelegate)CreateScaleTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateRotateTransform); 
#else
            base.AddMethod((CreateRotateTransformDelegate)CreateRotateTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateSkewTransform); 
#else
            base.AddMethod((CreateSkewTransformDelegate)CreateSkewTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateMatrixTransform); 
#else
            base.AddMethod((CreateMatrixTransformDelegate)CreateMatrixTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int, void**, int>)&CreateTransformGroup); 
#else
            base.AddMethod((CreateTransformGroupDelegate)CreateTransformGroup); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateTranslateTransform3D); 
#else
            base.AddMethod((CreateTranslateTransform3DDelegate)CreateTranslateTransform3D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateScaleTransform3D); 
#else
            base.AddMethod((CreateScaleTransform3DDelegate)CreateScaleTransform3D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateRotateTransform3D); 
#else
            base.AddMethod((CreateRotateTransform3DDelegate)CreateRotateTransform3D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateMatrixTransform3D); 
#else
            base.AddMethod((CreateMatrixTransform3DDelegate)CreateMatrixTransform3D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int, void**, int>)&CreateTransform3DGroup); 
#else
            base.AddMethod((CreateTransform3DGroupDelegate)CreateTransform3DGroup); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateEffectGroup); 
#else
            base.AddMethod((CreateEffectGroupDelegate)CreateEffectGroup); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateRectangleClip); 
#else
            base.AddMethod((CreateRectangleClipDelegate)CreateRectangleClip); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateAnimation); 
#else
            base.AddMethod((CreateAnimationDelegate)CreateAnimation); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int*, int>)&CheckDeviceState); 
#else
            base.AddMethod((CheckDeviceStateDelegate)CheckDeviceState); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDCompositionDevice), new __MicroComIDCompositionDeviceVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDCompositionDevice2Proxy : global::MicroCom.Runtime.MicroComProxyBase, IDCompositionDevice2
    {
        public void Commit()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Commit failed", __result);
        }

        public void WaitForCommitCompletion()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 1])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("WaitForCommitCompletion failed", __result);
        }

        public void GetFrameStatistics(DCOMPOSITION_FRAME_STATISTICS* statistics)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, statistics);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetFrameStatistics failed", __result);
        }

        public IDCompositionVisual CreateVisual()
        {
            int __result;
            void* __marshal_visual = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, &__marshal_visual);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateVisual failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVisual>(__marshal_visual, true);
        }

        public IDCompositionSurfaceFactory CreateSurfaceFactory(IUnknown renderingDevice)
        {
            int __result;
            using var __renderingDevice = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(renderingDevice);
            void* __marshal_surfaceFactory = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, __renderingDevice.Pointer, &__marshal_surfaceFactory);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSurfaceFactory failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionSurfaceFactory>(__marshal_surfaceFactory, true);
        }

        public IDCompositionSurface CreateSurface(uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode)
        {
            int __result;
            void* __marshal_surface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, width, height, pixelFormat, alphaMode, &__marshal_surface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSurface failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionSurface>(__marshal_surface, true);
        }

        public IDCompositionVirtualSurface CreateVirtualSurface(uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode)
        {
            int __result;
            void* __marshal_virtualSurface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, initialWidth, initialHeight, pixelFormat, alphaMode, &__marshal_virtualSurface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateVirtualSurface failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVirtualSurface>(__marshal_virtualSurface, true);
        }

        public void* CreateTranslateTransform()
        {
            int __result;
            void* translateTransform = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, &translateTransform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTranslateTransform failed", __result);
            return translateTransform;
        }

        public void* CreateScaleTransform()
        {
            int __result;
            void* scaleTransform = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, &scaleTransform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateScaleTransform failed", __result);
            return scaleTransform;
        }

        public void* CreateRotateTransform()
        {
            int __result;
            void* rotateTransform = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 9])(PPV, &rotateTransform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateRotateTransform failed", __result);
            return rotateTransform;
        }

        public void* CreateSkewTransform()
        {
            int __result;
            void* skewTransform = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 10])(PPV, &skewTransform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSkewTransform failed", __result);
            return skewTransform;
        }

        public void* CreateMatrixTransform()
        {
            int __result;
            void* matrixTransform = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 11])(PPV, &matrixTransform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateMatrixTransform failed", __result);
            return matrixTransform;
        }

        public void* CreateTransformGroup(void* transforms, int elements)
        {
            int __result;
            void* transformGroup = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int, void*, int>)(*PPV)[base.VTableSize + 12])(PPV, transforms, elements, &transformGroup);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTransformGroup failed", __result);
            return transformGroup;
        }

        public void* CreateTranslateTransform3D()
        {
            int __result;
            void* translateTransform3D = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 13])(PPV, &translateTransform3D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTranslateTransform3D failed", __result);
            return translateTransform3D;
        }

        public void* CreateScaleTransform3D()
        {
            int __result;
            void* scaleTransform3D = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 14])(PPV, &scaleTransform3D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateScaleTransform3D failed", __result);
            return scaleTransform3D;
        }

        public void* CreateRotateTransform3D()
        {
            int __result;
            void* rotateTransform3D = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 15])(PPV, &rotateTransform3D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateRotateTransform3D failed", __result);
            return rotateTransform3D;
        }

        public void* CreateMatrixTransform3D()
        {
            int __result;
            void* matrixTransform3D = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 16])(PPV, &matrixTransform3D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateMatrixTransform3D failed", __result);
            return matrixTransform3D;
        }

        public void* CreateTransform3DGroup(void* transforms3D, int elements)
        {
            int __result;
            void* transform3DGroup = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int, void*, int>)(*PPV)[base.VTableSize + 17])(PPV, transforms3D, elements, &transform3DGroup);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTransform3DGroup failed", __result);
            return transform3DGroup;
        }

        public void* CreateEffectGroup()
        {
            int __result;
            void* effectGroup = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 18])(PPV, &effectGroup);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateEffectGroup failed", __result);
            return effectGroup;
        }

        public void* CreateRectangleClip()
        {
            int __result;
            void* clip = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 19])(PPV, &clip);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateRectangleClip failed", __result);
            return clip;
        }

        public void* CreateAnimation()
        {
            int __result;
            void* animation = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 20])(PPV, &animation);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateAnimation failed", __result);
            return animation;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDCompositionDevice2), new Guid("75F6468D-1B8E-447C-9BC6-75FEA80B5B25"), (p, owns) => new __MicroComIDCompositionDevice2Proxy(p, owns));
        }

        protected __MicroComIDCompositionDevice2Proxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 21;
    }

    unsafe class __MicroComIDCompositionDevice2VTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CommitDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Commit(void* @this)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Commit();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int WaitForCommitCompletionDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int WaitForCommitCompletion(void* @this)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.WaitForCommitCompletion();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetFrameStatisticsDelegate(void* @this, DCOMPOSITION_FRAME_STATISTICS* statistics);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetFrameStatistics(void* @this, DCOMPOSITION_FRAME_STATISTICS* statistics)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GetFrameStatistics(statistics);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateVisualDelegate(void* @this, void** visual);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateVisual(void* @this, void** visual)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateVisual();
                        *visual = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSurfaceFactoryDelegate(void* @this, void* renderingDevice, void** surfaceFactory);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSurfaceFactory(void* @this, void* renderingDevice, void** surfaceFactory)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSurfaceFactory(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(renderingDevice, false));
                        *surfaceFactory = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSurfaceDelegate(void* @this, uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** surface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSurface(void* @this, uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** surface)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSurface(width, height, pixelFormat, alphaMode);
                        *surface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateVirtualSurfaceDelegate(void* @this, uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** virtualSurface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateVirtualSurface(void* @this, uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** virtualSurface)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateVirtualSurface(initialWidth, initialHeight, pixelFormat, alphaMode);
                        *virtualSurface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTranslateTransformDelegate(void* @this, void** translateTransform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTranslateTransform(void* @this, void** translateTransform)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTranslateTransform();
                        *translateTransform = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateScaleTransformDelegate(void* @this, void** scaleTransform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateScaleTransform(void* @this, void** scaleTransform)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateScaleTransform();
                        *scaleTransform = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateRotateTransformDelegate(void* @this, void** rotateTransform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateRotateTransform(void* @this, void** rotateTransform)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateRotateTransform();
                        *rotateTransform = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSkewTransformDelegate(void* @this, void** skewTransform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSkewTransform(void* @this, void** skewTransform)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSkewTransform();
                        *skewTransform = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateMatrixTransformDelegate(void* @this, void** matrixTransform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateMatrixTransform(void* @this, void** matrixTransform)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateMatrixTransform();
                        *matrixTransform = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTransformGroupDelegate(void* @this, void* transforms, int elements, void** transformGroup);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTransformGroup(void* @this, void* transforms, int elements, void** transformGroup)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTransformGroup(transforms, elements);
                        *transformGroup = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTranslateTransform3DDelegate(void* @this, void** translateTransform3D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTranslateTransform3D(void* @this, void** translateTransform3D)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTranslateTransform3D();
                        *translateTransform3D = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateScaleTransform3DDelegate(void* @this, void** scaleTransform3D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateScaleTransform3D(void* @this, void** scaleTransform3D)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateScaleTransform3D();
                        *scaleTransform3D = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateRotateTransform3DDelegate(void* @this, void** rotateTransform3D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateRotateTransform3D(void* @this, void** rotateTransform3D)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateRotateTransform3D();
                        *rotateTransform3D = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateMatrixTransform3DDelegate(void* @this, void** matrixTransform3D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateMatrixTransform3D(void* @this, void** matrixTransform3D)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateMatrixTransform3D();
                        *matrixTransform3D = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTransform3DGroupDelegate(void* @this, void* transforms3D, int elements, void** transform3DGroup);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTransform3DGroup(void* @this, void* transforms3D, int elements, void** transform3DGroup)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTransform3DGroup(transforms3D, elements);
                        *transform3DGroup = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateEffectGroupDelegate(void* @this, void** effectGroup);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateEffectGroup(void* @this, void** effectGroup)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateEffectGroup();
                        *effectGroup = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateRectangleClipDelegate(void* @this, void** clip);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateRectangleClip(void* @this, void** clip)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateRectangleClip();
                        *clip = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateAnimationDelegate(void* @this, void** animation);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateAnimation(void* @this, void** animation)
        {
            IDCompositionDevice2 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateAnimation();
                        *animation = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIDCompositionDevice2VTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Commit); 
#else
            base.AddMethod((CommitDelegate)Commit); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&WaitForCommitCompletion); 
#else
            base.AddMethod((WaitForCommitCompletionDelegate)WaitForCommitCompletion); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DCOMPOSITION_FRAME_STATISTICS*, int>)&GetFrameStatistics); 
#else
            base.AddMethod((GetFrameStatisticsDelegate)GetFrameStatistics); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateVisual); 
#else
            base.AddMethod((CreateVisualDelegate)CreateVisual); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateSurfaceFactory); 
#else
            base.AddMethod((CreateSurfaceFactoryDelegate)CreateSurfaceFactory); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void**, int>)&CreateSurface); 
#else
            base.AddMethod((CreateSurfaceDelegate)CreateSurface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void**, int>)&CreateVirtualSurface); 
#else
            base.AddMethod((CreateVirtualSurfaceDelegate)CreateVirtualSurface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateTranslateTransform); 
#else
            base.AddMethod((CreateTranslateTransformDelegate)CreateTranslateTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateScaleTransform); 
#else
            base.AddMethod((CreateScaleTransformDelegate)CreateScaleTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateRotateTransform); 
#else
            base.AddMethod((CreateRotateTransformDelegate)CreateRotateTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateSkewTransform); 
#else
            base.AddMethod((CreateSkewTransformDelegate)CreateSkewTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateMatrixTransform); 
#else
            base.AddMethod((CreateMatrixTransformDelegate)CreateMatrixTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int, void**, int>)&CreateTransformGroup); 
#else
            base.AddMethod((CreateTransformGroupDelegate)CreateTransformGroup); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateTranslateTransform3D); 
#else
            base.AddMethod((CreateTranslateTransform3DDelegate)CreateTranslateTransform3D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateScaleTransform3D); 
#else
            base.AddMethod((CreateScaleTransform3DDelegate)CreateScaleTransform3D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateRotateTransform3D); 
#else
            base.AddMethod((CreateRotateTransform3DDelegate)CreateRotateTransform3D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateMatrixTransform3D); 
#else
            base.AddMethod((CreateMatrixTransform3DDelegate)CreateMatrixTransform3D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int, void**, int>)&CreateTransform3DGroup); 
#else
            base.AddMethod((CreateTransform3DGroupDelegate)CreateTransform3DGroup); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateEffectGroup); 
#else
            base.AddMethod((CreateEffectGroupDelegate)CreateEffectGroup); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateRectangleClip); 
#else
            base.AddMethod((CreateRectangleClipDelegate)CreateRectangleClip); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateAnimation); 
#else
            base.AddMethod((CreateAnimationDelegate)CreateAnimation); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDCompositionDevice2), new __MicroComIDCompositionDevice2VTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDCompositionDevice3Proxy : __MicroComIDCompositionDevice2Proxy, IDCompositionDevice3
    {
        public void* CreateGaussianBlurEffect()
        {
            int __result;
            void* gaussianBlurEffect = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &gaussianBlurEffect);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateGaussianBlurEffect failed", __result);
            return gaussianBlurEffect;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDCompositionDevice3), new Guid("0987cb06-f916-48bf-8d35-ce7641781bd9"), (p, owns) => new __MicroComIDCompositionDevice3Proxy(p, owns));
        }

        protected __MicroComIDCompositionDevice3Proxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIDCompositionDevice3VTable : __MicroComIDCompositionDevice2VTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateGaussianBlurEffectDelegate(void* @this, void** gaussianBlurEffect);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateGaussianBlurEffect(void* @this, void** gaussianBlurEffect)
        {
            IDCompositionDevice3 __target = null;
            try
            {
                {
                    __target = (IDCompositionDevice3)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateGaussianBlurEffect();
                        *gaussianBlurEffect = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIDCompositionDevice3VTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateGaussianBlurEffect); 
#else
            base.AddMethod((CreateGaussianBlurEffectDelegate)CreateGaussianBlurEffect); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDCompositionDevice3), new __MicroComIDCompositionDevice3VTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDCompositionDesktopDeviceProxy : __MicroComIDCompositionDevice2Proxy, IDCompositionDesktopDevice
    {
        public IDCompositionVisual CreateTargetForHwnd(IntPtr hwnd, bool topmost)
        {
            int __result;
            void* __marshal_target = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, bool, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, hwnd, topmost, &__marshal_target);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTargetForHwnd failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVisual>(__marshal_target, true);
        }

        public IUnknown CreateSurfaceFromHandle(IntPtr handle)
        {
            int __result;
            void* __marshal_surface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, handle, &__marshal_surface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSurfaceFromHandle failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_surface, true);
        }

        public IUnknown CreateSurfaceFromHwnd(IntPtr hwnd)
        {
            int __result;
            void* __marshal_surface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, hwnd, &__marshal_surface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSurfaceFromHwnd failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_surface, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDCompositionDesktopDevice), new Guid("5f4633fe-1e08-4cb8-8c75-ce24333f5602"), (p, owns) => new __MicroComIDCompositionDesktopDeviceProxy(p, owns));
        }

        protected __MicroComIDCompositionDesktopDeviceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 3;
    }

    unsafe class __MicroComIDCompositionDesktopDeviceVTable : __MicroComIDCompositionDevice2VTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTargetForHwndDelegate(void* @this, IntPtr hwnd, bool topmost, void** target);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTargetForHwnd(void* @this, IntPtr hwnd, bool topmost, void** target)
        {
            IDCompositionDesktopDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDesktopDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTargetForHwnd(hwnd, topmost);
                        *target = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSurfaceFromHandleDelegate(void* @this, IntPtr handle, void** surface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSurfaceFromHandle(void* @this, IntPtr handle, void** surface)
        {
            IDCompositionDesktopDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDesktopDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSurfaceFromHandle(handle);
                        *surface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSurfaceFromHwndDelegate(void* @this, IntPtr hwnd, void** surface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSurfaceFromHwnd(void* @this, IntPtr hwnd, void** surface)
        {
            IDCompositionDesktopDevice __target = null;
            try
            {
                {
                    __target = (IDCompositionDesktopDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSurfaceFromHwnd(hwnd);
                        *surface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIDCompositionDesktopDeviceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, bool, void**, int>)&CreateTargetForHwnd); 
#else
            base.AddMethod((CreateTargetForHwndDelegate)CreateTargetForHwnd); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreateSurfaceFromHandle); 
#else
            base.AddMethod((CreateSurfaceFromHandleDelegate)CreateSurfaceFromHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreateSurfaceFromHwnd); 
#else
            base.AddMethod((CreateSurfaceFromHwndDelegate)CreateSurfaceFromHwnd); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDCompositionDesktopDevice), new __MicroComIDCompositionDesktopDeviceVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDCompositionVisualProxy : global::MicroCom.Runtime.MicroComProxyBase, IDCompositionVisual
    {
        public void SetOffsetX_IDCompositionAnimation(void* animation)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, animation);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetOffsetX_IDCompositionAnimation failed", __result);
        }

        public void SetOffsetX(float offsetX)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, float, int>)(*PPV)[base.VTableSize + 1])(PPV, offsetX);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetOffsetX failed", __result);
        }

        public void SetOffsetY_IDCompositionAnimation(void* animation)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, animation);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetOffsetY_IDCompositionAnimation failed", __result);
        }

        public void SetOffsetY(float offsetY)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, float, int>)(*PPV)[base.VTableSize + 3])(PPV, offsetY);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetOffsetY failed", __result);
        }

        public void SetTransform_IDCompositionTransform(void* transform)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, transform);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetTransform_IDCompositionTransform failed", __result);
        }

        public void SetTransform(void* matrix)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, matrix);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetTransform failed", __result);
        }

        public void SetTransformParent(IDCompositionVisual visual)
        {
            int __result;
            using var __visual = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(visual);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, __visual.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetTransformParent failed", __result);
        }

        public void SetEffect(void* effect)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, effect);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetEffect failed", __result);
        }

        public void SetBitmapInterpolationMode(int interpolationMode)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 8])(PPV, interpolationMode);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetBitmapInterpolationMode failed", __result);
        }

        public void SetBorderMode(int borderMode)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 9])(PPV, borderMode);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetBorderMode failed", __result);
        }

        public void SetClip_IDCompositionClip(void* clip)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 10])(PPV, clip);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetClip_IDCompositionClip failed", __result);
        }

        public void SetClip(void* rect)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 11])(PPV, rect);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetClip failed", __result);
        }

        public void SetContent(IUnknown content)
        {
            int __result;
            using var __content = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(content);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 12])(PPV, __content.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetContent failed", __result);
        }

        public void AddVisual(IDCompositionVisual visual, int insertAbove, IDCompositionVisual referenceVisual)
        {
            int __result;
            using var __visual = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(visual);
            using var __referenceVisual = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(referenceVisual);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int, void*, int>)(*PPV)[base.VTableSize + 13])(PPV, __visual.Pointer, insertAbove, __referenceVisual.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("AddVisual failed", __result);
        }

        public void RemoveVisual(IDCompositionVisual visual)
        {
            int __result;
            using var __visual = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(visual);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 14])(PPV, __visual.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("RemoveVisual failed", __result);
        }

        public void RemoveAllVisuals()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 15])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("RemoveAllVisuals failed", __result);
        }

        public void SetCompositeMode(int compositeMode)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 16])(PPV, compositeMode);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetCompositeMode failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDCompositionVisual), new Guid("4d93059d-097b-4651-9a60-f0f25116e2f3"), (p, owns) => new __MicroComIDCompositionVisualProxy(p, owns));
        }

        protected __MicroComIDCompositionVisualProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 17;
    }

    unsafe class __MicroComIDCompositionVisualVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetOffsetX_IDCompositionAnimationDelegate(void* @this, void* animation);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetOffsetX_IDCompositionAnimation(void* @this, void* animation)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetOffsetX_IDCompositionAnimation(animation);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetOffsetXDelegate(void* @this, float offsetX);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetOffsetX(void* @this, float offsetX)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetOffsetX(offsetX);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetOffsetY_IDCompositionAnimationDelegate(void* @this, void* animation);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetOffsetY_IDCompositionAnimation(void* @this, void* animation)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetOffsetY_IDCompositionAnimation(animation);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetOffsetYDelegate(void* @this, float offsetY);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetOffsetY(void* @this, float offsetY)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetOffsetY(offsetY);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetTransform_IDCompositionTransformDelegate(void* @this, void* transform);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetTransform_IDCompositionTransform(void* @this, void* transform)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetTransform_IDCompositionTransform(transform);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetTransformDelegate(void* @this, void* matrix);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetTransform(void* @this, void* matrix)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetTransform(matrix);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetTransformParentDelegate(void* @this, void* visual);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetTransformParent(void* @this, void* visual)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetTransformParent(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVisual>(visual, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetEffectDelegate(void* @this, void* effect);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetEffect(void* @this, void* effect)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetEffect(effect);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetBitmapInterpolationModeDelegate(void* @this, int interpolationMode);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetBitmapInterpolationMode(void* @this, int interpolationMode)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetBitmapInterpolationMode(interpolationMode);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetBorderModeDelegate(void* @this, int borderMode);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetBorderMode(void* @this, int borderMode)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetBorderMode(borderMode);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetClip_IDCompositionClipDelegate(void* @this, void* clip);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetClip_IDCompositionClip(void* @this, void* clip)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetClip_IDCompositionClip(clip);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetClipDelegate(void* @this, void* rect);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetClip(void* @this, void* rect)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetClip(rect);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetContentDelegate(void* @this, void* content);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetContent(void* @this, void* content)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetContent(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(content, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int AddVisualDelegate(void* @this, void* visual, int insertAbove, void* referenceVisual);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int AddVisual(void* @this, void* visual, int insertAbove, void* referenceVisual)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.AddVisual(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVisual>(visual, false), insertAbove, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVisual>(referenceVisual, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int RemoveVisualDelegate(void* @this, void* visual);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RemoveVisual(void* @this, void* visual)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RemoveVisual(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVisual>(visual, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int RemoveAllVisualsDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RemoveAllVisuals(void* @this)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RemoveAllVisuals();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetCompositeModeDelegate(void* @this, int compositeMode);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetCompositeMode(void* @this, int compositeMode)
        {
            IDCompositionVisual __target = null;
            try
            {
                {
                    __target = (IDCompositionVisual)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetCompositeMode(compositeMode);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIDCompositionVisualVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetOffsetX_IDCompositionAnimation); 
#else
            base.AddMethod((SetOffsetX_IDCompositionAnimationDelegate)SetOffsetX_IDCompositionAnimation); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, float, int>)&SetOffsetX); 
#else
            base.AddMethod((SetOffsetXDelegate)SetOffsetX); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetOffsetY_IDCompositionAnimation); 
#else
            base.AddMethod((SetOffsetY_IDCompositionAnimationDelegate)SetOffsetY_IDCompositionAnimation); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, float, int>)&SetOffsetY); 
#else
            base.AddMethod((SetOffsetYDelegate)SetOffsetY); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetTransform_IDCompositionTransform); 
#else
            base.AddMethod((SetTransform_IDCompositionTransformDelegate)SetTransform_IDCompositionTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetTransform); 
#else
            base.AddMethod((SetTransformDelegate)SetTransform); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetTransformParent); 
#else
            base.AddMethod((SetTransformParentDelegate)SetTransformParent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetEffect); 
#else
            base.AddMethod((SetEffectDelegate)SetEffect); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetBitmapInterpolationMode); 
#else
            base.AddMethod((SetBitmapInterpolationModeDelegate)SetBitmapInterpolationMode); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetBorderMode); 
#else
            base.AddMethod((SetBorderModeDelegate)SetBorderMode); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetClip_IDCompositionClip); 
#else
            base.AddMethod((SetClip_IDCompositionClipDelegate)SetClip_IDCompositionClip); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetClip); 
#else
            base.AddMethod((SetClipDelegate)SetClip); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetContent); 
#else
            base.AddMethod((SetContentDelegate)SetContent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int, void*, int>)&AddVisual); 
#else
            base.AddMethod((AddVisualDelegate)AddVisual); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&RemoveVisual); 
#else
            base.AddMethod((RemoveVisualDelegate)RemoveVisual); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&RemoveAllVisuals); 
#else
            base.AddMethod((RemoveAllVisualsDelegate)RemoveAllVisuals); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetCompositeMode); 
#else
            base.AddMethod((SetCompositeModeDelegate)SetCompositeMode); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDCompositionVisual), new __MicroComIDCompositionVisualVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDCompositionTargetProxy : global::MicroCom.Runtime.MicroComProxyBase, IDCompositionTarget
    {
        public void SetRoot(IDCompositionVisual visual)
        {
            int __result;
            using var __visual = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(visual);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, __visual.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetRoot failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDCompositionTarget), new Guid("eacdd04c-117e-4e17-88f4-d1b12b0e3d89"), (p, owns) => new __MicroComIDCompositionTargetProxy(p, owns));
        }

        protected __MicroComIDCompositionTargetProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIDCompositionTargetVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetRootDelegate(void* @this, void* visual);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetRoot(void* @this, void* visual)
        {
            IDCompositionTarget __target = null;
            try
            {
                {
                    __target = (IDCompositionTarget)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetRoot(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVisual>(visual, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIDCompositionTargetVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetRoot); 
#else
            base.AddMethod((SetRootDelegate)SetRoot); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDCompositionTarget), new __MicroComIDCompositionTargetVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDCompositionSurfaceFactoryProxy : global::MicroCom.Runtime.MicroComProxyBase, IDCompositionSurfaceFactory
    {
        public IDCompositionSurface CreateSurface(uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode)
        {
            int __result;
            void* __marshal_surface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, width, height, pixelFormat, alphaMode, &__marshal_surface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSurface failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionSurface>(__marshal_surface, true);
        }

        public IDCompositionVirtualSurface CreateVirtualSurface(uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode)
        {
            int __result;
            void* __marshal_virtualSurface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, initialWidth, initialHeight, pixelFormat, alphaMode, &__marshal_virtualSurface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateVirtualSurface failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDCompositionVirtualSurface>(__marshal_virtualSurface, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDCompositionSurfaceFactory), new Guid("e334bc12-3937-4e02-85eb-fcf4eb30d2c8"), (p, owns) => new __MicroComIDCompositionSurfaceFactoryProxy(p, owns));
        }

        protected __MicroComIDCompositionSurfaceFactoryProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIDCompositionSurfaceFactoryVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSurfaceDelegate(void* @this, uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** surface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSurface(void* @this, uint width, uint height, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** surface)
        {
            IDCompositionSurfaceFactory __target = null;
            try
            {
                {
                    __target = (IDCompositionSurfaceFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSurface(width, height, pixelFormat, alphaMode);
                        *surface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateVirtualSurfaceDelegate(void* @this, uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** virtualSurface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateVirtualSurface(void* @this, uint initialWidth, uint initialHeight, Avalonia.Win32.DirectX.DXGI_FORMAT pixelFormat, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE alphaMode, void** virtualSurface)
        {
            IDCompositionSurfaceFactory __target = null;
            try
            {
                {
                    __target = (IDCompositionSurfaceFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateVirtualSurface(initialWidth, initialHeight, pixelFormat, alphaMode);
                        *virtualSurface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIDCompositionSurfaceFactoryVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void**, int>)&CreateSurface); 
#else
            base.AddMethod((CreateSurfaceDelegate)CreateSurface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, uint, Avalonia.Win32.DirectX.DXGI_FORMAT, Avalonia.Win32.DirectX.DXGI_ALPHA_MODE, void**, int>)&CreateVirtualSurface); 
#else
            base.AddMethod((CreateVirtualSurfaceDelegate)CreateVirtualSurface); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDCompositionSurfaceFactory), new __MicroComIDCompositionSurfaceFactoryVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDCompositionSurfaceProxy : global::MicroCom.Runtime.MicroComProxyBase, IDCompositionSurface
    {
        public Avalonia.Win32.Interop.UnmanagedMethods.POINT BeginDraw(Avalonia.Win32.Interop.UnmanagedMethods.RECT* updateRect, System.Guid* iid, void** updateObject)
        {
            int __result;
            Avalonia.Win32.Interop.UnmanagedMethods.POINT updateOffset = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, updateRect, iid, updateObject, &updateOffset);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("BeginDraw failed", __result);
            return updateOffset;
        }

        public void EndDraw()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 1])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("EndDraw failed", __result);
        }

        public void SuspendDraw()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 2])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SuspendDraw failed", __result);
        }

        public void ResumeDraw()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 3])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ResumeDraw failed", __result);
        }

        public void Scroll(Avalonia.Win32.Interop.UnmanagedMethods.RECT* scrollRect, Avalonia.Win32.Interop.UnmanagedMethods.RECT* clipRect, int offsetX, int offsetY)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int, int, int>)(*PPV)[base.VTableSize + 4])(PPV, scrollRect, clipRect, offsetX, offsetY);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Scroll failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDCompositionSurface), new Guid("bb8a4953-2c99-4f5a-96f5-4819027fa3ac"), (p, owns) => new __MicroComIDCompositionSurfaceProxy(p, owns));
        }

        protected __MicroComIDCompositionSurfaceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 5;
    }

    unsafe class __MicroComIDCompositionSurfaceVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int BeginDrawDelegate(void* @this, Avalonia.Win32.Interop.UnmanagedMethods.RECT* updateRect, System.Guid* iid, void** updateObject, Avalonia.Win32.Interop.UnmanagedMethods.POINT* updateOffset);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int BeginDraw(void* @this, Avalonia.Win32.Interop.UnmanagedMethods.RECT* updateRect, System.Guid* iid, void** updateObject, Avalonia.Win32.Interop.UnmanagedMethods.POINT* updateOffset)
        {
            IDCompositionSurface __target = null;
            try
            {
                {
                    __target = (IDCompositionSurface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.BeginDraw(updateRect, iid, updateObject);
                        *updateOffset = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int EndDrawDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int EndDraw(void* @this)
        {
            IDCompositionSurface __target = null;
            try
            {
                {
                    __target = (IDCompositionSurface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.EndDraw();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SuspendDrawDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SuspendDraw(void* @this)
        {
            IDCompositionSurface __target = null;
            try
            {
                {
                    __target = (IDCompositionSurface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SuspendDraw();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ResumeDrawDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ResumeDraw(void* @this)
        {
            IDCompositionSurface __target = null;
            try
            {
                {
                    __target = (IDCompositionSurface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ResumeDraw();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ScrollDelegate(void* @this, Avalonia.Win32.Interop.UnmanagedMethods.RECT* scrollRect, Avalonia.Win32.Interop.UnmanagedMethods.RECT* clipRect, int offsetX, int offsetY);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Scroll(void* @this, Avalonia.Win32.Interop.UnmanagedMethods.RECT* scrollRect, Avalonia.Win32.Interop.UnmanagedMethods.RECT* clipRect, int offsetX, int offsetY)
        {
            IDCompositionSurface __target = null;
            try
            {
                {
                    __target = (IDCompositionSurface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Scroll(scrollRect, clipRect, offsetX, offsetY);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIDCompositionSurfaceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, Avalonia.Win32.Interop.UnmanagedMethods.RECT*, System.Guid*, void**, Avalonia.Win32.Interop.UnmanagedMethods.POINT*, int>)&BeginDraw); 
#else
            base.AddMethod((BeginDrawDelegate)BeginDraw); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&EndDraw); 
#else
            base.AddMethod((EndDrawDelegate)EndDraw); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&SuspendDraw); 
#else
            base.AddMethod((SuspendDrawDelegate)SuspendDraw); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&ResumeDraw); 
#else
            base.AddMethod((ResumeDrawDelegate)ResumeDraw); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, Avalonia.Win32.Interop.UnmanagedMethods.RECT*, Avalonia.Win32.Interop.UnmanagedMethods.RECT*, int, int, int>)&Scroll); 
#else
            base.AddMethod((ScrollDelegate)Scroll); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDCompositionSurface), new __MicroComIDCompositionSurfaceVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDCompositionVirtualSurfaceProxy : __MicroComIDCompositionSurfaceProxy, IDCompositionVirtualSurface
    {
        public void Resize(uint width, uint height)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, uint, int>)(*PPV)[base.VTableSize + 0])(PPV, width, height);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Resize failed", __result);
        }

        public void Trim(void* rectangles, int count)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int, int>)(*PPV)[base.VTableSize + 1])(PPV, rectangles, count);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Trim failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDCompositionVirtualSurface), new Guid("ae471c51-5f53-4a24-8d3e-d0c39c30b3f0"), (p, owns) => new __MicroComIDCompositionVirtualSurfaceProxy(p, owns));
        }

        protected __MicroComIDCompositionVirtualSurfaceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIDCompositionVirtualSurfaceVTable : __MicroComIDCompositionSurfaceVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ResizeDelegate(void* @this, uint width, uint height);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Resize(void* @this, uint width, uint height)
        {
            IDCompositionVirtualSurface __target = null;
            try
            {
                {
                    __target = (IDCompositionVirtualSurface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Resize(width, height);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int TrimDelegate(void* @this, void* rectangles, int count);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Trim(void* @this, void* rectangles, int count)
        {
            IDCompositionVirtualSurface __target = null;
            try
            {
                {
                    __target = (IDCompositionVirtualSurface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Trim(rectangles, count);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIDCompositionVirtualSurfaceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, uint, int>)&Resize); 
#else
            base.AddMethod((ResizeDelegate)Resize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int, int>)&Trim); 
#else
            base.AddMethod((TrimDelegate)Trim); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDCompositionVirtualSurface), new __MicroComIDCompositionVirtualSurfaceVTable().CreateVTable());
    }
}