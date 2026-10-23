using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;

namespace HavenOS.Home.NativeUI;

/// <summary>A session-scoped CUI materializer for an actual Home CPU raster, not a document/ink renderer.
/// It accepts only the exact emitted spe.raster Object and performs no asset/file/URI resolution.</summary>
public sealed class HomeProductivityCuiObjectSession : IDisposable
{
    private const int MaximumCuiCharacters = 16 * 1024;
    private const long MaximumRasterBytes = 64 * 1024 * 1024;
    private CuiControlLoader? _loader;
    private WriteableBitmap? _bitmap;
    private Image? _image;
    private Control? _root;
    private bool _disposed;
    public Control View => _root ?? throw new ObjectDisposedException(nameof(HomeProductivityCuiObjectSession));
    public Guid ObjectId { get; }
    public IReadOnlyList<string> RetainedUnsupportedProperties { get; }

    public HomeProductivityCuiObjectSession(HomeProductivityObjectRenderResult rendered, Guid expectedObjectId)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(rendered);
        if (expectedObjectId == Guid.Empty || rendered.RasterBindings.Count != 1 ||
            rendered.NotesBindings.Count != 0 || rendered.VectorBindings.Count != 0 || rendered.EquationBindings.Count != 0)
            throw new InvalidDataException("This CUI session requires one exact canonical raster binding.");
        var binding = rendered.RasterBindings[0];
        if (binding.ObjectId != expectedObjectId || string.IsNullOrWhiteSpace(binding.ControlId) || binding.ControlId.Length > 256)
            throw new InvalidDataException("The Home raster does not match the expected object identity.");
        ValidateCui(rendered.CuiSource, binding.ControlId);
        var frame = binding.Frame;
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Width > 8192 || frame.Height > 8192 ||
            frame.Stride < (long)frame.Width * 4 || (long)frame.Stride * frame.Height > MaximumRasterBytes)
            throw new InvalidDataException("The Home raster dimensions or byte count exceed the supported bound.");
        var pixels = frame.CopyPixels();
        if (pixels.LongLength != (long)frame.Stride * frame.Height)
            throw new InvalidDataException("The Home raster is not a complete premultiplied BGRA frame.");
        ObjectId = expectedObjectId;
        RetainedUnsupportedProperties = Array.AsReadOnly(rendered.RetainedUnsupportedProperties.ToArray());
        try
        {
            _bitmap = new WriteableBitmap(new(frame.Width, frame.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = _bitmap.Lock())
            {
                if (buffer.Address == IntPtr.Zero || buffer.RowBytes < checked(frame.Width * 4)) throw new InvalidDataException("The renderer returned an undersized pixel row.");
                for (var row = 0; row < frame.Height; row++)
                    Marshal.Copy(pixels, checked(row * frame.Stride), IntPtr.Add(buffer.Address, checked(row * buffer.RowBytes)), checked(frame.Width * 4));
            }
            var registry = new CuiControlRegistry();
            registry.RegisterObjectRenderer("spe.raster", component =>
            {
                if (component.Name != binding.ControlId || _image is not null)
                    throw new InvalidDataException("The CUI attempted to reuse or replace its sealed raster binding.");
                return _image = new Image { Source = _bitmap, Stretch = Stretch.Uniform };
            });
            var parser = new CuiRichParser();
            var document = parser.Parse(rendered.CuiSource, "home-productivity-object.cui");
            if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("Home's emitted object CUI is invalid.");
            _loader = new CuiControlLoader(registry);
            var loaded = _loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error) || _image is null)
                throw new InvalidDataException("Home's object CUI could not be materialized by its registered raster renderer.");
            _root = loaded.Root;
        }
        catch { Dispose(); throw; }
    }

    private static void ValidateCui(string source, string controlId)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > MaximumCuiCharacters)
            throw new InvalidDataException("The Home object CUI is empty or exceeds its bound.");
        using var text = new StringReader(source);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumCuiCharacters });
        var root = XDocument.Load(reader).Root;
        if (root is null || root.Name != "Cui" || root.Attributes().Any() || root.Elements().Count() != 1)
            throw new InvalidDataException("Only the emitted single-object Home CUI is supported by this renderer.");
        var value = root.Elements().Single();
        if (value.Name != "Object" || value.Elements().Any() || value.Attribute("Type")?.Value != "spe.raster" ||
            value.Attribute("id")?.Value != controlId || value.Attributes().Any(attribute => attribute.Name != "Type" && attribute.Name != "id"))
            throw new InvalidDataException("The Home CUI contains an unbound object or unsupported resource/action content.");
        if (root.DescendantNodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value)))
            throw new InvalidDataException("A raster object cannot introduce extra authored text content.");
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _root = null;
        var loader = _loader; _loader = null;
        var image = _image; _image = null;
        var bitmap = _bitmap; _bitmap = null;
        try { loader?.Dispose(); }
        finally
        {
            try { if (image is not null) image.Source = null; }
            finally { bitmap?.Dispose(); }
        }
    }
}
