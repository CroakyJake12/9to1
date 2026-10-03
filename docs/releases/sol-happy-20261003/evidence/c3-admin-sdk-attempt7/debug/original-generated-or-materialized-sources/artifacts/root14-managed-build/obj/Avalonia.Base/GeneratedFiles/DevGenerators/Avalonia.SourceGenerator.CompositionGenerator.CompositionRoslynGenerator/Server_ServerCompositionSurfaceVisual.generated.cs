
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
    unsafe partial class ServerCompositionSurfaceVisual : ServerSizeDependantVisual
    {
        internal ServerCompositionSurfaceVisual(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        ServerCompositionSurface? _surface;
        public ServerCompositionSurface? Surface
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
                    OnSurfaceChanging();
                    changed = true;
                }

                SetValue(s_IdOfSurfaceProperty, ref _surface, value);
                if (changed)
                    OnSurfaceChanged();
            }
        }

        partial void OnSurfaceChanged();
        partial void OnSurfaceChanging();
        internal readonly static CompositionProperty<ServerCompositionSurface?> s_IdOfSurfaceProperty = CompositionProperty.Register<ServerCompositionSurfaceVisual, ServerCompositionSurface?>("Surface", obj => ((ServerCompositionSurfaceVisual)obj)._surface, (obj, v) => ((ServerCompositionSurfaceVisual)obj)._surface = v, null);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionSurfaceVisualChangedFields>();
            if ((changed & CompositionSurfaceVisualChangedFields.Surface) == CompositionSurfaceVisualChangedFields.Surface)
                Surface = reader.ReadObject<ServerCompositionSurface?>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionSurfaceVisualChangedFields changed);
    }
}