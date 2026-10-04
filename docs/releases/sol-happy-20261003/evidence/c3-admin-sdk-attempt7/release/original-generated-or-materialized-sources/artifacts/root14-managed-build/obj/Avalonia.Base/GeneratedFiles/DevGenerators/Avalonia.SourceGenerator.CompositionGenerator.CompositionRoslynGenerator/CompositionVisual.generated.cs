
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
    abstract public unsafe partial class CompositionVisual : CompositionObject
    {
        CompositionVisualChangedFields _changedFieldsOfCompositionVisual;
        internal new ServerCompositionVisual Server { get; }

        internal CompositionVisual(Compositor compositor, ServerCompositionVisual server) : base(compositor, server)
        {
            Server = (ServerCompositionVisual)server;
            InitializeDefaults();
        }

        CompositionTarget? _root;
        internal CompositionTarget? Root
        {
            get
            {
                return _root;
            }

            set
            {
                var changed = false;
                if (_root != value)
                {
                    ValidateRootChange(_root, value);
                    OnRootChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _root = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Root;
                        RegisterForSerialization();
                    }
                }

                _root = value;
                if (changed)
                    OnRootChanged();
            }
        }

        partial void ValidateRootChange(CompositionTarget? oldValue, CompositionTarget? newValue);
        partial void OnRootChanged();
        partial void OnRootChanging();
        CompositionVisual? _parent;
        internal CompositionVisual? Parent
        {
            get
            {
                return _parent;
            }

            set
            {
                var changed = false;
                if (_parent != value)
                {
                    ValidateParentChange(_parent, value);
                    OnParentChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _parent = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Parent;
                        RegisterForSerialization();
                    }
                }

                _parent = value;
                if (changed)
                    OnParentChanged();
            }
        }

        partial void ValidateParentChange(CompositionVisual? oldValue, CompositionVisual? newValue);
        partial void OnParentChanged();
        partial void OnParentChanging();
        bool _visible;
        public bool Visible
        {
            get
            {
                return _visible;
            }

            set
            {
                var changed = false;
                if (_visible != value)
                {
                    ValidateVisibleChange(_visible, value);
                    OnVisibleChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _visible = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Visible;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfVisibleProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.VisibleAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("Visible", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.VisibleAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfVisibleProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "Visible", value);
                        }
                    }
                }

                _visible = value;
                if (changed)
                    OnVisibleChanged();
            }
        }

        partial void ValidateVisibleChange(bool oldValue, bool newValue);
        partial void OnVisibleChanged();
        partial void OnVisibleChanging();
        float _opacity;
        public float Opacity
        {
            get
            {
                return _opacity;
            }

            set
            {
                var changed = false;
                if (_opacity != value)
                {
                    ValidateOpacityChange(_opacity, value);
                    OnOpacityChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _opacity = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Opacity;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfOpacityProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.OpacityAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("Opacity", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.OpacityAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfOpacityProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "Opacity", value);
                        }
                    }
                }

                _opacity = value;
                if (changed)
                    OnOpacityChanged();
            }
        }

        partial void ValidateOpacityChange(float oldValue, float newValue);
        partial void OnOpacityChanged();
        partial void OnOpacityChanging();
        Avalonia.Platform.IGeometryImpl? _clip;
        internal Avalonia.Platform.IGeometryImpl? Clip
        {
            get
            {
                return _clip;
            }

            set
            {
                var changed = false;
                if (_clip != value)
                {
                    ValidateClipChange(_clip, value);
                    OnClipChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _clip = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Clip;
                        RegisterForSerialization();
                    }
                }

                _clip = value;
                if (changed)
                    OnClipChanged();
            }
        }

        partial void ValidateClipChange(Avalonia.Platform.IGeometryImpl? oldValue, Avalonia.Platform.IGeometryImpl? newValue);
        partial void OnClipChanged();
        partial void OnClipChanging();
        bool _clipToBounds;
        public bool ClipToBounds
        {
            get
            {
                return _clipToBounds;
            }

            set
            {
                var changed = false;
                if (_clipToBounds != value)
                {
                    ValidateClipToBoundsChange(_clipToBounds, value);
                    OnClipToBoundsChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _clipToBounds = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.ClipToBounds;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfClipToBoundsProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.ClipToBoundsAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("ClipToBounds", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.ClipToBoundsAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfClipToBoundsProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "ClipToBounds", value);
                        }
                    }
                }

                _clipToBounds = value;
                if (changed)
                    OnClipToBoundsChanged();
            }
        }

        partial void ValidateClipToBoundsChange(bool oldValue, bool newValue);
        partial void OnClipToBoundsChanged();
        partial void OnClipToBoundsChanging();
        Vector3D _offset;
        public Vector3D Offset
        {
            get
            {
                return _offset;
            }

            set
            {
                var changed = false;
                if (_offset != value)
                {
                    ValidateOffsetChange(_offset, value);
                    OnOffsetChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _offset = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Offset;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfOffsetProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.OffsetAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("Offset", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.OffsetAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfOffsetProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "Offset", value);
                        }
                    }
                }

                _offset = value;
                if (changed)
                    OnOffsetChanged();
            }
        }

        partial void ValidateOffsetChange(Vector3D oldValue, Vector3D newValue);
        partial void OnOffsetChanged();
        partial void OnOffsetChanging();
        Vector3D _translation;
        public Vector3D Translation
        {
            get
            {
                return _translation;
            }

            set
            {
                var changed = false;
                if (_translation != value)
                {
                    ValidateTranslationChange(_translation, value);
                    OnTranslationChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _translation = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Translation;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfTranslationProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.TranslationAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("Translation", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.TranslationAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfTranslationProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "Translation", value);
                        }
                    }
                }

                _translation = value;
                if (changed)
                    OnTranslationChanged();
            }
        }

        partial void ValidateTranslationChange(Vector3D oldValue, Vector3D newValue);
        partial void OnTranslationChanged();
        partial void OnTranslationChanging();
        Vector _size;
        public Vector Size
        {
            get
            {
                return _size;
            }

            set
            {
                var changed = false;
                if (_size != value)
                {
                    ValidateSizeChange(_size, value);
                    OnSizeChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _size = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Size;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfSizeProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.SizeAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("Size", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.SizeAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfSizeProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "Size", value);
                        }
                    }
                }

                _size = value;
                if (changed)
                    OnSizeChanged();
            }
        }

        partial void ValidateSizeChange(Vector oldValue, Vector newValue);
        partial void OnSizeChanged();
        partial void OnSizeChanging();
        Vector _anchorPoint;
        public Vector AnchorPoint
        {
            get
            {
                return _anchorPoint;
            }

            set
            {
                var changed = false;
                if (_anchorPoint != value)
                {
                    ValidateAnchorPointChange(_anchorPoint, value);
                    OnAnchorPointChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _anchorPoint = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.AnchorPoint;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfAnchorPointProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.AnchorPointAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("AnchorPoint", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.AnchorPointAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfAnchorPointProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "AnchorPoint", value);
                        }
                    }
                }

                _anchorPoint = value;
                if (changed)
                    OnAnchorPointChanged();
            }
        }

        partial void ValidateAnchorPointChange(Vector oldValue, Vector newValue);
        partial void OnAnchorPointChanged();
        partial void OnAnchorPointChanging();
        Vector3D _centerPoint;
        public Vector3D CenterPoint
        {
            get
            {
                return _centerPoint;
            }

            set
            {
                var changed = false;
                if (_centerPoint != value)
                {
                    ValidateCenterPointChange(_centerPoint, value);
                    OnCenterPointChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _centerPoint = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.CenterPoint;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfCenterPointProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.CenterPointAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("CenterPoint", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.CenterPointAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfCenterPointProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "CenterPoint", value);
                        }
                    }
                }

                _centerPoint = value;
                if (changed)
                    OnCenterPointChanged();
            }
        }

        partial void ValidateCenterPointChange(Vector3D oldValue, Vector3D newValue);
        partial void OnCenterPointChanged();
        partial void OnCenterPointChanging();
        float _rotationAngle;
        public float RotationAngle
        {
            get
            {
                return _rotationAngle;
            }

            set
            {
                var changed = false;
                if (_rotationAngle != value)
                {
                    ValidateRotationAngleChange(_rotationAngle, value);
                    OnRotationAngleChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _rotationAngle = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.RotationAngle;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfRotationAngleProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.RotationAngleAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("RotationAngle", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.RotationAngleAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfRotationAngleProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "RotationAngle", value);
                        }
                    }
                }

                _rotationAngle = value;
                if (changed)
                    OnRotationAngleChanged();
            }
        }

        partial void ValidateRotationAngleChange(float oldValue, float newValue);
        partial void OnRotationAngleChanged();
        partial void OnRotationAngleChanging();
        Quaternion _orientation;
        public Quaternion Orientation
        {
            get
            {
                return _orientation;
            }

            set
            {
                var changed = false;
                if (_orientation != value)
                {
                    ValidateOrientationChange(_orientation, value);
                    OnOrientationChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _orientation = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Orientation;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfOrientationProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.OrientationAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("Orientation", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.OrientationAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfOrientationProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "Orientation", value);
                        }
                    }
                }

                _orientation = value;
                if (changed)
                    OnOrientationChanged();
            }
        }

        partial void ValidateOrientationChange(Quaternion oldValue, Quaternion newValue);
        partial void OnOrientationChanged();
        partial void OnOrientationChanging();
        Vector3D _scale;
        public Vector3D Scale
        {
            get
            {
                return _scale;
            }

            set
            {
                var changed = false;
                if (_scale != value)
                {
                    ValidateScaleChange(_scale, value);
                    OnScaleChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _scale = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Scale;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfScaleProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.ScaleAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("Scale", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.ScaleAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfScaleProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "Scale", value);
                        }
                    }
                }

                _scale = value;
                if (changed)
                    OnScaleChanged();
            }
        }

        partial void ValidateScaleChange(Vector3D oldValue, Vector3D newValue);
        partial void OnScaleChanged();
        partial void OnScaleChanging();
        Avalonia.Matrix _transformMatrix;
        internal Avalonia.Matrix TransformMatrix
        {
            get
            {
                return _transformMatrix;
            }

            set
            {
                var changed = false;
                if (_transformMatrix != value)
                {
                    ValidateTransformMatrixChange(_transformMatrix, value);
                    OnTransformMatrixChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _transformMatrix = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.TransformMatrix;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionVisual.s_IdOfTransformMatrixProperty);
                        _changedFieldsOfCompositionVisual &= ~CompositionVisualChangedFields.TransformMatrixAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("TransformMatrix", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.TransformMatrixAnimated;
                                PendingAnimations[ServerCompositionVisual.s_IdOfTransformMatrixProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "TransformMatrix", value);
                        }
                    }
                }

                _transformMatrix = value;
                if (changed)
                    OnTransformMatrixChanged();
            }
        }

        partial void ValidateTransformMatrixChange(Avalonia.Matrix oldValue, Avalonia.Matrix newValue);
        partial void OnTransformMatrixChanged();
        partial void OnTransformMatrixChanging();
        CompositionVisual? _adornedVisual;
        internal CompositionVisual? AdornedVisual
        {
            get
            {
                return _adornedVisual;
            }

            set
            {
                var changed = false;
                if (_adornedVisual != value)
                {
                    ValidateAdornedVisualChange(_adornedVisual, value);
                    OnAdornedVisualChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _adornedVisual = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.AdornedVisual;
                        RegisterForSerialization();
                    }
                }

                _adornedVisual = value;
                if (changed)
                    OnAdornedVisualChanged();
            }
        }

        partial void ValidateAdornedVisualChange(CompositionVisual? oldValue, CompositionVisual? newValue);
        partial void OnAdornedVisualChanged();
        partial void OnAdornedVisualChanging();
        bool _adornerIsClipped;
        internal bool AdornerIsClipped
        {
            get
            {
                return _adornerIsClipped;
            }

            set
            {
                var changed = false;
                if (_adornerIsClipped != value)
                {
                    ValidateAdornerIsClippedChange(_adornerIsClipped, value);
                    OnAdornerIsClippedChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _adornerIsClipped = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.AdornerIsClipped;
                        RegisterForSerialization();
                    }
                }

                _adornerIsClipped = value;
                if (changed)
                    OnAdornerIsClippedChanged();
            }
        }

        partial void ValidateAdornerIsClippedChange(bool oldValue, bool newValue);
        partial void OnAdornerIsClippedChanged();
        partial void OnAdornerIsClippedChanging();
        Avalonia.Media.IBrush? _opacityMaskBrush;
        private Avalonia.Media.IBrush? OpacityMaskBrushTransportField
        {
            get
            {
                return _opacityMaskBrush;
            }

            set
            {
                var changed = false;
                if (_opacityMaskBrush != value)
                {
                    ValidateOpacityMaskBrushChange(_opacityMaskBrush, value);
                    OnOpacityMaskBrushChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _opacityMaskBrush = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.OpacityMaskBrush;
                        RegisterForSerialization();
                    }
                }

                _opacityMaskBrush = value;
                if (changed)
                    OnOpacityMaskBrushChanged();
            }
        }

        partial void ValidateOpacityMaskBrushChange(Avalonia.Media.IBrush? oldValue, Avalonia.Media.IBrush? newValue);
        partial void OnOpacityMaskBrushChanged();
        partial void OnOpacityMaskBrushChanging();
        Avalonia.Media.IImmutableEffect? _effect;
        internal Avalonia.Media.IImmutableEffect? Effect
        {
            get
            {
                return _effect;
            }

            set
            {
                var changed = false;
                if (_effect != value)
                {
                    ValidateEffectChange(_effect, value);
                    OnEffectChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _effect = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.Effect;
                        RegisterForSerialization();
                    }
                }

                _effect = value;
                if (changed)
                    OnEffectChanged();
            }
        }

        partial void ValidateEffectChange(Avalonia.Media.IImmutableEffect? oldValue, Avalonia.Media.IImmutableEffect? newValue);
        partial void OnEffectChanged();
        partial void OnEffectChanging();
        Avalonia.Media.RenderOptions _renderOptions;
        public Avalonia.Media.RenderOptions RenderOptions
        {
            get
            {
                return _renderOptions;
            }

            set
            {
                var changed = false;
                if (_renderOptions != value)
                {
                    ValidateRenderOptionsChange(_renderOptions, value);
                    OnRenderOptionsChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _renderOptions = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.RenderOptions;
                        RegisterForSerialization();
                    }
                }

                _renderOptions = value;
                if (changed)
                    OnRenderOptionsChanged();
            }
        }

        partial void ValidateRenderOptionsChange(Avalonia.Media.RenderOptions oldValue, Avalonia.Media.RenderOptions newValue);
        partial void OnRenderOptionsChanged();
        partial void OnRenderOptionsChanging();
        Avalonia.Media.TextOptions _textOptions;
        public Avalonia.Media.TextOptions TextOptions
        {
            get
            {
                return _textOptions;
            }

            set
            {
                var changed = false;
                if (_textOptions != value)
                {
                    ValidateTextOptionsChange(_textOptions, value);
                    OnTextOptionsChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _textOptions = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.TextOptions;
                        RegisterForSerialization();
                    }
                }

                _textOptions = value;
                if (changed)
                    OnTextOptionsChanged();
            }
        }

        partial void ValidateTextOptionsChange(Avalonia.Media.TextOptions oldValue, Avalonia.Media.TextOptions newValue);
        partial void OnTextOptionsChanged();
        partial void OnTextOptionsChanging();
        CompositionCacheMode? _cacheMode;
        internal CompositionCacheMode? CacheMode
        {
            get
            {
                return _cacheMode;
            }

            set
            {
                var changed = false;
                if (_cacheMode != value)
                {
                    ValidateCacheModeChange(_cacheMode, value);
                    OnCacheModeChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _cacheMode = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.CacheMode;
                        RegisterForSerialization();
                    }
                }

                _cacheMode = value;
                if (changed)
                    OnCacheModeChanged();
            }
        }

        partial void ValidateCacheModeChange(CompositionCacheMode? oldValue, CompositionCacheMode? newValue);
        partial void OnCacheModeChanged();
        partial void OnCacheModeChanging();
        void InitializeDefaults()
        {
            InitializeDefaultsExtra();
            Visible = true;
            Opacity = 1;
            ClipToBounds = true;
            Orientation = Quaternion.Identity;
            Scale = new Avalonia.Vector3D(1, 1, 1);
            TransformMatrix = Avalonia.Matrix.Identity;
        }

        partial void InitializeDefaultsExtra();
        private protected override void SerializeChangesCore(BatchStreamWriter writer)
        {
            base.SerializeChangesCore(writer);
            writer.Write(_changedFieldsOfCompositionVisual);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Root) == CompositionVisualChangedFields.Root)
                writer.WriteObject(_root?.Server!);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Parent) == CompositionVisualChangedFields.Parent)
                writer.WriteObject(_parent?.Server!);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.VisibleAnimated) == CompositionVisualChangedFields.VisibleAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfVisibleProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Visible) == CompositionVisualChangedFields.Visible)
                writer.Write(_visible);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.OpacityAnimated) == CompositionVisualChangedFields.OpacityAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfOpacityProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Opacity) == CompositionVisualChangedFields.Opacity)
                writer.Write(_opacity);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Clip) == CompositionVisualChangedFields.Clip)
                writer.WriteObject(_clip);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.ClipToBoundsAnimated) == CompositionVisualChangedFields.ClipToBoundsAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfClipToBoundsProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.ClipToBounds) == CompositionVisualChangedFields.ClipToBounds)
                writer.Write(_clipToBounds);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.OffsetAnimated) == CompositionVisualChangedFields.OffsetAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfOffsetProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Offset) == CompositionVisualChangedFields.Offset)
                writer.Write(_offset);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.TranslationAnimated) == CompositionVisualChangedFields.TranslationAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfTranslationProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Translation) == CompositionVisualChangedFields.Translation)
                writer.Write(_translation);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.SizeAnimated) == CompositionVisualChangedFields.SizeAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfSizeProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Size) == CompositionVisualChangedFields.Size)
                writer.Write(_size);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.AnchorPointAnimated) == CompositionVisualChangedFields.AnchorPointAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfAnchorPointProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.AnchorPoint) == CompositionVisualChangedFields.AnchorPoint)
                writer.Write(_anchorPoint);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.CenterPointAnimated) == CompositionVisualChangedFields.CenterPointAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfCenterPointProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.CenterPoint) == CompositionVisualChangedFields.CenterPoint)
                writer.Write(_centerPoint);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.RotationAngleAnimated) == CompositionVisualChangedFields.RotationAngleAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfRotationAngleProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.RotationAngle) == CompositionVisualChangedFields.RotationAngle)
                writer.Write(_rotationAngle);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.OrientationAnimated) == CompositionVisualChangedFields.OrientationAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfOrientationProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Orientation) == CompositionVisualChangedFields.Orientation)
                writer.Write(_orientation);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.ScaleAnimated) == CompositionVisualChangedFields.ScaleAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfScaleProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Scale) == CompositionVisualChangedFields.Scale)
                writer.Write(_scale);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.TransformMatrixAnimated) == CompositionVisualChangedFields.TransformMatrixAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionVisual.s_IdOfTransformMatrixProperty));
            else if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.TransformMatrix) == CompositionVisualChangedFields.TransformMatrix)
                writer.Write(_transformMatrix);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.AdornedVisual) == CompositionVisualChangedFields.AdornedVisual)
                writer.WriteObject(_adornedVisual?.Server!);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.AdornerIsClipped) == CompositionVisualChangedFields.AdornerIsClipped)
                writer.Write(_adornerIsClipped);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.OpacityMaskBrush) == CompositionVisualChangedFields.OpacityMaskBrush)
                writer.WriteObject(_opacityMaskBrush);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.Effect) == CompositionVisualChangedFields.Effect)
                writer.WriteObject(_effect);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.RenderOptions) == CompositionVisualChangedFields.RenderOptions)
                writer.Write(_renderOptions);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.TextOptions) == CompositionVisualChangedFields.TextOptions)
                writer.Write(_textOptions);
            if ((_changedFieldsOfCompositionVisual & CompositionVisualChangedFields.CacheMode) == CompositionVisualChangedFields.CacheMode)
                writer.WriteObject(_cacheMode?.Server!);
            {
                _changedFieldsOfCompositionVisual = default;
            }
        }

        internal override void StartAnimation(string propertyName, CompositionAnimation animation, Avalonia.Rendering.Composition.Expressions.ExpressionVariant? finalValue)
        {
            if (propertyName == "Visible")
            {
                var current = _visible;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfVisibleProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.VisibleAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "Opacity")
            {
                var current = _opacity;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfOpacityProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.OpacityAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "ClipToBounds")
            {
                var current = _clipToBounds;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfClipToBoundsProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.ClipToBoundsAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "Offset")
            {
                var current = _offset;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfOffsetProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.OffsetAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "Translation")
            {
                var current = _translation;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfTranslationProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.TranslationAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "Size")
            {
                var current = _size;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfSizeProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.SizeAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "AnchorPoint")
            {
                var current = _anchorPoint;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfAnchorPointProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.AnchorPointAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "CenterPoint")
            {
                var current = _centerPoint;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfCenterPointProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.CenterPointAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "RotationAngle")
            {
                var current = _rotationAngle;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfRotationAngleProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.RotationAngleAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "Orientation")
            {
                var current = _orientation;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfOrientationProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.OrientationAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "Scale")
            {
                var current = _scale;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfScaleProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.ScaleAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "TransformMatrix")
            {
                var current = _transformMatrix;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionVisual.s_IdOfTransformMatrixProperty] = server;
                _changedFieldsOfCompositionVisual |= CompositionVisualChangedFields.TransformMatrixAnimated;
                RegisterForSerialization();
                return;
            }

            base.StartAnimation(propertyName, animation, finalValue);
        }
    }
}