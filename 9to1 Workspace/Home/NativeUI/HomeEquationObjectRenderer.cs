using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CSharpMath.SkiaSharp;
using Haven.Core;
using HavenOS.Home.Core;
using SkiaSharp;

namespace HavenOS.Home.NativeUI;

/// <summary>Actual mathematical layout over the retained Notes source. No file/URI acquisition,
/// macro registry mutation, or persisted replacement of the canonical visual authoring graph.</summary>
internal static class HomeEquationObjectRenderer
{
    public static Control Render(JsonElement canonical, Guid objectId, ICollection<WriteableBitmap> ownedBitmaps)
    {
        var equation = HomeEquationObjectHandler.ReadCanonical(canonical, objectId);
        var stack = new StackPanel { Spacing = 6 };
        var layoutFailed = false;
        AutomationProperties.SetName(stack, string.IsNullOrWhiteSpace(equation.AccessibleAlternative) ? equation.Source : equation.AccessibleAlternative);
        if (equation.ViewMode is NotesEquationViewMode.Visual or NotesEquationViewMode.Split)
        {
            try
            {
            ValidateLayoutSource(equation);
            var painter = new MathPainter
            {
                DisplayErrorInline = false, FontSize = 32, TextColor = SKColors.Black,
                AntiAlias = true, LaTeX = equation.Source
            };
            if (painter.ErrorMessage is not null)
                throw new InvalidDataException("The mathematical source could not be parsed: " + painter.ErrorMessage[..Math.Min(painter.ErrorMessage.Length, 1024)]);
            var bounds = painter.Measure();
            if (painter.Display is null || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) ||
                !float.IsFinite(bounds.Top) || !float.IsFinite(bounds.Left) || bounds.Width <= 0 || bounds.Height <= 0 ||
                bounds.Width > 4080 || bounds.Height > 4080)
                throw new InvalidDataException("Mathematical layout is empty or exceeds the native surface bounds.");
            var width = checked((int)Math.Ceiling(bounds.Width) + 16);
            var height = checked((int)Math.Ceiling(bounds.Height) + 16);
            if ((long)width * height > 4 * 1024 * 1024)
                throw new InvalidDataException("Mathematical layout exceeds the bounded raster area.");
            using var raster = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(raster))
            {
                canvas.Clear(SKColors.Transparent);
                painter.Draw(canvas, 8 - bounds.Left, 8 - bounds.Top);
                canvas.Flush();
            }
            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            ownedBitmaps.Add(bitmap);
            using (var target = bitmap.Lock())
            {
                var row = new byte[checked(width * 4)];
                try
                {
                    for (var y = 0; y < height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(raster.GetPixels(), checked(y * raster.RowBytes)), row, 0, row.Length);
                        Marshal.Copy(row, 0, IntPtr.Add(target.Address, checked(y * target.RowBytes)), row.Length);
                    }
                }
                finally { Array.Clear(row); }
            }
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = width, MaxHeight = height };
            AutomationProperties.SetAutomationId(image, objectId.ToString("D"));
            stack.Children.Add(new Border { Background = Brushes.White, Child = image });
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            {
                layoutFailed = true;
                var message = new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap };
                AutomationProperties.SetAutomationId(message, objectId.ToString("D") + "-error");
                stack.Children.Add(message);
            }
        }
        if (layoutFailed || equation.ViewMode is NotesEquationViewMode.Source or NotesEquationViewMode.Split)
        {
            var source = new TextBlock { Text = equation.Source, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace") };
            AutomationProperties.SetAutomationId(source, objectId.ToString("D") + "-source");
            stack.Children.Add(source);
        }
        if (equation.Numbered && equation.Number is { } number)
            stack.Children.Add(new TextBlock { Text = "(" + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" });
        if (!string.IsNullOrWhiteSpace(equation.Label)) stack.Children.Add(new TextBlock { Text = equation.Label });
        return stack;
    }
    private static void ValidateLayoutSource(NotesEquationData equation)
    {
        if (!string.IsNullOrWhiteSpace(equation.VisualStructureJson) && equation.VisualStructureJson.Trim() != "{}")
            throw new NotSupportedException("Visual equation graph synchronization is unavailable here; the stored source is shown below.");
        if (equation.Macros.Count != 0)
            throw new NotSupportedException("Scoped custom equation macros are not supported by this renderer; the canonical macros remain preserved.");
        if (string.IsNullOrWhiteSpace(equation.Source) || equation.Source.Length > 8192)
            throw new InvalidDataException("Mathematical layout requires a nonempty source of at most 8192 characters.");
        var depth = 0; var commands = 0;
        for (var i = 0; i < equation.Source.Length; i++)
        {
            var current = equation.Source[i];
            if (current == '\\')
            {
                if (++commands > 256) throw new InvalidDataException("Mathematical layout exceeds the command budget.");
                if (i + 1 < equation.Source.Length && !char.IsLetter(equation.Source[i + 1])) i++;
            }
            else if (current == '{' && ++depth > 64) throw new InvalidDataException("Mathematical layout exceeds the nesting budget.");
            else if (current == '}' && --depth < 0) throw new InvalidDataException("Mathematical layout has unmatched groups.");
        }
        if (depth != 0) throw new InvalidDataException("Mathematical layout has unmatched groups.");
    }
}
