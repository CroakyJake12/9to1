using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Forms;
using HavenOS.Home.Core;

namespace NineToOne.Web.Forms;

/// <summary>Browser presentation lifetime around the unchanged canonical Forms workspace.
/// The host supplies an issuer-created original actor/root session; this adapter supplies no authority.</summary>
public sealed class FormsBrowserSession : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IDisposable, IBrowserCloseParticipant
{
    private readonly CancellationTokenSource _lifetime = new();
    private FormHostSession? _owner;
    private FormsCuiWorkspace? _workspace;
    private readonly Func<bool> _presentationCurrent;
    private Guid? _selected;
    private bool _disposed, _busy, _unknown, _reloadedUnknown, _admitted;
    private object? _titleBaseline;
    private bool _titleDirty;
    private string? _status;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid? FormID => _workspace?.FormID;
    public bool HasUnsavedChanges => !_disposed && (_busy || _unknown || _titleDirty || _workspace?.IsActionAvailable("9to1.Forms.DiscardInspector") == true);

    public FormsBrowserSession(FormHostSession owner, Guid? selected, Func<bool> presentationCurrent,
        Func<FormNativePreview, CancellationToken, Task>? showPreview = null)
    {
        _owner = owner; _selected = selected; _presentationCurrent = presentationCurrent;
        _workspace = new(owner.Publications, owner.Authoring, () => _selected ?? _workspace?.FormID,
            AvailableToOwner, showPreview);
        _workspace.TryGetValue("Title", out _titleBaseline);
        _workspace.PropertyChanged += OwnerChanged;
    }
    private bool PresentationCurrent()
    {
        try { return !_disposed && _presentationCurrent(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Forms presentation freshness failed: {0}", error.GetType().Name); return false; }
    }
    private bool AvailableToOwner(string command) => !_disposed && PresentationCurrent()
        && (_admitted || command == "9to1.Forms.Open")
        && command is not ("9to1.Forms.Publish" or "9to1.Forms.Close" or "9to1.Forms.Respond" or "9to1.Forms.NewResponse")
        && (command != "9to1.Forms.Create" || _workspace?.IsActionAvailable("9to1.Forms.DiscardInspector") != true)
        && (!_unknown || command == "9to1.Forms.Open" || _reloadedUnknown && command == "9to1.Forms.DiscardInspector");

    public async Task InitializeAsync(CancellationToken token)
    {
        await RequireCurrentAsync(token);
        if (_selected is not null) await _workspace!.DispatchAsync("9to1.Forms.Open", null, token);
        await RequireCurrentAsync(token);
        token.ThrowIfCancellationRequested();
        if (_disposed) throw new UnauthorizedAccessException("Forms original context is unavailable.");
    }
    internal void AdmitPresentation() { if (_disposed) throw new ObjectDisposedException(nameof(FormsBrowserSession)); _admitted = true; Changed(); }
    public async Task ValidateAsync(CancellationToken token)
    {
        try
        {
            await RequireCurrentAsync(token);
            if (FormID is { } id)
            {
                var read = await _owner!.Publications.ReadAsync(id, token);
                if (!read.Success) throw new UnauthorizedAccessException("The canonical Forms source is unavailable.");
            }
        }
        catch (UnauthorizedAccessException) { Revoke(); throw; }
    }
    private async Task RequireCurrentAsync(CancellationToken token)
    {
        if (_disposed || !PresentationCurrent() || _owner is not { } owner)
            throw new UnauthorizedAccessException("Forms original context is unavailable.");
        try { await owner.RequireCurrentAsync(token); }
        catch (Exception error) when (error is not OperationCanceledException)
        { throw new UnauthorizedAccessException("The original Forms actor and root could not be confirmed.", error); }
    }
    public bool TryGetValue(string path, out object? value)
    {
        if (!_disposed && !PresentationCurrent()) Dispose();
        if (_workspace is { } workspace)
        {
            if (path == "Status" && _status is not null) { value = _status; return true; }
            return workspace.TryGetValue(path, out value);
        }
        // Empty presentation values remove private bindings; they are not domain results.
        value = path == "Status" ? _status ?? "Forms is unavailable." : path.StartsWith("Can", StringComparison.Ordinal) ? false
            : path.EndsWith("Names", StringComparison.Ordinal) || path.EndsWith("Modes", StringComparison.Ordinal) ? Array.Empty<string>()
            : path.StartsWith("Selected", StringComparison.Ordinal) ? -1 : path is "RegexMatchMode" or "RegexCaseMode" ? 0 : "";
        return true;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || !_admitted || _busy || _unknown || !PresentationCurrent()) return false;
        if (_workspace?.TrySetValue(path, value) != true) return false;
        if (path == "Title") { _workspace.TryGetValue(path, out var current); _titleDirty = !Equals(current, _titleBaseline); }
        return true;
    }
    public bool? IsActionAvailable(string command) => !_disposed && !_busy && AvailableToOwner(command)
        && _workspace?.IsActionAvailable(command) == true;

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null) throw new ArgumentException("Forms commands do not accept an untyped parameter.", nameof(parameter));
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("Forms action is unavailable.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token; var writing = IsWrite(command); _busy = true;
        try
        {
            await RequireCurrentAsync(token); token.ThrowIfCancellationRequested();
            await _workspace!.DispatchAsync(command, parameter, token);
            await RequireCurrentAsync(token); token.ThrowIfCancellationRequested();
            _selected = _workspace.FormID ?? _selected;
            if (command == "9to1.Forms.Create") { _workspace.TryGetValue("Title", out _titleBaseline); _titleDirty = false; }
            if (command == "9to1.Forms.Open" && _unknown)
            {
                _reloadedUnknown = true;
                if (_workspace.IsActionAvailable("9to1.Forms.DiscardInspector") != true) _unknown = false;
            }
            if (command == "9to1.Forms.DiscardInspector" && _reloadedUnknown) _unknown = false;
            _status = _unknown ? UnknownMessage : null;
        }
        catch (UnauthorizedAccessException) { Revoke(); throw; }
        catch (InvalidOperationException error) when (error.Message == "PermissionDenied") { Revoke(); throw; }
        catch (Exception error)
        {
            if (!_disposed && writing && !AuthoritativeRejection(error)) { _unknown = true; _reloadedUnknown = false; }
            if (!_disposed) _status = _unknown ? UnknownMessage : error.Message;
            throw;
        }
        finally { _busy = false; Changed(); }
    }
    private const string UnknownMessage = "The save outcome is unknown. Open saved owner state before another edit; review and explicitly discard any retained inspector draft. An unknown creation requires owner recovery.";
    private static bool IsWrite(string command) => command is not ("9to1.Forms.Open" or "9to1.Forms.Preview"
        or "9to1.Forms.NextPage" or "9to1.Forms.NextField" or "9to1.Forms.DiscardInspector");
    private static bool AuthoritativeRejection(Exception error) => error is ArgumentException or NotSupportedException
        || error is InvalidOperationException && error.Message is "RevisionConflict" or "NotFound" or "AlreadyExists" or "InvalidArgument"
            or "AtomicStoreUnavailable" or "CapabilityUnavailable";
    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(HasUnsavedChanges
            ? new HomeCoreOperationResult<bool>(false, "FormsUnsavedChanges", "Save or explicitly discard the Forms inspector, and create or restore any uncreated title, before closing; an unknown save needs owner recovery.", false)
            : new(true, "Succeeded", "Forms owner has no unsaved inspector changes.", true));
    }
    private void OwnerChanged(object? sender, PropertyChangedEventArgs args) => Changed();
    private void Changed()
    {
        if (PropertyChanged is not { } handlers) return;
        foreach (PropertyChangedEventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, new(null)); }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Forms presentation observer failed: {0}", error.GetType().Name); }
        }
    }
    private void Revoke() { Dispose(); _status = "Forms access was denied. Reopen with the current authorised owner."; Changed(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _admitted = false;
        _titleBaseline = null; _titleDirty = false;
        var workspace = _workspace; _workspace = null; _selected = null;
        if (workspace is not null) workspace.PropertyChanged -= OwnerChanged;
        var owner = _owner; _owner = null; owner?.Dispose();
        try { _lifetime.Cancel(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Forms owner cancellation failed: {0}", error.GetType().Name); }
        finally { _lifetime.Dispose(); }
        _status = "Forms is unavailable.";
    }
}
