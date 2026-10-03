
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
    public unsafe partial class CompositionSurfaceVisual : CompositionContainerVisual
    {
        CompositionSurfaceVisualChangedFields _changedFieldsOfCompositionSurfaceVisual;
        internal new ServerCompositionSurfaceVisual Server { get; }

        internal CompositionSurfaceVisual(Compositor compositor, ServerCompositionSurfaceVisual server) : base(compositor, server)
        {
            Server = (ServerCompositionSurfaceVisual)server;
            InitializeDefaults();
        }

        CompositionSurface? _surface;
        public CompositionSurface? Surface
        {
            get
            {
                return _surface;
            }

            set
            {
                var changed = false;
                if (_surface != value)
                {
                    ValidateSurfaceChange(_surface, value);
                    OnSurfaceChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _surface = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionSurfaceVisual |= CompositionSurfaceVisualChangedFields.Surface;
                        RegisterForSerialization();
                    }
                }

                _surface = value;
                if (changed)
                    OnSurfaceChanged();
            }
        }

        partial void ValidateSurfaceChange(CompositionSurface? oldValue, CompositionSurface? newValue);
        partial void OnSurfaceChanged();
        partial void OnSurfaceChanging();
        void InitializeDefaults()
        {
            InitializeDefaultsExtra();
        }

        partial void InitializeDefaultsExtra();
        private protected override void SerializeChangesCore(BatchStreamWriter writer)
        {
            base.SerializeChangesCore(writer);
            writer.Write(_changedFieldsOfCompositionSurfaceVisual);
            if ((_changedFieldsOfCompositionSurfaceVisual & CompositionSurfaceVisualChangedFields.Surface) == CompositionSurfaceVisualChangedFields.Surface)
                writer.WriteObject(_surface?.Server!);
            {
                _changedFieldsOfCompositionSurfaceVisual = default;
            }
        }
    }
}