using System.Numerics;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Rendering.Composition.Expressions;

// Special license applies <see href="https://raw.githubusercontent.com/AvaloniaUI/Avalonia/master/src/Avalonia.Base/Rendering/Composition/License.md\">License.md</see>

namespace Avalonia.Rendering.Composition
{

    public class ScalarKeyFrameAnimation : KeyFrameAnimation
    {
        public ScalarKeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        internal override IAnimationInstance CreateInstance(Avalonia.Rendering.Composition.Server.ServerObject targetObject, ExpressionVariant? finalValue)
        {
            return new KeyFrameAnimationInstance<float>(ScalarInterpolator.Instance, _keyFrames.Snapshot(), CreateSnapshot(), 
                finalValue?.CastOrDefault<float>(), targetObject,
                DelayBehavior, DelayTime, Direction, Duration, IterationBehavior,
                IterationCount, StopBehavior);
        }
        
        private KeyFrames<float> _keyFrames = new KeyFrames<float>();
        private protected override IKeyFrames KeyFrames => _keyFrames;

        public void InsertKeyFrame(float normalizedProgressKey, float value, Avalonia.Animation.Easings.IEasing easingFunction)
        {
            _keyFrames.Insert(normalizedProgressKey, value, easingFunction);
        }
        
        public void InsertKeyFrame(float normalizedProgressKey, float value)
        {
            _keyFrames.Insert(normalizedProgressKey, value, Compositor.DefaultEasing);
        }
    }

    public partial class Compositor
    {
        public ScalarKeyFrameAnimation CreateScalarKeyFrameAnimation() => new ScalarKeyFrameAnimation(this);
    }

    public class DoubleKeyFrameAnimation : KeyFrameAnimation
    {
        public DoubleKeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        internal override IAnimationInstance CreateInstance(Avalonia.Rendering.Composition.Server.ServerObject targetObject, ExpressionVariant? finalValue)
        {
            return new KeyFrameAnimationInstance<double>(DoubleInterpolator.Instance, _keyFrames.Snapshot(), CreateSnapshot(), 
                finalValue?.CastOrDefault<double>(), targetObject,
                DelayBehavior, DelayTime, Direction, Duration, IterationBehavior,
                IterationCount, StopBehavior);
        }
        
        private KeyFrames<double> _keyFrames = new KeyFrames<double>();
        private protected override IKeyFrames KeyFrames => _keyFrames;

        public void InsertKeyFrame(float normalizedProgressKey, double value, Avalonia.Animation.Easings.IEasing easingFunction)
        {
            _keyFrames.Insert(normalizedProgressKey, value, easingFunction);
        }
        
        public void InsertKeyFrame(float normalizedProgressKey, double value)
        {
            _keyFrames.Insert(normalizedProgressKey, value, Compositor.DefaultEasing);
        }
    }

    public partial class Compositor
    {
        public DoubleKeyFrameAnimation CreateDoubleKeyFrameAnimation() => new DoubleKeyFrameAnimation(this);
    }

    public class BooleanKeyFrameAnimation : KeyFrameAnimation
    {
        public BooleanKeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        internal override IAnimationInstance CreateInstance(Avalonia.Rendering.Composition.Server.ServerObject targetObject, ExpressionVariant? finalValue)
        {
            return new KeyFrameAnimationInstance<bool>(BooleanInterpolator.Instance, _keyFrames.Snapshot(), CreateSnapshot(), 
                finalValue?.CastOrDefault<bool>(), targetObject,
                DelayBehavior, DelayTime, Direction, Duration, IterationBehavior,
                IterationCount, StopBehavior);
        }
        
        private KeyFrames<bool> _keyFrames = new KeyFrames<bool>();
        private protected override IKeyFrames KeyFrames => _keyFrames;

        public void InsertKeyFrame(float normalizedProgressKey, bool value, Avalonia.Animation.Easings.IEasing easingFunction)
        {
            _keyFrames.Insert(normalizedProgressKey, value, easingFunction);
        }
        
        public void InsertKeyFrame(float normalizedProgressKey, bool value)
        {
            _keyFrames.Insert(normalizedProgressKey, value, Compositor.DefaultEasing);
        }
    }

    public partial class Compositor
    {
        public BooleanKeyFrameAnimation CreateBooleanKeyFrameAnimation() => new BooleanKeyFrameAnimation(this);
    }

    public class ColorKeyFrameAnimation : KeyFrameAnimation
    {
        public ColorKeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        internal override IAnimationInstance CreateInstance(Avalonia.Rendering.Composition.Server.ServerObject targetObject, ExpressionVariant? finalValue)
        {
            return new KeyFrameAnimationInstance<Avalonia.Media.Color>(ColorInterpolator.Instance, _keyFrames.Snapshot(), CreateSnapshot(), 
                finalValue?.CastOrDefault<Avalonia.Media.Color>(), targetObject,
                DelayBehavior, DelayTime, Direction, Duration, IterationBehavior,
                IterationCount, StopBehavior);
        }
        
