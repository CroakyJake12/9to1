
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
    unsafe partial class ServerCompositionBitmapCache : ServerCompositionCacheMode
    {
        internal ServerCompositionBitmapCache(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        double _renderAtScale;
        public double RenderAtScale { get => _renderAtScale; set => SetAnimatedValue(s_IdOfRenderAtScaleProperty, out _renderAtScale, value); }

        internal readonly static CompositionProperty<double> s_IdOfRenderAtScaleProperty = CompositionProperty.Register<ServerCompositionBitmapCache, double>("RenderAtScale", obj => ((ServerCompositionBitmapCache)obj)._renderAtScale, (obj, v) => ((ServerCompositionBitmapCache)obj)._renderAtScale = v, obj => ((ServerCompositionBitmapCache)obj)._renderAtScale);
        bool _snapsToDevicePixels;
        public bool SnapsToDevicePixels { get => _snapsToDevicePixels; set => SetAnimatedValue(s_IdOfSnapsToDevicePixelsProperty, out _snapsToDevicePixels, value); }

        internal readonly static CompositionProperty<bool> s_IdOfSnapsToDevicePixelsProperty = CompositionProperty.Register<ServerCompositionBitmapCache, bool>("SnapsToDevicePixels", obj => ((ServerCompositionBitmapCache)obj)._snapsToDevicePixels, (obj, v) => ((ServerCompositionBitmapCache)obj)._snapsToDevicePixels = v, obj => ((ServerCompositionBitmapCache)obj)._snapsToDevicePixels);
        bool _enableClearType;
        public bool EnableClearType { get => _enableClearType; set => SetAnimatedValue(s_IdOfEnableClearTypeProperty, out _enableClearType, value); }

        internal readonly static CompositionProperty<bool> s_IdOfEnableClearTypeProperty = CompositionProperty.Register<ServerCompositionBitmapCache, bool>("EnableClearType", obj => ((ServerCompositionBitmapCache)obj)._enableClearType, (obj, v) => ((ServerCompositionBitmapCache)obj)._enableClearType = v, obj => ((ServerCompositionBitmapCache)obj)._enableClearType);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionBitmapCacheChangedFields>();
            if ((changed & CompositionBitmapCacheChangedFields.RenderAtScaleAnimated) == CompositionBitmapCacheChangedFields.RenderAtScaleAnimated)
                SetAnimatedValue(s_IdOfRenderAtScaleProperty, ref _renderAtScale, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionBitmapCacheChangedFields.RenderAtScale) == CompositionBitmapCacheChangedFields.RenderAtScale)
                RenderAtScale = reader.Read<double>();
            if ((changed & CompositionBitmapCacheChangedFields.SnapsToDevicePixelsAnimated) == CompositionBitmapCacheChangedFields.SnapsToDevicePixelsAnimated)
                SetAnimatedValue(s_IdOfSnapsToDevicePixelsProperty, ref _snapsToDevicePixels, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionBitmapCacheChangedFields.SnapsToDevicePixels) == CompositionBitmapCacheChangedFields.SnapsToDevicePixels)
                SnapsToDevicePixels = reader.Read<bool>();
            if ((changed & CompositionBitmapCacheChangedFields.EnableClearTypeAnimated) == CompositionBitmapCacheChangedFields.EnableClearTypeAnimated)
                SetAnimatedValue(s_IdOfEnableClearTypeProperty, ref _enableClearType, committedAt, reader.ReadObject<IAnimationInstance>());
            else if ((changed & CompositionBitmapCacheChangedFields.EnableClearType) == CompositionBitmapCacheChangedFields.EnableClearType)
                EnableClearType = reader.Read<bool>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionBitmapCacheChangedFields changed);
        public override CompositionProperty? GetCompositionProperty(string name)
        {
            if (name == "RenderAtScale")
                return s_IdOfRenderAtScaleProperty;
            if (name == "SnapsToDevicePixels")
                return s_IdOfSnapsToDevicePixelsProperty;
            if (name == "EnableClearType")
                return s_IdOfEnableClearTypeProperty;
            return base.GetCompositionProperty(name);
        }
    }
}