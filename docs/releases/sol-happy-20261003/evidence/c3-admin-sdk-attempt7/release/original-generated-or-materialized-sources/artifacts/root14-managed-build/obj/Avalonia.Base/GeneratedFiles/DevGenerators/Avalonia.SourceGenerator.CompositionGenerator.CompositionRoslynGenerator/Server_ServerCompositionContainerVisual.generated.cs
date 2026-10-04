
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

namespace Avalonia.Rendering.Composition.Server
{
    unsafe partial class ServerCompositionContainerVisual : ServerCompositionVisual
    {
        internal ServerCompositionContainerVisual(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
    }
}