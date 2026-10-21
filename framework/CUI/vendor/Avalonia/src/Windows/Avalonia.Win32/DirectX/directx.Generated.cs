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

namespace Avalonia.Win32.DirectX
{
    internal enum DXGI_FORMAT
    {
        DXGI_FORMAT_UNKNOWN = 0,
        DXGI_FORMAT_R32G32B32A32_TYPELESS = 1,
        DXGI_FORMAT_R32G32B32A32_FLOAT = 2,
        DXGI_FORMAT_R32G32B32A32_UINT = 3,
        DXGI_FORMAT_R32G32B32A32_SINT = 4,
        DXGI_FORMAT_R32G32B32_TYPELESS = 5,
        DXGI_FORMAT_R32G32B32_FLOAT = 6,
        DXGI_FORMAT_R32G32B32_UINT = 7,
        DXGI_FORMAT_R32G32B32_SINT = 8,
        DXGI_FORMAT_R16G16B16A16_TYPELESS = 9,
        DXGI_FORMAT_R16G16B16A16_FLOAT = 10,
        DXGI_FORMAT_R16G16B16A16_UNORM = 11,
        DXGI_FORMAT_R16G16B16A16_UINT = 12,
        DXGI_FORMAT_R16G16B16A16_SNORM = 13,
        DXGI_FORMAT_R16G16B16A16_SINT = 14,
        DXGI_FORMAT_R32G32_TYPELESS = 15,
        DXGI_FORMAT_R32G32_FLOAT = 16,
        DXGI_FORMAT_R32G32_UINT = 17,
        DXGI_FORMAT_R32G32_SINT = 18,
        DXGI_FORMAT_R32G8X24_TYPELESS = 19,
        DXGI_FORMAT_D32_FLOAT_S8X24_UINT = 20,
        DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS = 21,
        DXGI_FORMAT_X32_TYPELESS_G8X24_UINT = 22,
        DXGI_FORMAT_R10G10B10A2_TYPELESS = 23,
        DXGI_FORMAT_R10G10B10A2_UNORM = 24,
        DXGI_FORMAT_R10G10B10A2_UINT = 25,
        DXGI_FORMAT_R11G11B10_FLOAT = 26,
        DXGI_FORMAT_R8G8B8A8_TYPELESS = 27,
        DXGI_FORMAT_R8G8B8A8_UNORM = 28,
        DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = 29,
        DXGI_FORMAT_R8G8B8A8_UINT = 30,
        DXGI_FORMAT_R8G8B8A8_SNORM = 31,
        DXGI_FORMAT_R8G8B8A8_SINT = 32,
        DXGI_FORMAT_R16G16_TYPELESS = 33,
        DXGI_FORMAT_R16G16_FLOAT = 34,
        DXGI_FORMAT_R16G16_UNORM = 35,
        DXGI_FORMAT_R16G16_UINT = 36,
        DXGI_FORMAT_R16G16_SNORM = 37,
        DXGI_FORMAT_R16G16_SINT = 38,
        DXGI_FORMAT_R32_TYPELESS = 39,
        DXGI_FORMAT_D32_FLOAT = 40,
        DXGI_FORMAT_R32_FLOAT = 41,
        DXGI_FORMAT_R32_UINT = 42,
        DXGI_FORMAT_R32_SINT = 43,
        DXGI_FORMAT_R24G8_TYPELESS = 44,
        DXGI_FORMAT_D24_UNORM_S8_UINT = 45,
        DXGI_FORMAT_R24_UNORM_X8_TYPELESS = 46,
        DXGI_FORMAT_X24_TYPELESS_G8_UINT = 47,
        DXGI_FORMAT_R8G8_TYPELESS = 48,
        DXGI_FORMAT_R8G8_UNORM = 49,
        DXGI_FORMAT_R8G8_UINT = 50,
        DXGI_FORMAT_R8G8_SNORM = 51,
        DXGI_FORMAT_R8G8_SINT = 52,
        DXGI_FORMAT_R16_TYPELESS = 53,
        DXGI_FORMAT_R16_FLOAT = 54,
        DXGI_FORMAT_D16_UNORM = 55,
        DXGI_FORMAT_R16_UNORM = 56,
        DXGI_FORMAT_R16_UINT = 57,
        DXGI_FORMAT_R16_SNORM = 58,
        DXGI_FORMAT_R16_SINT = 59,
        DXGI_FORMAT_R8_TYPELESS = 60,
        DXGI_FORMAT_R8_UNORM = 61,
        DXGI_FORMAT_R8_UINT = 62,
        DXGI_FORMAT_R8_SNORM = 63,
        DXGI_FORMAT_R8_SINT = 64,
        DXGI_FORMAT_A8_UNORM = 65,
        DXGI_FORMAT_R1_UNORM = 66,
        DXGI_FORMAT_R9G9B9E5_SHAREDEXP = 67,
        DXGI_FORMAT_R8G8_B8G8_UNORM = 68,
        DXGI_FORMAT_G8R8_G8B8_UNORM = 69,
        DXGI_FORMAT_BC1_TYPELESS = 70,
        DXGI_FORMAT_BC1_UNORM = 71,
        DXGI_FORMAT_BC1_UNORM_SRGB = 72,
        DXGI_FORMAT_BC2_TYPELESS = 73,
        DXGI_FORMAT_BC2_UNORM = 74,
        DXGI_FORMAT_BC2_UNORM_SRGB = 75,
        DXGI_FORMAT_BC3_TYPELESS = 76,
        DXGI_FORMAT_BC3_UNORM = 77,
        DXGI_FORMAT_BC3_UNORM_SRGB = 78,
        DXGI_FORMAT_BC4_TYPELESS = 79,
        DXGI_FORMAT_BC4_UNORM = 80,
        DXGI_FORMAT_BC4_SNORM = 81,
        DXGI_FORMAT_BC5_TYPELESS = 82,
        DXGI_FORMAT_BC5_UNORM = 83,
        DXGI_FORMAT_BC5_SNORM = 84,
        DXGI_FORMAT_B5G6R5_UNORM = 85,
        DXGI_FORMAT_B5G5R5A1_UNORM = 86,
        DXGI_FORMAT_B8G8R8A8_UNORM = 87,
        DXGI_FORMAT_B8G8R8X8_UNORM = 88,
        DXGI_FORMAT_R10G10B10_XR_BIAS_A2_UNORM = 89,
        DXGI_FORMAT_B8G8R8A8_TYPELESS = 90,
        DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = 91,
        DXGI_FORMAT_B8G8R8X8_TYPELESS = 92,
        DXGI_FORMAT_B8G8R8X8_UNORM_SRGB = 93,
        DXGI_FORMAT_BC6H_TYPELESS = 94,
        DXGI_FORMAT_BC6H_UF16 = 95,
        DXGI_FORMAT_BC6H_SF16 = 96,
        DXGI_FORMAT_BC7_TYPELESS = 97,
        DXGI_FORMAT_BC7_UNORM = 98,
        DXGI_FORMAT_BC7_UNORM_SRGB = 99,
        DXGI_FORMAT_AYUV = 100,
        DXGI_FORMAT_Y410 = 101,
        DXGI_FORMAT_Y416 = 102,
        DXGI_FORMAT_NV12 = 103,
        DXGI_FORMAT_P010 = 104,
        DXGI_FORMAT_P016 = 105,
        DXGI_FORMAT_420_OPAQUE = 106,
        DXGI_FORMAT_YUY2 = 107,
        DXGI_FORMAT_Y210 = 108,
        DXGI_FORMAT_Y216 = 109,
        DXGI_FORMAT_NV11 = 110,
        DXGI_FORMAT_AI44 = 111,
        DXGI_FORMAT_IA44 = 112,
        DXGI_FORMAT_P8 = 113,
        DXGI_FORMAT_A8P8 = 114,
        DXGI_FORMAT_B4G4R4A4_UNORM = 115,
        DXGI_FORMAT_P208 = 130,
        DXGI_FORMAT_V208 = 131,
        DXGI_FORMAT_V408 = 132,
        DXGI_FORMAT_FORCE_UINT = -1
    }

    internal enum DXGI_MODE_SCANLINE_ORDER
    {
        DXGI_MODE_SCANLINE_ORDER_UNSPECIFIED = 0,
        DXGI_MODE_SCANLINE_ORDER_PROGRESSIVE = 1,
        DXGI_MODE_SCANLINE_ORDER_UPPER_FIELD_FIRST = 2,
        DXGI_MODE_SCANLINE_ORDER_LOWER_FIELD_FIRST = 3
    }

    internal enum DXGI_MODE_SCALING
    {
        DXGI_MODE_SCALING_UNSPECIFIED = 0,
        DXGI_MODE_SCALING_CENTERED = 1,
        DXGI_MODE_SCALING_STRETCHED = 2
    }

    internal enum D3D11_FEATURE
    {
        D3D11_FEATURE_THREADING,
        D3D11_FEATURE_DOUBLES,
        D3D11_FEATURE_FORMAT_SUPPORT,
        D3D11_FEATURE_FORMAT_SUPPORT2,
        D3D11_FEATURE_D3D10_X_HARDWARE_OPTIONS,
        D3D11_FEATURE_D3D11_OPTIONS,
        D3D11_FEATURE_ARCHITECTURE_INFO,
        D3D11_FEATURE_D3D9_OPTIONS,
        D3D11_FEATURE_SHADER_MIN_PRECISION_SUPPORT,
        D3D11_FEATURE_D3D9_SHADOW_SUPPORT,
        D3D11_FEATURE_D3D11_OPTIONS1,
        D3D11_FEATURE_D3D9_SIMPLE_INSTANCING_SUPPORT,
        D3D11_FEATURE_MARKER_SUPPORT,
        D3D11_FEATURE_D3D9_OPTIONS1,
        D3D11_FEATURE_D3D11_OPTIONS2,
        D3D11_FEATURE_D3D11_OPTIONS3,
        D3D11_FEATURE_GPU_VIRTUAL_ADDRESS_SUPPORT,
        D3D11_FEATURE_D3D11_OPTIONSS,
        D3D11_FEATURE_SHADER_CACHE
    }

    internal unsafe partial interface IDXGIObject : global::MicroCom.Runtime.IUnknown
    {
        void SetPrivateData(System.Guid* Name, uint DataSize, void** pData);
        void SetPrivateDataInterface(System.Guid* Name, IUnknown pUnknown);
        void* GetPrivateData(System.Guid* Name, uint* pDataSize);
        void* GetParent(System.Guid* riid);
    }

    internal unsafe partial interface IDXGIFactory : IDXGIObject
    {
        int EnumAdapters(uint Adapter, void* ppAdapter);
        void MakeWindowAssociation(IntPtr WindowHandle, uint Flags);
        IntPtr WindowAssociation { get; }

        IDXGISwapChain CreateSwapChain(IUnknown pDevice, DXGI_SWAP_CHAIN_DESC* pDesc);
        IDXGIAdapter CreateSoftwareAdapter(void* Module);
    }

    internal unsafe partial interface IDXGIDeviceSubObject : IDXGIObject
    {
        void* GetDevice(System.Guid* riid);
    }

    internal unsafe partial interface IDXGIAdapter : IDXGIObject
    {
        int EnumOutputs(uint Output, void* ppOutput);
        DXGI_ADAPTER_DESC Desc { get; }

        ulong CheckInterfaceSupport(System.Guid* InterfaceName);
    }

    internal unsafe partial interface IDXGISwapChain : IDXGIDeviceSubObject
    {
        int Present(uint SyncInterval, uint Flags);
        void* GetBuffer(uint Buffer, System.Guid* riid);
        void SetFullscreenState(int Fullscreen, IDXGIOutput pTarget);
        IDXGIOutput GetFullscreenState(int* pFullscreen);
        DXGI_SWAP_CHAIN_DESC Desc { get; }

        void ResizeBuffers(uint BufferCount, uint Width, uint Height, DXGI_FORMAT NewFormat, uint SwapChainFlags);
        void ResizeTarget(DXGI_MODE_DESC* pNewTargetParameters);
        IDXGIOutput ContainingOutput { get; }

        DXGI_FRAME_STATISTICS FrameStatistics { get; }

        uint LastPresentCount { get; }
    }

    internal unsafe partial interface IDXGIDevice : IDXGIObject
    {
        IDXGIAdapter Adapter { get; }

        IDXGISurface CreateSurface(DXGI_SURFACE_DESC* pDesc, uint NumSurfaces, uint Usage, void** pSharedResource);
        void QueryResourceResidency(IUnknown ppResources, DXGI_RESIDENCY* pResidencyStatus, uint NumResources);
        void SetGPUThreadPriority(int Priority);
        int GPUThreadPriority { get; }
    }

    internal unsafe partial interface IDXGIOutput : IDXGIObject
    {
        DXGI_OUTPUT_DESC Desc { get; }

        DXGI_MODE_DESC GetDisplayModeList(DXGI_FORMAT EnumFormat, uint Flags, uint* pNumModes);
        void FindClosestMatchingMode(DXGI_MODE_DESC* pModeToMatch, DXGI_MODE_DESC* pClosestMatch, IUnknown pConcernedDevice);
        void WaitForVBlank();
        void TakeOwnership(IUnknown pDevice, int Exclusive);
        void ReleaseOwnership();
        void GetGammaControlCapabilities(IntPtr pGammaCaps);
        void SetGammaControl(void* pArray);
        void GetGammaControl(IntPtr pArray);
        void SetDisplaySurface(IDXGISurface pScanoutSurface);
        void GetDisplaySurfaceData(IDXGISurface pDestination);
        DXGI_FRAME_STATISTICS FrameStatistics { get; }
    }

    internal unsafe partial interface IDXGISurface : IDXGIDeviceSubObject
    {
        DXGI_SURFACE_DESC Desc { get; }

        void Map(DXGI_MAPPED_RECT* pLockedRect, uint MapFlags);
        void Unmap();
    }

    internal unsafe partial interface IDXGIResource : IDXGIDeviceSubObject
    {
        IntPtr SharedHandle { get; }

        uint Usage { get; }

        void SetEvictionPriority(uint EvictionPriority);
        uint EvictionPriority { get; }
    }

    internal unsafe partial interface IDXGIKeyedMutex : IDXGIDeviceSubObject
    {
        void AcquireSync(ulong Key, uint dwMilliseconds);
        void ReleaseSync(ulong Key);
    }

