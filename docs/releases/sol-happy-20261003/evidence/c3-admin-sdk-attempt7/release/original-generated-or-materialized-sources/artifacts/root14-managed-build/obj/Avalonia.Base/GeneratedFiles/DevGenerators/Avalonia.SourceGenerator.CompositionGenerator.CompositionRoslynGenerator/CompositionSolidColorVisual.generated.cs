
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
    public unsafe partial class CompositionSolidColorVisual : CompositionContainerVisual
    {
        CompositionSolidColorVisualChangedFields _changedFieldsOfCompositionSolidColorVisual;
        internal new ServerCompositionSolidColorVisual Server { get; }

        internal CompositionSolidColorVisual(Compositor compositor, ServerCompositionSolidColorVisual server) : base(compositor, server)
        {
            Server = (ServerCompositionSolidColorVisual)server;
            InitializeDefaults();
        }

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
                    ValidateColorChange(_color, value);
                    OnColorChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _color = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionSolidColorVisual |= CompositionSolidColorVisualChangedFields.Color;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionSolidColorVisual.s_IdOfColorProperty);
                        _changedFieldsOfCompositionSolidColorVisual &= ~CompositionSolidColorVisualChangedFields.ColorAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("Color", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionSolidColorVisual |= CompositionSolidColorVisualChangedFields.ColorAnimated;
                                PendingAnimations[ServerCompositionSolidColorVisual.s_IdOfColorProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "Color", value);
                        }
                    }
                }

                _color = value;
                if (changed)
                    OnColorChanged();
            }
        }

        partial void ValidateColorChange(Avalonia.Media.Color oldValue, Avalonia.Media.Color newValue);
        partial void OnColorChanged();
        partial void OnColorChanging();
        void InitializeDefaults()
        {
            InitializeDefaultsExtra();
        }

        partial void InitializeDefaultsExtra();
        private protected override void SerializeChangesCore(BatchStreamWriter writer)
        {
            base.SerializeChangesCore(writer);
            writer.Write(_changedFieldsOfCompositionSolidColorVisual);
            if ((_changedFieldsOfCompositionSolidColorVisual & CompositionSolidColorVisualChangedFields.ColorAnimated) == CompositionSolidColorVisualChangedFields.ColorAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionSolidColorVisual.s_IdOfColorProperty));
            else if ((_changedFieldsOfCompositionSolidColorVisual & CompositionSolidColorVisualChangedFields.Color) == CompositionSolidColorVisualChangedFields.Color)
                writer.Write(_color);
            {
                _changedFieldsOfCompositionSolidColorVisual = default;
            }
        }

        internal override void StartAnimation(string propertyName, CompositionAnimation animation, Avalonia.Rendering.Composition.Expressions.ExpressionVariant? finalValue)
        {
            if (propertyName == "Color")
            {
                var current = _color;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionSolidColorVisual.s_IdOfColorProperty] = server;
                _changedFieldsOfCompositionSolidColorVisual |= CompositionSolidColorVisualChangedFields.ColorAnimated;
                RegisterForSerialization();
                return;
            }

            base.StartAnimation(propertyName, animation, finalValue);
        }
    }
}