        private KeyFrames<Avalonia.Media.Color> _keyFrames = new KeyFrames<Avalonia.Media.Color>();
        private protected override IKeyFrames KeyFrames => _keyFrames;

        public void InsertKeyFrame(float normalizedProgressKey, Avalonia.Media.Color value, Avalonia.Animation.Easings.IEasing easingFunction)
        {
            _keyFrames.Insert(normalizedProgressKey, value, easingFunction);
        }
        
        public void InsertKeyFrame(float normalizedProgressKey, Avalonia.Media.Color value)
        {
            _keyFrames.Insert(normalizedProgressKey, value, Compositor.DefaultEasing);
        }
    }

    public partial class Compositor
    {
        public ColorKeyFrameAnimation CreateColorKeyFrameAnimation() => new ColorKeyFrameAnimation(this);
    }

    public class VectorKeyFrameAnimation : KeyFrameAnimation
    {
        public VectorKeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        internal override IAnimationInstance CreateInstance(Avalonia.Rendering.Composition.Server.ServerObject targetObject, ExpressionVariant? finalValue)
        {
            return new KeyFrameAnimationInstance<Vector>(VectorInterpolator.Instance, _keyFrames.Snapshot(), CreateSnapshot(), 
                finalValue?.CastOrDefault<Vector>(), targetObject,
                DelayBehavior, DelayTime, Direction, Duration, IterationBehavior,
                IterationCount, StopBehavior);
        }
        
        private KeyFrames<Vector> _keyFrames = new KeyFrames<Vector>();
        private protected override IKeyFrames KeyFrames => _keyFrames;

        public void InsertKeyFrame(float normalizedProgressKey, Vector value, Avalonia.Animation.Easings.IEasing easingFunction)
        {
            _keyFrames.Insert(normalizedProgressKey, value, easingFunction);
        }
        
        public void InsertKeyFrame(float normalizedProgressKey, Vector value)
        {
            _keyFrames.Insert(normalizedProgressKey, value, Compositor.DefaultEasing);
        }
    }

    public partial class Compositor
    {
        public VectorKeyFrameAnimation CreateVectorKeyFrameAnimation() => new VectorKeyFrameAnimation(this);
    }

    public class Vector2KeyFrameAnimation : KeyFrameAnimation
    {
        public Vector2KeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        internal override IAnimationInstance CreateInstance(Avalonia.Rendering.Composition.Server.ServerObject targetObject, ExpressionVariant? finalValue)
        {
            return new KeyFrameAnimationInstance<Vector2>(Vector2Interpolator.Instance, _keyFrames.Snapshot(), CreateSnapshot(), 
                finalValue?.CastOrDefault<Vector2>(), targetObject,
                DelayBehavior, DelayTime, Direction, Duration, IterationBehavior,
                IterationCount, StopBehavior);
        }
        
        private KeyFrames<Vector2> _keyFrames = new KeyFrames<Vector2>();
        private protected override IKeyFrames KeyFrames => _keyFrames;

        public void InsertKeyFrame(float normalizedProgressKey, Vector2 value, Avalonia.Animation.Easings.IEasing easingFunction)
        {
            _keyFrames.Insert(normalizedProgressKey, value, easingFunction);
        }
        
        public void InsertKeyFrame(float normalizedProgressKey, Vector2 value)
        {
            _keyFrames.Insert(normalizedProgressKey, value, Compositor.DefaultEasing);
        }
    }

    public partial class Compositor
    {
        public Vector2KeyFrameAnimation CreateVector2KeyFrameAnimation() => new Vector2KeyFrameAnimation(this);
    }

    public class Vector3KeyFrameAnimation : KeyFrameAnimation
    {
        public Vector3KeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        internal override IAnimationInstance CreateInstance(Avalonia.Rendering.Composition.Server.ServerObject targetObject, ExpressionVariant? finalValue)
        {
            return new KeyFrameAnimationInstance<Vector3>(Vector3Interpolator.Instance, _keyFrames.Snapshot(), CreateSnapshot(), 
                finalValue?.CastOrDefault<Vector3>(), targetObject,
                DelayBehavior, DelayTime, Direction, Duration, IterationBehavior,
                IterationCount, StopBehavior);
        }
        
        private KeyFrames<Vector3> _keyFrames = new KeyFrames<Vector3>();
        private protected override IKeyFrames KeyFrames => _keyFrames;

