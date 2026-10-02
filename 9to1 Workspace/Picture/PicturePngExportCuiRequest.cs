using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Files;

namespace HavenOS.Images;

/// <summary>Owning export preparation UI. The injected host mounts actual Home approval; this form never grants or executes authority.</summary>
public sealed class PicturePngExportCuiRequest(PictureHomePngExportOperation operation, HostedItemId sourceFileId,
    FilesRevisionId sourceRevision, Guid documentId, long documentRevision, Guid expectedStoreId, Func<bool> isAvailable,
    Func<PicturePngExportIntent, CancellationToken, Task<string>> requestApproval)
    : ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private string _fileName = "Picture.png";
    private bool _acknowledged;
    private bool _busy;
    private bool _submitted;
    private PicturePngExportIntent? _prepared;
    private string _status = "Choose a name for a new PNG file in Pictures.";
    public event PropertyChangedEventHandler? PropertyChanged;

    public static CuiDocument LoadDocument()
    {
        const string resource = "HavenOS.Images.UI.PicturePngExport.cui";
        using var stream = typeof(PicturePngExportCuiRequest).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("The owning Picture PNG export CUI source is missing.");
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
            "FileName" => _fileName, "FlattenAcknowledged" => _acknowledged,
            "CanEdit" => !_busy && !_submitted && isAvailable(),
            "CanPrepare" => IsActionAvailable("9to1.Picture.Export.Prepare") == true,
            "CanRequestApproval" => IsActionAvailable("9to1.Picture.Export.RequestApproval") == true,
            "Status" => _status,
            "PreparedSummary" => _prepared is null ? "" : $"{_prepared.FileName} — {_prepared.SizeBytes:N0} bytes. A new PNG of the first frame, with edits applied and source metadata removed.",
            _ => null
        };
        return path is "FileName" or "FlattenAcknowledged" or "CanEdit" or "CanPrepare" or "CanRequestApproval" or "Status" or "PreparedSummary";
    }

    public bool TrySetValue(string path, object? value)
    {
        if (_busy || _submitted || !isAvailable()) return false;
        switch (path)
        {
            case "FileName" when value is string name: _fileName = name; break;
            case "FlattenAcknowledged" when value is bool acknowledged: _acknowledged = acknowledged; break;
            default: return false;
        }
        _prepared = null;
        _status = "Prepare the export to review this exact snapshot before requesting approval.";
        Changed(); return true;
    }

    public bool? IsActionAvailable(string action) => action switch
    {
        "9to1.Picture.Export.Prepare" => !_busy && !_submitted && isAvailable() && _acknowledged && !string.IsNullOrWhiteSpace(_fileName),
        "9to1.Picture.Export.RequestApproval" => !_busy && !_submitted && isAvailable() && _prepared is not null,
        _ => null
    };

    public async ValueTask DispatchAsync(string action, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parameter is not null) throw new ArgumentException("Export controls do not accept caller-supplied authority or destination paths.", nameof(parameter));
        if (IsActionAvailable(action) != true) throw new InvalidOperationException("This export request is unavailable.");
        _busy = true; Changed();
        try
        {
            if (action == "9to1.Picture.Export.Prepare")
            {
                _prepared = null;
                var prepared = await operation.PrepareAsync(sourceFileId, sourceRevision, documentId, documentRevision,
                    _fileName, _acknowledged, expectedStoreId, cancellationToken);
                if (!isAvailable()) throw new UnauthorizedAccessException("The current host no longer permits exporting this snapshot.");
                _prepared = prepared;
                _status = "The snapshot is prepared. Review it in Home to approve creating the new file.";
            }
            else
            {
                var prepared = _prepared!;
                _submitted = true; // A failed callback may already have created a durable Home request.
                _status = await requestApproval(prepared, cancellationToken);
            }
        }
        catch
        {
            _prepared = null;
            _status = _submitted ? "Check Home for this export request before starting another one."
                : "The export request did not complete. Refresh the source and try again.";
            throw;
        }
        finally { _busy = false; Changed(); }
    }

    public void RefreshAvailability()
    {
        if (!isAvailable()) { _prepared = null; _status = "Export is unavailable for the current source or destination access."; }
        Changed();
    }
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
