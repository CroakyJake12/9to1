using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

/// <summary>Borrow the SAME Files issuer, Home review owner, and canonical tool owner.
/// This adapter owns no global retirement, approval decision, duplicate store, or actor.</summary>
public sealed class FilesDeveloperOriginalProjectExecutionTrust(
    FilesDeveloperOriginalFolderSetupProducer originalBindings,
    IWorkspaceOriginalProcessStartConsentSource originalConsents,
    ITaskRunToolActionOwner originalToolOwner) : IDeveloperWorkspaceOriginalProjectExecutionTrustService
{
    [ThreadStatic] private static HashSet<FilesDeveloperOriginalProjectExecutionTrust>? _physical;
    public bool IsBoundToOriginalToolOwner(ITaskRunToolActionOwner sameOwner) => ReferenceEquals(originalToolOwner, sameOwner);
    // Existing ID-only clients cannot borrow a review for another project/root.
    public Task<bool> IsTrustedAsync(Guid workspaceId, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(false); }
    public void DemandExternalOriginalExecutionTrustJoin()
    {
        if (_physical?.Contains(this) == true) throw new InvalidOperationException("An actual execution-trust callback cannot join its borrower.");
        originalBindings.DemandExternalOriginalExecutionBindingJoin();
        originalConsents.DemandExternalOriginalProcessStartConsentJoin();
    }
    public Task<IDeveloperWorkspaceOriginalExecutionBinding> ResolveOriginalBindingAsync(
        DeveloperResolvedProject sameProject, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start(scope, retain, sources => sources.Observe(() => originalBindings.ResolveOriginalProjectBindingAsync(
            sameProject, sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)));
    public Task ValidateOriginalBindingAsync(DeveloperResolvedProject sameProject,
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start(scope, retain, async sources =>
        {
            await sources.ObserveVoid(() => originalBindings.ValidateOriginalProjectBindingAsync(sameProject, sameBinding,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            return true;
        });
    public Task<IWorkspaceOriginalProcessStartConsent> AcquireAndBindOriginalConsentAsync(
        DeveloperResolvedProject sameProject, IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IWorkspaceToolActionPreparation samePreparation, TaskExecutionSnapshot current,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start<IWorkspaceOriginalProcessStartConsent>(scope, retain, async sources =>
        {
            if (originalToolOwner is not IWorkspaceOriginalProcessStartConsentBindingOwner binder)
                throw new NotSupportedException("The SAME tool owner lacks the original final process-start consent binding port.");
            await sources.ObserveVoid(() => originalBindings.ValidateOriginalProjectBindingAsync(sameProject, sameBinding,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            sources.Invoke(() =>
            {
                if (!originalBindings.IsIssuedOriginalBinding(sameBinding) ||
                    samePreparation.CanonicalWorkspaceRoot != sameBinding.CanonicalRoot ||
                    samePreparation.OriginalAttempt.Snapshot.TaskId != current.TaskId ||
                    samePreparation.OriginalAttempt.Snapshot.ExecutionId != current.ExecutionId ||
                    samePreparation.OriginalCall.Name is not ("run_command" or "run_tests"))
                    throw new UnauthorizedAccessException("The actual project/root/preparation/Task/Run do not form one original execution request.");
                return true;
            });
            Task<IWorkspaceOriginalProcessStartConsent>? actual = null;
            IWorkspaceOriginalProcessStartConsent? consent = null; var owned = false; var errors = new List<Exception>();
            try
            {
                try { sources.Invoke(() => { actual = originalConsents.AcquireOriginalAsync(sameBinding, samePreparation,
                    current, sources.OriginalSynchronousScope, token); sources.RetainOriginalTask(actual); return true; }); }
                catch (Exception error) { Add(errors, error); }
                if (actual is not null)
                    try { consent = await actual.ConfigureAwait(false); }
                    catch (Exception error) { AddTask(errors, actual, error); }
                if (consent is not null)
                    try { sources.Invoke(() => { owned = originalConsents.IsIssuedOriginalConsent(consent, samePreparation);
                        if (!owned) throw new UnauthorizedAccessException("The returned Home consent is not currently issued for the SAME preparation."); return true; }); }
                    catch (Exception error) { Add(errors, error); }
                if (errors.Count != 0) throw new AggregateException(errors);
                if (consent is null) throw new UnauthorizedAccessException("No actual privately issued Home consent was returned.");
                await sources.ObserveVoid(() => originalConsents.ValidateOriginalConsentAsync(consent, samePreparation, token)).ConfigureAwait(false);
                sources.Invoke(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!originalConsents.IsIssuedOriginalConsent(consent, samePreparation) || !originalBindings.IsIssuedOriginalBinding(sameBinding))
                        throw new UnauthorizedAccessException("The original execution consent/root retired before binding.");
                    binder.BindOriginalProcessStartConsent(samePreparation, consent); return true;
                });
                return consent;
            }
            catch (Exception error) { Add(errors, error); }
            // Only an actual returned product already recognized by its private issuer is
            // locally cleanup-owned. An unrecognized late product remains on that issuer's
            // prepublished whole-close cohort; interface type/IDs never establish ownership.
            if (owned && consent is not null)
            {
                Task? close = null;
                try { OwnScope(() => { close = consent.DisposeAsync().AsTask(); }); }
                catch (Exception error) { Add(errors, error); }
                if (close is not null)
                {
                    try { sources.RetainOriginalTask(close); } catch (Exception error) { Add(errors, error); }
                    try { await close.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, close, error); }
                }
            }
            if (actual?.IsCanceled == true && errors.Count != 0 && errors.All(value => value is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Original prepared execution consent did not settle; no action was registered.", errors);
        });

    private Task<T> Start<T>(Action<Action> parentScope, Action<Task> parentRetain, Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(parentScope); ArgumentNullException.ThrowIfNull(parentRetain);
        var sources = new FilesOriginalReadSourceScope(callback => OwnScope(() => FilesOriginalSourceCallbackScope.Invoke(callback, parentScope)),
            actual => OwnScope(() => FilesOriginalSourceCallbackScope.Invoke(() => parentRetain(actual), parentScope)));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publication = new List<Exception>(); var driver = Drive();
        try { sources.RetainOriginalTask(driver); } catch (Exception error) { Add(publication, error); }
        start.TrySetResult(); return driver;
        async Task<T> Drive()
        {
            await start.Task.ConfigureAwait(false);
            if (publication.Count != 0) throw new AggregateException("The execution-trust driver could not be enrolled.", publication);
            return await sources.Observe(() => body(sources)).ConfigureAwait(false);
        }
    }
    private void OwnScope(Action callback)
    {
        var active = _physical ??= []; var added = active.Add(this);
        try { callback(); } finally { if (added) active.Remove(this); }
    }
    private static void AddTask(List<Exception> errors, Task actual, Exception observed)
    { foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { observed }.AsEnumerable()) Add(errors, cause); }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } group) { foreach (var cause in group.InnerExceptions) Add(errors, cause); }
        else if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error);
    }
}
