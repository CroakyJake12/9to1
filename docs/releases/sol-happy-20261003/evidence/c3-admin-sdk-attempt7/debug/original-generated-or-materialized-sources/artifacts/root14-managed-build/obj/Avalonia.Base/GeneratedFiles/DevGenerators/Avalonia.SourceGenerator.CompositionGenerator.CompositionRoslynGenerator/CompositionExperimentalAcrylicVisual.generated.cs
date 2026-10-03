
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
    internal unsafe partial class CompositionExperimentalAcrylicVisual : CompositionDrawListVisual
    {
        CompositionExperimentalAcrylicVisualChangedFields _changedFieldsOfCompositionExperimentalAcrylicVisual;
        Avalonia.Media.ImmutableExperimentalAcrylicMaterial _material;
        public Avalonia.Media.ImmutableExperimentalAcrylicMaterial Material
        {
            get
            {
                return _material;
            }

            set
            {
                var changed = false;
                if (_material != value)
                {
                    ValidateMaterialChange(_material, value);
                    OnMaterialChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _material = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionExperimentalAcrylicVisual |= CompositionExperimentalAcrylicVisualChangedFields.Material;
                        RegisterForSerialization();
                    }
                }

                _material = value;
                if (changed)
                    OnMaterialChanged();
            }
        }

        partial void ValidateMaterialChange(Avalonia.Media.ImmutableExperimentalAcrylicMaterial oldValue, Avalonia.Media.ImmutableExperimentalAcrylicMaterial newValue);
        partial void OnMaterialChanged();
        partial void OnMaterialChanging();
        CornerRadius _cornerRadius;
        public CornerRadius CornerRadius
        {
            get
            {
                return _cornerRadius;
            }

            set
            {
                var changed = false;
                if (_cornerRadius != value)
                {
                    ValidateCornerRadiusChange(_cornerRadius, value);
                    OnCornerRadiusChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _cornerRadius = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionExperimentalAcrylicVisual |= CompositionExperimentalAcrylicVisualChangedFields.CornerRadius;
                        RegisterForSerialization();
                    }
                }

                _cornerRadius = value;
                if (changed)
                    OnCornerRadiusChanged();
            }
        }

        partial void ValidateCornerRadiusChange(CornerRadius oldValue, CornerRadius newValue);
        partial void OnCornerRadiusChanged();
        partial void OnCornerRadiusChanging();
        void InitializeDefaults()
        {
            InitializeDefaultsExtra();
        }

        partial void InitializeDefaultsExtra();
        private protected override void SerializeChangesCore(BatchStreamWriter writer)
        {
            base.SerializeChangesCore(writer);
            writer.Write(_changedFieldsOfCompositionExperimentalAcrylicVisual);
            if ((_changedFieldsOfCompositionExperimentalAcrylicVisual & CompositionExperimentalAcrylicVisualChangedFields.Material) == CompositionExperimentalAcrylicVisualChangedFields.Material)
                writer.Write(_material);
            if ((_changedFieldsOfCompositionExperimentalAcrylicVisual & CompositionExperimentalAcrylicVisualChangedFields.CornerRadius) == CompositionExperimentalAcrylicVisualChangedFields.CornerRadius)
                writer.Write(_cornerRadius);
            {
                _changedFieldsOfCompositionExperimentalAcrylicVisual = default;
            }
        }
    }
}