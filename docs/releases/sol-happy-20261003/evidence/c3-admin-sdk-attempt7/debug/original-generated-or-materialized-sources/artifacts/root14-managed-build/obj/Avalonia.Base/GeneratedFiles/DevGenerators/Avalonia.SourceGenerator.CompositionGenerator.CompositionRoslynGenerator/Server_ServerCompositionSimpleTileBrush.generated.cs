
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
    unsafe partial class ServerCompositionSimpleTileBrush : ServerCompositionSimpleBrush
    {
        internal ServerCompositionSimpleTileBrush(ServerCompositor compositor) : base(compositor)
        {
            Initialize();
        }

        partial void Initialize();
        partial void DeserializeChangesExtra(BatchStreamReader c);
        Avalonia.Media.AlignmentX _alignmentX;
        public Avalonia.Media.AlignmentX AlignmentX
        {
            get
            {
                return _alignmentX;
            }

            set
            {
                var changed = false;
                if (_alignmentX != value)
                {
                    OnAlignmentXChanging();
                    changed = true;
                }

                SetValue(s_IdOfAlignmentXProperty, ref _alignmentX, value);
                if (changed)
                    OnAlignmentXChanged();
            }
        }

        partial void OnAlignmentXChanged();
        partial void OnAlignmentXChanging();
        internal readonly static CompositionProperty<Avalonia.Media.AlignmentX> s_IdOfAlignmentXProperty = CompositionProperty.Register<ServerCompositionSimpleTileBrush, Avalonia.Media.AlignmentX>("AlignmentX", obj => ((ServerCompositionSimpleTileBrush)obj)._alignmentX, (obj, v) => ((ServerCompositionSimpleTileBrush)obj)._alignmentX = v, null);
        Avalonia.Media.AlignmentY _alignmentY;
        public Avalonia.Media.AlignmentY AlignmentY
        {
            get
            {
                return _alignmentY;
            }

            set
            {
                var changed = false;
                if (_alignmentY != value)
                {
                    OnAlignmentYChanging();
                    changed = true;
                }

                SetValue(s_IdOfAlignmentYProperty, ref _alignmentY, value);
                if (changed)
                    OnAlignmentYChanged();
            }
        }

        partial void OnAlignmentYChanged();
        partial void OnAlignmentYChanging();
        internal readonly static CompositionProperty<Avalonia.Media.AlignmentY> s_IdOfAlignmentYProperty = CompositionProperty.Register<ServerCompositionSimpleTileBrush, Avalonia.Media.AlignmentY>("AlignmentY", obj => ((ServerCompositionSimpleTileBrush)obj)._alignmentY, (obj, v) => ((ServerCompositionSimpleTileBrush)obj)._alignmentY = v, null);
        Avalonia.RelativeRect _destinationRect;
        public Avalonia.RelativeRect DestinationRect
        {
            get
            {
                return _destinationRect;
            }

            set
            {
                var changed = false;
                if (_destinationRect != value)
                {
                    OnDestinationRectChanging();
                    changed = true;
                }

                SetValue(s_IdOfDestinationRectProperty, ref _destinationRect, value);
                if (changed)
                    OnDestinationRectChanged();
            }
        }

        partial void OnDestinationRectChanged();
        partial void OnDestinationRectChanging();
        internal readonly static CompositionProperty<Avalonia.RelativeRect> s_IdOfDestinationRectProperty = CompositionProperty.Register<ServerCompositionSimpleTileBrush, Avalonia.RelativeRect>("DestinationRect", obj => ((ServerCompositionSimpleTileBrush)obj)._destinationRect, (obj, v) => ((ServerCompositionSimpleTileBrush)obj)._destinationRect = v, null);
        Avalonia.RelativeRect _sourceRect;
        public Avalonia.RelativeRect SourceRect
        {
            get
            {
                return _sourceRect;
            }

            set
            {
                var changed = false;
                if (_sourceRect != value)
                {
                    OnSourceRectChanging();
                    changed = true;
                }

                SetValue(s_IdOfSourceRectProperty, ref _sourceRect, value);
                if (changed)
                    OnSourceRectChanged();
            }
        }

        partial void OnSourceRectChanged();
        partial void OnSourceRectChanging();
        internal readonly static CompositionProperty<Avalonia.RelativeRect> s_IdOfSourceRectProperty = CompositionProperty.Register<ServerCompositionSimpleTileBrush, Avalonia.RelativeRect>("SourceRect", obj => ((ServerCompositionSimpleTileBrush)obj)._sourceRect, (obj, v) => ((ServerCompositionSimpleTileBrush)obj)._sourceRect = v, null);
        Avalonia.Media.Stretch _stretch;
        public Avalonia.Media.Stretch Stretch
        {
            get
            {
                return _stretch;
            }

            set
            {
                var changed = false;
                if (_stretch != value)
                {
                    OnStretchChanging();
                    changed = true;
                }

                SetValue(s_IdOfStretchProperty, ref _stretch, value);
                if (changed)
                    OnStretchChanged();
            }
        }

        partial void OnStretchChanged();
        partial void OnStretchChanging();
        internal readonly static CompositionProperty<Avalonia.Media.Stretch> s_IdOfStretchProperty = CompositionProperty.Register<ServerCompositionSimpleTileBrush, Avalonia.Media.Stretch>("Stretch", obj => ((ServerCompositionSimpleTileBrush)obj)._stretch, (obj, v) => ((ServerCompositionSimpleTileBrush)obj)._stretch = v, null);
        Avalonia.Media.TileMode _tileMode;
        public Avalonia.Media.TileMode TileMode
        {
            get
            {
                return _tileMode;
            }

            set
            {
                var changed = false;
                if (_tileMode != value)
                {
                    OnTileModeChanging();
                    changed = true;
                }

                SetValue(s_IdOfTileModeProperty, ref _tileMode, value);
                if (changed)
                    OnTileModeChanged();
            }
        }

        partial void OnTileModeChanged();
        partial void OnTileModeChanging();
        internal readonly static CompositionProperty<Avalonia.Media.TileMode> s_IdOfTileModeProperty = CompositionProperty.Register<ServerCompositionSimpleTileBrush, Avalonia.Media.TileMode>("TileMode", obj => ((ServerCompositionSimpleTileBrush)obj)._tileMode, (obj, v) => ((ServerCompositionSimpleTileBrush)obj)._tileMode = v, null);
        protected override void DeserializeChangesCore(BatchStreamReader reader, TimeSpan committedAt)
        {
            base.DeserializeChangesCore(reader, committedAt);
            DeserializeChangesExtra(reader);
            var changed = reader.Read<CompositionSimpleTileBrushChangedFields>();
            if ((changed & CompositionSimpleTileBrushChangedFields.AlignmentX) == CompositionSimpleTileBrushChangedFields.AlignmentX)
                AlignmentX = reader.Read<Avalonia.Media.AlignmentX>();
            if ((changed & CompositionSimpleTileBrushChangedFields.AlignmentY) == CompositionSimpleTileBrushChangedFields.AlignmentY)
                AlignmentY = reader.Read<Avalonia.Media.AlignmentY>();
            if ((changed & CompositionSimpleTileBrushChangedFields.DestinationRect) == CompositionSimpleTileBrushChangedFields.DestinationRect)
                DestinationRect = reader.Read<Avalonia.RelativeRect>();
            if ((changed & CompositionSimpleTileBrushChangedFields.SourceRect) == CompositionSimpleTileBrushChangedFields.SourceRect)
                SourceRect = reader.Read<Avalonia.RelativeRect>();
            if ((changed & CompositionSimpleTileBrushChangedFields.Stretch) == CompositionSimpleTileBrushChangedFields.Stretch)
                Stretch = reader.Read<Avalonia.Media.Stretch>();
            if ((changed & CompositionSimpleTileBrushChangedFields.TileMode) == CompositionSimpleTileBrushChangedFields.TileMode)
                TileMode = reader.Read<Avalonia.Media.TileMode>();
            OnFieldsDeserialized(changed);
        }

        partial void OnFieldsDeserialized(CompositionSimpleTileBrushChangedFields changed);
        internal static void SerializeAllChanges(BatchStreamWriter writer, Avalonia.Media.AlignmentX alignmentX, Avalonia.Media.AlignmentY alignmentY, Avalonia.RelativeRect destinationRect, Avalonia.RelativeRect sourceRect, Avalonia.Media.Stretch stretch, Avalonia.Media.TileMode tileMode)
        {
            writer.Write(CompositionSimpleTileBrushChangedFields.AlignmentX | CompositionSimpleTileBrushChangedFields.AlignmentY | CompositionSimpleTileBrushChangedFields.DestinationRect | CompositionSimpleTileBrushChangedFields.SourceRect | CompositionSimpleTileBrushChangedFields.Stretch | CompositionSimpleTileBrushChangedFields.TileMode);
            writer.Write(alignmentX);
            writer.Write(alignmentY);
            writer.Write(destinationRect);
            writer.Write(sourceRect);
            writer.Write(stretch);
            writer.Write(tileMode);
        }
    }
}