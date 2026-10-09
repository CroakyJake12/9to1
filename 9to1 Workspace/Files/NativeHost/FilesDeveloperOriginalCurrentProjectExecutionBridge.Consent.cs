using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalCurrentProjectExecutionBridge
{
    private IWorkspaceOriginalProcessStartConsentSource CaptureConsents()
    {
        var actual = _consents() ?? throw new InvalidOperationException("The SAME configured Home consent owner is unavailable.");
        lock (_gate)
        {
            if (_actualConsents is not null && !ReferenceEquals(actual, _actualConsents))
                throw new UnauthorizedAccessException("The configured Home consent owner changed.");
            _actualConsents = actual;
        }
        return actual;
    }
    public Task<IWorkspaceOriginalProcessStartConsent> AcquireAndBindOriginalConsentAsync(
        DeveloperResolvedProject sameProject, IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IWorkspaceToolActionPreparation samePreparation, TaskExecutionSnapshot current,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start<IWorkspaceOriginalProcessStartConsent>(scope, retain, async (original, sources) =>
        {
            var binding = sources.Invoke(() => RequireBinding(sameBinding));
            sources.Invoke(() =>
            {
                if (!ReferenceEquals(_command.Value, binding.Frame) || sameProject.Reference != binding.Frame.Project)
                    throw new UnauthorizedAccessException("A different Dev invocation cannot borrow the binding's command READ.");
                DemandFrame(binding.Frame, sameProject);
                var action = binding.Frame.Context;
                var attempt = samePreparation.OriginalAttempt.Snapshot;
                if (samePreparation.ActionId != action.ActionId || samePreparation.OriginalAttempt.AttemptId != action.AttemptId ||
                    attempt.TaskId != action.TaskId || attempt.ExecutionId != action.ExecutionId || attempt.ContextId != action.ContextId ||
                    current.TaskId != action.TaskId || current.ExecutionId != action.ExecutionId || current.ContextId != action.ContextId ||
                    current.PersistenceRevision != action.PersistenceRevision || samePreparation.CanonicalWorkspaceRoot != binding.CanonicalRoot ||
                    samePreparation.OriginalCall.Name is not ("run_command" or "run_tests"))
                    throw new UnauthorizedAccessException("The actual preparation/action/attempt/Task/Run differs from the original command READ frame.");
                return true;
            });
            if (_tools is not IWorkspaceOriginalProcessStartConsentBindingOwner binder)
                throw new NotSupportedException("The SAME actual tool owner has no private final process-start consent binder.");
            await RevalidateBody(binding, original, sources, token).ConfigureAwait(false);
            var consents = sources.Invoke(CaptureConsents);
            Task<IWorkspaceOriginalProcessStartConsent>? actual = null;
            IWorkspaceOriginalProcessStartConsent? consent = null;
            var owned = false; var errors = new List<Exception>();
            try
            {
                try
                {
                    sources.Invoke(() =>
                    {
                        actual = consents.AcquireOriginalAsync(binding, samePreparation, current, sources.OriginalSynchronousScope, token);
                        sources.RetainOriginalTask(actual); return true;
                    });
                }
                catch (Exception error) { Add(errors, error); }
                if (actual is not null)
                    try { consent = await actual.ConfigureAwait(false); }
                    catch (Exception error) { AddTask(errors, actual, error); }
                if (consent is not null)
                    try
                    {
                        sources.Invoke(() =>
                        {
                            owned = consents.IsIssuedOriginalConsent(consent, samePreparation);
                            if (!owned) throw new UnauthorizedAccessException("The actual returned Home consent is not currently issued for the SAME preparation.");
                            return true;
                        });
                    }
                    catch (Exception error) { Add(errors, error); }
                Throw(errors);
                if (consent is null) throw new UnauthorizedAccessException("No privately issued genuine Home execution consent was returned.");
                await sources.ObserveVoid(() => consents.ValidateOriginalConsentAsync(consent, samePreparation, token)).ConfigureAwait(false);
                sources.Invoke(() =>
                {
                    token.ThrowIfCancellationRequested(); RequireBinding(binding);
                    if (!consents.IsIssuedOriginalConsent(consent, samePreparation))
                        throw new UnauthorizedAccessException("The Home consent retired before the first canonical action registration.");
                    binder.BindOriginalProcessStartConsent(samePreparation, consent); return true;
                });
                return consent;
            }
            catch (Exception error) { Add(errors, error); }
            // Only a recognized actual product becomes local cleanup-owned. Unrecognized
            // late products remain in the SAME Home source's prepublished whole-close map.
            if (owned && consent is not null)
            {
                Task? close = null;
                try { Scope(() => { close = consent.DisposeAsync().AsTask(); lock (_gate) original.Raw.Add(close); }); }
                catch (Exception error) { Add(errors, error); }
                if (close is not null)
                {
                    try { sources.RetainOriginalTask(close); } catch (Exception error) { Add(errors, error); }
                    try { await close.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, close, error); }
                }
            }
            if (actual?.IsCanceled == true && !Volatile.Read(ref original.CallbackFailed) && errors.All(Canceled))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            Throw(errors); throw new UnauthorizedAccessException("No actual execution consent was bound.");
        });
}
