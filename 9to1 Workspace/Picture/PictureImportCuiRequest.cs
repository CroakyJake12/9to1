using System.ComponentModel;
using Avalonia.Platform.Storage;
using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Images;

/// <summary>Picker-backed import preparation; the host owns pending Home approval and the submitted intent lifetime.</summary>
public sealed class PictureImportCuiRequest(PictureHomeImportOperation operation, IStorageProvider picker,
    PictureGlycinDecoder decoder, Func<bool> isAvailable,
    Func<PictureImportIntent, CancellationToken, Task<string>> requestApproval)
    : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private PictureImportIntent? _prepared;
    private bool _busy, _submitted, _disposed;
    private string _status = "Choose an image to import into Pictures. Original animation frames and metadata are retained.";
    public event PropertyChangedEventHandler? PropertyChanged;
    public static CuiDocument LoadDocument()
    {
        const string resource = "HavenOS.Images.UI.PictureImport.cui";
        using var stream = typeof(PictureImportCuiRequest).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("The owning Picture import CUI source is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), resource);
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "CanPick" => IsActionAvailable("9to1.Picture.Import.Pick") == true,
            "CanRequestApproval" => IsActionAvailable("9to1.Picture.Import.RequestApproval") == true,
            "Status" => _status,
            "PreparedSummary" => _prepared is null ? "" : $"{_prepared.Name} — {_prepared.SizeBytes:N0} bytes. Creates the original source and a separate editable Picture document.",
            _ => null
        };
        return path is "CanPick" or "CanRequestApproval" or "Status" or "PreparedSummary";
    }
    public bool? IsActionAvailable(string action) => action switch
    {
        "9to1.Picture.Import.Pick" => !_disposed && !_busy && !_submitted && isAvailable() && picker.CanOpen,
        "9to1.Picture.Import.RequestApproval" => !_disposed && !_busy && !_submitted && isAvailable() && _prepared is not null,
        _ => null
    };
    public async ValueTask DispatchAsync(string action, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parameter is not null) throw new ArgumentException("Import accepts only an actual native picker selection.", nameof(parameter));
        if (IsActionAvailable(action) != true) throw new InvalidOperationException("Import is currently unavailable.");
        _busy = true; Changed();
        try
        {
            if (action == "9to1.Picture.Import.Pick")
            {
                _prepared?.Dispose(); _prepared = null;
                using var picked = await PicturePickedImage.PickAsync(picker, decoder, cancellationToken);
                if (picked is null) { _status = "No image selected."; return; }
                var prepared = await operation.PrepareAsync(picked, cancellationToken);
                if (_disposed || !isAvailable()) { prepared.Dispose(); throw new UnauthorizedAccessException("Import access changed."); }
                _prepared = prepared;
                _status = "Review the selected image in Home to approve its import.";
            }
            else
            {
                _submitted = true; // The callback takes ownership even if creating the durable request later fails.
                _status = await requestApproval(_prepared!, cancellationToken);
            }
        }
        catch
        {
            if (!_submitted) { _prepared?.Dispose(); _prepared = null; }
            _status = _submitted ? "Check Home for this import request before starting another one." : "Import preparation did not complete.";
            throw;
        }
        finally { _busy = false; Changed(); }
    }
    public void RefreshAvailability()
    {
        if (!isAvailable() && !_submitted) { _prepared?.Dispose(); _prepared = null; _status = "Import is unavailable for the current destination access."; }
        Changed();
    }
    public void Dispose()
    {
        _disposed = true;
        if (!_submitted) _prepared?.Dispose();
        _prepared = null; Changed();
    }
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
