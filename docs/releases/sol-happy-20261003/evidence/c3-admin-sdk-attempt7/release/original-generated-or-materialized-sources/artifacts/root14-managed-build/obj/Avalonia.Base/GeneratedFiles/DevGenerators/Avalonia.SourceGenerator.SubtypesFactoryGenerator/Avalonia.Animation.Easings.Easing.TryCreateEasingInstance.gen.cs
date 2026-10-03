using System;
using System.Collections.Generic;

namespace Avalonia.Animation.Easings
{
    partial class Easing
    {
        private static partial bool TryCreateEasingInstance(string type,out Easing? instance)
        {
            var hasMatch = false;
            (hasMatch, instance) = type switch
            {
                "BackEaseIn" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.BackEaseIn()),
                "BackEaseInOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.BackEaseInOut()),
                "BackEaseOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.BackEaseOut()),
                "BounceEaseIn" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.BounceEaseIn()),
                "BounceEaseInOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.BounceEaseInOut()),
                "BounceEaseOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.BounceEaseOut()),
                "CircularEaseIn" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.CircularEaseIn()),
                "CircularEaseInOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.CircularEaseInOut()),
                "CircularEaseOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.CircularEaseOut()),
                "CubicEaseIn" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.CubicEaseIn()),
                "CubicEaseInOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.CubicEaseInOut()),
                "CubicEaseOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.CubicEaseOut()),
                "ElasticEaseIn" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.ElasticEaseIn()),
                "ElasticEaseInOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.ElasticEaseInOut()),
                "ElasticEaseOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.ElasticEaseOut()),
                "ExponentialEaseIn" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.ExponentialEaseIn()),
                "ExponentialEaseInOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.ExponentialEaseInOut()),
                "ExponentialEaseOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.ExponentialEaseOut()),
                "LinearEasing" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.LinearEasing()),
                "QuadraticEaseIn" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.QuadraticEaseIn()),
                "QuadraticEaseInOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.QuadraticEaseInOut()),
                "QuadraticEaseOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.QuadraticEaseOut()),
                "QuarticEaseIn" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.QuarticEaseIn()),
                "QuarticEaseInOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.QuarticEaseInOut()),
                "QuarticEaseOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.QuarticEaseOut()),
                "QuinticEaseIn" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.QuinticEaseIn()),
                "QuinticEaseInOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.QuinticEaseInOut()),
                "QuinticEaseOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.QuinticEaseOut()),
                "SineEaseIn" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.SineEaseIn()),
                "SineEaseInOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.SineEaseInOut()),
                "SineEaseOut" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.SineEaseOut()),
                "SplineEasing" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.SplineEasing()),
                "SpringEasing" => (true, (Avalonia.Animation.Easings.Easing?)new Avalonia.Animation.Easings.SpringEasing()),
                _ => (false, default(Avalonia.Animation.Easings.Easing?))
            };

            return hasMatch;
        }
    }
}