    internal unsafe partial interface IDXGIFactory1 : IDXGIFactory
    {
        int EnumAdapters1(uint Adapter, void** ppAdapter);
        int IsCurrent();
    }

    internal unsafe partial interface IDXGIAdapter1 : IDXGIAdapter
    {
        DXGI_ADAPTER_DESC1 Desc1 { get; }
    }

    internal unsafe partial interface IDXGIFactory2 : IDXGIFactory1
    {
        int IsWindowedStereoEnabled();
        IDXGISwapChain1 CreateSwapChainForHwnd(IUnknown pDevice, IntPtr hWnd, DXGI_SWAP_CHAIN_DESC1* pDesc, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, IDXGIOutput pRestrictToOutput);
        IDXGISwapChain1 CreateSwapChainForCoreWindow(IUnknown pDevice, IUnknown pWindow, DXGI_SWAP_CHAIN_DESC1* pDesc, IDXGIOutput pRestrictToOutput);
        void GetSharedResourceAdapterLuid(IntPtr hResource, ulong* pLuid);
        int RegisterStereoStatusWindow(IntPtr WindowHandle, uint wMsg);
        int RegisterStereoStatusEvent(IntPtr hEvent);
        void UnregisterStereoStatus(int dwCookie);
        int RegisterOcclusionStatusWindow(IntPtr WindowHandle, uint wMsg);
        int RegisterOcclusionStatusEvent(IntPtr hEvent);
        void UnregisterOcclusionStatus(int dwCookie);
        IDXGISwapChain1 CreateSwapChainForComposition(IUnknown pDevice, DXGI_SWAP_CHAIN_DESC1* pDesc, IDXGIOutput pRestrictToOutput);
    }

    internal unsafe partial interface IDXGISwapChain1 : IDXGISwapChain
    {
        DXGI_SWAP_CHAIN_DESC1 Desc1 { get; }

        DXGI_SWAP_CHAIN_FULLSCREEN_DESC FullscreenDesc { get; }

        IntPtr Hwnd { get; }

        void* GetCoreWindow(System.Guid* refiid);
        void Present1(uint SyncInterval, uint PresentFlags, DXGI_PRESENT_PARAMETERS* pPresentParameters);
        int IsTemporaryMonoSupported();
        IDXGIOutput RestrictToOutput { get; }

        void SetBackgroundColor(DXGI_RGBA* pColor);
        DXGI_RGBA BackgroundColor { get; }

        void SetRotation(DXGI_MODE_ROTATION Rotation);
        DXGI_MODE_ROTATION Rotation { get; }
    }

    internal unsafe partial interface ID3D11Device : global::MicroCom.Runtime.IUnknown
    {
        IUnknown CreateBuffer(IntPtr pDesc, IntPtr pInitialData);
        IUnknown CreateTexture1D(IntPtr pDesc, IntPtr pInitialData);
        ID3D11Texture2D CreateTexture2D(D3D11_TEXTURE2D_DESC* pDesc, IntPtr pInitialData);
        IUnknown CreateTexture3D(IntPtr pDesc, IntPtr pInitialData);
        IUnknown CreateShaderResourceView(IntPtr pResource, IntPtr pDesc);
        IUnknown CreateUnorderedAccessView(IntPtr pResource, IntPtr pDesc);
        IUnknown CreateRenderTargetView(IntPtr pResource, IntPtr pDesc);
        IUnknown CreateDepthStencilView(IntPtr pResource, IntPtr pDesc);
        IUnknown CreateInputLayout(IntPtr pInputElementDescs, uint NumElements, void* pShaderBytecodeWithInputSignature, IntPtr BytecodeLength);
        IUnknown CreateVertexShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage);
        IUnknown CreateGeometryShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage);
        IUnknown CreateGeometryShaderWithStreamOutput(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pSODeclaration, uint NumEntries, uint* pBufferStrides, uint NumStrides, uint RasterizedStream, IntPtr pClassLinkage);
        IUnknown CreatePixelShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage);
        IUnknown CreateHullShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage);
        IUnknown CreateDomainShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage);
        IUnknown CreateComputeShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage);
        IUnknown CreateClassLinkage();
        IUnknown CreateBlendState(IntPtr pBlendStateDesc);
        IUnknown CreateDepthStencilState(IntPtr pDepthStencilDesc);
        IUnknown CreateRasterizerState(IntPtr pRasterizerDesc);
        IUnknown CreateSamplerState(IntPtr pSamplerDesc);
        IUnknown CreateQuery(IntPtr pQueryDesc);
        IUnknown CreatePredicate(IntPtr pPredicateDesc);
        IUnknown CreateCounter(IntPtr pCounterDesc);
        IUnknown CreateDeferredContext(uint ContextFlags);
        IUnknown OpenSharedResource(IntPtr hResource, Guid* ReturnedInterface);
        void CheckFormatSupport(DXGI_FORMAT Format, uint* pFormatSupport);
        void CheckMultisampleQualityLevels(DXGI_FORMAT Format, uint SampleCount, uint* pNumQualityLevels);
        void CheckCounterInfo(IntPtr pCounterInfo);
        void CheckCounter(IntPtr pDesc, IntPtr pType, IntPtr pActiveCounters, IntPtr szName, uint* pNameLength, IntPtr szUnits, uint* pUnitsLength, IntPtr szDescription, uint* pDescriptionLength);
        void CheckFeatureSupport(D3D11_FEATURE Feature, void* pFeatureSupportData, uint FeatureSupportDataSize);
        void GetPrivateData(Guid* guid, uint* pDataSize, void* pData);
        void SetPrivateData(Guid* guid, uint DataSize, IntPtr* pData);
        void SetPrivateDataInterface(Guid* guid, IUnknown pData);
        D3D_FEATURE_LEVEL FeatureLevel { get; }

        uint CreationFlags { get; }

        int DeviceRemovedReason { get; }

        void GetImmediateContext(IntPtr* ppImmediateContext);
        void SetExceptionMode(uint RaiseFlags);
        uint ExceptionMode { get; }
    }

    internal unsafe partial interface ID3D11Device1 : ID3D11Device
    {
        void GetImmediateContext1(void** ppImmediateContext);
        IUnknown CreateDeferredContext1(uint ContextFlags);
        IUnknown CreateBlendState1(void* pBlendStateDesc);
        IUnknown CreateRasterizerState1(void* pRasterizerDesc);
        IUnknown CreateDeviceContextState(uint Flags, void* pFeatureLevels, uint FeatureLevels, uint SDKVersion, System.Guid* EmulatedInterface, void* pChosenFeatureLevel);
        IUnknown OpenSharedResource1(IntPtr hResource, Guid* ReturnedInterface);
        void OpenSharedResourceByName(ushort* lpName, int dwDesiredAccess, System.Guid* returnedInterface, void** ppResource);
    }

    internal unsafe partial interface ID3D11Texture2D : global::MicroCom.Runtime.IUnknown
    {
    }
}

