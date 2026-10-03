
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
    unsafe partial class ServerCompositionTarget : ServerObject
    {
        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        ServerCompositionVisual? _root;
        public ServerCompositionVisual? Root
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
        internal readonly static CompositionProperty<ServerCompositionVisual?> s_IdOfRootProperty = CompositionProperty.Register<ServerCompositionTarget, ServerCompositionVisual?>("Root", obj => ((ServerCompositionTarget)obj)._root, (obj, v) => ((ServerCompositionTarget)obj)._root = v, null);
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
                    OnIsEnabledChanging();
                    changed = true;
                }

                SetValue(s_IdOfIsEnabledProperty, ref _isEnabled, value);
                if (changed)
                    OnIsEnabledChanged();
            }
        }

        partial void OnIsEnabledChanged();
        partial void OnIsEnabledChanging();
        internal readonly static CompositionProperty<bool> s_IdOfIsEnabledProperty = CompositionProperty.Register<ServerCompositionTarget, bool>("IsEnabled", obj => ((ServerCompositionTarget)obj)._isEnabled, (obj, v) => ((ServerCompositionTarget)obj)._isEnabled = v, obj => ((ServerCompositionTarget)obj)._isEnabled);
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
                    OnDebugOverlaysChanging();
                    changed = true;
                }

                SetValue(s_IdOfDebugOverlaysProperty, ref _debugOverlays, value);
                if (changed)
                    OnDebugOverlaysChanged();
            }
        }

        partial void OnDebugOverlaysChanged();
        partial void OnDebugOverlaysChanging();
        internal readonly static CompositionProperty<RendererDebugOverlays> s_IdOfDebugOverlaysProperty = CompositionProperty.Register<ServerCompositionTarget, RendererDebugOverlays>("DebugOverlays", obj => ((ServerCompositionTarget)obj)._debugOverlays, (obj, v) => ((ServerCompositionTarget)obj)._debugOverlays = v, null);
        LayoutPassTiming _lastLayoutPassTiming;
        public LayoutPassTiming LastLayoutPassTiming
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
                    OnLastLayoutPassTimingChanging();
                    changed = true;
                }

                SetValue(s_IdOfLastLayoutPassTimingProperty, ref _lastLayoutPassTiming, value);
                if (changed)
                    OnLastLayoutPassTimingChanged();
            }
        }

        partial void OnLastLayoutPassTimingChanged();
        partial void OnLastLayoutPassTimingChanging();
        internal readonly static CompositionProperty<LayoutPassTiming> s_IdOfLastLayoutPassTimingProperty = CompositionProperty.Register<ServerCompositionTarget, LayoutPassTiming>("LastLayoutPassTiming", obj => ((ServerCompositionTarget)obj)._lastLayoutPassTiming, (obj, v) => ((ServerCompositionTarget)obj)._lastLayoutPassTiming = v, null);
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
                    OnScalingChanging();
                    changed = true;
                }

                SetValue(s_IdOfScalingProperty, ref _scaling, value);
                if (changed)
                    OnScalingChanged();
            }
        }

        partial void OnScalingChanged();
        partial void OnScalingChanging();
        internal readonly static CompositionProperty<double> s_IdOfScalingProperty = CompositionProperty.Register<ServerCompositionTarget, double>("Scaling", obj => ((ServerCompositionTarget)obj)._scaling, (obj, v) => ((ServerCompositionTarget)obj)._scaling = v, obj => ((ServerCompositionTarget)obj)._scaling);
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
                    OnPixelSizeChanging();
                    changed = true;
                }

                SetValue(s_IdOfPixelSizeProperty, ref _pixelSize, value);
                if (changed)
                    OnPixelSizeChanged();
            }
        }

        partial void OnPixelSizeChanged();
        partial void OnPixelSizeChanging();
        internal readonly static CompositionProperty<PixelSize> s_IdOfPixelSizeProperty = CompositionProperty.Register<ServerCompositionTarget, PixelSize>("PixelSize", obj => ((ServerCompositionTarget)obj)._pixelSize, (obj, v) => ((ServerCompositionTarget)obj)._pixelSize = v, null);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionTargetChangedFields>();
            if ((changed & CompositionTargetChangedFields.Root) == CompositionTargetChangedFields.Root)
                Root = reader.ReadObject<ServerCompositionVisual?>();
            if ((changed & CompositionTargetChangedFields.IsEnabled) == CompositionTargetChangedFields.IsEnabled)
                IsEnabled = reader.Read<bool>();
            if ((changed & CompositionTargetChangedFields.DebugOverlays) == CompositionTargetChangedFields.DebugOverlays)
                DebugOverlays = reader.Read<RendererDebugOverlays>();
            if ((changed & CompositionTargetChangedFields.LastLayoutPassTiming) == CompositionTargetChangedFields.LastLayoutPassTiming)
                LastLayoutPassTiming = reader.Read<LayoutPassTiming>();
            if ((changed & CompositionTargetChangedFields.Scaling) == CompositionTargetChangedFields.Scaling)
                Scaling = reader.Read<double>();
            if ((changed & CompositionTargetChangedFields.PixelSize) == CompositionTargetChangedFields.PixelSize)
                PixelSize = reader.Read<PixelSize>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionTargetChangedFields changed);
        public override CompositionProperty? GetCompositionProperty(string name)
        {
            if (name == "IsEnabled")
                return s_IdOfIsEnabledProperty;
            if (name == "Scaling")
                return s_IdOfScalingProperty;
            return base.GetCompositionProperty(name);
        }
    }
}