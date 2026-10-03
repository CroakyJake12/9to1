
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
    unsafe partial class ServerCompositionExperimentalAcrylicVisual : ServerCompositionDrawListVisual
    {
        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
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
                    OnMaterialChanging();
                    changed = true;
                }

                SetValue(s_IdOfMaterialProperty, ref _material, value);
                if (changed)
                    OnMaterialChanged();
            }
        }

        partial void OnMaterialChanged();
        partial void OnMaterialChanging();
        internal readonly static CompositionProperty<Avalonia.Media.ImmutableExperimentalAcrylicMaterial> s_IdOfMaterialProperty = CompositionProperty.Register<ServerCompositionExperimentalAcrylicVisual, Avalonia.Media.ImmutableExperimentalAcrylicMaterial>("Material", obj => ((ServerCompositionExperimentalAcrylicVisual)obj)._material, (obj, v) => ((ServerCompositionExperimentalAcrylicVisual)obj)._material = v, null);
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
                    OnCornerRadiusChanging();
                    changed = true;
                }

                SetValue(s_IdOfCornerRadiusProperty, ref _cornerRadius, value);
                if (changed)
                    OnCornerRadiusChanged();
            }
        }

        partial void OnCornerRadiusChanged();
        partial void OnCornerRadiusChanging();
        internal readonly static CompositionProperty<CornerRadius> s_IdOfCornerRadiusProperty = CompositionProperty.Register<ServerCompositionExperimentalAcrylicVisual, CornerRadius>("CornerRadius", obj => ((ServerCompositionExperimentalAcrylicVisual)obj)._cornerRadius, (obj, v) => ((ServerCompositionExperimentalAcrylicVisual)obj)._cornerRadius = v, null);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionExperimentalAcrylicVisualChangedFields>();
            if ((changed & CompositionExperimentalAcrylicVisualChangedFields.Material) == CompositionExperimentalAcrylicVisualChangedFields.Material)
                Material = reader.Read<Avalonia.Media.ImmutableExperimentalAcrylicMaterial>();
            if ((changed & CompositionExperimentalAcrylicVisualChangedFields.CornerRadius) == CompositionExperimentalAcrylicVisualChangedFields.CornerRadius)
                CornerRadius = reader.Read<CornerRadius>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionExperimentalAcrylicVisualChangedFields changed);
    }
}