namespace Avalonia.Win32.DirectX.Impl
{
    internal unsafe partial class __MicroComIDXGIObjectProxy : global::MicroCom.Runtime.MicroComProxyBase, IDXGIObject
    {
        public void SetPrivateData(System.Guid* Name, uint DataSize, void** pData)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, uint, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, Name, DataSize, pData);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetPrivateData failed", __result);
        }

        public void SetPrivateDataInterface(System.Guid* Name, IUnknown pUnknown)
        {
            int __result;
            using var __pUnknown = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pUnknown);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, Name, __pUnknown.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetPrivateDataInterface failed", __result);
        }

        public void* GetPrivateData(System.Guid* Name, uint* pDataSize)
        {
            int __result;
            void* pData = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, Name, pDataSize, &pData);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetPrivateData failed", __result);
            return pData;
        }

        public void* GetParent(System.Guid* riid)
        {
            int __result;
            void* ppParent = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, riid, &ppParent);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetParent failed", __result);
            return ppParent;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIObject), new Guid("aec22fb8-76f3-4639-9be0-28eb43a67a2e"), (p, owns) => new __MicroComIDXGIObjectProxy(p, owns));
        }

        protected __MicroComIDXGIObjectProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIDXGIObjectVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetPrivateDataDelegate(void* @this, System.Guid* Name, uint DataSize, void** pData);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetPrivateData(void* @this, System.Guid* Name, uint DataSize, void** pData)
        {
            IDXGIObject __target = null;
            try
            {
                {
                    __target = (IDXGIObject)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetPrivateData(Name, DataSize, pData);
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
        delegate int SetPrivateDataInterfaceDelegate(void* @this, System.Guid* Name, void* pUnknown);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetPrivateDataInterface(void* @this, System.Guid* Name, void* pUnknown)
        {
            IDXGIObject __target = null;
            try
            {
                {
                    __target = (IDXGIObject)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetPrivateDataInterface(Name, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(pUnknown, false));
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
        delegate int GetPrivateDataDelegate(void* @this, System.Guid* Name, uint* pDataSize, void** pData);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetPrivateData(void* @this, System.Guid* Name, uint* pDataSize, void** pData)
        {
            IDXGIObject __target = null;
            try
            {
                {
                    __target = (IDXGIObject)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetPrivateData(Name, pDataSize);
                        *pData = __result;
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
        delegate int GetParentDelegate(void* @this, System.Guid* riid, void** ppParent);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetParent(void* @this, System.Guid* riid, void** ppParent)
        {
            IDXGIObject __target = null;
            try
            {
                {
                    __target = (IDXGIObject)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetParent(riid);
                        *ppParent = __result;
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

        protected __MicroComIDXGIObjectVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, System.Guid*, uint, void**, int>)&SetPrivateData); 
#else
            base.AddMethod((SetPrivateDataDelegate)SetPrivateData); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, System.Guid*, void*, int>)&SetPrivateDataInterface); 
#else
            base.AddMethod((SetPrivateDataInterfaceDelegate)SetPrivateDataInterface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, System.Guid*, uint*, void**, int>)&GetPrivateData); 
#else
            base.AddMethod((GetPrivateDataDelegate)GetPrivateData); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, System.Guid*, void**, int>)&GetParent); 
#else
            base.AddMethod((GetParentDelegate)GetParent); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIObject), new __MicroComIDXGIObjectVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGIFactoryProxy : __MicroComIDXGIObjectProxy, IDXGIFactory
    {
        public int EnumAdapters(uint Adapter, void* ppAdapter)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, Adapter, ppAdapter);
            return __result;
        }

        public void MakeWindowAssociation(IntPtr WindowHandle, uint Flags)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, uint, int>)(*PPV)[base.VTableSize + 1])(PPV, WindowHandle, Flags);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("MakeWindowAssociation failed", __result);
        }

        public IntPtr WindowAssociation
        {
            get
            {
                int __result;
                IntPtr pWindowHandle = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, &pWindowHandle);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetWindowAssociation failed", __result);
                return pWindowHandle;
            }
        }

        public IDXGISwapChain CreateSwapChain(IUnknown pDevice, DXGI_SWAP_CHAIN_DESC* pDesc)
        {
            int __result;
            using var __pDevice = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pDevice);
            void* __marshal_ppSwapChain = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, __pDevice.Pointer, pDesc, &__marshal_ppSwapChain);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSwapChain failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGISwapChain>(__marshal_ppSwapChain, true);
        }

        public IDXGIAdapter CreateSoftwareAdapter(void* Module)
        {
            int __result;
            void* __marshal_ppAdapter = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, Module, &__marshal_ppAdapter);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSoftwareAdapter failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGIAdapter>(__marshal_ppAdapter, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIFactory), new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369"), (p, owns) => new __MicroComIDXGIFactoryProxy(p, owns));
        }

        protected __MicroComIDXGIFactoryProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 5;
    }

    unsafe class __MicroComIDXGIFactoryVTable : __MicroComIDXGIObjectVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int EnumAdaptersDelegate(void* @this, uint Adapter, void* ppAdapter);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int EnumAdapters(void* @this, uint Adapter, void* ppAdapter)
        {
            IDXGIFactory __target = null;
            try
            {
                {
                    __target = (IDXGIFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.EnumAdapters(Adapter, ppAdapter);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int MakeWindowAssociationDelegate(void* @this, IntPtr WindowHandle, uint Flags);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int MakeWindowAssociation(void* @this, IntPtr WindowHandle, uint Flags)
        {
            IDXGIFactory __target = null;
            try
            {
                {
                    __target = (IDXGIFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.MakeWindowAssociation(WindowHandle, Flags);
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
        delegate int GetWindowAssociationDelegate(void* @this, IntPtr* pWindowHandle);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetWindowAssociation(void* @this, IntPtr* pWindowHandle)
        {
            IDXGIFactory __target = null;
            try
            {
                {
                    __target = (IDXGIFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.WindowAssociation;
                        *pWindowHandle = __result;
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
        delegate int CreateSwapChainDelegate(void* @this, void* pDevice, DXGI_SWAP_CHAIN_DESC* pDesc, void** ppSwapChain);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSwapChain(void* @this, void* pDevice, DXGI_SWAP_CHAIN_DESC* pDesc, void** ppSwapChain)
        {
            IDXGIFactory __target = null;
            try
            {
                {
                    __target = (IDXGIFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSwapChain(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(pDevice, false), pDesc);
                        *ppSwapChain = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateSoftwareAdapterDelegate(void* @this, void* Module, void** ppAdapter);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSoftwareAdapter(void* @this, void* Module, void** ppAdapter)
        {
            IDXGIFactory __target = null;
            try
            {
                {
                    __target = (IDXGIFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSoftwareAdapter(Module);
                        *ppAdapter = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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

        protected __MicroComIDXGIFactoryVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, void*, int>)&EnumAdapters); 
#else
            base.AddMethod((EnumAdaptersDelegate)EnumAdapters); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, uint, int>)&MakeWindowAssociation); 
#else
            base.AddMethod((MakeWindowAssociationDelegate)MakeWindowAssociation); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr*, int>)&GetWindowAssociation); 
#else
            base.AddMethod((GetWindowAssociationDelegate)GetWindowAssociation); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, DXGI_SWAP_CHAIN_DESC*, void**, int>)&CreateSwapChain); 
#else
            base.AddMethod((CreateSwapChainDelegate)CreateSwapChain); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateSoftwareAdapter); 
#else
            base.AddMethod((CreateSoftwareAdapterDelegate)CreateSoftwareAdapter); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIFactory), new __MicroComIDXGIFactoryVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGIDeviceSubObjectProxy : __MicroComIDXGIObjectProxy, IDXGIDeviceSubObject
    {
        public void* GetDevice(System.Guid* riid)
        {
            int __result;
            void* ppDevice = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, riid, &ppDevice);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetDevice failed", __result);
            return ppDevice;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIDeviceSubObject), new Guid("3d3e0379-f9de-4d58-bb6c-18d62992f1a6"), (p, owns) => new __MicroComIDXGIDeviceSubObjectProxy(p, owns));
        }

        protected __MicroComIDXGIDeviceSubObjectProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIDXGIDeviceSubObjectVTable : __MicroComIDXGIObjectVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetDeviceDelegate(void* @this, System.Guid* riid, void** ppDevice);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetDevice(void* @this, System.Guid* riid, void** ppDevice)
        {
            IDXGIDeviceSubObject __target = null;
            try
            {
                {
                    __target = (IDXGIDeviceSubObject)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetDevice(riid);
                        *ppDevice = __result;
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

        protected __MicroComIDXGIDeviceSubObjectVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, System.Guid*, void**, int>)&GetDevice); 
#else
            base.AddMethod((GetDeviceDelegate)GetDevice); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIDeviceSubObject), new __MicroComIDXGIDeviceSubObjectVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGIAdapterProxy : __MicroComIDXGIObjectProxy, IDXGIAdapter
    {
        public int EnumOutputs(uint Output, void* ppOutput)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, Output, ppOutput);
            return __result;
        }

        public DXGI_ADAPTER_DESC Desc
        {
            get
            {
                int __result;
                DXGI_ADAPTER_DESC pDesc = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, &pDesc);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetDesc failed", __result);
                return pDesc;
            }
        }

        public ulong CheckInterfaceSupport(System.Guid* InterfaceName)
        {
            int __result;
            ulong pUMDVersion = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, InterfaceName, &pUMDVersion);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CheckInterfaceSupport failed", __result);
            return pUMDVersion;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIAdapter), new Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0"), (p, owns) => new __MicroComIDXGIAdapterProxy(p, owns));
        }

        protected __MicroComIDXGIAdapterProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 3;
    }

    unsafe class __MicroComIDXGIAdapterVTable : __MicroComIDXGIObjectVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int EnumOutputsDelegate(void* @this, uint Output, void* ppOutput);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int EnumOutputs(void* @this, uint Output, void* ppOutput)
        {
            IDXGIAdapter __target = null;
            try
            {
                {
                    __target = (IDXGIAdapter)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.EnumOutputs(Output, ppOutput);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetDescDelegate(void* @this, DXGI_ADAPTER_DESC* pDesc);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetDesc(void* @this, DXGI_ADAPTER_DESC* pDesc)
        {
            IDXGIAdapter __target = null;
            try
            {
                {
                    __target = (IDXGIAdapter)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Desc;
                        *pDesc = __result;
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
        delegate int CheckInterfaceSupportDelegate(void* @this, System.Guid* InterfaceName, ulong* pUMDVersion);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CheckInterfaceSupport(void* @this, System.Guid* InterfaceName, ulong* pUMDVersion)
        {
            IDXGIAdapter __target = null;
            try
            {
                {
                    __target = (IDXGIAdapter)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CheckInterfaceSupport(InterfaceName);
                        *pUMDVersion = __result;
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

        protected __MicroComIDXGIAdapterVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, void*, int>)&EnumOutputs); 
#else
            base.AddMethod((EnumOutputsDelegate)EnumOutputs); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_ADAPTER_DESC*, int>)&GetDesc); 
#else
            base.AddMethod((GetDescDelegate)GetDesc); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, System.Guid*, ulong*, int>)&CheckInterfaceSupport); 
#else
            base.AddMethod((CheckInterfaceSupportDelegate)CheckInterfaceSupport); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIAdapter), new __MicroComIDXGIAdapterVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGISwapChainProxy : __MicroComIDXGIDeviceSubObjectProxy, IDXGISwapChain
    {
        public int Present(uint SyncInterval, uint Flags)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, uint, int>)(*PPV)[base.VTableSize + 0])(PPV, SyncInterval, Flags);
            return __result;
        }

        public void* GetBuffer(uint Buffer, System.Guid* riid)
        {
            int __result;
            void* ppSurface = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, Buffer, riid, &ppSurface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetBuffer failed", __result);
            return ppSurface;
        }

        public void SetFullscreenState(int Fullscreen, IDXGIOutput pTarget)
        {
            int __result;
            using var __pTarget = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pTarget);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, Fullscreen, __pTarget.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetFullscreenState failed", __result);
        }

        public IDXGIOutput GetFullscreenState(int* pFullscreen)
        {
            int __result;
            void* __marshal_ppTarget = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, pFullscreen, &__marshal_ppTarget);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetFullscreenState failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGIOutput>(__marshal_ppTarget, true);
        }

        public DXGI_SWAP_CHAIN_DESC Desc
        {
            get
            {
                int __result;
                DXGI_SWAP_CHAIN_DESC pDesc = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, &pDesc);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetDesc failed", __result);
                return pDesc;
            }
        }

        public void ResizeBuffers(uint BufferCount, uint Width, uint Height, DXGI_FORMAT NewFormat, uint SwapChainFlags)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, uint, uint, DXGI_FORMAT, uint, int>)(*PPV)[base.VTableSize + 5])(PPV, BufferCount, Width, Height, NewFormat, SwapChainFlags);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ResizeBuffers failed", __result);
        }

        public void ResizeTarget(DXGI_MODE_DESC* pNewTargetParameters)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, pNewTargetParameters);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ResizeTarget failed", __result);
        }

        public IDXGIOutput ContainingOutput
        {
            get
            {
                int __result;
                void* __marshal_ppOutput = null;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, &__marshal_ppOutput);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetContainingOutput failed", __result);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGIOutput>(__marshal_ppOutput, true);
            }
        }

        public DXGI_FRAME_STATISTICS FrameStatistics
        {
            get
            {
                int __result;
                DXGI_FRAME_STATISTICS pStats = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, &pStats);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetFrameStatistics failed", __result);
                return pStats;
            }
        }

        public uint LastPresentCount
        {
            get
            {
                int __result;
                uint pLastPresentCount = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 9])(PPV, &pLastPresentCount);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetLastPresentCount failed", __result);
                return pLastPresentCount;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGISwapChain), new Guid("310d36a0-d2e7-4c0a-aa04-6a9d23b8886a"), (p, owns) => new __MicroComIDXGISwapChainProxy(p, owns));
        }

        protected __MicroComIDXGISwapChainProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 10;
    }

    unsafe class __MicroComIDXGISwapChainVTable : __MicroComIDXGIDeviceSubObjectVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int PresentDelegate(void* @this, uint SyncInterval, uint Flags);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Present(void* @this, uint SyncInterval, uint Flags)
        {
            IDXGISwapChain __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Present(SyncInterval, Flags);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetBufferDelegate(void* @this, uint Buffer, System.Guid* riid, void** ppSurface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetBuffer(void* @this, uint Buffer, System.Guid* riid, void** ppSurface)
        {
            IDXGISwapChain __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetBuffer(Buffer, riid);
                        *ppSurface = __result;
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
        delegate int SetFullscreenStateDelegate(void* @this, int Fullscreen, void* pTarget);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetFullscreenState(void* @this, int Fullscreen, void* pTarget)
        {
            IDXGISwapChain __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetFullscreenState(Fullscreen, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGIOutput>(pTarget, false));
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
        delegate int GetFullscreenStateDelegate(void* @this, int* pFullscreen, void** ppTarget);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetFullscreenState(void* @this, int* pFullscreen, void** ppTarget)
        {
            IDXGISwapChain __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetFullscreenState(pFullscreen);
                        *ppTarget = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int GetDescDelegate(void* @this, DXGI_SWAP_CHAIN_DESC* pDesc);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetDesc(void* @this, DXGI_SWAP_CHAIN_DESC* pDesc)
        {
            IDXGISwapChain __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Desc;
                        *pDesc = __result;
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
        delegate int ResizeBuffersDelegate(void* @this, uint BufferCount, uint Width, uint Height, DXGI_FORMAT NewFormat, uint SwapChainFlags);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ResizeBuffers(void* @this, uint BufferCount, uint Width, uint Height, DXGI_FORMAT NewFormat, uint SwapChainFlags)
        {
            IDXGISwapChain __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ResizeBuffers(BufferCount, Width, Height, NewFormat, SwapChainFlags);
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
        delegate int ResizeTargetDelegate(void* @this, DXGI_MODE_DESC* pNewTargetParameters);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ResizeTarget(void* @this, DXGI_MODE_DESC* pNewTargetParameters)
        {
            IDXGISwapChain __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ResizeTarget(pNewTargetParameters);
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
        delegate int GetContainingOutputDelegate(void* @this, void** ppOutput);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetContainingOutput(void* @this, void** ppOutput)
        {
            IDXGISwapChain __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ContainingOutput;
                        *ppOutput = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int GetFrameStatisticsDelegate(void* @this, DXGI_FRAME_STATISTICS* pStats);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetFrameStatistics(void* @this, DXGI_FRAME_STATISTICS* pStats)
        {
            IDXGISwapChain __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.FrameStatistics;
                        *pStats = __result;
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
        delegate int GetLastPresentCountDelegate(void* @this, uint* pLastPresentCount);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetLastPresentCount(void* @this, uint* pLastPresentCount)
        {
            IDXGISwapChain __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.LastPresentCount;
                        *pLastPresentCount = __result;
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

        protected __MicroComIDXGISwapChainVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, uint, int>)&Present); 
#else
            base.AddMethod((PresentDelegate)Present); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, System.Guid*, void**, int>)&GetBuffer); 
#else
            base.AddMethod((GetBufferDelegate)GetBuffer); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void*, int>)&SetFullscreenState); 
#else
            base.AddMethod((SetFullscreenStateDelegate)SetFullscreenState); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int*, void**, int>)&GetFullscreenState); 
#else
            base.AddMethod((GetFullscreenStateDelegate)GetFullscreenState); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_SWAP_CHAIN_DESC*, int>)&GetDesc); 
#else
            base.AddMethod((GetDescDelegate)GetDesc); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, uint, uint, DXGI_FORMAT, uint, int>)&ResizeBuffers); 
#else
            base.AddMethod((ResizeBuffersDelegate)ResizeBuffers); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_MODE_DESC*, int>)&ResizeTarget); 
#else
            base.AddMethod((ResizeTargetDelegate)ResizeTarget); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&GetContainingOutput); 
#else
            base.AddMethod((GetContainingOutputDelegate)GetContainingOutput); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_FRAME_STATISTICS*, int>)&GetFrameStatistics); 
#else
            base.AddMethod((GetFrameStatisticsDelegate)GetFrameStatistics); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint*, int>)&GetLastPresentCount); 
