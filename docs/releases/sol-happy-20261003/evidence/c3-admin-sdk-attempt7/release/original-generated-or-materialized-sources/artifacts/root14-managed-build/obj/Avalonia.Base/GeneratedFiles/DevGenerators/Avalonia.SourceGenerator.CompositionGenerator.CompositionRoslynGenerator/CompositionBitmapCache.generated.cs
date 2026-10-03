
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
    internal unsafe partial class CompositionBitmapCache : CompositionCacheMode
    {
        CompositionBitmapCacheChangedFields _changedFieldsOfCompositionBitmapCache;
        internal new ServerCompositionBitmapCache Server { get; }

        internal CompositionBitmapCache(Compositor compositor, ServerCompositionBitmapCache server) : base(compositor, server)
        {
            Server = (ServerCompositionBitmapCache)server;
            InitializeDefaults();
        }

        double _renderAtScale;
        public double RenderAtScale
        {
            get
            {
                return _renderAtScale;
            }

            set
            {
                var changed = false;
                if (_renderAtScale != value)
                {
                    ValidateRenderAtScaleChange(_renderAtScale, value);
                    OnRenderAtScaleChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _renderAtScale = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionBitmapCache |= CompositionBitmapCacheChangedFields.RenderAtScale;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionBitmapCache.s_IdOfRenderAtScaleProperty);
                        _changedFieldsOfCompositionBitmapCache &= ~CompositionBitmapCacheChangedFields.RenderAtScaleAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("RenderAtScale", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionBitmapCache |= CompositionBitmapCacheChangedFields.RenderAtScaleAnimated;
                                PendingAnimations[ServerCompositionBitmapCache.s_IdOfRenderAtScaleProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "RenderAtScale", value);
                        }
                    }
                }

                _renderAtScale = value;
                if (changed)
                    OnRenderAtScaleChanged();
            }
        }

        partial void ValidateRenderAtScaleChange(double oldValue, double newValue);
        partial void OnRenderAtScaleChanged();
        partial void OnRenderAtScaleChanging();
        bool _snapsToDevicePixels;
        public bool SnapsToDevicePixels
        {
            get
            {
                return _snapsToDevicePixels;
            }

            set
            {
                var changed = false;
                if (_snapsToDevicePixels != value)
                {
                    ValidateSnapsToDevicePixelsChange(_snapsToDevicePixels, value);
                    OnSnapsToDevicePixelsChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _snapsToDevicePixels = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionBitmapCache |= CompositionBitmapCacheChangedFields.SnapsToDevicePixels;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionBitmapCache.s_IdOfSnapsToDevicePixelsProperty);
                        _changedFieldsOfCompositionBitmapCache &= ~CompositionBitmapCacheChangedFields.SnapsToDevicePixelsAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("SnapsToDevicePixels", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionBitmapCache |= CompositionBitmapCacheChangedFields.SnapsToDevicePixelsAnimated;
                                PendingAnimations[ServerCompositionBitmapCache.s_IdOfSnapsToDevicePixelsProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "SnapsToDevicePixels", value);
                        }
                    }
                }

                _snapsToDevicePixels = value;
                if (changed)
                    OnSnapsToDevicePixelsChanged();
            }
        }

        partial void ValidateSnapsToDevicePixelsChange(bool oldValue, bool newValue);
        partial void OnSnapsToDevicePixelsChanged();
        partial void OnSnapsToDevicePixelsChanging();
        bool _enableClearType;
        public bool EnableClearType
        {
            get
            {
                return _enableClearType;
            }

            set
            {
                var changed = false;
                if (_enableClearType != value)
                {
                    ValidateEnableClearTypeChange(_enableClearType, value);
                    OnEnableClearTypeChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _enableClearType = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionBitmapCache |= CompositionBitmapCacheChangedFields.EnableClearType;
                        RegisterForSerialization();
                        // Reset previous animation if any
                        PendingAnimations.Remove(ServerCompositionBitmapCache.s_IdOfEnableClearTypeProperty);
                        _changedFieldsOfCompositionBitmapCache &= ~CompositionBitmapCacheChangedFields.EnableClearTypeAnimated;
                        // Check for implicit animations
                        if (ImplicitAnimations != null && ImplicitAnimations.TryGetValue("EnableClearType", out var animation) == true)
                        {
                            // Animation affects only current property
                            if (animation is CompositionAnimation a)
                            {
                                _changedFieldsOfCompositionBitmapCache |= CompositionBitmapCacheChangedFields.EnableClearTypeAnimated;
                                PendingAnimations[ServerCompositionBitmapCache.s_IdOfEnableClearTypeProperty] = a.CreateInstance(this.Server, value);
                            }

                            // Animation is triggered by the current field, but does not necessary affects it
                            StartAnimationGroup(animation, "EnableClearType", value);
                        }
                    }
                }

                _enableClearType = value;
                if (changed)
                    OnEnableClearTypeChanged();
            }
        }

        partial void ValidateEnableClearTypeChange(bool oldValue, bool newValue);
        partial void OnEnableClearTypeChanged();
        partial void OnEnableClearTypeChanging();
        void InitializeDefaults()
        {
            InitializeDefaultsExtra();
            RenderAtScale = 1.0;
            SnapsToDevicePixels = false;
            EnableClearType = false;
        }

        partial void InitializeDefaultsExtra();
        private protected override void SerializeChangesCore(BatchStreamWriter writer)
        {
            base.SerializeChangesCore(writer);
            writer.Write(_changedFieldsOfCompositionBitmapCache);
            if ((_changedFieldsOfCompositionBitmapCache & CompositionBitmapCacheChangedFields.RenderAtScaleAnimated) == CompositionBitmapCacheChangedFields.RenderAtScaleAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionBitmapCache.s_IdOfRenderAtScaleProperty));
            else if ((_changedFieldsOfCompositionBitmapCache & CompositionBitmapCacheChangedFields.RenderAtScale) == CompositionBitmapCacheChangedFields.RenderAtScale)
                writer.Write(_renderAtScale);
            if ((_changedFieldsOfCompositionBitmapCache & CompositionBitmapCacheChangedFields.SnapsToDevicePixelsAnimated) == CompositionBitmapCacheChangedFields.SnapsToDevicePixelsAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionBitmapCache.s_IdOfSnapsToDevicePixelsProperty));
            else if ((_changedFieldsOfCompositionBitmapCache & CompositionBitmapCacheChangedFields.SnapsToDevicePixels) == CompositionBitmapCacheChangedFields.SnapsToDevicePixels)
                writer.Write(_snapsToDevicePixels);
            if ((_changedFieldsOfCompositionBitmapCache & CompositionBitmapCacheChangedFields.EnableClearTypeAnimated) == CompositionBitmapCacheChangedFields.EnableClearTypeAnimated)
                writer.WriteObject(PendingAnimations.GetAndRemove(ServerCompositionBitmapCache.s_IdOfEnableClearTypeProperty));
            else if ((_changedFieldsOfCompositionBitmapCache & CompositionBitmapCacheChangedFields.EnableClearType) == CompositionBitmapCacheChangedFields.EnableClearType)
                writer.Write(_enableClearType);
            {
                _changedFieldsOfCompositionBitmapCache = default;
            }
        }

        internal override void StartAnimation(string propertyName, CompositionAnimation animation, Avalonia.Rendering.Composition.Expressions.ExpressionVariant? finalValue)
        {
            if (propertyName == "RenderAtScale")
            {
                var current = _renderAtScale;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionBitmapCache.s_IdOfRenderAtScaleProperty] = server;
                _changedFieldsOfCompositionBitmapCache |= CompositionBitmapCacheChangedFields.RenderAtScaleAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "SnapsToDevicePixels")
            {
                var current = _snapsToDevicePixels;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionBitmapCache.s_IdOfSnapsToDevicePixelsProperty] = server;
                _changedFieldsOfCompositionBitmapCache |= CompositionBitmapCacheChangedFields.SnapsToDevicePixelsAnimated;
                RegisterForSerialization();
                return;
            }

            if (propertyName == "EnableClearType")
            {
                var current = _enableClearType;
                var server = animation.CreateInstance(this.Server, finalValue);
                PendingAnimations[ServerCompositionBitmapCache.s_IdOfEnableClearTypeProperty] = server;
                _changedFieldsOfCompositionBitmapCache |= CompositionBitmapCacheChangedFields.EnableClearTypeAnimated;
                RegisterForSerialization();
                return;
            }

            base.StartAnimation(propertyName, animation, finalValue);
        }
    }
}