
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

namespace Avalonia.Rendering.Composition
{
    public unsafe partial class CompositionContainerVisual : CompositionVisual
    {
        internal new ServerCompositionContainerVisual Server { get; }

        internal CompositionContainerVisual(Compositor compositor, ServerCompositionContainerVisual server) : base(compositor, server)
        {
            Server = (ServerCompositionContainerVisual)server;
            InitializeDefaults();
        }

        void InitializeDefaults()
        {
            InitializeDefaultsExtra();
        }

        partial void InitializeDefaultsExtra();
    }
}