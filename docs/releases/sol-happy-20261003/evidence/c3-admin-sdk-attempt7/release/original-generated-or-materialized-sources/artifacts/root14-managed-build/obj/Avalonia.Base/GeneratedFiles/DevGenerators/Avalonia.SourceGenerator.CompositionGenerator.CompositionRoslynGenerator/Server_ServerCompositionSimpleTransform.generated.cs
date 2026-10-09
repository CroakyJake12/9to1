
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
    unsafe partial class ServerCompositionSimpleTransform : SimpleServerRenderResource
    {
        internal ServerCompositionSimpleTransform(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        Avalonia.Matrix _value;
        public Avalonia.Matrix Value
        {
            get
            {
                return _value;
            }

            set
            {
                var changed = false;
                if (_value != value)
                {
                    OnValueChanging();
                    changed = true;
                }

                SetValue(s_IdOfValueProperty, ref _value, value);
                if (changed)
                    OnValueChanged();
            }
        }

        partial void OnValueChanged();
        partial void OnValueChanging();
        internal readonly static CompositionProperty<Avalonia.Matrix> s_IdOfValueProperty = CompositionProperty.Register<ServerCompositionSimpleTransform, Avalonia.Matrix>("Value", obj => ((ServerCompositionSimpleTransform)obj)._value, (obj, v) => ((ServerCompositionSimpleTransform)obj)._value = v, null);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionSimpleTransformChangedFields>();
            if ((changed & CompositionSimpleTransformChangedFields.Value) == CompositionSimpleTransformChangedFields.Value)
                Value = reader.Read<Avalonia.Matrix>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionSimpleTransformChangedFields changed);
        internal static void SerializeAllChanges(BatchStreamWriter writer, Avalonia.Matrix value)
        {
            writer.Write(CompositionSimpleTransformChangedFields.Value);
            writer.Write(value);
        }
    }
}