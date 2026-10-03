using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;

namespace HavenOS.Home.NativeUI;

/// <summary>Renders the shared object's authored CUI and detached donor frames. No artifact storage or mutation.</summary>
public sealed class HomeProductivityCuiSurface : UserControl, IDisposable
{
    private readonly CuiControlLoader _loader;
    private readonly List<WriteableBitmap> _bitmaps = [];
    private bool _disposed;
    public IReadOnlyList<string> RetainedUnsupportedProperties { get; }
    public HomeProductivityCuiSurface(HomeProductivityObjectRenderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        RetainedUnsupportedProperties = Array.AsReadOnly(result.RetainedUnsupportedProperties.ToArray());
        if (RetainedUnsupportedProperties.Count > 0)
        {
            var limitations = "Preserved properties not displayed by this surface: " + string.Join(", ", RetainedUnsupportedProperties);
            ToolTip.SetTip(this, limitations); AutomationProperties.SetHelpText(this, limitations);
        }
        var bindings = result.RasterBindings.ToArray();
        if (bindings.Any(b => b is null || b.ObjectId == Guid.Empty || string.IsNullOrWhiteSpace(b.ControlId) || b.Frame is null) ||
            bindings.Select(b => b.ControlId).Distinct(StringComparer.Ordinal).Count() != bindings.Length)
            throw new InvalidDataException("Productivity frame bindings require unique controls and stable object identities.");
        var byId = bindings.ToDictionary(b => b.ControlId, StringComparer.Ordinal);
        var notes = result.NotesBindings.ToArray();
        if (notes.Any(b => b is null || b.ObjectId == Guid.Empty || string.IsNullOrWhiteSpace(b.ControlId)) ||
            notes.Select(b => b.ControlId).Concat(byId.Keys).Distinct(StringComparer.Ordinal).Count() != notes.Length + byId.Count)
            throw new InvalidDataException("Shared Notes bindings require unique stable identities.");
        var notesById = notes.ToDictionary(b => b.ControlId, b => b with { CanonicalBlock = b.CanonicalBlock.Clone() }, StringComparer.Ordinal);
        var vectors = result.VectorBindings.ToArray();
        if (vectors.Any(binding => binding is null || binding.ObjectId == Guid.Empty || string.IsNullOrWhiteSpace(binding.ControlId)) ||
            vectors.Select(binding => binding.ControlId).Concat(notesById.Keys).Concat(byId.Keys).Distinct(StringComparer.Ordinal).Count() != vectors.Length + notes.Length + bindings.Length)
            throw new InvalidDataException("Shared vector bindings require unique canonical identities.");
        var vectorById = vectors.ToDictionary(binding => binding.ControlId,
            binding => binding with { CanonicalShape = binding.CanonicalShape.Clone() }, StringComparer.Ordinal);
        var equations = result.EquationBindings.ToArray();
        if (equations.Any(binding => binding is null || binding.ObjectId == Guid.Empty || string.IsNullOrWhiteSpace(binding.ControlId)) ||
            equations.Select(binding => binding.ControlId).Concat(vectorById.Keys).Concat(notesById.Keys).Concat(byId.Keys)
                .Distinct(StringComparer.Ordinal).Count() != equations.Length + vectors.Length + notes.Length + bindings.Length)
            throw new InvalidDataException("Shared equation bindings require unique canonical identities.");
        var equationById = equations.ToDictionary(binding => binding.ControlId,
            binding => binding with { CanonicalBlock = binding.CanonicalBlock.Clone() }, StringComparer.Ordinal);
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("spe.raster", component =>
        {
            if (component.Name is null || !byId.TryGetValue(component.Name, out var binding) || !consumed.Add(component.Name))
                throw new InvalidDataException("The authored productivity raster has no unique bound frame.");
            var frame = binding.Frame;
            var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _bitmaps.Add(bitmap);
            using (var target = bitmap.Lock())
            {
                var pixels = frame.CopyPixels();
                for (var row = 0; row < frame.Height; row++)
                    Marshal.Copy(pixels, row * frame.Stride, IntPtr.Add(target.Address, row * target.RowBytes), frame.Width * 4);
            }
            return new Image { Source = bitmap, Stretch = Stretch.Uniform };
        });
        registry.RegisterObjectRenderer("spe.notes", component =>
        {
            if (component.Name is null || !notesById.TryGetValue(component.Name, out var binding) || !consumed.Add(component.Name) ||
                !binding.CanonicalBlock.TryGetProperty("Id", out var id) || !id.TryGetGuid(out var canonicalId) || canonicalId != binding.ObjectId)
                throw new InvalidDataException("The authored Notes object has no unique canonical binding.");
            return HomeNotesObjectRenderer.Render(binding.CanonicalBlock);
        });
        registry.RegisterObjectRenderer("spe.vector", component =>
        {
            if (component.Name is null || !vectorById.TryGetValue(component.Name, out var binding) || !consumed.Add(component.Name))
                throw new InvalidDataException("The authored vector object has no unique canonical binding.");
            return HomeVectorObjectRenderer.Render(binding.CanonicalShape, binding.ObjectId);
        });
        registry.RegisterObjectRenderer("spe.equation", component =>
        {
            if (component.Name is null || !equationById.TryGetValue(component.Name, out var binding) || !consumed.Add(component.Name))
                throw new InvalidDataException("The authored equation has no unique canonical binding.");
            return HomeEquationObjectRenderer.Render(binding.CanonicalBlock, binding.ObjectId, _bitmaps);
        });
        _loader = new CuiControlLoader(registry);
        try
        {
            var loaded = _loader.TryLoad(new CuiRichParser().Parse(result.CuiSource));
            if (loaded.Root is null || loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error) || consumed.Count != bindings.Length + notes.Length + vectors.Length + equations.Length)
                throw new InvalidDataException("The productivity CUI did not render all declared frames.");
            Content = loaded.Root;
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Content = null; _loader.Dispose();
        foreach (var bitmap in _bitmaps) bitmap.Dispose();
        _bitmaps.Clear();
    }
}