#else
            base.AddMethod((GetLastPresentCountDelegate)GetLastPresentCount); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGISwapChain), new __MicroComIDXGISwapChainVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGIDeviceProxy : __MicroComIDXGIObjectProxy, IDXGIDevice
    {
        public IDXGIAdapter Adapter
        {
            get
            {
                int __result;
                void* __marshal_pAdapter = null;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &__marshal_pAdapter);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetAdapter failed", __result);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGIAdapter>(__marshal_pAdapter, true);
            }
        }

        public IDXGISurface CreateSurface(DXGI_SURFACE_DESC* pDesc, uint NumSurfaces, uint Usage, void** pSharedResource)
        {
            int __result;
            void* __marshal_ppSurface = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, uint, uint, void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, pDesc, NumSurfaces, Usage, pSharedResource, &__marshal_ppSurface);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSurface failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGISurface>(__marshal_ppSurface, true);
        }

        public void QueryResourceResidency(IUnknown ppResources, DXGI_RESIDENCY* pResidencyStatus, uint NumResources)
        {
            int __result;
            using var __ppResources = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(ppResources);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, uint, int>)(*PPV)[base.VTableSize + 2])(PPV, __ppResources.Pointer, pResidencyStatus, NumResources);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("QueryResourceResidency failed", __result);
        }

        public void SetGPUThreadPriority(int Priority)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 3])(PPV, Priority);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetGPUThreadPriority failed", __result);
        }

        public int GPUThreadPriority
        {
            get
            {
                int __result;
                int pPriority = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, &pPriority);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetGPUThreadPriority failed", __result);
                return pPriority;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIDevice), new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"), (p, owns) => new __MicroComIDXGIDeviceProxy(p, owns));
        }

        protected __MicroComIDXGIDeviceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 5;
    }

    unsafe class __MicroComIDXGIDeviceVTable : __MicroComIDXGIObjectVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetAdapterDelegate(void* @this, void** pAdapter);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetAdapter(void* @this, void** pAdapter)
        {
            IDXGIDevice __target = null;
            try
            {
                {
                    __target = (IDXGIDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Adapter;
                        *pAdapter = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateSurfaceDelegate(void* @this, DXGI_SURFACE_DESC* pDesc, uint NumSurfaces, uint Usage, void** pSharedResource, void** ppSurface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSurface(void* @this, DXGI_SURFACE_DESC* pDesc, uint NumSurfaces, uint Usage, void** pSharedResource, void** ppSurface)
        {
            IDXGIDevice __target = null;
            try
            {
                {
                    __target = (IDXGIDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSurface(pDesc, NumSurfaces, Usage, pSharedResource);
                        *ppSurface = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int QueryResourceResidencyDelegate(void* @this, void* ppResources, DXGI_RESIDENCY* pResidencyStatus, uint NumResources);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int QueryResourceResidency(void* @this, void* ppResources, DXGI_RESIDENCY* pResidencyStatus, uint NumResources)
        {
            IDXGIDevice __target = null;
            try
            {
                {
                    __target = (IDXGIDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.QueryResourceResidency(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(ppResources, false), pResidencyStatus, NumResources);
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
        delegate int SetGPUThreadPriorityDelegate(void* @this, int Priority);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetGPUThreadPriority(void* @this, int Priority)
        {
            IDXGIDevice __target = null;
            try
            {
                {
                    __target = (IDXGIDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetGPUThreadPriority(Priority);
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
        delegate int GetGPUThreadPriorityDelegate(void* @this, int* pPriority);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetGPUThreadPriority(void* @this, int* pPriority)
        {
            IDXGIDevice __target = null;
            try
            {
                {
                    __target = (IDXGIDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GPUThreadPriority;
                        *pPriority = __result;
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

        protected __MicroComIDXGIDeviceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&GetAdapter); 
#else
            base.AddMethod((GetAdapterDelegate)GetAdapter); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_SURFACE_DESC*, uint, uint, void**, void**, int>)&CreateSurface); 
#else
            base.AddMethod((CreateSurfaceDelegate)CreateSurface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, DXGI_RESIDENCY*, uint, int>)&QueryResourceResidency); 
#else
            base.AddMethod((QueryResourceResidencyDelegate)QueryResourceResidency); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetGPUThreadPriority); 
#else
            base.AddMethod((SetGPUThreadPriorityDelegate)SetGPUThreadPriority); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int*, int>)&GetGPUThreadPriority); 
#else
            base.AddMethod((GetGPUThreadPriorityDelegate)GetGPUThreadPriority); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIDevice), new __MicroComIDXGIDeviceVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGIOutputProxy : __MicroComIDXGIObjectProxy, IDXGIOutput
    {
        public DXGI_OUTPUT_DESC Desc
        {
            get
            {
                int __result;
                DXGI_OUTPUT_DESC pDesc = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &pDesc);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetDesc failed", __result);
                return pDesc;
            }
        }

        public DXGI_MODE_DESC GetDisplayModeList(DXGI_FORMAT EnumFormat, uint Flags, uint* pNumModes)
        {
            int __result;
            DXGI_MODE_DESC pDesc = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, DXGI_FORMAT, uint, void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, EnumFormat, Flags, pNumModes, &pDesc);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetDisplayModeList failed", __result);
            return pDesc;
        }

        public void FindClosestMatchingMode(DXGI_MODE_DESC* pModeToMatch, DXGI_MODE_DESC* pClosestMatch, IUnknown pConcernedDevice)
        {
            int __result;
            using var __pConcernedDevice = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pConcernedDevice);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, pModeToMatch, pClosestMatch, __pConcernedDevice.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("FindClosestMatchingMode failed", __result);
        }

        public void WaitForVBlank()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 3])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("WaitForVBlank failed", __result);
        }

        public void TakeOwnership(IUnknown pDevice, int Exclusive)
        {
            int __result;
            using var __pDevice = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pDevice);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int, int>)(*PPV)[base.VTableSize + 4])(PPV, __pDevice.Pointer, Exclusive);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("TakeOwnership failed", __result);
        }

        public void ReleaseOwnership()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 5])(PPV);
        }

        public void GetGammaControlCapabilities(IntPtr pGammaCaps)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, int>)(*PPV)[base.VTableSize + 6])(PPV, pGammaCaps);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetGammaControlCapabilities failed", __result);
        }

        public void SetGammaControl(void* pArray)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, pArray);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetGammaControl failed", __result);
        }

        public void GetGammaControl(IntPtr pArray)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, int>)(*PPV)[base.VTableSize + 8])(PPV, pArray);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetGammaControl failed", __result);
        }

        public void SetDisplaySurface(IDXGISurface pScanoutSurface)
        {
            int __result;
            using var __pScanoutSurface = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pScanoutSurface);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 9])(PPV, __pScanoutSurface.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetDisplaySurface failed", __result);
        }

        public void GetDisplaySurfaceData(IDXGISurface pDestination)
        {
            int __result;
            using var __pDestination = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pDestination);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 10])(PPV, __pDestination.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetDisplaySurfaceData failed", __result);
        }

        public DXGI_FRAME_STATISTICS FrameStatistics
        {
            get
            {
                int __result;
                DXGI_FRAME_STATISTICS pStats = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 11])(PPV, &pStats);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetFrameStatistics failed", __result);
                return pStats;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIOutput), new Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa"), (p, owns) => new __MicroComIDXGIOutputProxy(p, owns));
        }

        protected __MicroComIDXGIOutputProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 12;
    }

    unsafe class __MicroComIDXGIOutputVTable : __MicroComIDXGIObjectVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetDescDelegate(void* @this, DXGI_OUTPUT_DESC* pDesc);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetDesc(void* @this, DXGI_OUTPUT_DESC* pDesc)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Desc;
                        *pDesc = __result;
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
        delegate int GetDisplayModeListDelegate(void* @this, DXGI_FORMAT EnumFormat, uint Flags, uint* pNumModes, DXGI_MODE_DESC* pDesc);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetDisplayModeList(void* @this, DXGI_FORMAT EnumFormat, uint Flags, uint* pNumModes, DXGI_MODE_DESC* pDesc)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetDisplayModeList(EnumFormat, Flags, pNumModes);
                        *pDesc = __result;
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
        delegate int FindClosestMatchingModeDelegate(void* @this, DXGI_MODE_DESC* pModeToMatch, DXGI_MODE_DESC* pClosestMatch, void* pConcernedDevice);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int FindClosestMatchingMode(void* @this, DXGI_MODE_DESC* pModeToMatch, DXGI_MODE_DESC* pClosestMatch, void* pConcernedDevice)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.FindClosestMatchingMode(pModeToMatch, pClosestMatch, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(pConcernedDevice, false));
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
        delegate int WaitForVBlankDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int WaitForVBlank(void* @this)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.WaitForVBlank();
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
        delegate int TakeOwnershipDelegate(void* @this, void* pDevice, int Exclusive);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int TakeOwnership(void* @this, void* pDevice, int Exclusive)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.TakeOwnership(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(pDevice, false), Exclusive);
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
        delegate void ReleaseOwnershipDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ReleaseOwnership(void* @this)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ReleaseOwnership();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetGammaControlCapabilitiesDelegate(void* @this, IntPtr pGammaCaps);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetGammaControlCapabilities(void* @this, IntPtr pGammaCaps)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GetGammaControlCapabilities(pGammaCaps);
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
        delegate int SetGammaControlDelegate(void* @this, void* pArray);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetGammaControl(void* @this, void* pArray)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetGammaControl(pArray);
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
        delegate int GetGammaControlDelegate(void* @this, IntPtr pArray);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetGammaControl(void* @this, IntPtr pArray)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GetGammaControl(pArray);
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
        delegate int SetDisplaySurfaceDelegate(void* @this, void* pScanoutSurface);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetDisplaySurface(void* @this, void* pScanoutSurface)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetDisplaySurface(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGISurface>(pScanoutSurface, false));
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
        delegate int GetDisplaySurfaceDataDelegate(void* @this, void* pDestination);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetDisplaySurfaceData(void* @this, void* pDestination)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GetDisplaySurfaceData(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGISurface>(pDestination, false));
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
        delegate int GetFrameStatisticsDelegate(void* @this, DXGI_FRAME_STATISTICS* pStats);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetFrameStatistics(void* @this, DXGI_FRAME_STATISTICS* pStats)
        {
            IDXGIOutput __target = null;
            try
            {
                {
                    __target = (IDXGIOutput)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.FrameStatistics;
                        *pStats = __result;
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

        protected __MicroComIDXGIOutputVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_OUTPUT_DESC*, int>)&GetDesc); 
#else
            base.AddMethod((GetDescDelegate)GetDesc); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_FORMAT, uint, uint*, DXGI_MODE_DESC*, int>)&GetDisplayModeList); 
#else
            base.AddMethod((GetDisplayModeListDelegate)GetDisplayModeList); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_MODE_DESC*, DXGI_MODE_DESC*, void*, int>)&FindClosestMatchingMode); 
#else
            base.AddMethod((FindClosestMatchingModeDelegate)FindClosestMatchingMode); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&WaitForVBlank); 
#else
            base.AddMethod((WaitForVBlankDelegate)WaitForVBlank); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int, int>)&TakeOwnership); 
#else
            base.AddMethod((TakeOwnershipDelegate)TakeOwnership); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&ReleaseOwnership); 
#else
            base.AddMethod((ReleaseOwnershipDelegate)ReleaseOwnership); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, int>)&GetGammaControlCapabilities); 
#else
            base.AddMethod((GetGammaControlCapabilitiesDelegate)GetGammaControlCapabilities); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetGammaControl); 
#else
            base.AddMethod((SetGammaControlDelegate)SetGammaControl); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, int>)&GetGammaControl); 
#else
            base.AddMethod((GetGammaControlDelegate)GetGammaControl); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetDisplaySurface); 
#else
            base.AddMethod((SetDisplaySurfaceDelegate)SetDisplaySurface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&GetDisplaySurfaceData); 
#else
            base.AddMethod((GetDisplaySurfaceDataDelegate)GetDisplaySurfaceData); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_FRAME_STATISTICS*, int>)&GetFrameStatistics); 
#else
            base.AddMethod((GetFrameStatisticsDelegate)GetFrameStatistics); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIOutput), new __MicroComIDXGIOutputVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGISurfaceProxy : __MicroComIDXGIDeviceSubObjectProxy, IDXGISurface
    {
        public DXGI_SURFACE_DESC Desc
        {
            get
            {
                int __result;
                DXGI_SURFACE_DESC pDesc = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &pDesc);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetDesc failed", __result);
                return pDesc;
            }
        }

        public void Map(DXGI_MAPPED_RECT* pLockedRect, uint MapFlags)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, uint, int>)(*PPV)[base.VTableSize + 1])(PPV, pLockedRect, MapFlags);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Map failed", __result);
        }

        public void Unmap()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 2])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Unmap failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGISurface), new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec"), (p, owns) => new __MicroComIDXGISurfaceProxy(p, owns));
        }

        protected __MicroComIDXGISurfaceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 3;
    }

    unsafe class __MicroComIDXGISurfaceVTable : __MicroComIDXGIDeviceSubObjectVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetDescDelegate(void* @this, DXGI_SURFACE_DESC* pDesc);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetDesc(void* @this, DXGI_SURFACE_DESC* pDesc)
        {
            IDXGISurface __target = null;
            try
            {
                {
                    __target = (IDXGISurface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Desc;
                        *pDesc = __result;
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
        delegate int MapDelegate(void* @this, DXGI_MAPPED_RECT* pLockedRect, uint MapFlags);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Map(void* @this, DXGI_MAPPED_RECT* pLockedRect, uint MapFlags)
        {
            IDXGISurface __target = null;
            try
            {
                {
                    __target = (IDXGISurface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Map(pLockedRect, MapFlags);
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
        delegate int UnmapDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Unmap(void* @this)
        {
            IDXGISurface __target = null;
            try
            {
                {
                    __target = (IDXGISurface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Unmap();
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

        protected __MicroComIDXGISurfaceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_SURFACE_DESC*, int>)&GetDesc); 
#else
            base.AddMethod((GetDescDelegate)GetDesc); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_MAPPED_RECT*, uint, int>)&Map); 
#else
            base.AddMethod((MapDelegate)Map); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Unmap); 
#else
            base.AddMethod((UnmapDelegate)Unmap); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGISurface), new __MicroComIDXGISurfaceVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGIResourceProxy : __MicroComIDXGIDeviceSubObjectProxy, IDXGIResource
    {
        public IntPtr SharedHandle
        {
            get
            {
                int __result;
                IntPtr pSharedHandle = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &pSharedHandle);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetSharedHandle failed", __result);
                return pSharedHandle;
            }
        }

        public uint Usage
        {
            get
            {
                int __result;
                uint pUsage = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, &pUsage);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetUsage failed", __result);
                return pUsage;
            }
        }

        public void SetEvictionPriority(uint EvictionPriority)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, int>)(*PPV)[base.VTableSize + 2])(PPV, EvictionPriority);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetEvictionPriority failed", __result);
        }

        public uint EvictionPriority
        {
            get
            {
                int __result;
                uint pEvictionPriority = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, &pEvictionPriority);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetEvictionPriority failed", __result);
                return pEvictionPriority;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIResource), new Guid(" 035f3ab4-482e-4e50-b41f-8a7f8bd8960b"), (p, owns) => new __MicroComIDXGIResourceProxy(p, owns));
        }

        protected __MicroComIDXGIResourceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIDXGIResourceVTable : __MicroComIDXGIDeviceSubObjectVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetSharedHandleDelegate(void* @this, IntPtr* pSharedHandle);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetSharedHandle(void* @this, IntPtr* pSharedHandle)
        {
            IDXGIResource __target = null;
            try
            {
                {
                    __target = (IDXGIResource)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.SharedHandle;
                        *pSharedHandle = __result;
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
        delegate int GetUsageDelegate(void* @this, uint* pUsage);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetUsage(void* @this, uint* pUsage)
        {
            IDXGIResource __target = null;
            try
            {
                {
                    __target = (IDXGIResource)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Usage;
                        *pUsage = __result;
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
        delegate int SetEvictionPriorityDelegate(void* @this, uint EvictionPriority);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetEvictionPriority(void* @this, uint EvictionPriority)
        {
            IDXGIResource __target = null;
            try
            {
                {
                    __target = (IDXGIResource)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetEvictionPriority(EvictionPriority);
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
        delegate int GetEvictionPriorityDelegate(void* @this, uint* pEvictionPriority);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetEvictionPriority(void* @this, uint* pEvictionPriority)
        {
            IDXGIResource __target = null;
            try
            {
                {
                    __target = (IDXGIResource)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.EvictionPriority;
                        *pEvictionPriority = __result;
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

        protected __MicroComIDXGIResourceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr*, int>)&GetSharedHandle); 
#else
            base.AddMethod((GetSharedHandleDelegate)GetSharedHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint*, int>)&GetUsage); 
#else
            base.AddMethod((GetUsageDelegate)GetUsage); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, int>)&SetEvictionPriority); 
#else
            base.AddMethod((SetEvictionPriorityDelegate)SetEvictionPriority); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint*, int>)&GetEvictionPriority); 
#else
            base.AddMethod((GetEvictionPriorityDelegate)GetEvictionPriority); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIResource), new __MicroComIDXGIResourceVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGIKeyedMutexProxy : __MicroComIDXGIDeviceSubObjectProxy, IDXGIKeyedMutex
    {
        public void AcquireSync(ulong Key, uint dwMilliseconds)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, ulong, uint, int>)(*PPV)[base.VTableSize + 0])(PPV, Key, dwMilliseconds);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("AcquireSync failed", __result);
        }

        public void ReleaseSync(ulong Key)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, ulong, int>)(*PPV)[base.VTableSize + 1])(PPV, Key);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ReleaseSync failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIKeyedMutex), new Guid(" 9d8e1289-d7b3-465f-8126-250e349af85d"), (p, owns) => new __MicroComIDXGIKeyedMutexProxy(p, owns));
        }

        protected __MicroComIDXGIKeyedMutexProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIDXGIKeyedMutexVTable : __MicroComIDXGIDeviceSubObjectVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int AcquireSyncDelegate(void* @this, ulong Key, uint dwMilliseconds);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int AcquireSync(void* @this, ulong Key, uint dwMilliseconds)
        {
            IDXGIKeyedMutex __target = null;
            try
            {
                {
                    __target = (IDXGIKeyedMutex)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.AcquireSync(Key, dwMilliseconds);
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
        delegate int ReleaseSyncDelegate(void* @this, ulong Key);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ReleaseSync(void* @this, ulong Key)
        {
            IDXGIKeyedMutex __target = null;
            try
            {
                {
                    __target = (IDXGIKeyedMutex)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ReleaseSync(Key);
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

        protected __MicroComIDXGIKeyedMutexVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, ulong, uint, int>)&AcquireSync); 
#else
            base.AddMethod((AcquireSyncDelegate)AcquireSync); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, ulong, int>)&ReleaseSync); 
#else
            base.AddMethod((ReleaseSyncDelegate)ReleaseSync); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIKeyedMutex), new __MicroComIDXGIKeyedMutexVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGIFactory1Proxy : __MicroComIDXGIFactoryProxy, IDXGIFactory1
    {
        public int EnumAdapters1(uint Adapter, void** ppAdapter)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, Adapter, ppAdapter);
            return __result;
        }

        public int IsCurrent()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 1])(PPV);
            return __result;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIFactory1), new Guid("770aae78-f26f-4dba-a829-253c83d1b387"), (p, owns) => new __MicroComIDXGIFactory1Proxy(p, owns));
        }

        protected __MicroComIDXGIFactory1Proxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIDXGIFactory1VTable : __MicroComIDXGIFactoryVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int EnumAdapters1Delegate(void* @this, uint Adapter, void** ppAdapter);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int EnumAdapters1(void* @this, uint Adapter, void** ppAdapter)
        {
            IDXGIFactory1 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.EnumAdapters1(Adapter, ppAdapter);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsCurrentDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsCurrent(void* @this)
        {
            IDXGIFactory1 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsCurrent();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIDXGIFactory1VTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)&EnumAdapters1); 
