
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
    unsafe partial class ServerCompositionSimpleRadialGradientBrush : ServerCompositionSimpleGradientBrush
    {
        internal ServerCompositionSimpleRadialGradientBrush(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        Avalonia.RelativePoint _center;
        public Avalonia.RelativePoint Center
        {
            get
            {
                return _center;
            }

            set
            {
                var changed = false;
                if (_center != value)
                {
                    OnCenterChanging();
                    changed = true;
                }

                SetValue(s_IdOfCenterProperty, ref _center, value);
                if (changed)
                    OnCenterChanged();
            }
        }

        partial void OnCenterChanged();
        partial void OnCenterChanging();
        internal readonly static CompositionProperty<Avalonia.RelativePoint> s_IdOfCenterProperty = CompositionProperty.Register<ServerCompositionSimpleRadialGradientBrush, Avalonia.RelativePoint>("Center", obj => ((ServerCompositionSimpleRadialGradientBrush)obj)._center, (obj, v) => ((ServerCompositionSimpleRadialGradientBrush)obj)._center = v, null);
        Avalonia.RelativePoint _gradientOrigin;
        public Avalonia.RelativePoint GradientOrigin
        {
            get
            {
                return _gradientOrigin;
            }

            set
            {
                var changed = false;
                if (_gradientOrigin != value)
                {
                    OnGradientOriginChanging();
                    changed = true;
                }

                SetValue(s_IdOfGradientOriginProperty, ref _gradientOrigin, value);
                if (changed)
                    OnGradientOriginChanged();
            }
        }

        partial void OnGradientOriginChanged();
        partial void OnGradientOriginChanging();
        internal readonly static CompositionProperty<Avalonia.RelativePoint> s_IdOfGradientOriginProperty = CompositionProperty.Register<ServerCompositionSimpleRadialGradientBrush, Avalonia.RelativePoint>("GradientOrigin", obj => ((ServerCompositionSimpleRadialGradientBrush)obj)._gradientOrigin, (obj, v) => ((ServerCompositionSimpleRadialGradientBrush)obj)._gradientOrigin = v, null);
        Avalonia.RelativeScalar _radiusX;
        public Avalonia.RelativeScalar RadiusX
        {
            get
            {
                return _radiusX;
            }

            set
            {
                var changed = false;
                if (_radiusX != value)
                {
                    OnRadiusXChanging();
                    changed = true;
                }

                SetValue(s_IdOfRadiusXProperty, ref _radiusX, value);
                if (changed)
                    OnRadiusXChanged();
            }
        }

        partial void OnRadiusXChanged();
        partial void OnRadiusXChanging();
        internal readonly static CompositionProperty<Avalonia.RelativeScalar> s_IdOfRadiusXProperty = CompositionProperty.Register<ServerCompositionSimpleRadialGradientBrush, Avalonia.RelativeScalar>("RadiusX", obj => ((ServerCompositionSimpleRadialGradientBrush)obj)._radiusX, (obj, v) => ((ServerCompositionSimpleRadialGradientBrush)obj)._radiusX = v, null);
        Avalonia.RelativeScalar _radiusY;
        public Avalonia.RelativeScalar RadiusY
        {
            get
            {
                return _radiusY;
            }

            set
            {
                var changed = false;
                if (_radiusY != value)
                {
                    OnRadiusYChanging();
                    changed = true;
                }

                SetValue(s_IdOfRadiusYProperty, ref _radiusY, value);
                if (changed)
                    OnRadiusYChanged();
            }
        }

        partial void OnRadiusYChanged();
        partial void OnRadiusYChanging();
        internal readonly static CompositionProperty<Avalonia.RelativeScalar> s_IdOfRadiusYProperty = CompositionProperty.Register<ServerCompositionSimpleRadialGradientBrush, Avalonia.RelativeScalar>("RadiusY", obj => ((ServerCompositionSimpleRadialGradientBrush)obj)._radiusY, (obj, v) => ((ServerCompositionSimpleRadialGradientBrush)obj)._radiusY = v, null);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionSimpleRadialGradientBrushChangedFields>();
            if ((changed & CompositionSimpleRadialGradientBrushChangedFields.Center) == CompositionSimpleRadialGradientBrushChangedFields.Center)
                Center = reader.Read<Avalonia.RelativePoint>();
            if ((changed & CompositionSimpleRadialGradientBrushChangedFields.GradientOrigin) == CompositionSimpleRadialGradientBrushChangedFields.GradientOrigin)
                GradientOrigin = reader.Read<Avalonia.RelativePoint>();
            if ((changed & CompositionSimpleRadialGradientBrushChangedFields.RadiusX) == CompositionSimpleRadialGradientBrushChangedFields.RadiusX)
                RadiusX = reader.Read<Avalonia.RelativeScalar>();
            if ((changed & CompositionSimpleRadialGradientBrushChangedFields.RadiusY) == CompositionSimpleRadialGradientBrushChangedFields.RadiusY)
                RadiusY = reader.Read<Avalonia.RelativeScalar>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionSimpleRadialGradientBrushChangedFields changed);
        internal static void SerializeAllChanges(BatchStreamWriter writer, Avalonia.RelativePoint center, Avalonia.RelativePoint gradientOrigin, Avalonia.RelativeScalar radiusX, Avalonia.RelativeScalar radiusY)
        {
            writer.Write(CompositionSimpleRadialGradientBrushChangedFields.Center | CompositionSimpleRadialGradientBrushChangedFields.GradientOrigin | CompositionSimpleRadialGradientBrushChangedFields.RadiusX | CompositionSimpleRadialGradientBrushChangedFields.RadiusY);
            writer.Write(center);
            writer.Write(gradientOrigin);
            writer.Write(radiusX);
            writer.Write(radiusY);
        }
    }
}