
#nullable enable
#pragma warning disable CS0108, CS0114

using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Avalonia.Rendering.Composition.Server;
using Avalonia.Rendering.Composition.Transport;
using Avalonia.Rendering.Composition.Animations;

[System.Flags]
enum CompositionBitmapCacheChangedFields : byte
{
    RenderAtScale = 1,
    RenderAtScaleAnimated = 2,
    SnapsToDevicePixels = 4,
    SnapsToDevicePixelsAnimated = 8,
    EnableClearType = 16,
    EnableClearTypeAnimated = 32
}