#else
            base.AddMethod((EnumAdapters1Delegate)EnumAdapters1); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsCurrent); 
#else
            base.AddMethod((IsCurrentDelegate)IsCurrent); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIFactory1), new __MicroComIDXGIFactory1VTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGIAdapter1Proxy : __MicroComIDXGIAdapterProxy, IDXGIAdapter1
    {
        public DXGI_ADAPTER_DESC1 Desc1
        {
            get
            {
                int __result;
                DXGI_ADAPTER_DESC1 pDesc = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &pDesc);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetDesc1 failed", __result);
                return pDesc;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIAdapter1), new Guid("29038f61-3839-4626-91fd-086879011a05"), (p, owns) => new __MicroComIDXGIAdapter1Proxy(p, owns));
        }

        protected __MicroComIDXGIAdapter1Proxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIDXGIAdapter1VTable : __MicroComIDXGIAdapterVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetDesc1Delegate(void* @this, DXGI_ADAPTER_DESC1* pDesc);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetDesc1(void* @this, DXGI_ADAPTER_DESC1* pDesc)
        {
            IDXGIAdapter1 __target = null;
            try
            {
                {
                    __target = (IDXGIAdapter1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Desc1;
                        *pDesc = __result;
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

        protected __MicroComIDXGIAdapter1VTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_ADAPTER_DESC1*, int>)&GetDesc1); 
#else
            base.AddMethod((GetDesc1Delegate)GetDesc1); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIAdapter1), new __MicroComIDXGIAdapter1VTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGIFactory2Proxy : __MicroComIDXGIFactory1Proxy, IDXGIFactory2
    {
        public int IsWindowedStereoEnabled()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
            return __result;
        }

        public IDXGISwapChain1 CreateSwapChainForHwnd(IUnknown pDevice, IntPtr hWnd, DXGI_SWAP_CHAIN_DESC1* pDesc, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, IDXGIOutput pRestrictToOutput)
        {
            int __result;
            using var __pDevice = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pDevice);
            using var __pRestrictToOutput = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pRestrictToOutput);
            void* __marshal_ppSwapChain = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, IntPtr, void*, void*, void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, __pDevice.Pointer, hWnd, pDesc, pFullscreenDesc, __pRestrictToOutput.Pointer, &__marshal_ppSwapChain);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSwapChainForHwnd failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGISwapChain1>(__marshal_ppSwapChain, true);
        }

        public IDXGISwapChain1 CreateSwapChainForCoreWindow(IUnknown pDevice, IUnknown pWindow, DXGI_SWAP_CHAIN_DESC1* pDesc, IDXGIOutput pRestrictToOutput)
        {
            int __result;
            using var __pDevice = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pDevice);
            using var __pWindow = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pWindow);
            using var __pRestrictToOutput = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pRestrictToOutput);
            void* __marshal_ppSwapChain = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, __pDevice.Pointer, __pWindow.Pointer, pDesc, __pRestrictToOutput.Pointer, &__marshal_ppSwapChain);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSwapChainForCoreWindow failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGISwapChain1>(__marshal_ppSwapChain, true);
        }

        public void GetSharedResourceAdapterLuid(IntPtr hResource, ulong* pLuid)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, hResource, pLuid);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetSharedResourceAdapterLuid failed", __result);
        }

        public int RegisterStereoStatusWindow(IntPtr WindowHandle, uint wMsg)
        {
            int __result;
            int pdwCookie = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, uint, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, WindowHandle, wMsg, &pdwCookie);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("RegisterStereoStatusWindow failed", __result);
            return pdwCookie;
        }

        public int RegisterStereoStatusEvent(IntPtr hEvent)
        {
            int __result;
            int pdwCookie = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, hEvent, &pdwCookie);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("RegisterStereoStatusEvent failed", __result);
            return pdwCookie;
        }

        public void UnregisterStereoStatus(int dwCookie)
        {
            ((delegate* unmanaged[Stdcall]<void*, int, void>)(*PPV)[base.VTableSize + 6])(PPV, dwCookie);
        }

        public int RegisterOcclusionStatusWindow(IntPtr WindowHandle, uint wMsg)
        {
            int __result;
            int pdwCookie = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, uint, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, WindowHandle, wMsg, &pdwCookie);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("RegisterOcclusionStatusWindow failed", __result);
            return pdwCookie;
        }

        public int RegisterOcclusionStatusEvent(IntPtr hEvent)
        {
            int __result;
            int pdwCookie = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, hEvent, &pdwCookie);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("RegisterOcclusionStatusEvent failed", __result);
            return pdwCookie;
        }

        public void UnregisterOcclusionStatus(int dwCookie)
        {
            ((delegate* unmanaged[Stdcall]<void*, int, void>)(*PPV)[base.VTableSize + 9])(PPV, dwCookie);
        }

        public IDXGISwapChain1 CreateSwapChainForComposition(IUnknown pDevice, DXGI_SWAP_CHAIN_DESC1* pDesc, IDXGIOutput pRestrictToOutput)
        {
            int __result;
            using var __pDevice = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pDevice);
            using var __pRestrictToOutput = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pRestrictToOutput);
            void* __marshal_ppSwapChain = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, void*, int>)(*PPV)[base.VTableSize + 10])(PPV, __pDevice.Pointer, pDesc, __pRestrictToOutput.Pointer, &__marshal_ppSwapChain);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSwapChainForComposition failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGISwapChain1>(__marshal_ppSwapChain, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGIFactory2), new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0"), (p, owns) => new __MicroComIDXGIFactory2Proxy(p, owns));
        }

        protected __MicroComIDXGIFactory2Proxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 11;
    }

    unsafe class __MicroComIDXGIFactory2VTable : __MicroComIDXGIFactory1VTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsWindowedStereoEnabledDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsWindowedStereoEnabled(void* @this)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsWindowedStereoEnabled();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSwapChainForHwndDelegate(void* @this, void* pDevice, IntPtr hWnd, DXGI_SWAP_CHAIN_DESC1* pDesc, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, void* pRestrictToOutput, void** ppSwapChain);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSwapChainForHwnd(void* @this, void* pDevice, IntPtr hWnd, DXGI_SWAP_CHAIN_DESC1* pDesc, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, void* pRestrictToOutput, void** ppSwapChain)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSwapChainForHwnd(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(pDevice, false), hWnd, pDesc, pFullscreenDesc, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGIOutput>(pRestrictToOutput, false));
                        *ppSwapChain = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateSwapChainForCoreWindowDelegate(void* @this, void* pDevice, void* pWindow, DXGI_SWAP_CHAIN_DESC1* pDesc, void* pRestrictToOutput, void** ppSwapChain);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSwapChainForCoreWindow(void* @this, void* pDevice, void* pWindow, DXGI_SWAP_CHAIN_DESC1* pDesc, void* pRestrictToOutput, void** ppSwapChain)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSwapChainForCoreWindow(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(pDevice, false), global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(pWindow, false), pDesc, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGIOutput>(pRestrictToOutput, false));
                        *ppSwapChain = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int GetSharedResourceAdapterLuidDelegate(void* @this, IntPtr hResource, ulong* pLuid);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetSharedResourceAdapterLuid(void* @this, IntPtr hResource, ulong* pLuid)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GetSharedResourceAdapterLuid(hResource, pLuid);
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
        delegate int RegisterStereoStatusWindowDelegate(void* @this, IntPtr WindowHandle, uint wMsg, int* pdwCookie);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RegisterStereoStatusWindow(void* @this, IntPtr WindowHandle, uint wMsg, int* pdwCookie)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RegisterStereoStatusWindow(WindowHandle, wMsg);
                        *pdwCookie = __result;
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
        delegate int RegisterStereoStatusEventDelegate(void* @this, IntPtr hEvent, int* pdwCookie);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RegisterStereoStatusEvent(void* @this, IntPtr hEvent, int* pdwCookie)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RegisterStereoStatusEvent(hEvent);
                        *pdwCookie = __result;
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
        delegate void UnregisterStereoStatusDelegate(void* @this, int dwCookie);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void UnregisterStereoStatus(void* @this, int dwCookie)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.UnregisterStereoStatus(dwCookie);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int RegisterOcclusionStatusWindowDelegate(void* @this, IntPtr WindowHandle, uint wMsg, int* pdwCookie);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RegisterOcclusionStatusWindow(void* @this, IntPtr WindowHandle, uint wMsg, int* pdwCookie)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RegisterOcclusionStatusWindow(WindowHandle, wMsg);
                        *pdwCookie = __result;
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
        delegate int RegisterOcclusionStatusEventDelegate(void* @this, IntPtr hEvent, int* pdwCookie);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RegisterOcclusionStatusEvent(void* @this, IntPtr hEvent, int* pdwCookie)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RegisterOcclusionStatusEvent(hEvent);
                        *pdwCookie = __result;
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
        delegate void UnregisterOcclusionStatusDelegate(void* @this, int dwCookie);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void UnregisterOcclusionStatus(void* @this, int dwCookie)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.UnregisterOcclusionStatus(dwCookie);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSwapChainForCompositionDelegate(void* @this, void* pDevice, DXGI_SWAP_CHAIN_DESC1* pDesc, void* pRestrictToOutput, void** ppSwapChain);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSwapChainForComposition(void* @this, void* pDevice, DXGI_SWAP_CHAIN_DESC1* pDesc, void* pRestrictToOutput, void** ppSwapChain)
        {
            IDXGIFactory2 __target = null;
            try
            {
                {
                    __target = (IDXGIFactory2)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSwapChainForComposition(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(pDevice, false), pDesc, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGIOutput>(pRestrictToOutput, false));
                        *ppSwapChain = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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

        protected __MicroComIDXGIFactory2VTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsWindowedStereoEnabled); 
#else
            base.AddMethod((IsWindowedStereoEnabledDelegate)IsWindowedStereoEnabled); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, IntPtr, DXGI_SWAP_CHAIN_DESC1*, DXGI_SWAP_CHAIN_FULLSCREEN_DESC*, void*, void**, int>)&CreateSwapChainForHwnd); 
#else
            base.AddMethod((CreateSwapChainForHwndDelegate)CreateSwapChainForHwnd); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void*, DXGI_SWAP_CHAIN_DESC1*, void*, void**, int>)&CreateSwapChainForCoreWindow); 
#else
            base.AddMethod((CreateSwapChainForCoreWindowDelegate)CreateSwapChainForCoreWindow); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, ulong*, int>)&GetSharedResourceAdapterLuid); 
#else
            base.AddMethod((GetSharedResourceAdapterLuidDelegate)GetSharedResourceAdapterLuid); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, uint, int*, int>)&RegisterStereoStatusWindow); 
#else
            base.AddMethod((RegisterStereoStatusWindowDelegate)RegisterStereoStatusWindow); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, int*, int>)&RegisterStereoStatusEvent); 
#else
            base.AddMethod((RegisterStereoStatusEventDelegate)RegisterStereoStatusEvent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void>)&UnregisterStereoStatus); 
#else
            base.AddMethod((UnregisterStereoStatusDelegate)UnregisterStereoStatus); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, uint, int*, int>)&RegisterOcclusionStatusWindow); 
#else
            base.AddMethod((RegisterOcclusionStatusWindowDelegate)RegisterOcclusionStatusWindow); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, int*, int>)&RegisterOcclusionStatusEvent); 
#else
            base.AddMethod((RegisterOcclusionStatusEventDelegate)RegisterOcclusionStatusEvent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void>)&UnregisterOcclusionStatus); 
#else
            base.AddMethod((UnregisterOcclusionStatusDelegate)UnregisterOcclusionStatus); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, DXGI_SWAP_CHAIN_DESC1*, void*, void**, int>)&CreateSwapChainForComposition); 
