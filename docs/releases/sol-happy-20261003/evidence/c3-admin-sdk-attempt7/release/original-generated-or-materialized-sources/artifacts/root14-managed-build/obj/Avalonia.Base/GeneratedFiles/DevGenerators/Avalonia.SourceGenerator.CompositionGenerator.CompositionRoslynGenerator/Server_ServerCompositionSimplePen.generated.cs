
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
    unsafe partial class ServerCompositionSimplePen : SimpleServerRenderResource
    {
        internal ServerCompositionSimplePen(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        Avalonia.Media.IBrush? _brush;
        public Avalonia.Media.IBrush? Brush
        {
            get
            {
                return _brush;
            }

            set
            {
                var changed = false;
                if (_brush != value)
                {
                    OnBrushChanging();
                    changed = true;
                }

                SetValue(s_IdOfBrushProperty, ref _brush, value);
                if (changed)
                    OnBrushChanged();
            }
        }

        partial void OnBrushChanged();
        partial void OnBrushChanging();
        internal readonly static CompositionProperty<Avalonia.Media.IBrush?> s_IdOfBrushProperty = CompositionProperty.Register<ServerCompositionSimplePen, Avalonia.Media.IBrush?>("Brush", obj => ((ServerCompositionSimplePen)obj)._brush, (obj, v) => ((ServerCompositionSimplePen)obj)._brush = v, null);
        Avalonia.Media.Immutable.ImmutableDashStyle? _dashStyle;
        public Avalonia.Media.Immutable.ImmutableDashStyle? DashStyle
        {
            get
            {
                return _dashStyle;
            }

            set
            {
                var changed = false;
                if (_dashStyle != value)
                {
                    OnDashStyleChanging();
                    changed = true;
                }

                SetValue(s_IdOfDashStyleProperty, ref _dashStyle, value);
                if (changed)
                    OnDashStyleChanged();
            }
        }

        partial void OnDashStyleChanged();
        partial void OnDashStyleChanging();
        internal readonly static CompositionProperty<Avalonia.Media.Immutable.ImmutableDashStyle?> s_IdOfDashStyleProperty = CompositionProperty.Register<ServerCompositionSimplePen, Avalonia.Media.Immutable.ImmutableDashStyle?>("DashStyle", obj => ((ServerCompositionSimplePen)obj)._dashStyle, (obj, v) => ((ServerCompositionSimplePen)obj)._dashStyle = v, null);
        Avalonia.Media.PenLineCap _lineCap;
        public Avalonia.Media.PenLineCap LineCap
        {
            get
            {
                return _lineCap;
            }

            set
            {
                var changed = false;
                if (_lineCap != value)
                {
                    OnLineCapChanging();
                    changed = true;
                }

                SetValue(s_IdOfLineCapProperty, ref _lineCap, value);
                if (changed)
                    OnLineCapChanged();
            }
        }

        partial void OnLineCapChanged();
        partial void OnLineCapChanging();
        internal readonly static CompositionProperty<Avalonia.Media.PenLineCap> s_IdOfLineCapProperty = CompositionProperty.Register<ServerCompositionSimplePen, Avalonia.Media.PenLineCap>("LineCap", obj => ((ServerCompositionSimplePen)obj)._lineCap, (obj, v) => ((ServerCompositionSimplePen)obj)._lineCap = v, null);
        Avalonia.Media.PenLineJoin _lineJoin;
        public Avalonia.Media.PenLineJoin LineJoin
        {
            get
            {
                return _lineJoin;
            }

            set
            {
                var changed = false;
                if (_lineJoin != value)
                {
                    OnLineJoinChanging();
                    changed = true;
                }

                SetValue(s_IdOfLineJoinProperty, ref _lineJoin, value);
                if (changed)
                    OnLineJoinChanged();
            }
        }

        partial void OnLineJoinChanged();
        partial void OnLineJoinChanging();
        internal readonly static CompositionProperty<Avalonia.Media.PenLineJoin> s_IdOfLineJoinProperty = CompositionProperty.Register<ServerCompositionSimplePen, Avalonia.Media.PenLineJoin>("LineJoin", obj => ((ServerCompositionSimplePen)obj)._lineJoin, (obj, v) => ((ServerCompositionSimplePen)obj)._lineJoin = v, null);
        double _miterLimit;
        public double MiterLimit
        {
            get
            {
                return _miterLimit;
            }

            set
            {
                var changed = false;
                if (_miterLimit != value)
                {
                    OnMiterLimitChanging();
                    changed = true;
                }

                SetValue(s_IdOfMiterLimitProperty, ref _miterLimit, value);
                if (changed)
                    OnMiterLimitChanged();
            }
        }

        partial void OnMiterLimitChanged();
        partial void OnMiterLimitChanging();
        internal readonly static CompositionProperty<double> s_IdOfMiterLimitProperty = CompositionProperty.Register<ServerCompositionSimplePen, double>("MiterLimit", obj => ((ServerCompositionSimplePen)obj)._miterLimit, (obj, v) => ((ServerCompositionSimplePen)obj)._miterLimit = v, obj => ((ServerCompositionSimplePen)obj)._miterLimit);
        double _thickness;
        public double Thickness
        {
            get
            {
                return _thickness;
            }

            set
            {
                var changed = false;
                if (_thickness != value)
                {
                    OnThicknessChanging();
                    changed = true;
                }

                SetValue(s_IdOfThicknessProperty, ref _thickness, value);
                if (changed)
                    OnThicknessChanged();
            }
        }

        partial void OnThicknessChanged();
        partial void OnThicknessChanging();
        internal readonly static CompositionProperty<double> s_IdOfThicknessProperty = CompositionProperty.Register<ServerCompositionSimplePen, double>("Thickness", obj => ((ServerCompositionSimplePen)obj)._thickness, (obj, v) => ((ServerCompositionSimplePen)obj)._thickness = v, obj => ((ServerCompositionSimplePen)obj)._thickness);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionSimplePenChangedFields>();
            if ((changed & CompositionSimplePenChangedFields.Brush) == CompositionSimplePenChangedFields.Brush)
                Brush = reader.ReadObject<Avalonia.Media.IBrush?>();
            if ((changed & CompositionSimplePenChangedFields.DashStyle) == CompositionSimplePenChangedFields.DashStyle)
                DashStyle = reader.ReadObject<Avalonia.Media.Immutable.ImmutableDashStyle?>();
            if ((changed & CompositionSimplePenChangedFields.LineCap) == CompositionSimplePenChangedFields.LineCap)
                LineCap = reader.Read<Avalonia.Media.PenLineCap>();
            if ((changed & CompositionSimplePenChangedFields.LineJoin) == CompositionSimplePenChangedFields.LineJoin)
                LineJoin = reader.Read<Avalonia.Media.PenLineJoin>();
            if ((changed & CompositionSimplePenChangedFields.MiterLimit) == CompositionSimplePenChangedFields.MiterLimit)
                MiterLimit = reader.Read<double>();
            if ((changed & CompositionSimplePenChangedFields.Thickness) == CompositionSimplePenChangedFields.Thickness)
                Thickness = reader.Read<double>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionSimplePenChangedFields changed);
        internal static void SerializeAllChanges(BatchStreamWriter writer, Avalonia.Media.IBrush? brush, Avalonia.Media.Immutable.ImmutableDashStyle? dashStyle, Avalonia.Media.PenLineCap lineCap, Avalonia.Media.PenLineJoin lineJoin, double miterLimit, double thickness)
        {
            writer.Write(CompositionSimplePenChangedFields.Brush | CompositionSimplePenChangedFields.DashStyle | CompositionSimplePenChangedFields.LineCap | CompositionSimplePenChangedFields.LineJoin | CompositionSimplePenChangedFields.MiterLimit | CompositionSimplePenChangedFields.Thickness);
            writer.WriteObject(brush);
            writer.WriteObject(dashStyle);
            writer.Write(lineCap);
            writer.Write(lineJoin);
            writer.Write(miterLimit);
            writer.Write(thickness);
        }
    }
}