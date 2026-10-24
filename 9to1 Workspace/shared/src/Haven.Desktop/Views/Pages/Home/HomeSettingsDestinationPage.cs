using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Only an explicit false current-owner predicate produces this expected denial.
/// Actual Home, storage and caller faults keep their original cause and Task status.</summary>
internal sealed class HomeSettingsAuthorityRefusal(string message) : Exception(message);

/// <summary>The same canonical Home Appearance fragment and native preference owner used by
/// standalone Home. This tab borrows the source/context; it never disposes either or owns a file.</summary>
internal sealed class HomeSettingsDestinationPage : UserControl, ICuiActionDispatcher, ICuiActionAvailability
{
    private readonly NativeHomeThemePreferences _source;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly CuiControlLoader _loader = new();
    private readonly CuiViewModel _bindings = new();
    private readonly DesktopOriginalWorkLifetime _work;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private bool _active, _unavailable, _stopped;
    private int _busyCount;
    private bool IsBusy => _busyCount != 0;
    private long _generation;
    private string? _status;
    internal sealed record AdmissionObservation(Task? Source, Exception Cause, CancellationToken RequestedToken);
    private readonly List<AdmissionObservation> _admissionObservations = [];
    internal IReadOnlyList<AdmissionObservation> AdmissionObservations => _admissionObservations.ToArray();

    internal HomeSettingsDestinationPage(NativeHomeThemePreferences source, Func<CancellationToken, Task> admit)
    {
        Dispatcher.UIThread.VerifyAccess();
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        _work = new(StopAsync, CleanupAsync);
        var document = HavenOS.Home.HomeCuiSurface.LoadDefault().Document;
        var theme = document.Components.Single(component => component.IsThemeScope);
        var fragment = Descendants(theme).Single(component => component.AuthoredId == "home-appearance-settings");
        var scope = new CuiComponent(theme.Type, theme.Name, theme.Classes, theme.Properties, theme.Actions,
            [fragment], theme.Condition, theme.List, theme.Span, theme.DefaultTheme, theme.Repeat,
            theme.ElseChildren, theme.Groups, theme.IsDefinition, theme.PropertyRegion);
        _loader.SetThemePreferenceContext(source.Context);
        _loader.SetBindingContext(_bindings); _loader.SetActionDispatcher(this);
        var loaded = _loader.TryLoad(document with { Components = [scope] });
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The canonical Home Appearance fragment could not be rendered.");
        _loader.WireBindings(loaded.Root); Content = loaded.Root;
        _source.Changed += OnSourceChanged;
        RefreshBindings();
    }

    internal bool IsAlive => !_work.IsRetiring;
    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();
    internal NativeHomeThemePreferences.SettingsWriteOutcome? LastWrite { get; private set; }

