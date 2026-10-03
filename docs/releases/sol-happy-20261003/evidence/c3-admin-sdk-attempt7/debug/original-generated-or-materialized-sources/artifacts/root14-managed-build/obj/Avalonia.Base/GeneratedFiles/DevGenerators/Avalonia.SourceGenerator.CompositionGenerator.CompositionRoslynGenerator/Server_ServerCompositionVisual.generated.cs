
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
    abstract unsafe partial class ServerCompositionVisual : ServerObject
    {
        internal ServerCompositionVisual(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        ServerCompositionTarget? _root;
        public ServerCompositionTarget? Root
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
                    OnRootChanging();
                    changed = true;
                }

                SetValue(s_IdOfRootProperty, ref _root, value);
                if (changed)
                    OnRootChanged();
            }
        }

        partial void OnRootChanged();
        partial void OnRootChanging();
        internal readonly static CompositionProperty<ServerCompositionTarget?> s_IdOfRootProperty = CompositionProperty.Register<ServerCompositionVisual, ServerCompositionTarget?>("Root", obj => ((ServerCompositionVisual)obj)._root, (obj, v) => ((ServerCompositionVisual)obj)._root = v, null);
        ServerCompositionVisual? _parent;
        public ServerCompositionVisual? Parent
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
                    OnParentChanging();
                    changed = true;
                }

                SetValue(s_IdOfParentProperty, ref _parent, value);
                if (changed)
                    OnParentChanged();
            }
        }

        partial void OnParentChanged();
        partial void OnParentChanging();
        internal readonly static CompositionProperty<ServerCompositionVisual?> s_IdOfParentProperty = CompositionProperty.Register<ServerCompositionVisual, ServerCompositionVisual?>("Parent", obj => ((ServerCompositionVisual)obj)._parent, (obj, v) => ((ServerCompositionVisual)obj)._parent = v, null);
        bool _visible;
        public bool Visible { get => _visible; set => SetAnimatedValue(s_IdOfVisibleProperty, out _visible, value); }

        internal readonly static CompositionProperty<bool> s_IdOfVisibleProperty = CompositionProperty.Register<ServerCompositionVisual, bool>("Visible", obj => ((ServerCompositionVisual)obj)._visible, (obj, v) => ((ServerCompositionVisual)obj)._visible = v, obj => ((ServerCompositionVisual)obj)._visible);
        float _opacity;
        public float Opacity { get => _opacity; set => SetAnimatedValue(s_IdOfOpacityProperty, out _opacity, value); }

        internal readonly static CompositionProperty<float> s_IdOfOpacityProperty = CompositionProperty.Register<ServerCompositionVisual, float>("Opacity", obj => ((ServerCompositionVisual)obj)._opacity, (obj, v) => ((ServerCompositionVisual)obj)._opacity = v, obj => ((ServerCompositionVisual)obj)._opacity);
        Avalonia.Platform.IGeometryImpl? _clip;
        public Avalonia.Platform.IGeometryImpl? Clip
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
                    OnClipChanging();
                    changed = true;
                }

                SetValue(s_IdOfClipProperty, ref _clip, value);
                if (changed)
                    OnClipChanged();
            }
        }

        partial void OnClipChanged();
        partial void OnClipChanging();
        internal readonly static CompositionProperty<Avalonia.Platform.IGeometryImpl?> s_IdOfClipProperty = CompositionProperty.Register<ServerCompositionVisual, Avalonia.Platform.IGeometryImpl?>("Clip", obj => ((ServerCompositionVisual)obj)._clip, (obj, v) => ((ServerCompositionVisual)obj)._clip = v, null);
        bool _clipToBounds;
        public bool ClipToBounds { get => _clipToBounds; set => SetAnimatedValue(s_IdOfClipToBoundsProperty, out _clipToBounds, value); }

        internal readonly static CompositionProperty<bool> s_IdOfClipToBoundsProperty = CompositionProperty.Register<ServerCompositionVisual, bool>("ClipToBounds", obj => ((ServerCompositionVisual)obj)._clipToBounds, (obj, v) => ((ServerCompositionVisual)obj)._clipToBounds = v, obj => ((ServerCompositionVisual)obj)._clipToBounds);
        Vector3D _offset;
        public Vector3D Offset { get => _offset; set => SetAnimatedValue(s_IdOfOffsetProperty, out _offset, value); }

        internal readonly static CompositionProperty<Vector3D> s_IdOfOffsetProperty = CompositionProperty.Register<ServerCompositionVisual, Vector3D>("Offset", obj => ((ServerCompositionVisual)obj)._offset, (obj, v) => ((ServerCompositionVisual)obj)._offset = v, obj => ((ServerCompositionVisual)obj)._offset);
        Vector3D _translation;
        public Vector3D Translation { get => _translation; set => SetAnimatedValue(s_IdOfTranslationProperty, out _translation, value); }

        internal readonly static CompositionProperty<Vector3D> s_IdOfTranslationProperty = CompositionProperty.Register<ServerCompositionVisual, Vector3D>("Translation", obj => ((ServerCompositionVisual)obj)._translation, (obj, v) => ((ServerCompositionVisual)obj)._translation = v, obj => ((ServerCompositionVisual)obj)._translation);
        Vector _size;
        public Vector Size { get => _size; set => SetAnimatedValue(s_IdOfSizeProperty, out _size, value); }

        internal readonly static CompositionProperty<Vector> s_IdOfSizeProperty = CompositionProperty.Register<ServerCompositionVisual, Vector>("Size", obj => ((ServerCompositionVisual)obj)._size, (obj, v) => ((ServerCompositionVisual)obj)._size = v, obj => ((ServerCompositionVisual)obj)._size);
        Vector _anchorPoint;
        public Vector AnchorPoint { get => _anchorPoint; set => SetAnimatedValue(s_IdOfAnchorPointProperty, out _anchorPoint, value); }

        internal readonly static CompositionProperty<Vector> s_IdOfAnchorPointProperty = CompositionProperty.Register<ServerCompositionVisual, Vector>("AnchorPoint", obj => ((ServerCompositionVisual)obj)._anchorPoint, (obj, v) => ((ServerCompositionVisual)obj)._anchorPoint = v, obj => ((ServerCompositionVisual)obj)._anchorPoint);
        Vector3D _centerPoint;
        public Vector3D CenterPoint { get => _centerPoint; set => SetAnimatedValue(s_IdOfCenterPointProperty, out _centerPoint, value); }

        internal readonly static CompositionProperty<Vector3D> s_IdOfCenterPointProperty = CompositionProperty.Register<ServerCompositionVisual, Vector3D>("CenterPoint", obj => ((ServerCompositionVisual)obj)._centerPoint, (obj, v) => ((ServerCompositionVisual)obj)._centerPoint = v, obj => ((ServerCompositionVisual)obj)._centerPoint);
        float _rotationAngle;
        public float RotationAngle { get => _rotationAngle; set => SetAnimatedValue(s_IdOfRotationAngleProperty, out _rotationAngle, value); }

        internal readonly static CompositionProperty<float> s_IdOfRotationAngleProperty = CompositionProperty.Register<ServerCompositionVisual, float>("RotationAngle", obj => ((ServerCompositionVisual)obj)._rotationAngle, (obj, v) => ((ServerCompositionVisual)obj)._rotationAngle = v, obj => ((ServerCompositionVisual)obj)._rotationAngle);
        Quaternion _orientation;
        public Quaternion Orientation { get => _orientation; set => SetAnimatedValue(s_IdOfOrientationProperty, out _orientation, value); }

        internal readonly static CompositionProperty<Quaternion> s_IdOfOrientationProperty = CompositionProperty.Register<ServerCompositionVisual, Quaternion>("Orientation", obj => ((ServerCompositionVisual)obj)._orientation, (obj, v) => ((ServerCompositionVisual)obj)._orientation = v, obj => ((ServerCompositionVisual)obj)._orientation);
        Vector3D _scale;
        public Vector3D Scale { get => _scale; set => SetAnimatedValue(s_IdOfScaleProperty, out _scale, value); }

        internal readonly static CompositionProperty<Vector3D> s_IdOfScaleProperty = CompositionProperty.Register<ServerCompositionVisual, Vector3D>("Scale", obj => ((ServerCompositionVisual)obj)._scale, (obj, v) => ((ServerCompositionVisual)obj)._scale = v, obj => ((ServerCompositionVisual)obj)._scale);
        Avalonia.Matrix _transformMatrix;
        public Avalonia.Matrix TransformMatrix { get => _transformMatrix; set => SetAnimatedValue(s_IdOfTransformMatrixProperty, out _transformMatrix, value); }

        internal readonly static CompositionProperty<Avalonia.Matrix> s_IdOfTransformMatrixProperty = CompositionProperty.Register<ServerCompositionVisual, Avalonia.Matrix>("TransformMatrix", obj => ((ServerCompositionVisual)obj)._transformMatrix, (obj, v) => ((ServerCompositionVisual)obj)._transformMatrix = v, null);
        ServerCompositionVisual? _adornedVisual;
        public ServerCompositionVisual? AdornedVisual
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
                    OnAdornedVisualChanging();
                    changed = true;
                }

                SetValue(s_IdOfAdornedVisualProperty, ref _adornedVisual, value);
                if (changed)
                    OnAdornedVisualChanged();
            }
        }

        partial void OnAdornedVisualChanged();
        partial void OnAdornedVisualChanging();
        internal readonly static CompositionProperty<ServerCompositionVisual?> s_IdOfAdornedVisualProperty = CompositionProperty.Register<ServerCompositionVisual, ServerCompositionVisual?>("AdornedVisual", obj => ((ServerCompositionVisual)obj)._adornedVisual, (obj, v) => ((ServerCompositionVisual)obj)._adornedVisual = v, null);
        bool _adornerIsClipped;
        public bool AdornerIsClipped
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
                    OnAdornerIsClippedChanging();
                    changed = true;
                }

                SetValue(s_IdOfAdornerIsClippedProperty, ref _adornerIsClipped, value);
                if (changed)
                    OnAdornerIsClippedChanged();
            }
        }

        partial void OnAdornerIsClippedChanged();
        partial void OnAdornerIsClippedChanging();
        internal readonly static CompositionProperty<bool> s_IdOfAdornerIsClippedProperty = CompositionProperty.Register<ServerCompositionVisual, bool>("AdornerIsClipped", obj => ((ServerCompositionVisual)obj)._adornerIsClipped, (obj, v) => ((ServerCompositionVisual)obj)._adornerIsClipped = v, obj => ((ServerCompositionVisual)obj)._adornerIsClipped);
        Avalonia.Media.IBrush? _opacityMaskBrush;
        public Avalonia.Media.IBrush? OpacityMaskBrush
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
                    OnOpacityMaskBrushChanging();
                    changed = true;
                }

                SetValue(s_IdOfOpacityMaskBrushProperty, ref _opacityMaskBrush, value);
                if (changed)
                    OnOpacityMaskBrushChanged();
            }
        }

        partial void OnOpacityMaskBrushChanged();
        partial void OnOpacityMaskBrushChanging();
        internal readonly static CompositionProperty<Avalonia.Media.IBrush?> s_IdOfOpacityMaskBrushProperty = CompositionProperty.Register<ServerCompositionVisual, Avalonia.Media.IBrush?>("OpacityMaskBrush", obj => ((ServerCompositionVisual)obj)._opacityMaskBrush, (obj, v) => ((ServerCompositionVisual)obj)._opacityMaskBrush = v, null);
        Avalonia.Media.IImmutableEffect? _effect;
        public Avalonia.Media.IImmutableEffect? Effect
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
                    OnEffectChanging();
                    changed = true;
                }

                SetValue(s_IdOfEffectProperty, ref _effect, value);
                if (changed)
                    OnEffectChanged();
            }
        }

        partial void OnEffectChanged();
        partial void OnEffectChanging();
        internal readonly static CompositionProperty<Avalonia.Media.IImmutableEffect?> s_IdOfEffectProperty = CompositionProperty.Register<ServerCompositionVisual, Avalonia.Media.IImmutableEffect?>("Effect", obj => ((ServerCompositionVisual)obj)._effect, (obj, v) => ((ServerCompositionVisual)obj)._effect = v, null);
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
                    OnRenderOptionsChanging();
                    changed = true;
                }

                SetValue(s_IdOfRenderOptionsProperty, ref _renderOptions, value);
                if (changed)
                    OnRenderOptionsChanged();
            }
        }

        partial void OnRenderOptionsChanged();
        partial void OnRenderOptionsChanging();
        internal readonly static CompositionProperty<Avalonia.Media.RenderOptions> s_IdOfRenderOptionsProperty = CompositionProperty.Register<ServerCompositionVisual, Avalonia.Media.RenderOptions>("RenderOptions", obj => ((ServerCompositionVisual)obj)._renderOptions, (obj, v) => ((ServerCompositionVisual)obj)._renderOptions = v, null);
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
                    OnTextOptionsChanging();
                    changed = true;
                }

                SetValue(s_IdOfTextOptionsProperty, ref _textOptions, value);
                if (changed)
                    OnTextOptionsChanged();
            }
        }

        partial void OnTextOptionsChanged();
        partial void OnTextOptionsChanging();
        internal readonly static CompositionProperty<Avalonia.Media.TextOptions> s_IdOfTextOptionsProperty = CompositionProperty.Register<ServerCompositionVisual, Avalonia.Media.TextOptions>("TextOptions", obj => ((ServerCompositionVisual)obj)._textOptions, (obj, v) => ((ServerCompositionVisual)obj)._textOptions = v, null);
        ServerCompositionCacheMode? _cacheMode;
        public ServerCompositionCacheMode? CacheMode
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
                    OnCacheModeChanging();
                    changed = true;
                }

                SetValue(s_IdOfCacheModeProperty, ref _cacheMode, value);
                if (changed)
                    OnCacheModeChanged();
            }
        }

        partial void OnCacheModeChanged();
        partial void OnCacheModeChanging();
        internal readonly static CompositionProperty<ServerCompositionCacheMode?> s_IdOfCacheModeProperty = CompositionProperty.Register<ServerCompositionVisual, ServerCompositionCacheMode?>("CacheMode", obj => ((ServerCompositionVisual)obj)._cacheMode, (obj, v) => ((ServerCompositionVisual)obj)._cacheMode = v, null);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionVisualChangedFields>();
            if ((changed & CompositionVisualChangedFields.Root) == CompositionVisualChangedFields.Root)
                Root = reader.ReadObject<ServerCompositionTarget?>();
            if ((changed & CompositionVisualChangedFields.Parent) == CompositionVisualChangedFields.Parent)
                Parent = reader.ReadObject<ServerCompositionVisual?>();
            if ((changed & CompositionVisualChangedFields.VisibleAnimated) == CompositionVisualChangedFields.VisibleAnimated)
                SetAnimatedValue(s_IdOfVisibleProperty, ref _visible, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.Visible) == CompositionVisualChangedFields.Visible)
                Visible = reader.Read<bool>();
            if ((changed & CompositionVisualChangedFields.OpacityAnimated) == CompositionVisualChangedFields.OpacityAnimated)
                SetAnimatedValue(s_IdOfOpacityProperty, ref _opacity, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.Opacity) == CompositionVisualChangedFields.Opacity)
                Opacity = reader.Read<float>();
            if ((changed & CompositionVisualChangedFields.Clip) == CompositionVisualChangedFields.Clip)
                Clip = reader.ReadObject<Avalonia.Platform.IGeometryImpl?>();
            if ((changed & CompositionVisualChangedFields.ClipToBoundsAnimated) == CompositionVisualChangedFields.ClipToBoundsAnimated)
                SetAnimatedValue(s_IdOfClipToBoundsProperty, ref _clipToBounds, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.ClipToBounds) == CompositionVisualChangedFields.ClipToBounds)
                ClipToBounds = reader.Read<bool>();
            if ((changed & CompositionVisualChangedFields.OffsetAnimated) == CompositionVisualChangedFields.OffsetAnimated)
                SetAnimatedValue(s_IdOfOffsetProperty, ref _offset, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.Offset) == CompositionVisualChangedFields.Offset)
                Offset = reader.Read<Vector3D>();
            if ((changed & CompositionVisualChangedFields.TranslationAnimated) == CompositionVisualChangedFields.TranslationAnimated)
                SetAnimatedValue(s_IdOfTranslationProperty, ref _translation, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.Translation) == CompositionVisualChangedFields.Translation)
                Translation = reader.Read<Vector3D>();
            if ((changed & CompositionVisualChangedFields.SizeAnimated) == CompositionVisualChangedFields.SizeAnimated)
                SetAnimatedValue(s_IdOfSizeProperty, ref _size, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.Size) == CompositionVisualChangedFields.Size)
                Size = reader.Read<Vector>();
            if ((changed & CompositionVisualChangedFields.AnchorPointAnimated) == CompositionVisualChangedFields.AnchorPointAnimated)
                SetAnimatedValue(s_IdOfAnchorPointProperty, ref _anchorPoint, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.AnchorPoint) == CompositionVisualChangedFields.AnchorPoint)
                AnchorPoint = reader.Read<Vector>();
            if ((changed & CompositionVisualChangedFields.CenterPointAnimated) == CompositionVisualChangedFields.CenterPointAnimated)
                SetAnimatedValue(s_IdOfCenterPointProperty, ref _centerPoint, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.CenterPoint) == CompositionVisualChangedFields.CenterPoint)
                CenterPoint = reader.Read<Vector3D>();
            if ((changed & CompositionVisualChangedFields.RotationAngleAnimated) == CompositionVisualChangedFields.RotationAngleAnimated)
                SetAnimatedValue(s_IdOfRotationAngleProperty, ref _rotationAngle, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.RotationAngle) == CompositionVisualChangedFields.RotationAngle)
                RotationAngle = reader.Read<float>();
            if ((changed & CompositionVisualChangedFields.OrientationAnimated) == CompositionVisualChangedFields.OrientationAnimated)
                SetAnimatedValue(s_IdOfOrientationProperty, ref _orientation, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.Orientation) == CompositionVisualChangedFields.Orientation)
                Orientation = reader.Read<Quaternion>();
            if ((changed & CompositionVisualChangedFields.ScaleAnimated) == CompositionVisualChangedFields.ScaleAnimated)
                SetAnimatedValue(s_IdOfScaleProperty, ref _scale, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.Scale) == CompositionVisualChangedFields.Scale)
                Scale = reader.Read<Vector3D>();
            if ((changed & CompositionVisualChangedFields.TransformMatrixAnimated) == CompositionVisualChangedFields.TransformMatrixAnimated)
                SetAnimatedValue(s_IdOfTransformMatrixProperty, ref _transformMatrix, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionVisualChangedFields.TransformMatrix) == CompositionVisualChangedFields.TransformMatrix)
                TransformMatrix = reader.Read<Avalonia.Matrix>();
            if ((changed & CompositionVisualChangedFields.AdornedVisual) == CompositionVisualChangedFields.AdornedVisual)
                AdornedVisual = reader.ReadObject<ServerCompositionVisual?>();
            if ((changed & CompositionVisualChangedFields.AdornerIsClipped) == CompositionVisualChangedFields.AdornerIsClipped)
                AdornerIsClipped = reader.Read<bool>();
            if ((changed & CompositionVisualChangedFields.OpacityMaskBrush) == CompositionVisualChangedFields.OpacityMaskBrush)
                OpacityMaskBrush = reader.ReadObject<Avalonia.Media.IBrush?>();
            if ((changed & CompositionVisualChangedFields.Effect) == CompositionVisualChangedFields.Effect)
                Effect = reader.ReadObject<Avalonia.Media.IImmutableEffect?>();
            if ((changed & CompositionVisualChangedFields.RenderOptions) == CompositionVisualChangedFields.RenderOptions)
                RenderOptions = reader.Read<Avalonia.Media.RenderOptions>();
            if ((changed & CompositionVisualChangedFields.TextOptions) == CompositionVisualChangedFields.TextOptions)
                TextOptions = reader.Read<Avalonia.Media.TextOptions>();
            if ((changed & CompositionVisualChangedFields.CacheMode) == CompositionVisualChangedFields.CacheMode)
                CacheMode = reader.ReadObject<ServerCompositionCacheMode?>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionVisualChangedFields changed);
        public override CompositionProperty? GetCompositionProperty(string name)
        {
            if (name == "Visible")
                return s_IdOfVisibleProperty;
            if (name == "Opacity")
                return s_IdOfOpacityProperty;
            if (name == "ClipToBounds")
                return s_IdOfClipToBoundsProperty;
            if (name == "Offset")
                return s_IdOfOffsetProperty;
            if (name == "Translation")
                return s_IdOfTranslationProperty;
            if (name == "Size")
                return s_IdOfSizeProperty;
            if (name == "AnchorPoint")
                return s_IdOfAnchorPointProperty;
            if (name == "CenterPoint")
                return s_IdOfCenterPointProperty;
            if (name == "RotationAngle")
                return s_IdOfRotationAngleProperty;
            if (name == "Orientation")
                return s_IdOfOrientationProperty;
            if (name == "Scale")
                return s_IdOfScaleProperty;
            if (name == "AdornerIsClipped")
                return s_IdOfAdornerIsClippedProperty;
            return base.GetCompositionProperty(name);
        }
    }
}