        public void InsertKeyFrame(float normalizedProgressKey, Vector3 value, Avalonia.Animation.Easings.IEasing easingFunction)
        {
            _keyFrames.Insert(normalizedProgressKey, value, easingFunction);
        }
        
        public void InsertKeyFrame(float normalizedProgressKey, Vector3 value)
        {
            _keyFrames.Insert(normalizedProgressKey, value, Compositor.DefaultEasing);
        }
    }

    public partial class Compositor
    {
        public Vector3KeyFrameAnimation CreateVector3KeyFrameAnimation() => new Vector3KeyFrameAnimation(this);
    }

    public class Vector3DKeyFrameAnimation : KeyFrameAnimation
    {
        public Vector3DKeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        internal override IAnimationInstance CreateInstance(Avalonia.Rendering.Composition.Server.ServerObject targetObject, ExpressionVariant? finalValue)
        {
            return new KeyFrameAnimationInstance<Vector3D>(Vector3DInterpolator.Instance, _keyFrames.Snapshot(), CreateSnapshot(), 
                finalValue?.CastOrDefault<Vector3D>(), targetObject,
                DelayBehavior, DelayTime, Direction, Duration, IterationBehavior,
                IterationCount, StopBehavior);
        }
        
        private KeyFrames<Vector3D> _keyFrames = new KeyFrames<Vector3D>();
        private protected override IKeyFrames KeyFrames => _keyFrames;

        public void InsertKeyFrame(float normalizedProgressKey, Vector3D value, Avalonia.Animation.Easings.IEasing easingFunction)
        {
            _keyFrames.Insert(normalizedProgressKey, value, easingFunction);
        }
        
        public void InsertKeyFrame(float normalizedProgressKey, Vector3D value)
        {
            _keyFrames.Insert(normalizedProgressKey, value, Compositor.DefaultEasing);
        }
    }

    public partial class Compositor
    {
        public Vector3DKeyFrameAnimation CreateVector3DKeyFrameAnimation() => new Vector3DKeyFrameAnimation(this);
    }

    public class Vector4KeyFrameAnimation : KeyFrameAnimation
    {
        public Vector4KeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        internal override IAnimationInstance CreateInstance(Avalonia.Rendering.Composition.Server.ServerObject targetObject, ExpressionVariant? finalValue)
        {
            return new KeyFrameAnimationInstance<Vector4>(Vector4Interpolator.Instance, _keyFrames.Snapshot(), CreateSnapshot(), 
                finalValue?.CastOrDefault<Vector4>(), targetObject,
                DelayBehavior, DelayTime, Direction, Duration, IterationBehavior,
                IterationCount, StopBehavior);
        }
        
        private KeyFrames<Vector4> _keyFrames = new KeyFrames<Vector4>();
        private protected override IKeyFrames KeyFrames => _keyFrames;

        public void InsertKeyFrame(float normalizedProgressKey, Vector4 value, Avalonia.Animation.Easings.IEasing easingFunction)
        {
            _keyFrames.Insert(normalizedProgressKey, value, easingFunction);
        }
        
        public void InsertKeyFrame(float normalizedProgressKey, Vector4 value)
        {
            _keyFrames.Insert(normalizedProgressKey, value, Compositor.DefaultEasing);
        }
    }

    public partial class Compositor
    {
        public Vector4KeyFrameAnimation CreateVector4KeyFrameAnimation() => new Vector4KeyFrameAnimation(this);
    }

    public class QuaternionKeyFrameAnimation : KeyFrameAnimation
    {
        public QuaternionKeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        internal override IAnimationInstance CreateInstance(Avalonia.Rendering.Composition.Server.ServerObject targetObject, ExpressionVariant? finalValue)
        {
            return new KeyFrameAnimationInstance<Quaternion>(QuaternionInterpolator.Instance, _keyFrames.Snapshot(), CreateSnapshot(), 
                finalValue?.CastOrDefault<Quaternion>(), targetObject,
                DelayBehavior, DelayTime, Direction, Duration, IterationBehavior,
                IterationCount, StopBehavior);
        }
        
        private KeyFrames<Quaternion> _keyFrames = new KeyFrames<Quaternion>();
        private protected override IKeyFrames KeyFrames => _keyFrames;

        public void InsertKeyFrame(float normalizedProgressKey, Quaternion value, Avalonia.Animation.Easings.IEasing easingFunction)
        {
            _keyFrames.Insert(normalizedProgressKey, value, easingFunction);
        }
        
        public void InsertKeyFrame(float normalizedProgressKey, Quaternion value)
        {
            _keyFrames.Insert(normalizedProgressKey, value, Compositor.DefaultEasing);
        }
    }

    public partial class Compositor
    {
        public QuaternionKeyFrameAnimation CreateQuaternionKeyFrameAnimation() => new QuaternionKeyFrameAnimation(this);
    }
}