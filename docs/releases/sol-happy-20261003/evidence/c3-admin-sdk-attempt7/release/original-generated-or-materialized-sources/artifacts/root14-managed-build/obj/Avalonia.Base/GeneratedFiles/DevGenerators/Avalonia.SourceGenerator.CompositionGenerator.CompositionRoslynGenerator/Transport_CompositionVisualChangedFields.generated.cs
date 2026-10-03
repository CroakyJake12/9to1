
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
enum CompositionVisualChangedFields : ulong
{
    Root = 1,
    Parent = 2,
    Visible = 4,
    VisibleAnimated = 8,
    Opacity = 16,
    OpacityAnimated = 32,
    Clip = 64,
    ClipToBounds = 128,
    ClipToBoundsAnimated = 256,
    Offset = 512,
    OffsetAnimated = 1024,
    Translation = 2048,
    TranslationAnimated = 4096,
    Size = 8192,
    SizeAnimated = 16384,
    AnchorPoint = 32768,
    AnchorPointAnimated = 65536,
    CenterPoint = 131072,
    CenterPointAnimated = 262144,
    RotationAngle = 524288,
    RotationAngleAnimated = 1048576,
    Orientation = 2097152,
    OrientationAnimated = 4194304,
    Scale = 8388608,
    ScaleAnimated = 16777216,
    TransformMatrix = 33554432,
    TransformMatrixAnimated = 67108864,
    AdornedVisual = 134217728,
    AdornerIsClipped = 268435456,
    OpacityMaskBrush = 536870912,
    Effect = 1073741824,
    RenderOptions = 2147483648,
    TextOptions = 4294967296,
    CacheMode = 8589934592
}