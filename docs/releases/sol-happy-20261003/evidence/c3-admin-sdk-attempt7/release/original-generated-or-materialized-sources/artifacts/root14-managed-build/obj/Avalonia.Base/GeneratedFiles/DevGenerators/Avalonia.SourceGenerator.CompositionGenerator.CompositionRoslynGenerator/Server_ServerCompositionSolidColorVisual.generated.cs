
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
    unsafe partial class ServerCompositionSolidColorVisual : ServerSizeDependantVisual
    {
        internal ServerCompositionSolidColorVisual(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        Avalonia.Media.Color _color;
        public Avalonia.Media.Color Color { get => _color; set => SetAnimatedValue(s_IdOfColorProperty, out _color, value); }

        internal readonly static CompositionProperty<Avalonia.Media.Color> s_IdOfColorProperty = CompositionProperty.Register<ServerCompositionSolidColorVisual, Avalonia.Media.Color>("Color", obj => ((ServerCompositionSolidColorVisual)obj)._color, (obj, v) => ((ServerCompositionSolidColorVisual)obj)._color = v, obj => ((ServerCompositionSolidColorVisual)obj)._color);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionSolidColorVisualChangedFields>();
            if ((changed & CompositionSolidColorVisualChangedFields.ColorAnimated) == CompositionSolidColorVisualChangedFields.ColorAnimated)
                SetAnimatedValue(s_IdOfColorProperty, ref _color, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionSolidColorVisualChangedFields.Color) == CompositionSolidColorVisualChangedFields.Color)
                Color = reader.Read<Avalonia.Media.Color>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionSolidColorVisualChangedFields changed);
        public override CompositionProperty? GetCompositionProperty(string name)
        {
            if (name == "Color")
                return s_IdOfColorProperty;
            return base.GetCompositionProperty(name);
        }
    }
}