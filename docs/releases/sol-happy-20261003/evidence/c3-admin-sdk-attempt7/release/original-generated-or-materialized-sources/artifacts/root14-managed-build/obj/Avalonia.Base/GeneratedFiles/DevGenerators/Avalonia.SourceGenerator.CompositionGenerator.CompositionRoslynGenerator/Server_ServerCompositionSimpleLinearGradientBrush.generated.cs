
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
    unsafe partial class ServerCompositionSimpleLinearGradientBrush : ServerCompositionSimpleGradientBrush
    {
        internal ServerCompositionSimpleLinearGradientBrush(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        Avalonia.RelativePoint _startPoint;
        public Avalonia.RelativePoint StartPoint
        {
            get
            {
                return _startPoint;
            }

            set
            {
                var changed = false;
                if (_startPoint != value)
                {
                    OnStartPointChanging();
                    changed = true;
                }

                SetValue(s_IdOfStartPointProperty, ref _startPoint, value);
                if (changed)
                    OnStartPointChanged();
            }
        }

        partial void OnStartPointChanged();
        partial void OnStartPointChanging();
        internal readonly static CompositionProperty<Avalonia.RelativePoint> s_IdOfStartPointProperty = CompositionProperty.Register<ServerCompositionSimpleLinearGradientBrush, Avalonia.RelativePoint>("StartPoint", obj => ((ServerCompositionSimpleLinearGradientBrush)obj)._startPoint, (obj, v) => ((ServerCompositionSimpleLinearGradientBrush)obj)._startPoint = v, null);
        Avalonia.RelativePoint _endPoint;
        public Avalonia.RelativePoint EndPoint
        {
            get
            {
                return _endPoint;
            }

            set
            {
                var changed = false;
                if (_endPoint != value)
                {
                    OnEndPointChanging();
                    changed = true;
                }

                SetValue(s_IdOfEndPointProperty, ref _endPoint, value);
                if (changed)
                    OnEndPointChanged();
            }
        }

        partial void OnEndPointChanged();
        partial void OnEndPointChanging();
        internal readonly static CompositionProperty<Avalonia.RelativePoint> s_IdOfEndPointProperty = CompositionProperty.Register<ServerCompositionSimpleLinearGradientBrush, Avalonia.RelativePoint>("EndPoint", obj => ((ServerCompositionSimpleLinearGradientBrush)obj)._endPoint, (obj, v) => ((ServerCompositionSimpleLinearGradientBrush)obj)._endPoint = v, null);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionSimpleLinearGradientBrushChangedFields>();
            if ((changed & CompositionSimpleLinearGradientBrushChangedFields.StartPoint) == CompositionSimpleLinearGradientBrushChangedFields.StartPoint)
                StartPoint = reader.Read<Avalonia.RelativePoint>();
            if ((changed & CompositionSimpleLinearGradientBrushChangedFields.EndPoint) == CompositionSimpleLinearGradientBrushChangedFields.EndPoint)
                EndPoint = reader.Read<Avalonia.RelativePoint>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionSimpleLinearGradientBrushChangedFields changed);
        internal static void SerializeAllChanges(BatchStreamWriter writer, Avalonia.RelativePoint startPoint, Avalonia.RelativePoint endPoint)
        {
            writer.Write(CompositionSimpleLinearGradientBrushChangedFields.StartPoint | CompositionSimpleLinearGradientBrushChangedFields.EndPoint);
            writer.Write(startPoint);
            writer.Write(endPoint);
        }
    }
}