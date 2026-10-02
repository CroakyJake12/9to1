using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Compatibility;

namespace NineToOne.Os.Shell;

/// <summary>Original installed-app/session framework proposal. No installation, approval or process transport.</summary>
public sealed class CompatibilityFrameworkChoiceBindings(CompatibilityRoutingService routing,
    IAuthenticatedResourceActorSource actors, Guid applicationId, long applicationRevision,
    AuthenticatedResourceActor originalActor, CancellationToken hostLifetime)
    : ICuiBindingContext, ICuiRepeatItemBindingContext, ICuiActionDispatcher, INotifyPropertyChanged, IDisposable
{
    private readonly CuiViewModel bindings = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime);
    private sealed class FrameworkChoice(CompatibilityBackendObservation backend) { public CompatibilityBackendObservation Backend { get; } = backend; }
    private CompatibilityRoutingPreview? displayed;
    private readonly object lifetimeGate = new();
    private bool disposed;
    private bool cancellationFinished;
    private int requests;
    public event PropertyChangedEventHandler? PropertyChanged
    { add => bindings.PropertyChanged += value; remove => bindings.PropertyChanged -= value; }
    public bool TryGetValue(string path, out object? value) => bindings.TryGetValue(path, out value);
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = item is FrameworkChoice choice && displayed is not null && bindings.GetOrCreateList<FrameworkChoice>("Frameworks").Any(row => ReferenceEquals(row, choice))
            ? path switch { "Id" => choice.Backend.BackendId, "Label" => choice.Backend.Kind switch { CompatibilityBackendKind.Wine => "Wine", CompatibilityBackendKind.WindowsEnvironment => "Windows environment", _ => "Android runtime" },
                "Details" => choice.Backend.Eligible ? "Available for framework review" : choice.Backend.ExclusionReason,
                "CanChoose" => choice.Backend.Eligible && displayed.Status is CompatibilityRoutingStatus.ReviewRequired or CompatibilityRoutingStatus.PreferredBackendUnavailable or CompatibilityRoutingStatus.RequestedBackendUnavailable,
                _ => null } : null;
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public async Task RefreshAsync(CancellationToken ct = default) => await RequestAsync(null, null, ct);
    private async Task RequestAsync(FrameworkChoice? choice, CompatibilityRoutingPreview? expectedDisplay, CancellationToken ct)
    {
        CancellationToken originalLifetime;
        lock (lifetimeGate) { if (disposed) return; requests++; originalLifetime = lifetime.Token; }
        var entered = false;
        try
        {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct, originalLifetime);
        await gate.WaitAsync(request.Token); entered = true;
        var requestAdmitted = choice is null;
        try
        {
            if (disposed) return;
            if (choice is not null && (!ReferenceEquals(displayed, expectedDisplay) || !bindings.GetOrCreateList<FrameworkChoice>("Frameworks").Any(row => ReferenceEquals(row, choice)) || !choice.Backend.Eligible))
                throw new UnauthorizedAccessException("Choose a framework from the currently displayed original app.");
            requestAdmitted = true;
            var preview = await routing.PreviewForActorAsync(applicationId, applicationRevision, originalActor, choice?.Backend.BackendId, request.Token);
            if (await actors.GetCurrentAsync(request.Token) != originalActor)
                throw new UnauthorizedAccessException("The original Home actor changed.");
            request.Token.ThrowIfCancellationRequested(); if (disposed) return;
            displayed = preview; bindings.Set("Status", preview.Reason); bindings.Set("State", preview.Status.ToString());
            bindings.Set("Proposed", preview.ProposedBackend?.BackendId ?? "No framework selected");
            var rows = bindings.GetOrCreateList<FrameworkChoice>("Frameworks"); rows.Clear();
            foreach (var backend in preview.Backends) rows.Add(new(backend));
        }
        catch (IOException)
        {
            try
            {
                if (await actors.GetCurrentAsync(request.Token) != originalActor)
                    throw new UnauthorizedAccessException("The original Home actor changed while checking frameworks.");
                request.Token.ThrowIfCancellationRequested();
                if (!disposed && requestAdmitted)
                    ClearUnavailable("No current compatibility owner could confirm an eligible framework. Check frameworks again after its runtime becomes available.");
            }
            catch
            {
                if (!disposed && requestAdmitted) ClearUnavailable("The original app or Home session is unavailable. Reopen its current compatibility settings.");
                throw;
            }
        }
        catch
        {
            if (!disposed && requestAdmitted)
                ClearUnavailable("The original app or Home session is unavailable. Reopen its current compatibility settings.");
            throw;
        }
        finally { if (entered) gate.Release(); }
        }
        finally
        {
            lock (lifetimeGate)
            {
                requests--;
                if (disposed && cancellationFinished && requests == 0) lifetime.Dispose();
            }
        }
    }
    private void ClearUnavailable(string message)
    {
        displayed = null; bindings.GetOrCreateList<FrameworkChoice>("Frameworks").Clear();
        bindings.Set("Proposed", "No framework selected"); bindings.Set("State", "Unavailable"); bindings.Set("Status", message);
    }
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken ct = default)
    {
        if (disposed) return;
        var originalDisplay = displayed;
        if (command == "Refresh") await RefreshAsync(ct);
        else if (command == "ChooseFramework" && parameter is FrameworkChoice choice)
            await RequestAsync(choice, originalDisplay, ct);
    }
    public void Dispose()
    {
        lock (lifetimeGate) { if (disposed) return; disposed = true; }
        try { lifetime.Cancel(); }
        finally
        {
            lock (lifetimeGate) { cancellationFinished = true; if (requests == 0) lifetime.Dispose(); }
        }
        displayed = null; bindings.GetOrCreateList<FrameworkChoice>("Frameworks").Clear();
    }
}