#else
            base.AddMethod((CreateSwapChainForCompositionDelegate)CreateSwapChainForComposition); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGIFactory2), new __MicroComIDXGIFactory2VTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIDXGISwapChain1Proxy : __MicroComIDXGISwapChainProxy, IDXGISwapChain1
    {
        public DXGI_SWAP_CHAIN_DESC1 Desc1
        {
            get
            {
                int __result;
                DXGI_SWAP_CHAIN_DESC1 pDesc = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &pDesc);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetDesc1 failed", __result);
                return pDesc;
            }
        }

        public DXGI_SWAP_CHAIN_FULLSCREEN_DESC FullscreenDesc
        {
            get
            {
                int __result;
                DXGI_SWAP_CHAIN_FULLSCREEN_DESC pDesc = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, &pDesc);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetFullscreenDesc failed", __result);
                return pDesc;
            }
        }

        public IntPtr Hwnd
        {
            get
            {
                int __result;
                IntPtr pHwnd = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, &pHwnd);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetHwnd failed", __result);
                return pHwnd;
            }
        }

        public void* GetCoreWindow(System.Guid* refiid)
        {
            int __result;
            void* ppUnk = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, refiid, &ppUnk);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetCoreWindow failed", __result);
            return ppUnk;
        }

        public void Present1(uint SyncInterval, uint PresentFlags, DXGI_PRESENT_PARAMETERS* pPresentParameters)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, uint, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, SyncInterval, PresentFlags, pPresentParameters);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Present1 failed", __result);
        }

        public int IsTemporaryMonoSupported()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 5])(PPV);
            return __result;
        }

        public IDXGIOutput RestrictToOutput
        {
            get
            {
                int __result;
                void* __marshal_ppRestrictToOutput = null;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, &__marshal_ppRestrictToOutput);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetRestrictToOutput failed", __result);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IDXGIOutput>(__marshal_ppRestrictToOutput, true);
            }
        }

        public void SetBackgroundColor(DXGI_RGBA* pColor)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, pColor);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetBackgroundColor failed", __result);
        }

        public DXGI_RGBA BackgroundColor
        {
            get
            {
                int __result;
                DXGI_RGBA pColor = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, &pColor);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetBackgroundColor failed", __result);
                return pColor;
            }
        }

        public void SetRotation(DXGI_MODE_ROTATION Rotation)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, DXGI_MODE_ROTATION, int>)(*PPV)[base.VTableSize + 9])(PPV, Rotation);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetRotation failed", __result);
        }

        public DXGI_MODE_ROTATION Rotation
        {
            get
            {
                int __result;
                DXGI_MODE_ROTATION pRotation = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 10])(PPV, &pRotation);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetRotation failed", __result);
                return pRotation;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IDXGISwapChain1), new Guid("790a45f7-0d42-4876-983a-0a55cfe6f4aa"), (p, owns) => new __MicroComIDXGISwapChain1Proxy(p, owns));
        }

        protected __MicroComIDXGISwapChain1Proxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 11;
    }

    unsafe class __MicroComIDXGISwapChain1VTable : __MicroComIDXGISwapChainVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetDesc1Delegate(void* @this, DXGI_SWAP_CHAIN_DESC1* pDesc);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetDesc1(void* @this, DXGI_SWAP_CHAIN_DESC1* pDesc)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Desc1;
                        *pDesc = __result;
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
        delegate int GetFullscreenDescDelegate(void* @this, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pDesc);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetFullscreenDesc(void* @this, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pDesc)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.FullscreenDesc;
                        *pDesc = __result;
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
        delegate int GetHwndDelegate(void* @this, IntPtr* pHwnd);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetHwnd(void* @this, IntPtr* pHwnd)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Hwnd;
                        *pHwnd = __result;
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
        delegate int GetCoreWindowDelegate(void* @this, System.Guid* refiid, void** ppUnk);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetCoreWindow(void* @this, System.Guid* refiid, void** ppUnk)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetCoreWindow(refiid);
                        *ppUnk = __result;
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
        delegate int Present1Delegate(void* @this, uint SyncInterval, uint PresentFlags, DXGI_PRESENT_PARAMETERS* pPresentParameters);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Present1(void* @this, uint SyncInterval, uint PresentFlags, DXGI_PRESENT_PARAMETERS* pPresentParameters)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Present1(SyncInterval, PresentFlags, pPresentParameters);
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
        delegate int IsTemporaryMonoSupportedDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsTemporaryMonoSupported(void* @this)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsTemporaryMonoSupported();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetRestrictToOutputDelegate(void* @this, void** ppRestrictToOutput);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetRestrictToOutput(void* @this, void** ppRestrictToOutput)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RestrictToOutput;
                        *ppRestrictToOutput = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int SetBackgroundColorDelegate(void* @this, DXGI_RGBA* pColor);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetBackgroundColor(void* @this, DXGI_RGBA* pColor)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetBackgroundColor(pColor);
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
        delegate int GetBackgroundColorDelegate(void* @this, DXGI_RGBA* pColor);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetBackgroundColor(void* @this, DXGI_RGBA* pColor)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.BackgroundColor;
                        *pColor = __result;
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
        delegate int SetRotationDelegate(void* @this, DXGI_MODE_ROTATION Rotation);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetRotation(void* @this, DXGI_MODE_ROTATION Rotation)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetRotation(Rotation);
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
        delegate int GetRotationDelegate(void* @this, DXGI_MODE_ROTATION* pRotation);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetRotation(void* @this, DXGI_MODE_ROTATION* pRotation)
        {
            IDXGISwapChain1 __target = null;
            try
            {
                {
                    __target = (IDXGISwapChain1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Rotation;
                        *pRotation = __result;
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

        protected __MicroComIDXGISwapChain1VTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_SWAP_CHAIN_DESC1*, int>)&GetDesc1); 
#else
            base.AddMethod((GetDesc1Delegate)GetDesc1); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_SWAP_CHAIN_FULLSCREEN_DESC*, int>)&GetFullscreenDesc); 
#else
            base.AddMethod((GetFullscreenDescDelegate)GetFullscreenDesc); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr*, int>)&GetHwnd); 
#else
            base.AddMethod((GetHwndDelegate)GetHwnd); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, System.Guid*, void**, int>)&GetCoreWindow); 
#else
            base.AddMethod((GetCoreWindowDelegate)GetCoreWindow); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, uint, DXGI_PRESENT_PARAMETERS*, int>)&Present1); 
#else
            base.AddMethod((Present1Delegate)Present1); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsTemporaryMonoSupported); 
#else
            base.AddMethod((IsTemporaryMonoSupportedDelegate)IsTemporaryMonoSupported); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&GetRestrictToOutput); 
#else
            base.AddMethod((GetRestrictToOutputDelegate)GetRestrictToOutput); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_RGBA*, int>)&SetBackgroundColor); 
#else
            base.AddMethod((SetBackgroundColorDelegate)SetBackgroundColor); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_RGBA*, int>)&GetBackgroundColor); 
#else
            base.AddMethod((GetBackgroundColorDelegate)GetBackgroundColor); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_MODE_ROTATION, int>)&SetRotation); 
#else
            base.AddMethod((SetRotationDelegate)SetRotation); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_MODE_ROTATION*, int>)&GetRotation); 
