
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
    internal unsafe partial class CompositionTarget : CompositionObject
    {
        CompositionTargetChangedFields _changedFieldsOfCompositionTarget;
        internal new ServerCompositionTarget Server { get; }

        internal CompositionTarget(Compositor compositor, ServerCompositionTarget server) : base(compositor, server)
        {
            Server = (ServerCompositionTarget)server;
            InitializeDefaults();
        }

        CompositionVisual? _root;
        public CompositionVisual? Root
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
                        _changedFieldsOfCompositionTarget |= CompositionTargetChangedFields.Root;
                        RegisterForSerialization();
                    }
                }

                _root = value;
                if (changed)
                    OnRootChanged();
            }
        }

        partial void ValidateRootChange(CompositionVisual? oldValue, CompositionVisual? newValue);
        partial void OnRootChanged();
        partial void OnRootChanging();
        bool _isEnabled;
        public bool IsEnabled
        {
            get
            {
                return _isEnabled;
            }

            set
            {
                var changed = false;
                if (_isEnabled != value)
                {
                    ValidateIsEnabledChange(_isEnabled, value);
                    OnIsEnabledChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _isEnabled = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionTarget |= CompositionTargetChangedFields.IsEnabled;
                        RegisterForSerialization();
                    }
                }

                _isEnabled = value;
                if (changed)
                    OnIsEnabledChanged();
            }
        }

        partial void ValidateIsEnabledChange(bool oldValue, bool newValue);
        partial void OnIsEnabledChanged();
        partial void OnIsEnabledChanging();
        RendererDebugOverlays _debugOverlays;
        public RendererDebugOverlays DebugOverlays
        {
            get
            {
                return _debugOverlays;
            }

            set
            {
                var changed = false;
                if (_debugOverlays != value)
                {
                    ValidateDebugOverlaysChange(_debugOverlays, value);
                    OnDebugOverlaysChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _debugOverlays = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionTarget |= CompositionTargetChangedFields.DebugOverlays;
                        RegisterForSerialization();
                    }
                }

                _debugOverlays = value;
                if (changed)
                    OnDebugOverlaysChanged();
            }
        }

        partial void ValidateDebugOverlaysChange(RendererDebugOverlays oldValue, RendererDebugOverlays newValue);
        partial void OnDebugOverlaysChanged();
        partial void OnDebugOverlaysChanging();
        LayoutPassTiming _lastLayoutPassTiming;
        internal LayoutPassTiming LastLayoutPassTiming
        {
            get
            {
                return _lastLayoutPassTiming;
            }

            set
            {
                var changed = false;
                if (_lastLayoutPassTiming != value)
                {
                    ValidateLastLayoutPassTimingChange(_lastLayoutPassTiming, value);
                    OnLastLayoutPassTimingChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _lastLayoutPassTiming = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionTarget |= CompositionTargetChangedFields.LastLayoutPassTiming;
                        RegisterForSerialization();
                    }
                }

                _lastLayoutPassTiming = value;
                if (changed)
                    OnLastLayoutPassTimingChanged();
            }
        }

        partial void ValidateLastLayoutPassTimingChange(LayoutPassTiming oldValue, LayoutPassTiming newValue);
        partial void OnLastLayoutPassTimingChanged();
        partial void OnLastLayoutPassTimingChanging();
        double _scaling;
        public double Scaling
        {
            get
            {
                return _scaling;
            }

            set
            {
                var changed = false;
                if (_scaling != value)
                {
                    ValidateScalingChange(_scaling, value);
                    OnScalingChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _scaling = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionTarget |= CompositionTargetChangedFields.Scaling;
                        RegisterForSerialization();
                    }
                }

                _scaling = value;
                if (changed)
                    OnScalingChanged();
            }
        }

        partial void ValidateScalingChange(double oldValue, double newValue);
        partial void OnScalingChanged();
        partial void OnScalingChanging();
        PixelSize _pixelSize;
        public PixelSize PixelSize
        {
            get
            {
                return _pixelSize;
            }

            set
            {
                var changed = false;
                if (_pixelSize != value)
                {
                    ValidatePixelSizeChange(_pixelSize, value);
                    OnPixelSizeChanging();
                    changed = true;
                    {
                        // Update the backing value
                        _pixelSize = value;
                        // Register object for serialization in the next batch
                        _changedFieldsOfCompositionTarget |= CompositionTargetChangedFields.PixelSize;
                        RegisterForSerialization();
                    }
                }

                _pixelSize = value;
                if (changed)
                    OnPixelSizeChanged();
            }
        }

        partial void ValidatePixelSizeChange(PixelSize oldValue, PixelSize newValue);
        partial void OnPixelSizeChanged();
        partial void OnPixelSizeChanging();
        void InitializeDefaults()
        {
            InitializeDefaultsExtra();
        }

        partial void InitializeDefaultsExtra();
        private protected override void SerializeChangesCore(BatchStreamWriter writer)
        {
            base.SerializeChangesCore(writer);
            writer.Write(_changedFieldsOfCompositionTarget);
            if ((_changedFieldsOfCompositionTarget & CompositionTargetChangedFields.Root) == CompositionTargetChangedFields.Root)
                writer.WriteObject(_root?.Server!);
            if ((_changedFieldsOfCompositionTarget & CompositionTargetChangedFields.IsEnabled) == CompositionTargetChangedFields.IsEnabled)
                writer.Write(_isEnabled);
            if ((_changedFieldsOfCompositionTarget & CompositionTargetChangedFields.DebugOverlays) == CompositionTargetChangedFields.DebugOverlays)
                writer.Write(_debugOverlays);
            if ((_changedFieldsOfCompositionTarget & CompositionTargetChangedFields.LastLayoutPassTiming) == CompositionTargetChangedFields.LastLayoutPassTiming)
                writer.Write(_lastLayoutPassTiming);
            if ((_changedFieldsOfCompositionTarget & CompositionTargetChangedFields.Scaling) == CompositionTargetChangedFields.Scaling)
                writer.Write(_scaling);
            if ((_changedFieldsOfCompositionTarget & CompositionTargetChangedFields.PixelSize) == CompositionTargetChangedFields.PixelSize)
                writer.Write(_pixelSize);
            {
                _changedFieldsOfCompositionTarget = default;
            }
        }
    }
}