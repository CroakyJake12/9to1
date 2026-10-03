
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
    unsafe partial class ServerCompositionSimpleConicGradientBrush : ServerCompositionSimpleGradientBrush
    {
        internal ServerCompositionSimpleConicGradientBrush(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        double _angle;
        public double Angle
        {
            get
            {
                return _angle;
            }

            set
            {
                var changed = false;
                if (_angle != value)
                {
                    OnAngleChanging();
                    changed = true;
                }

                SetValue(s_IdOfAngleProperty, ref _angle, value);
                if (changed)
                    OnAngleChanged();
            }
        }

        partial void OnAngleChanged();
        partial void OnAngleChanging();
        internal readonly static CompositionProperty<double> s_IdOfAngleProperty = CompositionProperty.Register<ServerCompositionSimpleConicGradientBrush, double>("Angle", obj => ((ServerCompositionSimpleConicGradientBrush)obj)._angle, (obj, v) => ((ServerCompositionSimpleConicGradientBrush)obj)._angle = v, obj => ((ServerCompositionSimpleConicGradientBrush)obj)._angle);
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
        internal readonly static CompositionProperty<Avalonia.RelativePoint> s_IdOfCenterProperty = CompositionProperty.Register<ServerCompositionSimpleConicGradientBrush, Avalonia.RelativePoint>("Center", obj => ((ServerCompositionSimpleConicGradientBrush)obj)._center, (obj, v) => ((ServerCompositionSimpleConicGradientBrush)obj)._center = v, null);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionSimpleConicGradientBrushChangedFields>();
            if ((changed & CompositionSimpleConicGradientBrushChangedFields.Angle) == CompositionSimpleConicGradientBrushChangedFields.Angle)
                Angle = reader.Read<double>();
            if ((changed & CompositionSimpleConicGradientBrushChangedFields.Center) == CompositionSimpleConicGradientBrushChangedFields.Center)
                Center = reader.Read<Avalonia.RelativePoint>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionSimpleConicGradientBrushChangedFields changed);
        internal static void SerializeAllChanges(BatchStreamWriter writer, double angle, Avalonia.RelativePoint center)
        {
            writer.Write(CompositionSimpleConicGradientBrushChangedFields.Angle | CompositionSimpleConicGradientBrushChangedFields.Center);
            writer.Write(angle);
            writer.Write(center);
        }
    }
}