    internal Task ActivateAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); _active = true;
        return _work.RunAsync(original => RefreshAsync(original, token), actual => _initialization ??= actual);
    }
    internal void Deactivate()
    { Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation; }

    public bool? IsActionAvailable(string command) => IsAlive && _active && !IsBusy && !_unavailable &&
        command is "ApplyHomeAppearance" or "RefreshHomeAppearance";

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_work.IsRetiring) return ValueTask.CompletedTask; // Already queued presentation acquires no new business work.
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Home Appearance action is unavailable.");
        return new(_work.RunAsync(async original =>
        {
            if (command == "RefreshHomeAppearance") { await RefreshAsync(original, cancellationToken); return; }
            var generation = _generation;
            original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
            cancellationToken.ThrowIfCancellationRequested();
            NativeHomeThemePreferences.SettingsWriteOutcome? write = null;
            _busyCount++;
            try
            {
                RefreshBindings();

                if (!await original.AwaitAsync(IsAdmittedAsync(original.Token)))
                { Deny(original, false); return; }
                if (!original.IsAcceptedPublicationCurrent) return;
                cancellationToken.ThrowIfCancellationRequested();
                write = LastWrite = _source.CreateSettingsWriteOutcome();
                // The preference owner retains its actual business lifetime. A tab's private
                // retirement token is not permission to cancel a global atomic preference write.
                var receipt = await original.AwaitAsync(write.ApplyAsync(parameter));
                if (!original.IsAcceptedPublicationCurrent) return;
                if (!await original.AwaitAsync(IsAdmittedAsync(original.Token)))
                { Deny(original, receipt.Succeeded); return; }
                original.RunAcceptedPublicationCallback(() =>
                {
                    _status = receipt.Succeeded ? "Appearance saved." : receipt.Diagnostic ??
                        "Appearance changed. Refresh before choosing again.";
                    RefreshBindings();
                });
            }
            catch (Exception cause)
            {
                if (write?.RetainPostCommitFailure(null, cause) == true)
                {
                    // AwaitAsync already retains an actual failed source. Preserve the confirmed
                    // save outcome independently; no UI observer can replay Set or invent failure.
                    original.Retain(cause);
                    Deny(original, true);
                    return;
                }
                if (original.IsAcceptedPublicationCurrent) Deny(original, false);
                throw;
            }
            finally
            {
                _busyCount--;
                if (original.IsAcceptedPublicationCurrent)
                    try { original.RunAcceptedPublicationCallback(RefreshBindings); }
                    catch (Exception cause)
                    {
                        if (write?.RetainPostCommitFailure(null, cause) != true) throw;
                        original.Retain(cause); _unavailable = true;
                        _status = NativeHomeThemePreferences.SettingsWriteOutcome.SavedObservationDiagnostic;
                    }
            }
        }));
    }

    private async Task<bool> IsAdmittedAsync(CancellationToken token)
    {
        // Only the owning producer's explicit denial is a normal unavailable result. Real
        // source/caller cancellation and IO exceptions keep the SAME original failed task.
        Task actual;
        try { actual = _admit(token) ?? throw new InvalidOperationException("The original Home Settings admission returned no Task."); }
        catch (OperationCanceledException cause)
        {
            _admissionObservations.Add(new(null, cause, token));
            throw new AggregateException("The Home Settings authority callback faulted before returning its source.", cause);
        }
        try { await actual; return true; }
        catch (Exception cause)
        {
            // Capture the raw terminal payload once: Task.Exception need not return the same
            // wrapper on repeated reads. Only a sole actual owner refusal is a quiet denial.
            var terminal = actual.IsFaulted ? actual.Exception : null;
            var failures = terminal?.InnerExceptions;
            if (cause is HomeSettingsAuthorityRefusal && failures is { Count: 1 } &&
                failures[0] is HomeSettingsAuthorityRefusal) return false;
            // Await exposes only one cause. A refusal sharing its raw Task with an I/O or
            // cancellation failure must preserve the entire terminal payload.
            var observed = failures is { Count: > 1 } ? terminal! : cause;
            _admissionObservations.Add(new(actual, observed, token));
            if (actual.IsFaulted && (cause is OperationCanceledException || failures is { Count: > 1 }))
                throw new AggregateException("The original Home Settings authority source faulted.", failures!);
            throw;
        }
    }

    private async Task RefreshAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        var generation = ++_generation;
        original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
        caller.ThrowIfCancellationRequested();
        _busyCount++;
        try
        {
            RefreshBindings();

            if (!await original.AwaitAsync(IsAdmittedAsync(original.Token))) { Deny(original, false); return; }
            if (!original.IsAcceptedPublicationCurrent) return;
            // Borrow, do not repoint or dispose, the SAME live source/context used by Home chrome.
            await original.AwaitAsync(_source.RefreshAsync());
            if (!original.IsAcceptedPublicationCurrent) return;
            caller.ThrowIfCancellationRequested();
            if (!await original.AwaitAsync(IsAdmittedAsync(original.Token))) { Deny(original, false); return; }
            original.RunAcceptedPublicationCallback(() => { _unavailable = false; _status = _source.Diagnostic; });
        }
        catch
        {
            if (original.IsAcceptedPublicationCurrent) Deny(original, false);
            throw;
        }
        finally
        {
            _busyCount--;
            if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(RefreshBindings);
        }
    }

    private void Deny(DesktopOriginalWorkLifetime.Original original, bool saved)
    {
        if (!original.IsAcceptedPublicationCurrent) return;
        try
        {
            original.RunAcceptedPublicationCallback(() =>
            {
                _unavailable = true;
                _status = saved ? NativeHomeThemePreferences.SettingsWriteOutcome.SavedObservationDiagnostic
                    : "Appearance is unavailable. Reopen Home for the current profile.";
                RefreshBindings();
            });
        }
        catch (Exception cause) when (saved)
        {
            LastWrite?.RetainPostCommitFailure(null, cause);
            original.Retain(cause); // The confirmed save remains distinguishable from its broken observer.
        }
    }
    private void RefreshBindings()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_active && !_unavailable)
            foreach (var (key, value) in _source.SettingsBindings()) _bindings.Set(key, value);
        else
        {
            _bindings.Set("HomeAppearanceScope", "Applies to this device profile.");
            _bindings.Set("HomeThemeChoices", Array.Empty<object>());
            _bindings.Set("HomeAppearanceChoices", Array.Empty<object>());
            _bindings.Set("HomeAccessibilityChoices", Array.Empty<object>());
        }
        var available = _active && IsAlive && !IsBusy && !_unavailable;
        _bindings.Set("HomeAppearanceAvailable", available);
        _bindings.Set("HomeAppearanceCanRefresh", available);
        if (_status is not null) _bindings.Set("HomeAppearanceStatus", _status);
        _loader.RefreshBindings();
    }

    private void OnSourceChanged(object? sender, EventArgs args)
    {
        if (_work.IsRetiring) return;
        Task actual;
        try
        {
            actual = _work.RunAsync(async original =>
            {
                var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(async () =>
                {
                    if (!_active || IsBusy || _unavailable) return;
                    var generation = _generation;
                    original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
                    if (!await original.AwaitAsync(IsAdmittedAsync(original.Token))) { Deny(original, false); return; }
                    if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(RefreshBindings);
                }).GetTask();
                var callback = await original.AwaitAsync(dispatch).ConfigureAwait(false);
                await original.AwaitAsync(callback).ConfigureAwait(false);
            });
        }
        catch (ObjectDisposedException) when (_work.IsRetiring) { return; }
        ObserveNotification(actual);
    }
    private static async void ObserveNotification(Task actual)
    {
        try { await actual; }
        catch (Exception cause) { System.Diagnostics.Trace.TraceError("Retained Home Appearance notification failed: {0}", cause); }
    }
    internal Task<bool> TryPrepareToCloseAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); _work.DemandExternalClose(); token.ThrowIfCancellationRequested();
        if (_preparation is not null && !_preparation.IsCompletedSuccessfully) return _preparation;
        return _preparation = _work.RunAsync(_ => Task.FromResult(true));
    }
    internal Task CloseAndDrainAsync() => _work.CloseAndDrainAsync();
    private async Task StopAsync()
    {
        Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation;
        _source.Changed -= OnSourceChanged;
        // Seal page admission first. Let admitted CUI actions acknowledge their same global
        // writes before disposing this loader's caller token. The shared source remains live.
        var failures = new List<Exception>();
        async Task Join(Task actual)
        {
            try { await actual; }
            catch (Exception cause)
            {
                foreach (var error in actual.Exception?.InnerExceptions ?? new[] { cause }.AsEnumerable())
                    if (!failures.Any(known => ReferenceEquals(known, error))) failures.Add(error);
            }
        }
        // A failed action is still a terminal acknowledgement. Join it, retain its causes,
        // then join the loader's actual disposal/late-dispatch receipts as the production host does.
        await Join(_loader.WhenActionsIdleAsync());
        var disposed = false;
        try { _loader.Dispose(); disposed = true; } catch (Exception cause) { failures.Add(cause); }
        await Join(_loader.WhenActionsIdleAsync());
        _stopped = disposed;
        if (failures.Count != 0) throw new AggregateException("Original Home Settings actions or loader disposal failed.", failures);
    }
    private Task CleanupAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_stopped) throw new InvalidOperationException("The original Settings actions must settle before detachment.");
        Content = null; return Task.CompletedTask;
    }
    private static IEnumerable<CuiComponent> Descendants(CuiComponent node) =>
        new[] { node }.Concat(node.Children.SelectMany(Descendants)).Concat(node.ElseChildren.SelectMany(Descendants));
}
