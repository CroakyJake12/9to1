using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Images;
using HavenOS.Home.Core;
using NineToOne.Web.Media;

namespace NineToOne.Web.Picture;

/// <summary>Local device adapter over unchanged canonical Picture state and native raster services.</summary>
public sealed class PictureBrowserSession : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IPictureBrowserMedia _media;
    private readonly PictureCropService _engine = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private PictureDocument? _document;
    private string _sourceBase64 = "", _sourceName = "", _lastId = "";
    private long _savedRevision = -1;
    private readonly List<string> _undo = [], _redo = [];
    private LocalSummary[] _documents = [];
    private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal)
    { ["CropX"] = "0", ["CropY"] = "0", ["CropWidth"] = "100", ["CropHeight"] = "100", ["ResizeWidth"] = "100", ["ResizeHeight"] = "100" };
    private bool _disposed, _presentationAttached = true, _preparingPresentation;
    private readonly HashSet<string> _pendingInput = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _appliedFields;
    public long StateGeneration { get; private set; }
    public long SavedRevision => _savedRevision;
    public bool HasPendingInput => _pendingInput.Count != 0;
    public PictureBrowserSession(IPictureBrowserMedia media)
    { _media = media; _appliedFields = new(_fields, StringComparer.Ordinal); }

    internal void PrepareUnadmittedPresentation()
    {
        CheckAlive();
        if (_document is not null || IsBusy || IsDirty) throw new InvalidOperationException("Only a new Picture session can prepare independently.");
        _presentationAttached = false;
    }
    internal async Task<bool> PrepareCandidateAsync(string documentId, CancellationToken token)
    {
        CheckAlive();
        if (_presentationAttached || _preparingPresentation) throw new InvalidOperationException("This session is not an independent presentation candidate.");
        _preparingPresentation = true;
        try { return await OpenAsync(documentId, token); }
        finally { _preparingPresentation = false; }
    }
    internal void AdmitPresentation()
    {
        CheckAlive();
        if (IsBusy || IsDirty || HasPendingInput) throw new InvalidOperationException("A pending Picture operation or input cannot transfer.");
        if (_document is not null)
        {
            var bytes = RetainedOriginal(_document);
            Directory.CreateDirectory(SourceDirectory);
            if (File.Exists(_document.SourcePath)) CheckSourceIntegrity(_document);
            else
            {
                var temporary = Path.Combine(SourceDirectory, $".admit-{Guid.NewGuid():N}.tmp");
                try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, _document.SourcePath!, overwrite: false); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        _presentationAttached = true;
    }
    internal void DetachPresentation()
    { CheckAlive(); _presentationAttached = false; }
    internal void PublishPresentation()
    { CheckAlive(); if (!_presentationAttached) throw new InvalidOperationException("Only the coherent current owner may publish its presentation."); Notify(stateChanged: false); }
    private byte[] RetainedOriginal(PictureDocument document)
    {
        CheckLocalSource(document); var bytes = Convert.FromBase64String(_sourceBase64); CheckPng(bytes);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), document.SourceRevision, StringComparison.Ordinal))
            throw new PictureBrowserException("SourceChanged", "The retained original PNG failed its integrity check.");
        return bytes;
    }
    private void ConsumeInput(params string[] fields)
    { foreach (var field in fields) { _pendingInput.Remove(field); _appliedFields[field] = _fields[field]; } }
    private Task<bool> EditInputAsync(Func<PictureDocument, PictureDocument> edit, string[] fields, CancellationToken token)
        => OperateAsync(async () => { Apply(edit(RequireDocument())); ConsumeInput(fields); return await SaveCoreAsync(token); }, token);
    public PictureDocument? Document => _document;
    public bool IsDirty { get; private set; }
    public bool IsBusy { get; private set; }
    public bool ShowOriginal { get; private set; }
    public string Status { get; private set; } = "Import a static PNG to create an editable local document.";
    public string? ErrorCode { get; private set; }
    public string? PresentationWarning { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Retain the current document until the shell has prepared every live participant.</summary>
    public async Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken token = default)
    {
        if (_disposed) return new(true, "Succeeded", "The Picture session is already closed.", true);
        if (HasPendingInput) return new(false, "UnsavedInput", "Apply or restore the typed crop/resize values before leaving Picture. Its current document and input are retained.", false);
        var attempted = false;
        var prepared = await OperateAsync(async () =>
        {
            attempted = true;
            return await SaveBeforeLeavingAsync(token);
        }, token);
        if (_disposed) return new(true, "Succeeded", "The Picture session is already closed.", true);
        if (!attempted) return new(false, "OperationBusy", "Picture is still processing an operation. Its document and unsaved edits are retained.", false);
        return prepared && !IsDirty
            ? new(true, "Succeeded", "Picture's local document is saved and ready to close.", true)
            : new(false, ErrorCode ?? "StorageFailed", Status, false);
    }

    public async Task RefreshAsync(CancellationToken token = default)
    {
        var values = (await CallAsync("list", new { }, token)).Deserialize<LocalSummary[]>(Json) ?? [];
        CheckAlive(); _documents = values; Notify();
    }

    public Task<bool> ImportAsync(CancellationToken token = default) => OperateAsync(async () =>
    {
        if (!await SaveBeforeLeavingAsync(token)) return false;
        var picked = (await CallAsync("pick", new { }, token)).Deserialize<PickedFile>(Json)
            ?? throw new InvalidDataException("The selected image could not be read.");
        CheckAlive();
        var bytes = Convert.FromBase64String(picked.Base64); CheckPng(bytes);
        var path = Path.Combine(SourceDirectory, $"{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(SourceDirectory); File.WriteAllBytes(path, bytes);
        try
        {
            var candidate = _engine.OpenSource(path); CheckCapacity(candidate);
            using var preview = Render(candidate);
            CheckAlive(); _document = candidate; _sourceBase64 = picked.Base64; _sourceName = picked.Name;
            _savedRevision = -1; _undo.Clear(); _redo.Clear(); ShowOriginal = false; SetDirty(true);
            SetDimensionFields(candidate); return await SaveCoreAsync(token);
        }
        catch { if (_document?.SourcePath != path && File.Exists(path)) File.Delete(path); throw; }
    }, token);

    public Task<bool> OpenAsync(string documentId, CancellationToken token = default) => OperateAsync(async () =>
    {
        if (!Guid.TryParse(documentId, out var id) || id == Guid.Empty) throw new ArgumentException("Choose a valid local document.");
        if (!await SaveBeforeLeavingAsync(token)) return false;
        var bundle = (await CallAsync("open", new { documentId }, token)).Deserialize<LocalBundle>(Json)
            ?? throw new InvalidDataException("The stored document is invalid.");
        CheckAlive();
        if (bundle.DocumentJson is null || bundle.SourceBase64 is null || string.IsNullOrWhiteSpace(bundle.Name)
            || bundle.Undo is null || bundle.Redo is null)
            throw new InvalidDataException("The local document package is incomplete.");
        var document = Decode(bundle.DocumentJson);
        if (document.DocumentId != id || bundle.DocumentId != documentId || document.Revision != bundle.Revision
            || bundle.Undo.Length > 32 || bundle.Redo.Length > 32)
            throw new InvalidDataException("The saved document identity or revision is invalid.");
        CheckLocalSource(document);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(bundle.SourceBase64); }
        catch (FormatException) { throw new PictureBrowserException("SourceChanged", "The retained original PNG bytes are corrupt."); }
        CheckPng(bytes);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), document.SourceRevision, StringComparison.Ordinal))
            throw new PictureBrowserException("SourceChanged", "The retained original image failed its integrity check.");
        foreach (var snapshot in bundle.Undo.Concat(bundle.Redo))
        {
            var history = Decode(snapshot); CheckLocalSource(history); CheckCapacity(history);
            if (history.DocumentId != id || history.SourcePath != document.SourcePath || history.SourceRevision != document.SourceRevision)
                throw new InvalidDataException("History references a different document or source.");
        }
        if (_presentationAttached) { Directory.CreateDirectory(SourceDirectory); File.WriteAllBytes(document.SourcePath!, bytes); }
        // Candidate preview uses the unchanged owner renderer over retained original bytes,
        // without overwriting an active session's canonical native source cache.
        if (!_presentationAttached) _sourceBase64 = bundle.SourceBase64;
        using var preview = Render(document);
        CheckAlive(); _document = document; _sourceBase64 = bundle.SourceBase64; _sourceName = bundle.Name;
        _savedRevision = document.Revision; _lastId = documentId;
        _undo.Clear(); _undo.AddRange(bundle.Undo); _redo.Clear(); _redo.AddRange(bundle.Redo);
        ShowOriginal = false; SetDimensionFields(document); SetDirty(false);
        Status = $"Opened local Picture document · revision {_savedRevision}"; return true;
    }, token);

    public Task<bool> EditAsync(Func<PictureDocument, PictureDocument> edit, CancellationToken token = default) => OperateAsync(async () =>
    {
        Apply(edit(RequireDocument())); return await SaveCoreAsync(token);
    }, token);

    private void Apply(PictureDocument candidate)
    {
        var before = RequireDocument();
        if (ReferenceEquals(before, candidate)) return;
        if (candidate.DocumentId != before.DocumentId || candidate.Revision != checked(before.Revision + 1)
            || candidate.SourcePath != before.SourcePath || candidate.SourceRevision != before.SourceRevision)
            throw new InvalidDataException("The canonical edit returned an incompatible identity or revision.");
        CheckCapacity(candidate); using var preview = Render(candidate); candidate.Serialize();
        _undo.Add(Encode(before)); if (_undo.Count > 32) _undo.RemoveAt(0);
        _redo.Clear(); _document = candidate; ShowOriginal = false; SetDirty(true); SetDimensionFields(candidate);
    }

    public Task<bool> UndoAsync(bool redo = false, CancellationToken token = default) => OperateAsync(async () =>
    {
        var current = RequireDocument(); var from = redo ? _redo : _undo; var to = redo ? _undo : _redo;
        if (from.Count == 0) throw new PictureBrowserException("HistoryUnavailable", "No history step is available.");
        var restored = WithRevision(Decode(from[^1]), checked(current.Revision + 1));
        if (restored.DocumentId != current.DocumentId || restored.SourcePath != current.SourcePath || restored.SourceRevision != current.SourceRevision)
            throw new InvalidDataException("History references a different source or document.");
        using var preview = Render(restored);
        to.Add(Encode(current)); if (to.Count > 32) to.RemoveAt(0); from.RemoveAt(from.Count - 1);
        _document = restored; ShowOriginal = false; SetDimensionFields(restored); SetDirty(true); return await SaveCoreAsync(token);
    }, token);

    public Task<bool> SaveAsync(CancellationToken token = default) => OperateAsync(() => SaveCoreAsync(token), token);
    private Task<bool> SaveBeforeLeavingAsync(CancellationToken token)
    {
        if (HasPendingInput) throw new PictureBrowserException("UnsavedInput", "Apply or restore typed crop/resize values before replacing or closing this document.");
        return IsDirty ? SaveCoreAsync(token) : Task.FromResult(true);
    }
    private async Task<bool> SaveCoreAsync(CancellationToken token)
    {
        var document = RequireDocument();
        if (!IsDirty) { Status = $"Saved in this browser · revision {_savedRevision}"; return true; }
        CheckSourceIntegrity(document);
        var bundle = new LocalBundle(document.DocumentId.ToString(), _sourceName, document.Revision, Encode(document), _sourceBase64, _undo.ToArray(), _redo.ToArray());
        var receipt = await CallAsync("commit", new { bundle, expectedRevision = _savedRevision }, token);
        if (receipt.GetProperty("revision").GetInt64() != document.Revision) throw new InvalidDataException("Invalid save receipt.");
        if (_disposed) return true;
        _savedRevision = document.Revision; _lastId = document.DocumentId.ToString(); SetDirty(false);
        Status = $"Saved in this browser · revision {_savedRevision}";
        try { await RefreshAsync(); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"PicturePostCommitListFailed: {error.GetType().Name}");
            if (!_disposed) Status += "; the document list did not refresh. The edit was saved.";
        }
        return true;
    }

    public Task<bool> CloseAsync(CancellationToken token = default) => OperateAsync(async () =>
    {
        if (!await SaveBeforeLeavingAsync(token)) return false;
        CheckAlive(); _document = null; _sourceBase64 = _sourceName = ""; _savedRevision = -1;
        _undo.Clear(); _redo.Clear(); ShowOriginal = false; SetDirty(false);
        Status = "Closed. The saved editable document remains in this browser."; return true;
    }, token);

    public Task<bool> ExportAsync(PictureMetadataExportMode mode, CancellationToken token = default) => OperateAsync(async () =>
    {
        var document = RequireDocument(); var path = Path.Combine(SourceDirectory, $"export-{Guid.NewGuid():N}.png");
        try
        {
            token.ThrowIfCancellationRequested(); CheckSourceIntegrity(document);
            try { _engine.ExportPng(document, path, mode); }
            catch (NotSupportedException error) { throw new PictureBrowserException("ExportUnsupportedFeature", error.Message); }
            token.ThrowIfCancellationRequested();
            await CallAsync("download", new { name = "Picture-" + document.DocumentId.ToString("N") + ".png", base64 = Convert.ToBase64String(File.ReadAllBytes(path)) }, token);
            CheckAlive(); Status = $"PNG download started · metadata {mode}. Editable state and original bytes are unchanged."; return true;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }, token);

    /// <summary>Returns an owned real native bitmap; the presentation must dispose it.</summary>
    public Bitmap? RenderPreview()
    {
        if (_disposed || _document is null) return null;
        if (_presentationAttached) CheckSourceIntegrity(_document);
        if (!_presentationAttached)
        {
            using var input = new MemoryStream(RetainedOriginal(_document), writable: false);
            var source = new Bitmap(input);
            if (ShowOriginal) return source;
            using (source) return PictureCropService.Render(source, _document);
        }
        if (ShowOriginal) return new Bitmap(_document.SourcePath!);
        return Render(_document);
    }
    private Bitmap Render(PictureDocument document)
    {
        CheckCapacity(document);
        if (!_presentationAttached)
        {
            using var input = new MemoryStream(RetainedOriginal(document), writable: false);
            using var stagedSource = new Bitmap(input);
            return PictureCropService.Render(stagedSource, document);
        }
        CheckSourceIntegrity(document);
        using var source = new Bitmap(document.SourcePath!);
        return PictureCropService.Render(source, document);
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = null; if (_disposed) return false;
        if (_fields.TryGetValue(path, out var field)) { value = field; return true; }
        value = path switch
        {
            "Status" => Status + (ErrorCode is null ? "" : " · " + ErrorCode) + (PresentationWarning is null ? "" : " · " + PresentationWarning),
            "Identity" => _document is null ? "No document open" : $"Document {_document.DocumentId} · revision {_document.Revision} · {_document.CanvasWidth} × {_document.CanvasHeight} · {_document.Operations.Count} operations · original {_sourceName}",
            "CanEdit" => _presentationAttached && !IsBusy && _document is not null, "NotBusy" => _presentationAttached && !IsBusy,
            "CanUndo" => _presentationAttached && !IsBusy && _undo.Count > 0, "CanRedo" => _presentationAttached && !IsBusy && _redo.Count > 0,
            "ComparisonLabel" => ShowOriginal ? "Show current edits" : "Compare original",
            "Documents" => _documents.Select(x => new Dictionary<string, object?> { ["ID"] = x.DocumentId, ["Label"] = $"{x.Name} · revision {x.Revision}" }).ToArray(),
            _ => null
        };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || !_presentationAttached || IsBusy || !_fields.ContainsKey(path)) return false;
        var next = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (_fields[path] == next) return true;
        _fields[path] = next;
        if (next == _appliedFields[path]) _pendingInput.Remove(path); else _pendingInput.Add(path);
        Notify(); return true;
    }
    public bool? IsActionAvailable(string command) => new[] { "Import", "Refresh", "OpenDocument", "OpenDocumentRow", "Reopen", "Undo", "Redo",
        "Crop", "Rotate", "FlipHorizontal", "FlipVertical", "Resize", "Save", "Close", "Compare", "ExportPreserve", "ExportLocation", "ExportAll" }.Contains(command);
    public bool CanExecute(string command, object? parameter)
    {
        if (_disposed || !_presentationAttached || IsBusy) return false;
        return command switch
        {
            "Import" or "Refresh" or "OpenDocument" => true, "Reopen" => _lastId.Length > 0,
            "Undo" => _document is not null && _undo.Count > 0, "Redo" => _document is not null && _redo.Count > 0,
            "Crop" or "Rotate" or "FlipHorizontal" or "FlipVertical" or "Resize" or "Save" or "Close" or "Compare"
                or "ExportPreserve" or "ExportLocation" or "ExportAll" => _document is not null,
            _ => false
        };
    }
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case "Crop": await EditInputAsync(document => document.Crop(Number("CropX"), Number("CropY"), Number("CropWidth"), Number("CropHeight")),
                ["CropX", "CropY", "CropWidth", "CropHeight"], cancellationToken); return;
            case "Resize": await EditInputAsync(document => document.Resize(Number("ResizeWidth"), Number("ResizeHeight")),
                ["ResizeWidth", "ResizeHeight"], cancellationToken); return;
            case "Import": await ImportAsync(cancellationToken); return;
            case "OpenDocument": await OpenAsync(Convert.ToString(parameter, CultureInfo.InvariantCulture) ?? "", cancellationToken); return;
            case "Reopen": await OpenAsync(_lastId, cancellationToken); return;
            case "Save": await SaveAsync(cancellationToken); return;
            case "Close": await CloseAsync(cancellationToken); return;
            case "Undo": await UndoAsync(false, cancellationToken); return;
            case "Redo": await UndoAsync(true, cancellationToken); return;
            case "ExportPreserve": await ExportAsync(PictureMetadataExportMode.Preserve, cancellationToken); return;
            case "ExportLocation": await ExportAsync(PictureMetadataExportMode.RemoveLocation, cancellationToken); return;
            case "ExportAll": await ExportAsync(PictureMetadataExportMode.RemoveAll, cancellationToken); return;
            case "Compare": await OperateAsync(() => { ShowOriginal = !ShowOriginal; Status = ShowOriginal ? "Original source preview. Edits remain intact." : "Current canonical edit preview."; return Task.FromResult(true); }, cancellationToken); return;
            case "Refresh": await OperateAsync(async () => { await RefreshAsync(cancellationToken); return true; }, cancellationToken); return;
        }
        await EditAsync(document => command switch
        {
            "Crop" => document.Crop(Number("CropX"), Number("CropY"), Number("CropWidth"), Number("CropHeight")),
            "Rotate" => document.Rotate(), "FlipHorizontal" => document.Flip(true), "FlipVertical" => document.Flip(false),
            "Resize" => document.Resize(Number("ResizeWidth"), Number("ResizeHeight")),
            _ => throw new PictureBrowserException("CapabilityUnavailable", "This Picture action is not connected.")
        }, cancellationToken);
    }

    private async Task<bool> OperateAsync(Func<Task<bool>> operation, CancellationToken token)
    {
        if (!await _operations.WaitAsync(0, token)) return false;
        try
        {
            CheckAlive();
            if (!_presentationAttached && !_preparingPresentation) throw new PictureBrowserException("PresentationNotAdmitted", "This prepared Picture view has not been admitted.");
            IsBusy = true; ErrorCode = null; Notify(); return await operation();
        }
        catch (OperationCanceledException) { if (!_disposed) { ErrorCode = "OperationCancelled"; Status = "Operation cancelled. Prior saved state is intact."; } return false; }
        catch (PictureBrowserException error) { if (!_disposed) { ErrorCode = error.Code; Status = error.Message; } return false; }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or NotSupportedException or JsonException or OverflowException or FormatException or UnauthorizedAccessException)
        {
            if (!_disposed)
            {
                ErrorCode = error switch { FileNotFoundException => "LinkedAssetUnavailable", UnauthorizedAccessException => "PermissionDenied",
                    NotSupportedException => "UnsupportedCodecFeature", IOException => "RenderFailed", _ => "InvalidArgument" };
                Status = "The operation failed. Prior saved state is intact. " + error.Message;
            }
            return false;
        }
        finally { IsBusy = false; try { Notify(); } finally { _operations.Release(); } }
    }
    private async Task<JsonElement> CallAsync(string action, object arguments, CancellationToken token = default)
    {
        using var response = JsonDocument.Parse(await _media.InvokeAsync(action, JsonSerializer.Serialize(arguments, Json), token));
        if (!response.RootElement.GetProperty("ok").GetBoolean()) throw new PictureBrowserException(response.RootElement.GetProperty("code").GetString()!, response.RootElement.GetProperty("message").GetString()!);
        return response.RootElement.GetProperty("value").Clone();
    }
    private PictureDocument RequireDocument() => _document ?? throw new PictureBrowserException("DocumentNotFound", "Open a local document first.");
    private int Number(string name) => int.Parse(_fields[name], CultureInfo.InvariantCulture);
    private void SetDimensionFields(PictureDocument document)
    {
        foreach (var (field, dimension) in new[] { ("CropWidth", document.CanvasWidth), ("ResizeWidth", document.CanvasWidth), ("CropHeight", document.CanvasHeight), ("ResizeHeight", document.CanvasHeight) })
        {
            var text = dimension.ToString(CultureInfo.InvariantCulture); _appliedFields[field] = text;
            if (!_pendingInput.Contains(field)) _fields[field] = text;
        }
    }
    private static string Encode(PictureDocument document) => Encoding.UTF8.GetString(document.Serialize());
    private static PictureDocument Decode(string json) => PictureDocument.Deserialize(Encoding.UTF8.GetBytes(json));
    private static PictureDocument WithRevision(PictureDocument document, long revision) => new()
    {
        DocumentId = document.DocumentId, SchemaVersion = document.SchemaVersion, DisplayName = document.DisplayName,
        FileId = document.FileId, SourcePath = document.SourcePath, SourceRevision = document.SourceRevision,
        CanvasWidth = document.CanvasWidth, CanvasHeight = document.CanvasHeight, Operations = document.Operations, Revision = revision
    };
    private static string SourceDirectory => Path.Combine(Path.GetTempPath(), "9to1-picture-local", "sources");
    private static void CheckLocalSource(PictureDocument document)
    {
        var path = document.SourcePath;
        if (document.FileId is not null || path is null || Path.GetFullPath(path) != path || Path.GetDirectoryName(path) != SourceDirectory
            || Path.GetExtension(path) != ".png" || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _))
            throw new InvalidDataException("This local document has an unavailable source identity.");
    }
    private static void CheckSourceIntegrity(PictureDocument document)
    {
        CheckLocalSource(document);
        using var stream = File.OpenRead(document.SourcePath!);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), document.SourceRevision, StringComparison.Ordinal))
            throw new PictureBrowserException("SourceChanged", "The original PNG source changed. Reopen the intact saved copy before editing.");
    }
    private static void CheckCapacity(PictureDocument document)
    {
        if ((long)document.CanvasWidth * document.CanvasHeight > 4_000_000 || document.Operations.Count > 128)
            throw new PictureBrowserException("CapacityExceeded", "This foreground adapter previews up to 4 million pixels and 128 operations. Your prior saved document is intact.");
    }
    // Capability guard only; Avalonia/Skia owns all image decode/rendering. Never silently flatten animation or discard an unsupported profile.
    private static void CheckPng(byte[] bytes)
    {
        if (bytes.Length > 8 * 1024 * 1024) throw new PictureBrowserException("CapacityExceeded", "The PNG exceeds this adapter's 8 MiB source limit.");
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new PictureBrowserException("UnsupportedFormat", "This surface currently imports static PNG only.");
        var offset = 8; var srgb = false; var chromatic = false;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > bytes.Length - offset - 12) throw new InvalidDataException("The PNG chunk structure is invalid.");
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (type == "IHDR")
            {
                if (length != 13 || bytes[offset + 16] != 8) throw new PictureBrowserException("UnsupportedCodecFeature", "This surface currently supports 8-bit PNG, with alpha preserved.");
                var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8, 4)); var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12, 4));
                if (width == 0 || height == 0 || (ulong)width * height > 4_000_000) throw new PictureBrowserException("CapacityExceeded", "The source exceeds this adapter's 4 million pixel limit.");
            }
            if (type is "acTL" or "fcTL" or "fdAT") throw new PictureBrowserException("UnsupportedCodecFeature", "Animated PNG needs an animation-preserving editor. This surface will not flatten it.");
            if (type == "iCCP") throw new PictureBrowserException("ColourProfileUnsupported", "Embedded ICC profiles require the colour-managed Picture path. This surface will not silently strip the profile.");
            if (type == "gAMA" && (length != 4 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8, 4)) != 45455))
                throw new PictureBrowserException("ColourProfileUnsupported", "This surface currently supports the declared sRGB baseline only.");
            if (type == "sRGB") srgb = true; if (type == "cHRM") chromatic = true;
            offset = checked(offset + (int)length + 12); if (type == "IEND") break;
        }
        if (chromatic && !srgb) throw new PictureBrowserException("ColourProfileUnsupported", "Explicit chromaticities without sRGB need the colour-managed Picture path.");
    }
    private void CheckAlive() { if (_disposed) throw new OperationCanceledException("The Picture session was disposed."); }
    private void SetDirty(bool dirty)
    {
        IsDirty = dirty;
        if (_presentationAttached) try { _media.SetDirty(dirty); } catch (Exception error) { PresentationFailed(error, "The browser leave-page warning could not update."); }
    }
    private void PresentationFailed(Exception error, string message)
    { PresentationWarning = message; Console.Error.WriteLine($"PicturePresentationFailed: {error.GetType().Name}"); }
    private void Notify(bool stateChanged = true)
    {
        if (stateChanged) StateGeneration = checked(StateGeneration + 1);
        foreach (var observer in PropertyChanged?.GetInvocationList().Cast<PropertyChangedEventHandler>() ?? [])
            try { observer(this, new(null)); } catch (Exception error) { PresentationFailed(error, "Preview update failed; saved edits remain durable."); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        var ownedMedia = _presentationAttached; _presentationAttached = false; _pendingInput.Clear();
        _document = null; _sourceBase64 = _sourceName = _lastId = ""; _undo.Clear(); _redo.Clear(); _documents = [];
        _savedRevision = -1; IsDirty = false; ShowOriginal = false;
        if (ownedMedia) try { _media.Release(); } catch (Exception error) { PresentationFailed(error, "Browser device cleanup failed."); throw; }
    }
    private sealed record PickedFile(string Name, string Base64);
    private sealed record LocalSummary(string DocumentId, string Name, long Revision);
    private sealed record LocalBundle(string DocumentId, string Name, long Revision, string DocumentJson, string SourceBase64, string[] Undo, string[] Redo);
}

public sealed class PictureBrowserException(string code, string message) : Exception(message)
{ public string Code { get; } = code; }