#else
            base.AddMethod((GetRotationDelegate)GetRotation); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IDXGISwapChain1), new __MicroComIDXGISwapChain1VTable().CreateVTable());
    }

    internal unsafe partial class __MicroComID3D11DeviceProxy : global::MicroCom.Runtime.MicroComProxyBase, ID3D11Device
    {
        public IUnknown CreateBuffer(IntPtr pDesc, IntPtr pInitialData)
        {
            int __result;
            void* __marshal_ppBuffer = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, pDesc, pInitialData, &__marshal_ppBuffer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateBuffer failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppBuffer, true);
        }

        public IUnknown CreateTexture1D(IntPtr pDesc, IntPtr pInitialData)
        {
            int __result;
            void* __marshal_ppTexture1D = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, pDesc, pInitialData, &__marshal_ppTexture1D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTexture1D failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppTexture1D, true);
        }

        public ID3D11Texture2D CreateTexture2D(D3D11_TEXTURE2D_DESC* pDesc, IntPtr pInitialData)
        {
            int __result;
            void* __marshal_ppTexture2D = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, pDesc, pInitialData, &__marshal_ppTexture2D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTexture2D failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<ID3D11Texture2D>(__marshal_ppTexture2D, true);
        }

        public IUnknown CreateTexture3D(IntPtr pDesc, IntPtr pInitialData)
        {
            int __result;
            void* __marshal_ppTexture3D = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, pDesc, pInitialData, &__marshal_ppTexture3D);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTexture3D failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppTexture3D, true);
        }

        public IUnknown CreateShaderResourceView(IntPtr pResource, IntPtr pDesc)
        {
            int __result;
            void* __marshal_ppSRView = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, pResource, pDesc, &__marshal_ppSRView);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateShaderResourceView failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppSRView, true);
        }

        public IUnknown CreateUnorderedAccessView(IntPtr pResource, IntPtr pDesc)
        {
            int __result;
            void* __marshal_ppUAView = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, pResource, pDesc, &__marshal_ppUAView);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateUnorderedAccessView failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppUAView, true);
        }

        public IUnknown CreateRenderTargetView(IntPtr pResource, IntPtr pDesc)
        {
            int __result;
            void* __marshal_ppRTView = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, pResource, pDesc, &__marshal_ppRTView);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateRenderTargetView failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppRTView, true);
        }

        public IUnknown CreateDepthStencilView(IntPtr pResource, IntPtr pDesc)
        {
            int __result;
            void* __marshal_ppDepthStencilView = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, pResource, pDesc, &__marshal_ppDepthStencilView);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateDepthStencilView failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppDepthStencilView, true);
        }

        public IUnknown CreateInputLayout(IntPtr pInputElementDescs, uint NumElements, void* pShaderBytecodeWithInputSignature, IntPtr BytecodeLength)
        {
            int __result;
            void* __marshal_ppInputLayout = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, uint, void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, pInputElementDescs, NumElements, pShaderBytecodeWithInputSignature, BytecodeLength, &__marshal_ppInputLayout);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateInputLayout failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppInputLayout, true);
        }

        public IUnknown CreateVertexShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage)
        {
            int __result;
            void* __marshal_ppVertexShader = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 9])(PPV, pShaderBytecode, BytecodeLength, pClassLinkage, &__marshal_ppVertexShader);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateVertexShader failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppVertexShader, true);
        }

        public IUnknown CreateGeometryShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage)
        {
            int __result;
            void* __marshal_ppGeometryShader = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 10])(PPV, pShaderBytecode, BytecodeLength, pClassLinkage, &__marshal_ppGeometryShader);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateGeometryShader failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppGeometryShader, true);
        }

        public IUnknown CreateGeometryShaderWithStreamOutput(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pSODeclaration, uint NumEntries, uint* pBufferStrides, uint NumStrides, uint RasterizedStream, IntPtr pClassLinkage)
        {
            int __result;
            void* __marshal_ppGeometryShader = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, uint, void*, uint, uint, IntPtr, void*, int>)(*PPV)[base.VTableSize + 11])(PPV, pShaderBytecode, BytecodeLength, pSODeclaration, NumEntries, pBufferStrides, NumStrides, RasterizedStream, pClassLinkage, &__marshal_ppGeometryShader);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateGeometryShaderWithStreamOutput failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppGeometryShader, true);
        }

        public IUnknown CreatePixelShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage)
        {
            int __result;
            void* __marshal_ppPixelShader = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 12])(PPV, pShaderBytecode, BytecodeLength, pClassLinkage, &__marshal_ppPixelShader);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreatePixelShader failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppPixelShader, true);
        }

        public IUnknown CreateHullShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage)
        {
            int __result;
            void* __marshal_ppHullShader = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 13])(PPV, pShaderBytecode, BytecodeLength, pClassLinkage, &__marshal_ppHullShader);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateHullShader failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppHullShader, true);
        }

        public IUnknown CreateDomainShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage)
        {
            int __result;
            void* __marshal_ppDomainShader = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 14])(PPV, pShaderBytecode, BytecodeLength, pClassLinkage, &__marshal_ppDomainShader);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateDomainShader failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppDomainShader, true);
        }

        public IUnknown CreateComputeShader(IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage)
        {
            int __result;
            void* __marshal_ppComputeShader = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void*, int>)(*PPV)[base.VTableSize + 15])(PPV, pShaderBytecode, BytecodeLength, pClassLinkage, &__marshal_ppComputeShader);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateComputeShader failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppComputeShader, true);
        }

        public IUnknown CreateClassLinkage()
        {
            int __result;
            void* __marshal_ppLinkage = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 16])(PPV, &__marshal_ppLinkage);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateClassLinkage failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppLinkage, true);
        }

        public IUnknown CreateBlendState(IntPtr pBlendStateDesc)
        {
            int __result;
            void* __marshal_ppBlendState = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 17])(PPV, pBlendStateDesc, &__marshal_ppBlendState);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateBlendState failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppBlendState, true);
        }

        public IUnknown CreateDepthStencilState(IntPtr pDepthStencilDesc)
        {
            int __result;
            void* __marshal_ppDepthStencilState = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 18])(PPV, pDepthStencilDesc, &__marshal_ppDepthStencilState);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateDepthStencilState failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppDepthStencilState, true);
        }

        public IUnknown CreateRasterizerState(IntPtr pRasterizerDesc)
        {
            int __result;
            void* __marshal_ppRasterizerState = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 19])(PPV, pRasterizerDesc, &__marshal_ppRasterizerState);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateRasterizerState failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppRasterizerState, true);
        }

        public IUnknown CreateSamplerState(IntPtr pSamplerDesc)
        {
            int __result;
            void* __marshal_ppSamplerState = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 20])(PPV, pSamplerDesc, &__marshal_ppSamplerState);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSamplerState failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppSamplerState, true);
        }

        public IUnknown CreateQuery(IntPtr pQueryDesc)
        {
            int __result;
            void* __marshal_ppQuery = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 21])(PPV, pQueryDesc, &__marshal_ppQuery);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateQuery failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppQuery, true);
        }

        public IUnknown CreatePredicate(IntPtr pPredicateDesc)
        {
            int __result;
            void* __marshal_ppPredicate = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 22])(PPV, pPredicateDesc, &__marshal_ppPredicate);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreatePredicate failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppPredicate, true);
        }

        public IUnknown CreateCounter(IntPtr pCounterDesc)
        {
            int __result;
            void* __marshal_ppCounter = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 23])(PPV, pCounterDesc, &__marshal_ppCounter);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateCounter failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppCounter, true);
        }

        public IUnknown CreateDeferredContext(uint ContextFlags)
        {
            int __result;
            void* __marshal_ppDeferredContext = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, void*, int>)(*PPV)[base.VTableSize + 24])(PPV, ContextFlags, &__marshal_ppDeferredContext);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateDeferredContext failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppDeferredContext, true);
        }

        public IUnknown OpenSharedResource(IntPtr hResource, Guid* ReturnedInterface)
        {
            int __result;
            void* __marshal_ppResource = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, void*, int>)(*PPV)[base.VTableSize + 25])(PPV, hResource, ReturnedInterface, &__marshal_ppResource);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("OpenSharedResource failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppResource, true);
        }

        public void CheckFormatSupport(DXGI_FORMAT Format, uint* pFormatSupport)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, DXGI_FORMAT, void*, int>)(*PPV)[base.VTableSize + 26])(PPV, Format, pFormatSupport);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CheckFormatSupport failed", __result);
        }

        public void CheckMultisampleQualityLevels(DXGI_FORMAT Format, uint SampleCount, uint* pNumQualityLevels)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, DXGI_FORMAT, uint, void*, int>)(*PPV)[base.VTableSize + 27])(PPV, Format, SampleCount, pNumQualityLevels);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CheckMultisampleQualityLevels failed", __result);
        }

        public void CheckCounterInfo(IntPtr pCounterInfo)
        {
            ((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)(*PPV)[base.VTableSize + 28])(PPV, pCounterInfo);
        }

        public void CheckCounter(IntPtr pDesc, IntPtr pType, IntPtr pActiveCounters, IntPtr szName, uint* pNameLength, IntPtr szUnits, uint* pUnitsLength, IntPtr szDescription, uint* pDescriptionLength)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, IntPtr, void*, IntPtr, void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 29])(PPV, pDesc, pType, pActiveCounters, szName, pNameLength, szUnits, pUnitsLength, szDescription, pDescriptionLength);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CheckCounter failed", __result);
        }

        public void CheckFeatureSupport(D3D11_FEATURE Feature, void* pFeatureSupportData, uint FeatureSupportDataSize)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, D3D11_FEATURE, void*, uint, int>)(*PPV)[base.VTableSize + 30])(PPV, Feature, pFeatureSupportData, FeatureSupportDataSize);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CheckFeatureSupport failed", __result);
        }

        public void GetPrivateData(Guid* guid, uint* pDataSize, void* pData)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, int>)(*PPV)[base.VTableSize + 31])(PPV, guid, pDataSize, pData);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetPrivateData failed", __result);
        }

        public void SetPrivateData(Guid* guid, uint DataSize, IntPtr* pData)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, uint, void*, int>)(*PPV)[base.VTableSize + 32])(PPV, guid, DataSize, pData);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetPrivateData failed", __result);
        }

        public void SetPrivateDataInterface(Guid* guid, IUnknown pData)
        {
            int __result;
            using var __pData = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(pData);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 33])(PPV, guid, __pData.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetPrivateDataInterface failed", __result);
        }

        public D3D_FEATURE_LEVEL FeatureLevel
        {
            get
            {
                D3D_FEATURE_LEVEL __result;
                __result = (D3D_FEATURE_LEVEL)((delegate* unmanaged[Stdcall]<void*, D3D_FEATURE_LEVEL>)(*PPV)[base.VTableSize + 34])(PPV);
                return __result;
            }
        }

        public uint CreationFlags
        {
            get
            {
                uint __result;
                __result = (uint)((delegate* unmanaged[Stdcall]<void*, uint>)(*PPV)[base.VTableSize + 35])(PPV);
                return __result;
            }
        }

        public int DeviceRemovedReason
        {
            get
            {
                int __result;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 36])(PPV);
                return __result;
            }
        }

        public void GetImmediateContext(IntPtr* ppImmediateContext)
        {
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 37])(PPV, ppImmediateContext);
        }

        public void SetExceptionMode(uint RaiseFlags)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, int>)(*PPV)[base.VTableSize + 38])(PPV, RaiseFlags);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetExceptionMode failed", __result);
        }

        public uint ExceptionMode
        {
            get
            {
                uint __result;
                __result = (uint)((delegate* unmanaged[Stdcall]<void*, uint>)(*PPV)[base.VTableSize + 39])(PPV);
                return __result;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(ID3D11Device), new Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140"), (p, owns) => new __MicroComID3D11DeviceProxy(p, owns));
        }

        protected __MicroComID3D11DeviceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 40;
    }

    unsafe class __MicroComID3D11DeviceVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateBufferDelegate(void* @this, IntPtr pDesc, IntPtr pInitialData, void** ppBuffer);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateBuffer(void* @this, IntPtr pDesc, IntPtr pInitialData, void** ppBuffer)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateBuffer(pDesc, pInitialData);
                        *ppBuffer = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateTexture1DDelegate(void* @this, IntPtr pDesc, IntPtr pInitialData, void** ppTexture1D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTexture1D(void* @this, IntPtr pDesc, IntPtr pInitialData, void** ppTexture1D)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTexture1D(pDesc, pInitialData);
                        *ppTexture1D = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateTexture2DDelegate(void* @this, D3D11_TEXTURE2D_DESC* pDesc, IntPtr pInitialData, void** ppTexture2D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTexture2D(void* @this, D3D11_TEXTURE2D_DESC* pDesc, IntPtr pInitialData, void** ppTexture2D)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTexture2D(pDesc, pInitialData);
                        *ppTexture2D = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateTexture3DDelegate(void* @this, IntPtr pDesc, IntPtr pInitialData, void** ppTexture3D);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTexture3D(void* @this, IntPtr pDesc, IntPtr pInitialData, void** ppTexture3D)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTexture3D(pDesc, pInitialData);
                        *ppTexture3D = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateShaderResourceViewDelegate(void* @this, IntPtr pResource, IntPtr pDesc, void** ppSRView);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateShaderResourceView(void* @this, IntPtr pResource, IntPtr pDesc, void** ppSRView)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateShaderResourceView(pResource, pDesc);
                        *ppSRView = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateUnorderedAccessViewDelegate(void* @this, IntPtr pResource, IntPtr pDesc, void** ppUAView);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateUnorderedAccessView(void* @this, IntPtr pResource, IntPtr pDesc, void** ppUAView)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateUnorderedAccessView(pResource, pDesc);
                        *ppUAView = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateRenderTargetViewDelegate(void* @this, IntPtr pResource, IntPtr pDesc, void** ppRTView);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateRenderTargetView(void* @this, IntPtr pResource, IntPtr pDesc, void** ppRTView)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateRenderTargetView(pResource, pDesc);
                        *ppRTView = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateDepthStencilViewDelegate(void* @this, IntPtr pResource, IntPtr pDesc, void** ppDepthStencilView);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateDepthStencilView(void* @this, IntPtr pResource, IntPtr pDesc, void** ppDepthStencilView)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateDepthStencilView(pResource, pDesc);
                        *ppDepthStencilView = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateInputLayoutDelegate(void* @this, IntPtr pInputElementDescs, uint NumElements, void* pShaderBytecodeWithInputSignature, IntPtr BytecodeLength, void** ppInputLayout);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateInputLayout(void* @this, IntPtr pInputElementDescs, uint NumElements, void* pShaderBytecodeWithInputSignature, IntPtr BytecodeLength, void** ppInputLayout)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateInputLayout(pInputElementDescs, NumElements, pShaderBytecodeWithInputSignature, BytecodeLength);
                        *ppInputLayout = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateVertexShaderDelegate(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppVertexShader);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateVertexShader(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppVertexShader)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateVertexShader(pShaderBytecode, BytecodeLength, pClassLinkage);
                        *ppVertexShader = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateGeometryShaderDelegate(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppGeometryShader);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateGeometryShader(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppGeometryShader)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateGeometryShader(pShaderBytecode, BytecodeLength, pClassLinkage);
                        *ppGeometryShader = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateGeometryShaderWithStreamOutputDelegate(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pSODeclaration, uint NumEntries, uint* pBufferStrides, uint NumStrides, uint RasterizedStream, IntPtr pClassLinkage, void** ppGeometryShader);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateGeometryShaderWithStreamOutput(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pSODeclaration, uint NumEntries, uint* pBufferStrides, uint NumStrides, uint RasterizedStream, IntPtr pClassLinkage, void** ppGeometryShader)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateGeometryShaderWithStreamOutput(pShaderBytecode, BytecodeLength, pSODeclaration, NumEntries, pBufferStrides, NumStrides, RasterizedStream, pClassLinkage);
                        *ppGeometryShader = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreatePixelShaderDelegate(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppPixelShader);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreatePixelShader(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppPixelShader)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreatePixelShader(pShaderBytecode, BytecodeLength, pClassLinkage);
                        *ppPixelShader = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateHullShaderDelegate(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppHullShader);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateHullShader(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppHullShader)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateHullShader(pShaderBytecode, BytecodeLength, pClassLinkage);
                        *ppHullShader = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateDomainShaderDelegate(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppDomainShader);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateDomainShader(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppDomainShader)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateDomainShader(pShaderBytecode, BytecodeLength, pClassLinkage);
                        *ppDomainShader = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateComputeShaderDelegate(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppComputeShader);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateComputeShader(void* @this, IntPtr pShaderBytecode, IntPtr BytecodeLength, IntPtr pClassLinkage, void** ppComputeShader)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateComputeShader(pShaderBytecode, BytecodeLength, pClassLinkage);
                        *ppComputeShader = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateClassLinkageDelegate(void* @this, void** ppLinkage);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateClassLinkage(void* @this, void** ppLinkage)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateClassLinkage();
                        *ppLinkage = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateBlendStateDelegate(void* @this, IntPtr pBlendStateDesc, void** ppBlendState);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateBlendState(void* @this, IntPtr pBlendStateDesc, void** ppBlendState)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateBlendState(pBlendStateDesc);
                        *ppBlendState = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateDepthStencilStateDelegate(void* @this, IntPtr pDepthStencilDesc, void** ppDepthStencilState);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateDepthStencilState(void* @this, IntPtr pDepthStencilDesc, void** ppDepthStencilState)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateDepthStencilState(pDepthStencilDesc);
                        *ppDepthStencilState = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateRasterizerStateDelegate(void* @this, IntPtr pRasterizerDesc, void** ppRasterizerState);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateRasterizerState(void* @this, IntPtr pRasterizerDesc, void** ppRasterizerState)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateRasterizerState(pRasterizerDesc);
                        *ppRasterizerState = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateSamplerStateDelegate(void* @this, IntPtr pSamplerDesc, void** ppSamplerState);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSamplerState(void* @this, IntPtr pSamplerDesc, void** ppSamplerState)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSamplerState(pSamplerDesc);
                        *ppSamplerState = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateQueryDelegate(void* @this, IntPtr pQueryDesc, void** ppQuery);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateQuery(void* @this, IntPtr pQueryDesc, void** ppQuery)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateQuery(pQueryDesc);
                        *ppQuery = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreatePredicateDelegate(void* @this, IntPtr pPredicateDesc, void** ppPredicate);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreatePredicate(void* @this, IntPtr pPredicateDesc, void** ppPredicate)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreatePredicate(pPredicateDesc);
                        *ppPredicate = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateCounterDelegate(void* @this, IntPtr pCounterDesc, void** ppCounter);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateCounter(void* @this, IntPtr pCounterDesc, void** ppCounter)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateCounter(pCounterDesc);
                        *ppCounter = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateDeferredContextDelegate(void* @this, uint ContextFlags, void** ppDeferredContext);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateDeferredContext(void* @this, uint ContextFlags, void** ppDeferredContext)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateDeferredContext(ContextFlags);
                        *ppDeferredContext = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int OpenSharedResourceDelegate(void* @this, IntPtr hResource, Guid* ReturnedInterface, void** ppResource);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int OpenSharedResource(void* @this, IntPtr hResource, Guid* ReturnedInterface, void** ppResource)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.OpenSharedResource(hResource, ReturnedInterface);
                        *ppResource = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CheckFormatSupportDelegate(void* @this, DXGI_FORMAT Format, uint* pFormatSupport);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CheckFormatSupport(void* @this, DXGI_FORMAT Format, uint* pFormatSupport)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.CheckFormatSupport(Format, pFormatSupport);
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
        delegate int CheckMultisampleQualityLevelsDelegate(void* @this, DXGI_FORMAT Format, uint SampleCount, uint* pNumQualityLevels);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CheckMultisampleQualityLevels(void* @this, DXGI_FORMAT Format, uint SampleCount, uint* pNumQualityLevels)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.CheckMultisampleQualityLevels(Format, SampleCount, pNumQualityLevels);
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
        delegate void CheckCounterInfoDelegate(void* @this, IntPtr pCounterInfo);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void CheckCounterInfo(void* @this, IntPtr pCounterInfo)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.CheckCounterInfo(pCounterInfo);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CheckCounterDelegate(void* @this, IntPtr pDesc, IntPtr pType, IntPtr pActiveCounters, IntPtr szName, uint* pNameLength, IntPtr szUnits, uint* pUnitsLength, IntPtr szDescription, uint* pDescriptionLength);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CheckCounter(void* @this, IntPtr pDesc, IntPtr pType, IntPtr pActiveCounters, IntPtr szName, uint* pNameLength, IntPtr szUnits, uint* pUnitsLength, IntPtr szDescription, uint* pDescriptionLength)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.CheckCounter(pDesc, pType, pActiveCounters, szName, pNameLength, szUnits, pUnitsLength, szDescription, pDescriptionLength);
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
        delegate int CheckFeatureSupportDelegate(void* @this, D3D11_FEATURE Feature, void* pFeatureSupportData, uint FeatureSupportDataSize);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CheckFeatureSupport(void* @this, D3D11_FEATURE Feature, void* pFeatureSupportData, uint FeatureSupportDataSize)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.CheckFeatureSupport(Feature, pFeatureSupportData, FeatureSupportDataSize);
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
        delegate int GetPrivateDataDelegate(void* @this, Guid* guid, uint* pDataSize, void* pData);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetPrivateData(void* @this, Guid* guid, uint* pDataSize, void* pData)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GetPrivateData(guid, pDataSize, pData);
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
        delegate int SetPrivateDataDelegate(void* @this, Guid* guid, uint DataSize, IntPtr* pData);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetPrivateData(void* @this, Guid* guid, uint DataSize, IntPtr* pData)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetPrivateData(guid, DataSize, pData);
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
        delegate int SetPrivateDataInterfaceDelegate(void* @this, Guid* guid, void* pData);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetPrivateDataInterface(void* @this, Guid* guid, void* pData)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetPrivateDataInterface(guid, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(pData, false));
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
        delegate D3D_FEATURE_LEVEL GetFeatureLevelDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static D3D_FEATURE_LEVEL GetFeatureLevel(void* @this)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.FeatureLevel;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate uint GetCreationFlagsDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static uint GetCreationFlags(void* @this)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreationFlags;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetDeviceRemovedReasonDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetDeviceRemovedReason(void* @this)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.DeviceRemovedReason;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void GetImmediateContextDelegate(void* @this, IntPtr* ppImmediateContext);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void GetImmediateContext(void* @this, IntPtr* ppImmediateContext)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GetImmediateContext(ppImmediateContext);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetExceptionModeDelegate(void* @this, uint RaiseFlags);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetExceptionMode(void* @this, uint RaiseFlags)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetExceptionMode(RaiseFlags);
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
        delegate uint GetExceptionModeDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static uint GetExceptionMode(void* @this)
        {
            ID3D11Device __target = null;
            try
            {
                {
                    __target = (ID3D11Device)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ExceptionMode;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComID3D11DeviceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void**, int>)&CreateBuffer); 
#else
            base.AddMethod((CreateBufferDelegate)CreateBuffer); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void**, int>)&CreateTexture1D); 
#else
            base.AddMethod((CreateTexture1DDelegate)CreateTexture1D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, D3D11_TEXTURE2D_DESC*, IntPtr, void**, int>)&CreateTexture2D); 
