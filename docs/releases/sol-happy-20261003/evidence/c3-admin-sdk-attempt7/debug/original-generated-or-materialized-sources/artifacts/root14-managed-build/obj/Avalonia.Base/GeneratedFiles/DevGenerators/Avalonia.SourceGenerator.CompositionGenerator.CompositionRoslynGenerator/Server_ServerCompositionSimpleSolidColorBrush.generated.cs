
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
    unsafe partial class ServerCompositionSimpleSolidColorBrush : ServerCompositionSimpleBrush
    {
        internal ServerCompositionSimpleSolidColorBrush(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        Avalonia.Media.Color _color;
        public Avalonia.Media.Color Color
        {
            get
            {
                return _color;
            }

            set
            {
                var changed = false;
                if (_color != value)
                {
                    OnColorChanging();
                    changed = true;
                }

                SetValue(s_IdOfColorProperty, ref _color, value);
                if (changed)
                    OnColorChanged();
            }
        }

        partial void OnColorChanged();
        partial void OnColorChanging();
        internal readonly static CompositionProperty<Avalonia.Media.Color> s_IdOfColorProperty = CompositionProperty.Register<ServerCompositionSimpleSolidColorBrush, Avalonia.Media.Color>("Color", obj => ((ServerCompositionSimpleSolidColorBrush)obj)._color, (obj, v) => ((ServerCompositionSimpleSolidColorBrush)obj)._color = v, obj => ((ServerCompositionSimpleSolidColorBrush)obj)._color);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionSimpleSolidColorBrushChangedFields>();
            if ((changed & CompositionSimpleSolidColorBrushChangedFields.Color) == CompositionSimpleSolidColorBrushChangedFields.Color)
                Color = reader.Read<Avalonia.Media.Color>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionSimpleSolidColorBrushChangedFields changed);
        internal static void SerializeAllChanges(BatchStreamWriter writer, Avalonia.Media.Color color)
        {
            writer.Write(CompositionSimpleSolidColorBrushChangedFields.Color);
            writer.Write(color);
        }
    }
}