
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
enum CompositionSimpleRadialGradientBrushChangedFields : byte
{
    Center = 1,
    GradientOrigin = 2,
    RadiusX = 4,
    RadiusY = 8
}