#else
            base.AddMethod((CreateTexture2DDelegate)CreateTexture2D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void**, int>)&CreateTexture3D); 
#else
            base.AddMethod((CreateTexture3DDelegate)CreateTexture3D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void**, int>)&CreateShaderResourceView); 
#else
            base.AddMethod((CreateShaderResourceViewDelegate)CreateShaderResourceView); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void**, int>)&CreateUnorderedAccessView); 
#else
            base.AddMethod((CreateUnorderedAccessViewDelegate)CreateUnorderedAccessView); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void**, int>)&CreateRenderTargetView); 
#else
            base.AddMethod((CreateRenderTargetViewDelegate)CreateRenderTargetView); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, void**, int>)&CreateDepthStencilView); 
#else
            base.AddMethod((CreateDepthStencilViewDelegate)CreateDepthStencilView); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, uint, void*, IntPtr, void**, int>)&CreateInputLayout); 
#else
            base.AddMethod((CreateInputLayoutDelegate)CreateInputLayout); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void**, int>)&CreateVertexShader); 
#else
            base.AddMethod((CreateVertexShaderDelegate)CreateVertexShader); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void**, int>)&CreateGeometryShader); 
#else
            base.AddMethod((CreateGeometryShaderDelegate)CreateGeometryShader); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, uint, uint*, uint, uint, IntPtr, void**, int>)&CreateGeometryShaderWithStreamOutput); 
#else
            base.AddMethod((CreateGeometryShaderWithStreamOutputDelegate)CreateGeometryShaderWithStreamOutput); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void**, int>)&CreatePixelShader); 
#else
            base.AddMethod((CreatePixelShaderDelegate)CreatePixelShader); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void**, int>)&CreateHullShader); 
#else
            base.AddMethod((CreateHullShaderDelegate)CreateHullShader); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void**, int>)&CreateDomainShader); 
#else
            base.AddMethod((CreateDomainShaderDelegate)CreateDomainShader); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, void**, int>)&CreateComputeShader); 
#else
            base.AddMethod((CreateComputeShaderDelegate)CreateComputeShader); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateClassLinkage); 
#else
            base.AddMethod((CreateClassLinkageDelegate)CreateClassLinkage); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreateBlendState); 
#else
            base.AddMethod((CreateBlendStateDelegate)CreateBlendState); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreateDepthStencilState); 
#else
            base.AddMethod((CreateDepthStencilStateDelegate)CreateDepthStencilState); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreateRasterizerState); 
#else
            base.AddMethod((CreateRasterizerStateDelegate)CreateRasterizerState); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreateSamplerState); 
#else
            base.AddMethod((CreateSamplerStateDelegate)CreateSamplerState); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreateQuery); 
#else
            base.AddMethod((CreateQueryDelegate)CreateQuery); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreatePredicate); 
#else
            base.AddMethod((CreatePredicateDelegate)CreatePredicate); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&CreateCounter); 
#else
            base.AddMethod((CreateCounterDelegate)CreateCounter); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)&CreateDeferredContext); 
#else
            base.AddMethod((CreateDeferredContextDelegate)CreateDeferredContext); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, Guid*, void**, int>)&OpenSharedResource); 
#else
            base.AddMethod((OpenSharedResourceDelegate)OpenSharedResource); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_FORMAT, uint*, int>)&CheckFormatSupport); 
#else
            base.AddMethod((CheckFormatSupportDelegate)CheckFormatSupport); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, DXGI_FORMAT, uint, uint*, int>)&CheckMultisampleQualityLevels); 
#else
            base.AddMethod((CheckMultisampleQualityLevelsDelegate)CheckMultisampleQualityLevels); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)&CheckCounterInfo); 
#else
            base.AddMethod((CheckCounterInfoDelegate)CheckCounterInfo); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr, IntPtr, IntPtr, uint*, IntPtr, uint*, IntPtr, uint*, int>)&CheckCounter); 
#else
            base.AddMethod((CheckCounterDelegate)CheckCounter); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, D3D11_FEATURE, void*, uint, int>)&CheckFeatureSupport); 
#else
            base.AddMethod((CheckFeatureSupportDelegate)CheckFeatureSupport); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, Guid*, uint*, void*, int>)&GetPrivateData); 
#else
            base.AddMethod((GetPrivateDataDelegate)GetPrivateData); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, Guid*, uint, IntPtr*, int>)&SetPrivateData); 
#else
            base.AddMethod((SetPrivateDataDelegate)SetPrivateData); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, Guid*, void*, int>)&SetPrivateDataInterface); 
#else
            base.AddMethod((SetPrivateDataInterfaceDelegate)SetPrivateDataInterface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, D3D_FEATURE_LEVEL>)&GetFeatureLevel); 
#else
            base.AddMethod((GetFeatureLevelDelegate)GetFeatureLevel); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint>)&GetCreationFlags); 
#else
            base.AddMethod((GetCreationFlagsDelegate)GetCreationFlags); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&GetDeviceRemovedReason); 
#else
            base.AddMethod((GetDeviceRemovedReasonDelegate)GetDeviceRemovedReason); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr*, void>)&GetImmediateContext); 
#else
            base.AddMethod((GetImmediateContextDelegate)GetImmediateContext); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, int>)&SetExceptionMode); 
#else
            base.AddMethod((SetExceptionModeDelegate)SetExceptionMode); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint>)&GetExceptionMode); 
#else
            base.AddMethod((GetExceptionModeDelegate)GetExceptionMode); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(ID3D11Device), new __MicroComID3D11DeviceVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComID3D11Device1Proxy : __MicroComID3D11DeviceProxy, ID3D11Device1
    {
        public void GetImmediateContext1(void** ppImmediateContext)
        {
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 0])(PPV, ppImmediateContext);
        }

        public IUnknown CreateDeferredContext1(uint ContextFlags)
        {
            int __result;
            void* __marshal_ppDeferredContext = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, ContextFlags, &__marshal_ppDeferredContext);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateDeferredContext1 failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppDeferredContext, true);
        }

        public IUnknown CreateBlendState1(void* pBlendStateDesc)
        {
            int __result;
            void* __marshal_ppBlendState = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, pBlendStateDesc, &__marshal_ppBlendState);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateBlendState1 failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppBlendState, true);
        }

        public IUnknown CreateRasterizerState1(void* pRasterizerDesc)
        {
            int __result;
            void* __marshal_ppRasterizerState = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, pRasterizerDesc, &__marshal_ppRasterizerState);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateRasterizerState1 failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppRasterizerState, true);
        }

        public IUnknown CreateDeviceContextState(uint Flags, void* pFeatureLevels, uint FeatureLevels, uint SDKVersion, System.Guid* EmulatedInterface, void* pChosenFeatureLevel)
        {
            int __result;
            void* __marshal_ppContextState = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, void*, uint, uint, void*, void*, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, Flags, pFeatureLevels, FeatureLevels, SDKVersion, EmulatedInterface, pChosenFeatureLevel, &__marshal_ppContextState);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateDeviceContextState failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppContextState, true);
        }

        public IUnknown OpenSharedResource1(IntPtr hResource, Guid* ReturnedInterface)
        {
            int __result;
            void* __marshal_ppResource = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, hResource, ReturnedInterface, &__marshal_ppResource);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("OpenSharedResource1 failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppResource, true);
        }

        public void OpenSharedResourceByName(ushort* lpName, int dwDesiredAccess, System.Guid* returnedInterface, void** ppResource)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int, void*, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, lpName, dwDesiredAccess, returnedInterface, ppResource);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("OpenSharedResourceByName failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(ID3D11Device1), new Guid("a04bfb29-08ef-43d6-a49c-a9bdbdcbe686"), (p, owns) => new __MicroComID3D11Device1Proxy(p, owns));
        }

        protected __MicroComID3D11Device1Proxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 7;
    }

    unsafe class __MicroComID3D11Device1VTable : __MicroComID3D11DeviceVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void GetImmediateContext1Delegate(void* @this, void** ppImmediateContext);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void GetImmediateContext1(void* @this, void** ppImmediateContext)
        {
            ID3D11Device1 __target = null;
            try
            {
                {
                    __target = (ID3D11Device1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GetImmediateContext1(ppImmediateContext);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateDeferredContext1Delegate(void* @this, uint ContextFlags, void** ppDeferredContext);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateDeferredContext1(void* @this, uint ContextFlags, void** ppDeferredContext)
        {
            ID3D11Device1 __target = null;
            try
            {
                {
                    __target = (ID3D11Device1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateDeferredContext1(ContextFlags);
                        *ppDeferredContext = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateBlendState1Delegate(void* @this, void* pBlendStateDesc, void** ppBlendState);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateBlendState1(void* @this, void* pBlendStateDesc, void** ppBlendState)
        {
            ID3D11Device1 __target = null;
            try
            {
                {
                    __target = (ID3D11Device1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateBlendState1(pBlendStateDesc);
                        *ppBlendState = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateRasterizerState1Delegate(void* @this, void* pRasterizerDesc, void** ppRasterizerState);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateRasterizerState1(void* @this, void* pRasterizerDesc, void** ppRasterizerState)
        {
            ID3D11Device1 __target = null;
            try
            {
                {
                    __target = (ID3D11Device1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateRasterizerState1(pRasterizerDesc);
                        *ppRasterizerState = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int CreateDeviceContextStateDelegate(void* @this, uint Flags, void* pFeatureLevels, uint FeatureLevels, uint SDKVersion, System.Guid* EmulatedInterface, void* pChosenFeatureLevel, void** ppContextState);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateDeviceContextState(void* @this, uint Flags, void* pFeatureLevels, uint FeatureLevels, uint SDKVersion, System.Guid* EmulatedInterface, void* pChosenFeatureLevel, void** ppContextState)
        {
            ID3D11Device1 __target = null;
            try
            {
                {
                    __target = (ID3D11Device1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateDeviceContextState(Flags, pFeatureLevels, FeatureLevels, SDKVersion, EmulatedInterface, pChosenFeatureLevel);
                        *ppContextState = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int OpenSharedResource1Delegate(void* @this, IntPtr hResource, Guid* ReturnedInterface, void** ppResource);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int OpenSharedResource1(void* @this, IntPtr hResource, Guid* ReturnedInterface, void** ppResource)
        {
            ID3D11Device1 __target = null;
            try
            {
                {
                    __target = (ID3D11Device1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.OpenSharedResource1(hResource, ReturnedInterface);
                        *ppResource = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
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
        delegate int OpenSharedResourceByNameDelegate(void* @this, ushort* lpName, int dwDesiredAccess, System.Guid* returnedInterface, void** ppResource);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int OpenSharedResourceByName(void* @this, ushort* lpName, int dwDesiredAccess, System.Guid* returnedInterface, void** ppResource)
        {
            ID3D11Device1 __target = null;
            try
            {
                {
                    __target = (ID3D11Device1)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OpenSharedResourceByName(lpName, dwDesiredAccess, returnedInterface, ppResource);
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

        protected __MicroComID3D11Device1VTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, void>)&GetImmediateContext1); 
#else
            base.AddMethod((GetImmediateContext1Delegate)GetImmediateContext1); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)&CreateDeferredContext1); 
#else
            base.AddMethod((CreateDeferredContext1Delegate)CreateDeferredContext1); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateBlendState1); 
#else
            base.AddMethod((CreateBlendState1Delegate)CreateBlendState1); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateRasterizerState1); 
#else
            base.AddMethod((CreateRasterizerState1Delegate)CreateRasterizerState1); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, void*, uint, uint, System.Guid*, void*, void**, int>)&CreateDeviceContextState); 
#else
            base.AddMethod((CreateDeviceContextStateDelegate)CreateDeviceContextState); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, Guid*, void**, int>)&OpenSharedResource1); 
#else
            base.AddMethod((OpenSharedResource1Delegate)OpenSharedResource1); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, ushort*, int, System.Guid*, void**, int>)&OpenSharedResourceByName); 
#else
            base.AddMethod((OpenSharedResourceByNameDelegate)OpenSharedResourceByName); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(ID3D11Device1), new __MicroComID3D11Device1VTable().CreateVTable());
    }

    internal unsafe partial class __MicroComID3D11Texture2DProxy : global::MicroCom.Runtime.MicroComProxyBase, ID3D11Texture2D
    {
        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(ID3D11Texture2D), new Guid(" 6f15aaf2-d208-4e89-9ab4-489535d34f9c"), (p, owns) => new __MicroComID3D11Texture2DProxy(p, owns));
        }

        protected __MicroComID3D11Texture2DProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 0;
    }

    unsafe class __MicroComID3D11Texture2DVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        protected __MicroComID3D11Texture2DVTable()
        {
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(ID3D11Texture2D), new __MicroComID3D11Texture2DVTable().CreateVTable());
    }
}