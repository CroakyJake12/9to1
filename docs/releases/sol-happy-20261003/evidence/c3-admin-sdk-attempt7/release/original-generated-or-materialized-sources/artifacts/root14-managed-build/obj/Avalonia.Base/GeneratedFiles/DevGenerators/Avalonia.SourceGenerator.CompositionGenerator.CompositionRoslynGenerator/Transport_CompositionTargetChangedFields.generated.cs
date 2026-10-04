
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
enum CompositionTargetChangedFields : byte
{
    Root = 1,
    IsEnabled = 2,
    DebugOverlays = 4,
    LastLayoutPassTiming = 8,
    Scaling = 16,
    PixelSize = 32
}