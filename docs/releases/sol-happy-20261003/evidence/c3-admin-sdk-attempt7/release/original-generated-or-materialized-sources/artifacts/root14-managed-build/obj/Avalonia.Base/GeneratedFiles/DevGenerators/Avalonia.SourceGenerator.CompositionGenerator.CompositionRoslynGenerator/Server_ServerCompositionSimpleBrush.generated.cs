
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
    unsafe partial class ServerCompositionSimpleBrush : SimpleServerRenderResource
    {
        internal ServerCompositionSimpleBrush(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        double _opacity;
        public double Opacity
        {
            get
            {
                return _opacity;
            }

            set
            {
                var changed = false;
                if (_opacity != value)
                {
                    OnOpacityChanging();
                    changed = true;
                }

                SetValue(s_IdOfOpacityProperty, ref _opacity, value);
                if (changed)
                    OnOpacityChanged();
            }
        }

        partial void OnOpacityChanged();
        partial void OnOpacityChanging();
        internal readonly static CompositionProperty<double> s_IdOfOpacityProperty = CompositionProperty.Register<ServerCompositionSimpleBrush, double>("Opacity", obj => ((ServerCompositionSimpleBrush)obj)._opacity, (obj, v) => ((ServerCompositionSimpleBrush)obj)._opacity = v, obj => ((ServerCompositionSimpleBrush)obj)._opacity);
        RelativePoint _transformOrigin;
        public RelativePoint TransformOrigin
        {
            get
            {
                return _transformOrigin;
            }

            set
            {
                var changed = false;
                if (_transformOrigin != value)
                {
                    OnTransformOriginChanging();
                    changed = true;
                }

                SetValue(s_IdOfTransformOriginProperty, ref _transformOrigin, value);
                if (changed)
                    OnTransformOriginChanged();
            }
        }

        partial void OnTransformOriginChanged();
        partial void OnTransformOriginChanging();
        internal readonly static CompositionProperty<RelativePoint> s_IdOfTransformOriginProperty = CompositionProperty.Register<ServerCompositionSimpleBrush, RelativePoint>("TransformOrigin", obj => ((ServerCompositionSimpleBrush)obj)._transformOrigin, (obj, v) => ((ServerCompositionSimpleBrush)obj)._transformOrigin = v, null);
        Avalonia.Media.ITransform? _transform;
        public Avalonia.Media.ITransform? Transform
        {
            get
            {
                return _transform;
            }

            set
            {
                var changed = false;
                if (_transform != value)
                {
                    OnTransformChanging();
                    changed = true;
                }

                SetValue(s_IdOfTransformProperty, ref _transform, value);
                if (changed)
                    OnTransformChanged();
            }
        }

        partial void OnTransformChanged();
        partial void OnTransformChanging();
        internal readonly static CompositionProperty<Avalonia.Media.ITransform?> s_IdOfTransformProperty = CompositionProperty.Register<ServerCompositionSimpleBrush, Avalonia.Media.ITransform?>("Transform", obj => ((ServerCompositionSimpleBrush)obj)._transform, (obj, v) => ((ServerCompositionSimpleBrush)obj)._transform = v, null);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionSimpleBrushChangedFields>();
            if ((changed & CompositionSimpleBrushChangedFields.Opacity) == CompositionSimpleBrushChangedFields.Opacity)
                Opacity = reader.Read<double>();
            if ((changed & CompositionSimpleBrushChangedFields.TransformOrigin) == CompositionSimpleBrushChangedFields.TransformOrigin)
                TransformOrigin = reader.Read<RelativePoint>();
            if ((changed & CompositionSimpleBrushChangedFields.Transform) == CompositionSimpleBrushChangedFields.Transform)
                Transform = reader.ReadObject<Avalonia.Media.ITransform?>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionSimpleBrushChangedFields changed);
        internal static void SerializeAllChanges(BatchStreamWriter writer, double opacity, RelativePoint transformOrigin, Avalonia.Media.ITransform? transform)
        {
            writer.Write(CompositionSimpleBrushChangedFields.Opacity | CompositionSimpleBrushChangedFields.TransformOrigin | CompositionSimpleBrushChangedFields.Transform);
            writer.Write(opacity);
            writer.Write(transformOrigin);
            writer.WriteObject(transform);
        }
    }
}