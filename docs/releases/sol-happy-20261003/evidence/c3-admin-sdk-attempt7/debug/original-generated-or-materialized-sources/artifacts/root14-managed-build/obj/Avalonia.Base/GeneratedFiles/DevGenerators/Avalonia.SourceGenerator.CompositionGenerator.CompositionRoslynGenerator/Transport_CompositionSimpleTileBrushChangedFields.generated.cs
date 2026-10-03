
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
enum CompositionSimpleTileBrushChangedFields : byte
{
    AlignmentX = 1,
    AlignmentY = 2,
    DestinationRect = 4,
    SourceRect = 8,
    Stretch = 16,
    TileMode = 32
}