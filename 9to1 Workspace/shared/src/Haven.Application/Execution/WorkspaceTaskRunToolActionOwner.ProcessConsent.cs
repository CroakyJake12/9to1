namespace Haven.Application;

public sealed partial class WorkspaceTaskRunEffectAuthority
{
    private readonly Func<IWorkspaceOriginalProcessStartConsentSource>? _processConsents;
    internal sealed partial class Fence : IWorkspaceOriginalProcessStartEntryReleaseFence
    {
        private IWorkspaceOriginalProcessStartConsentSource? _consentSource;
        private IWorkspaceOriginalProcessStartConsent? _consent;
        private IWorkspaceOriginalProcessStartEntry? _startEntry;
        private Task<IWorkspaceOriginalProcessStartEntry>? _startEntryAcquisition;
        private Task? _startEntryRelease;
        private Task? _consentClose;
        private string? _startTarget;
        private string? _startSha;
        private readonly CloudflareOriginalTaskLedger _consentStages = new();
        private readonly CloudflareOriginalTaskLedger _entryClosing = new();
        private readonly CloudflareOriginalTaskLedger _consentClosing = new();
        internal bool HasOriginalProcessConsent { get { lock (_sync) return _consent is not null; } }
        internal void BindOriginalProcessStartConsent(IWorkspaceOriginalProcessStartConsent consent)
        {
            ArgumentNullException.ThrowIfNull(consent);
            var source = Issuer._processConsents?.Invoke()
                ?? throw new InvalidOperationException("DEV_EXECUTION_SETUP_REQUIRED: genuine configured Home execution consent source is unavailable.");
            ITaskRunToolActionPreparation preparation;
            lock (_sync) preparation = _preparation ?? throw new UnauthorizedAccessException("Actual private original preparation is unavailable.");
            if (OriginalCall.Name is not ("run_command" or "run_tests") || !source.IsIssuedOriginalConsent(consent, preparation))
                throw new UnauthorizedAccessException("SAME actual Home consent for this exact original command preparation is required.");
            lock (_sync)
            {
                if (_closed || _pins != 0 || _startEntryAcquisition is not null || _consent is not null)
                    throw new UnauthorizedAccessException("Original process consent cannot be replaced or attached after dispatch admission.");
                _consentSource = source; _consent = consent; _consentStages.BindOriginalOwner(this);
                _entryClosing.BindOriginalOwner(this); _consentClosing.BindOriginalOwner(this);
            }
        }
        private async Task AcquireOriginalProcessStartEntryAsync(CancellationToken token)
        {
            Task<IWorkspaceOriginalProcessStartEntry>? acquisition; TaskCompletionSource? begin = null;
            lock (_sync)
            {
                if (_consent is null) return; // Existing ordinary/free-local invocation remains unchanged.
                if (_closed) throw new UnauthorizedAccessException("The original command fence retired.");
                if (_startEntryAcquisition is null)
                {
                    begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _startEntryAcquisition = AcquireEntryPublishedAsync(begin.Task, token);
                }
                acquisition = _startEntryAcquisition;
            }
            begin?.SetResult(); await acquisition.ConfigureAwait(false);
        }
        private async Task<IWorkspaceOriginalProcessStartEntry> AcquireEntryPublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            return await _consentStages.RunToOriginalSettlementAsync<IWorkspaceOriginalProcessStartEntry>(async () =>
            {
                IWorkspaceOriginalProcessStartConsentSource source; IWorkspaceOriginalProcessStartConsent consent; ITaskRunToolActionPreparation preparation;
                lock (_sync)
                {
                    if (_closed || _consentSource is null || _consent is null || _preparation is null)
                        throw new UnauthorizedAccessException("The genuine original Home consent binding retired.");
                    source = _consentSource; consent = _consent; preparation = _preparation;
                }
                if (!ReferenceEquals(Issuer._processConsents?.Invoke(), source) || !source.IsIssuedOriginalConsent(consent, preparation))
                    throw new UnauthorizedAccessException("The actual configured Home execution issuer changed.");
                await _consentStages.AwaitAsync(_consentStages.Invoke(() => source.ValidateOriginalConsentAsync(consent, preparation, token))).ConfigureAwait(false);
                var entry = await _consentStages.CaptureOriginalAcquisitionAsync(() => source.EnterOriginalProcessStartAsync(consent, preparation, token),
                    actual => { lock (_sync) _startEntry = actual; }).ConfigureAwait(false);
                if (!source.IsIssuedOriginalEntry(consent, preparation, entry))
                    throw new UnauthorizedAccessException("The private held execution entry does not belong to the SAME consent/preparation.");
                lock (_sync) if (_closed) throw new UnauthorizedAccessException("Original process fence retired after entry acquisition.");
                return entry;
            }).ConfigureAwait(false);
        }
        private void DemandOriginalProcessStartConsent(string root, string target, string sha)
        {
            if (_consent is null) return;
            if (_consentSource is null || _preparation is null || _startEntry is null || _startEntryAcquisition?.IsCompletedSuccessfully != true
                || !_consentSource.IsIssuedOriginalEntry(_consent, _preparation, _startEntry) || _consentStages.OriginalErrors.Count != 0)
                throw new UnauthorizedAccessException("SAME successful held Home process-start entry is required at the finite native boundary.");
            _startEntry.DemandOriginalProcessStart(root, target, sha);
        }
        private T RunOriginalProcessStartConsent<T>(string root, string target, string sha, Func<T> body)
        {
            if (_consent is null) return body();
            DemandOriginalProcessStartConsent(root, target, sha); _startTarget = target; _startSha = sha;
            return _startEntry!.RunOriginalProcessStart(root, target, sha, body);
        }
        public void DemandExternalOriginalProcessStartEntryJoin()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            _consentSource?.DemandExternalOriginalProcessStartConsentJoin();
            _startEntry?.DemandExternalOriginalProcessStartEntryJoin();
        }
        public Task ReleaseOriginalProcessStartEntryAsync(string root, string target, string sha)
        {
            DemandExternalOriginalProcessStartEntryJoin();
            lock (_sync)
            {
                if (root != CanonicalWorkspaceRoot || string.IsNullOrWhiteSpace(target) || sha.Length != 64 || !sha.All(Uri.IsHexDigit)
                    || _startTarget is not null && (_startTarget != target || _startSha != sha))
                    throw new UnauthorizedAccessException("Original process cleanup arguments do not match the SAME native start.");
            }
            return CloseOriginalStartEntryAsync();
        }
        private Task CloseOriginalStartEntryAsync()
        {
            DemandExternalOriginalProcessStartEntryJoin(); Task actual; TaskCompletionSource begin;
            lock (_sync)
            {
                if (_startEntryRelease is not null) return _startEntryRelease;
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously); actual = ReleaseEntryPublishedAsync(begin.Task); _startEntryRelease = actual;
            }
            begin.SetResult(); return actual;
        }
        private async Task ReleaseEntryPublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            Task<IWorkspaceOriginalProcessStartEntry>? acquisition; lock (_sync) acquisition = _startEntryAcquisition;
            if (acquisition is not null) try { await _entryClosing.AwaitAsync(acquisition).ConfigureAwait(false); } catch (Exception cause) { _entryClosing.Retain(cause); }
            if (_startEntry is { } entry)
                try { await _entryClosing.ObserveOriginalCloseAsync(entry.DisposeAsync).ConfigureAwait(false); } catch (Exception cause) { _entryClosing.Retain(cause); }
            await _consentStages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in _consentStages.OriginalErrors) _entryClosing.Retain(cause);
            await _entryClosing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_entryClosing.OriginalErrors.Count != 0) throw new AggregateException("Original execution entry acquisition/cleanup failed.", _entryClosing.OriginalErrors);
        }
        internal Task CloseOriginalProcessConsentAsync()
        {
            DemandExternalOriginalProcessStartEntryJoin(); Task actual; TaskCompletionSource begin;
            lock (_sync)
            {
                if (_consentClose is not null) return _consentClose;
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously); actual = CloseConsentPublishedAsync(begin.Task); _consentClose = actual;
            }
            begin.SetResult(); return actual;
        }
        private async Task CloseConsentPublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            // Calling the private child release below is a local request, then actual join;
            // it must not recursively apply this encompassing driver's external-join guard.
            Task release; TaskCompletionSource? releaseBegin = null;
            lock (_sync)
            {
                if (_startEntryRelease is null)
                { releaseBegin = new(TaskCreationOptions.RunContinuationsAsynchronously); _startEntryRelease = ReleaseEntryPublishedAsync(releaseBegin.Task); }
                release = _startEntryRelease;
            }
            releaseBegin?.SetResult();
            try { await _consentClosing.AwaitAsync(release).ConfigureAwait(false); } catch (Exception cause) { _consentClosing.Retain(cause); }
            if (_consent is { } consent)
                try { await _consentClosing.ObserveOriginalCloseAsync(consent.DisposeAsync).ConfigureAwait(false); } catch (Exception cause) { _consentClosing.Retain(cause); }
            await _consentClosing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_consentClosing.OriginalErrors.Count != 0) throw new AggregateException("Original Home process consent/entry cleanup failed.", _consentClosing.OriginalErrors);
        }
    }
}

public sealed partial class WorkspaceTaskRunToolActionOwner : IWorkspaceOriginalProcessStartConsentBindingOwner
{
    public void BindOriginalProcessStartConsent(ITaskRunToolActionPreparation preparation, IWorkspaceOriginalProcessStartConsent consent)
    {
        if (preparation is not Preparation original || !IsIssued(original) || original.OriginalExecution is not null || original.Fence is null
            || original.OriginalCall.Name is not ("run_command" or "run_tests"))
            throw new UnauthorizedAccessException("SAME private unstarted command preparation is required for additional Home execution consent.");
        if (_service is not IWorkspaceOriginalProcessStartEntryReleaseSource physical)
            throw new InvalidOperationException("DEV_EXECUTION_SETUP_REQUIRED: actual physical process-start entry release producer is unavailable.");
        physical.DemandOriginalProcessStartEntryReleaseSupport(original.Fence);
        original.Fence.BindOriginalProcessStartConsent(consent);
    }
}
