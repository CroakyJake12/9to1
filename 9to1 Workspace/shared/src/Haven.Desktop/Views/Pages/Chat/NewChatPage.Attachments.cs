using System.Text.Json;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private readonly HashSet<Guid> _pendingAttachmentIds = [];

    private IReadOnlyList<ChatAttachmentChip> BuildAttachmentChips()
    {
        var chips = new List<ChatAttachmentChip>();
        if (_activeAgent is { } agent)
            chips.Add(new ChatAttachmentChip($"agent:{agent.Id:D}", agent.Name, string.IsNullOrWhiteSpace(agent.IconKey) ? "agents" : agent.IconKey));
        chips.AddRange(_activeInstructions.Select(item =>
            new ChatAttachmentChip($"instruction:{item.Id:D}", item.Name, string.IsNullOrWhiteSpace(item.IconKey) ? "prompt" : item.IconKey)));
        if (_messageAttachments is not null)
        {
            chips.AddRange(_persistedAttachments.Select(item =>
                new ChatAttachmentChip($"attachment:{item.Id:D}", item.OriginalName, AttachmentIconForKind(item.Kind))));
        }
        else
        {
            chips.AddRange(_taskAttachments.Files.Select(path =>
                new ChatAttachmentChip("file:" + path, Path.GetFileName(path), AttachmentIconForFile(path))));
        }
        chips.AddRange(_taskAttachments.Capabilities.Select(item =>
            new ChatAttachmentChip($"capability:{item.Id:D}", item.Name, "plugin")));
        chips.AddRange(_taskAttachments.Apps.Select(item =>
            new ChatAttachmentChip($"app:{item.Id:D}", item.Name, string.IsNullOrWhiteSpace(item.IconKey) ? "all-modes" : item.IconKey)));
        return chips;
    }

    private void OnAttachmentRemoveRequested(object? sender, string id)
    {
        if (_originalWork.IsRetiring) return;
        _ = RunChatOriginalAsync(() => OnAttachmentRemoveRequestedOriginalBodyAsync(sender, id));
    }

    private async Task OnAttachmentRemoveRequestedOriginalBodyAsync(object? sender, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var changed = false;
        if (TryReadGuid(id, "attachment:", out var attachmentId) && _messageAttachments is not null)
        {
            try
            {
                await AwaitChatOriginalAsync(_messageAttachments.DeleteAsync(attachmentId, RequireChatOriginal().Token));
                changed = _persistedAttachments.RemoveAll(item => item.Id == attachmentId) > 0;
                _pendingAttachmentIds.Remove(attachmentId);
                if (_attachmentSourcePaths.Remove(attachmentId, out var sourcePath))
                    _taskAttachments.RemoveFile(sourcePath);
                await AwaitChatOriginalAsync(RefreshPersistedAttachmentPromptContextAsync());
                await AwaitChatOriginalAsync(SavePendingAttachmentDraftAsync());
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                RetainHandledOriginalChatCause(exception);
                _scene.SetStatus("The attachment could not be removed: " + exception.Message);
                return;
            }
        }
        else if (id.StartsWith("file:", StringComparison.Ordinal))
        {
            var path = id[5..];
            changed = _taskAttachments.RemoveFile(path);
            if (changed)
            {
                _attachedImages.RemoveAll(item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
                _attachedContext.Remove(path);
            }
        }
        else if (TryReadGuid(id, "capability:", out var capabilityId))
            changed = _taskAttachments.RemoveCapability(capabilityId);
        else if (TryReadGuid(id, "app:", out var appId))
            changed = _taskAttachments.RemoveApp(appId);
        else if (TryReadGuid(id, "instruction:", out var instructionId))
            changed = _activeInstructions.RemoveAll(item => item.Id == instructionId) > 0;
        else if (TryReadGuid(id, "agent:", out var agentId) && _activeAgent?.Id == agentId)
        {
            _activeAgent = null;
            changed = true;
        }

        if (!changed) return;
        RefreshAttachmentStatus();
        RefreshResponseControls();
        _scene.SetStatus("Attachment removed from this chat.");
        FocusComposer();
    }

    private Task LoadPendingPersistedAttachmentsAsync(Conversation conversation) => RunChatNestedOriginalAsync(() => LoadPendingPersistedAttachmentsOriginalBodyAsync(conversation));

    private async Task LoadPendingPersistedAttachmentsOriginalBodyAsync(Conversation conversation)
    {
        _pendingAttachmentIds.Clear();
        _persistedAttachments.Clear();
        _attachmentSourcePaths.Clear();
        if (_conversationProduction is null || conversation.IsTemporary) return;

        try
        {
            // Read the newest durable state: a pending older write must settle first so a stale
            // read can never be restored over newer typed text.
            Task? pendingDraftWrite;
            lock (_originalDraftGate) pendingDraftWrite = _originalDraftChain;
            if (pendingDraftWrite is { IsCompleted: false }) await pendingDraftWrite.ConfigureAwait(false);
            var branch = await AwaitChatOriginalAsync(_conversationProduction.GetCurrentBranchAsync(conversation.Id, RequireChatOriginal().Token))
                         ?? await AwaitChatOriginalAsync(_conversationProduction.EnsureRootBranchAsync(conversation.Id, RequireChatOriginal().Token));
            RememberOriginalDraftBranch(conversation.Id, branch.Id);
            var draft = await AwaitChatOriginalAsync(_conversationProduction.GetDraftAsync(conversation.Id, branch.Id, RequireChatOriginal().Token));
            if (draft is null) return;

            // The SAME canonical draft owns text and pending attachments. Restore the text
            // before the attachment ids are consulted (a text-only draft has no ids), and only
            // when the user has not typed since this read began: a late restore must never
            // overwrite newer typing.
            RestoreOriginalDraftText(draft.Content);
            if (string.IsNullOrWhiteSpace(draft.AttachmentIdsJson)) return;

            Guid[] ids;
            try
            {
                ids = JsonSerializer.Deserialize<Guid[]>(draft.AttachmentIdsJson) ?? [];
            }
            catch (JsonException originalCaughtCause)
            {
                RetainHandledOriginalChatCause(originalCaughtCause);
                ids = [];
            }
            foreach (var id in ids.Where(id => id != Guid.Empty)) _pendingAttachmentIds.Add(id);
            if (_pendingAttachmentIds.Count == 0) return;

            var attachments = await AwaitChatOriginalAsync(_conversationProduction.GetAttachmentsAsync(conversation.Id, messageId: null, RequireChatOriginal().Token));
            _persistedAttachments.AddRange(attachments.Where(item => _pendingAttachmentIds.Contains(item.Id)));
            _pendingAttachmentIds.IntersectWith(_persistedAttachments.Select(item => item.Id));
            await AwaitChatOriginalAsync(RefreshPersistedAttachmentPromptContextAsync());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            RetainHandledOriginalChatCause(exception);
            _scene.SetStatus("Pending attachments could not be restored: " + exception.Message);
        }
    }

    private Task<Guid?> EnsureAttachmentBranchAsync(CancellationToken cancellationToken) => RunChatNestedOriginalAsync(() => EnsureAttachmentBranchOriginalBodyAsync(cancellationToken));

    private async Task<Guid?> EnsureAttachmentBranchOriginalBodyAsync(CancellationToken cancellationToken)
    {
        using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, RequireChatOriginal().Token);
        cancellationToken = originalCancellation.Token;
        if (_conversationProduction is null || _conversation.IsTemporary) return null;
        var branch = await AwaitChatOriginalAsync(_conversationProduction.GetCurrentBranchAsync(_conversation.Id, cancellationToken))
                     ?? await AwaitChatOriginalAsync(_conversationProduction.EnsureRootBranchAsync(_conversation.Id, cancellationToken));
        RememberOriginalDraftBranch(_conversation.Id, branch.Id);
        return branch.Id;
    }

    private Task SavePendingAttachmentDraftAsync(CancellationToken cancellationToken = default) => RunChatNestedOriginalAsync(() => SavePendingAttachmentDraftOriginalBodyAsync(cancellationToken));

    /// <summary>Enrols a current composer+attachment snapshot on the SAME keyed ordering chain
    /// every canonical draft mutation uses. Returns the chain tail, which is the durability
    /// acknowledgement for this exact snapshot.</summary>
    private Task SavePendingAttachmentDraftOriginalBodyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return EnqueueOriginalDraftMutationAsync(CaptureOriginalDraftMutation(OriginalDraftInputOwner, _scene.Instruction.Text, _pendingAttachmentIds));
    }

    // ---- canonical conversation draft ---------------------------------------------------
    // ONE keyed ordering owner for every canonical draft mutation: text autosave, attachment
    // snapshot, transition flush, retirement flush and accepted-send retirement all enqueue
    // exact captured snapshots on one admitted chain and execute strictly in submission order.
    // A late older write can therefore never overwrite or remove newer user data. Mutations are
    // RAW page originals: durable local I/O joined by close, with no presentation publication
    // demand, so a completed write never fails after a target replacement or retirement.
    //
    // OWNERSHIP: unsent composer text belongs to a conversation, not to whichever target the
    // page currently displays. A replacement captures the OUTGOING owner at admission and only
    // commits ownership to the incoming conversation after that outgoing draft is genuinely
    // acknowledged AND the acknowledged text is no longer in the composer, so text typed while
    // a held flush is still pending is never autosaved under the incoming conversation id.
    private readonly object _originalDraftGate = new();
    /// <summary>The branch this page last resolved for each conversation's canonical draft. A
    /// snapshot carries it, so a DELAYED write cannot silently retarget a draft at whatever
    /// branch happens to be current when that write finally executes.</summary>
    private readonly Dictionary<Guid, Guid> _originalDraftBranches = [];
    private long _originalComposerRevision;
    /// <summary>The text the draft machinery last observed in the composer. Instruction
    /// invalidation is NOT a text edit: the same element also raises it for caret, focus, state
    /// and motion updates, so a text-change comparison is the only honest edit signal.</summary>
    private string _originalComposerText = string.Empty;
    /// <summary>The conversation that owns the composer's unsent text for draft purposes.</summary>
    private Conversation? _originalDraftInputOwner;
    private OriginalDraftMutation? _originalDraftQueued;
    private Task? _originalDraftChain;
    private Task? _originalDraftRetirementFlush;
    private bool _suppressOriginalDraftSave;
    /// <summary>True while the admitted send body sits between its own programmatic composer
    /// clear and its final restore/commit decision. That deliberate clear is not a deletion.</summary>
    private volatile bool _originalSendInFlight;
    private long _originalDraftRestoreRevision = -1;
    private Guid _originalDraftRestoreConversationId;

    private Conversation OriginalDraftInputOwner => _originalDraftInputOwner ?? _conversation;

    private sealed record OriginalDraftMutation(Conversation Target, string Content, Guid[] AttachmentIds, Guid? BranchId)
    {
        // This is the destination of THIS mutation's actual canonical write, never a live
        // alias for whichever branch becomes current later. Access is under _originalDraftGate.
        public Guid? ResolvedBranchId { get; private set; } = BranchId;
        public void BindResolvedBranch(Guid branchId) => ResolvedBranchId = branchId;

        public bool HasSameInputAs(OriginalDraftMutation other) =>
            Target.Id == other.Target.Id && string.Equals(Content, other.Content, StringComparison.Ordinal) &&
            AttachmentIds.SequenceEqual(other.AttachmentIds);

        /// <summary>The exact accepted identity of one snapshot: owning conversation, text,
        /// captured attachment ids and captured branch. A later edit, a different attachment set,
        /// a different conversation or a different branch makes it false, so an accepted send can
        /// never retire a newer draft and equal text on a NEW branch never deduplicates onto the
        /// older branch's write. An unresolved destination is not evidence of equal branches;
        /// the owning write binds its snapshot to the branch it actually resolved.</summary>
        public bool SameAs(OriginalDraftMutation other) =>
            HasSameInputAs(other) &&
            ResolvedBranchId is { } branchId && other.ResolvedBranchId == branchId;
    }

    private sealed record OriginalDraftTransition(OriginalDraftMutation Outgoing, string ComposerText, long Revision);

    /// <summary>The exact canonical draft identity one admitted send consumed, captured at the
    /// instant of its programmatic clear.</summary>
    private sealed record OriginalAcceptedSend(Conversation Target, long Revision, OriginalDraftMutation Consumed,
        Task? DraftAcknowledgement);

    private OriginalAcceptedSend CaptureOriginalAcceptedSend(Conversation target, long revision,
        string content, IEnumerable<Guid> attachmentIds)
    {
        lock (_originalDraftGate)
        {
            var consumed = CaptureOriginalDraftMutation(target, content, attachmentIds);
            if (_originalDraftQueued is { } queued && queued.HasSameInputAs(consumed) &&
                (consumed.ResolvedBranchId is null || queued.ResolvedBranchId == consumed.ResolvedBranchId))
            {
                // Share the EXACT pending write's identity and acknowledgement. An unresolved
                // branch is not equality evidence, but this actual input snapshot has an owner
                // whose real branch resolution can be joined before committed retirement.
                return new(target, revision, queued, _originalDraftChain);
            }
            return new(target, revision, consumed, null);
        }
    }

    /// <summary>Captured at the instant of a real edit/transition: the composer text and pending
    /// attachment ids frozen against the exact conversation that owned them, together with the
    /// branch key this page had resolved for that conversation at capture time.</summary>
    private OriginalDraftMutation CaptureOriginalDraftMutation(Conversation target, string content, IEnumerable<Guid> attachmentIds)
    {
        bool known;
        Guid branchId;
        lock (_originalDraftGate) known = _originalDraftBranches.TryGetValue(target.Id, out branchId);
        return new(target, content ?? string.Empty, attachmentIds.OrderBy(id => id).ToArray(), known ? branchId : null);
    }

    private void RememberOriginalDraftBranch(Guid conversationId, Guid branchId)
    {
        if (branchId == Guid.Empty) return;
        lock (_originalDraftGate) _originalDraftBranches[conversationId] = branchId;
    }

    /// <summary>The exact unsent text this page still owns: the composer text plus every input
    /// parked in the in-memory send queue. A queued input has NOT been sent, so it must be kept
    /// in the canonical draft instead of being deleted along with the empty composer.</summary>
    private static string CaptureUnsentDraftText(string? composerText, IReadOnlyList<string> queuedMessages)
    {
        if (queuedMessages.Count == 0) return composerText ?? string.Empty;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(composerText)) parts.Add(composerText);
        foreach (var queued in queuedMessages)
            if (!string.IsNullOrWhiteSpace(queued)) parts.Add(queued);
        return string.Join("\n", parts);
    }

    /// <summary>The outgoing owner's exact unsent snapshot plus the composer revision it was
    /// captured at: the revision decides whether newer typing has since arrived.</summary>
    private OriginalDraftTransition CaptureOriginalDraftTransition()
    {
        var owner = OriginalDraftInputOwner;
        var composerText = _scene.Instruction.Text ?? string.Empty;
        var mutation = CaptureOriginalDraftMutation(owner, CaptureUnsentDraftText(composerText, _scene.QueuedComposerMessages), _pendingAttachmentIds);
        return new(mutation, composerText, Volatile.Read(ref _originalComposerRevision));
    }

    /// <summary>Commits draft-input ownership after a genuine transition acknowledgement. Only
    /// an EMPTY composer (the acknowledged text is gone) moves ownership to the incoming
    /// conversation; text that arrived during the transition keeps its real outgoing owner.</summary>
    private void CommitOriginalDraftTransitionOwnership(Conversation incoming, OriginalDraftTransition transition)
    {
        if (_conversation.Id != incoming.Id) return; // A newer selection already committed its own.
        _originalDraftInputOwner = string.IsNullOrEmpty(_scene.Instruction.Text) && !_scene.HasQueuedComposerMessages
            ? incoming
            : transition.Outgoing.Target;
    }

    /// <summary>Acknowledges a transition's exact outgoing snapshot. While a send is in flight
    /// the composer is deliberately clear and the outcome is unknown, so the SAME committed-input
    /// acknowledgement owns that retirement instead of this flush deleting a draft the send may
    /// still have to restore.</summary>
    private Task FlushOriginalConversationDraftAsync(OriginalDraftTransition transition)
    {
        if (_originalSendInFlight) return Task.CompletedTask;
        var acknowledgement = EnqueueOriginalDraftMutationAsync(transition.Outgoing);
        return acknowledgement ?? Task.CompletedTask;
    }

    /// <summary>Retires the canonical draft of a GENUINELY COMMITTED input. Only the exact
    /// consumed identity may be retired: newer typing in the same conversation bumps the
    /// revision, and a newer text or attachment set makes the queued snapshot differ, so a newer
    /// draft (typed or attached while the response streamed) is always preserved.</summary>
    private async Task RetireCommittedSendDraftAsync(OriginalAcceptedSend accepted)
    {
        if (_conversationProduction is null || accepted.Target.IsTemporary) return;
        if (accepted.DraftAcknowledgement is { } acknowledgement)
            await RequireChatOriginal().AwaitAsync(acknowledgement).ConfigureAwait(false);
        // The pre-seal final-draft owner already waits for this send. It captures the settled
        // outcome; admitting or awaiting a new mutation here would join that owner back to self.
        if (_originalWork.IsRetiring) return;
        lock (_originalDraftGate)
        {
            // A newer snapshot for the SAME conversation is newer user data: never retire it.
            if (_originalDraftQueued is { } queued && queued.Target.Id == accepted.Target.Id && !queued.SameAs(accepted.Consumed))
                return;
            // The composer may still own this conversation's text; newer typing there wins.
            if (OriginalDraftInputOwner.Id == accepted.Target.Id && Volatile.Read(ref _originalComposerRevision) != accepted.Revision)
                return;
        }
        _ = EnqueueOriginalDraftMutationAsync(new OriginalDraftMutation(accepted.Target, string.Empty, [], accepted.Consumed.ResolvedBranchId));
    }

    private Task EnqueueOriginalDraftMutationAsync(OriginalDraftMutation mutation)
    {
        if (_conversationProduction is null || mutation.Target.IsTemporary) return Task.CompletedTask;
        lock (_originalDraftGate)
        {
            if (_originalDraftQueued is { } queued && queued.SameAs(mutation))
                return _originalDraftChain ?? Task.CompletedTask;
            _originalDraftQueued = mutation;
            _originalDraftChain = AdmitOriginalDraftMutationAsync(_originalDraftChain, mutation);
            return _originalDraftChain;
        }
    }

    private Task AdmitOriginalDraftMutationAsync(Task? previous, OriginalDraftMutation mutation)
    {
        try
        {
            return _originalWork.RunAsync(original => WriteOriginalDraftInOrderAsync(original, previous, mutation));
        }
        catch (Exception error) when (_originalWork.IsOriginalAdmissionRefusal(error) || error is ObjectDisposedException)
        {
            // The refusal is retained by the lifetime ledger and reported at close. The snapshot
            // stays pending so the next admitted request retries it.
            _originalDraftQueued = null;
            return previous ?? Task.CompletedTask;
        }
    }

    private async Task WriteOriginalDraftInOrderAsync(
        NewChatOriginalWorkLifetime.Original original, Task? previous, OriginalDraftMutation mutation)
    {
        if (previous is not null)
        {
            // Strict ordering: this mutation cannot execute before an older one settles. EVERY
            // direct sibling of an older real failure stays exact on this original instead of
            // being reduced to the first observed exception.
            try { await previous.ConfigureAwait(false); }
            catch (Exception error) { original.Capture(previous, error); }
        }
        await WriteOriginalDraftAsync(mutation).ConfigureAwait(false);
    }

    /// <summary>The ONE pre-seal flush owner shared by RequestRetirement and CloseAndDrainAsync.
    /// Repeated close requests share the SAME acknowledgement; no duplicate write and no new
    /// work after the permanent seal.</summary>
    private Task DemandOriginalDraftRetirementAsync()
    {
        lock (_originalDraftGate)
            if (_originalDraftRetirementFlush is { } existing) return existing;
        Task? inFlightSend;
        lock (_originalSendGate) inFlightSend = _originalSendInFlight ? _originalSend : null;
        lock (_originalDraftGate)
            return _originalDraftRetirementFlush ??= AdmitOriginalDraftRetirementAsync(inFlightSend);
    }

    private Task AdmitOriginalDraftRetirementAsync(Task? inFlightSend)
    {
        if (_conversationProduction is null) return _originalDraftChain ?? Task.CompletedTask;
        // Snapshot the EXACT predecessor BEFORE admission. The deferred body must never read the
        // mutable chain field: it is reassigned to the admitted task immediately after admission,
        // so a body that read the field could await its OWN task and deadlock retirement.
        var previous = _originalDraftChain;
        try
        {
            var admitted = _originalWork.RunAsync(original =>
                WriteOriginalDraftRetirementAsync(original, previous, inFlightSend));
            _originalDraftChain = admitted;
            return admitted;
        }
        catch (Exception error) when (_originalWork.IsOriginalAdmissionRefusal(error) || error is ObjectDisposedException)
        {
            // The refusal is retained by the lifetime ledger and reported by the SAME close; the
            // composer text is never presented as though it had been stored.
            return previous ?? Task.CompletedTask;
        }
    }

    /// <summary>Acknowledges the FINAL unsent state of this page before the permanent seal, on
    /// the SAME ordering chain. It first waits for an in-flight send to settle: that send's
    /// deliberate clear is not a deletion, a committed send retires its own draft, and a failed
    /// send has already put its unsent text back. The capture is therefore the real outcome, not
    /// a guess taken while the outcome was still unknown.</summary>
    private async Task WriteOriginalDraftRetirementAsync(
        NewChatOriginalWorkLifetime.Original original, Task? previous, Task? inFlightSend)
    {
        if (previous is not null)
        {
            try { await previous.ConfigureAwait(false); }
            catch (Exception error) { original.Capture(previous, error); }
        }
        if (inFlightSend is { IsCompleted: false })
        {
            try { await inFlightSend.ConfigureAwait(false); }
            catch (Exception error) { original.Capture(inFlightSend, error); }
        }
        await WriteOriginalDraftAsync(CaptureOriginalDraftMutation(
            OriginalDraftInputOwner, CaptureUnsentDraftText(_scene.Instruction.Text, _scene.QueuedComposerMessages),
            _pendingAttachmentIds)).ConfigureAwait(false);
    }

    /// <summary>The one canonical draft write. A durability write is a bounded local commit: it
    /// is not abandoned mid-write by retirement cancellation, and its real failure propagates
    /// rather than being swallowed or published as a false clean receipt. The branch key comes
    /// from the snapshot when this page had already resolved it, so a delayed write never
    /// retargets a draft at a branch that replaced it.</summary>
    private async Task WriteOriginalDraftAsync(OriginalDraftMutation mutation)
    {
        var target = mutation.Target;
        if (_conversationProduction is null || target.IsTemporary) return;
        var branchId = mutation.BranchId;
        if (branchId is null)
        {
            var branch = await _conversationProduction.GetCurrentBranchAsync(target.Id, CancellationToken.None).ConfigureAwait(false);
            if (branch is null && (mutation.AttachmentIds.Length != 0 || !string.IsNullOrWhiteSpace(mutation.Content)))
                branch = await _conversationProduction.EnsureRootBranchAsync(target.Id, CancellationToken.None).ConfigureAwait(false);
            branchId = branch?.Id;
        }
        if (branchId is null) return;
        lock (_originalDraftGate)
        {
            RememberOriginalDraftBranch(target.Id, branchId.Value);
            // Bind THIS actual write, including an accepted send that captured its same input
            // before resolution. A newer queued mutation is a distinct object and stays intact.
            mutation.BindResolvedBranch(branchId.Value);
        }
        if (mutation.AttachmentIds.Length == 0 && string.IsNullOrWhiteSpace(mutation.Content))
        {
            await _conversationProduction.DeleteDraftAsync(target.Id, branchId.Value, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        await _conversationProduction.SaveDraftAsync(
            new ConversationDraft(
                target.Id,
                branchId.Value,
                mutation.Content ?? string.Empty,
                JsonSerializer.Serialize(mutation.AttachmentIds),
                DateTimeOffset.UtcNow),
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Publishes text into the composer as a PROGRAMMATIC publication, never as a user
    /// edit. The observed text is recorded immediately, so neither the synchronous invalidation
    /// raised by this setter nor a caret/focus/motion invalidation raised later on the same
    /// element (including a deferred raise from an enclosing update) is mistaken for typing.</summary>
    private void PublishOriginalComposerText(string text, bool placeCaretAtEnd)
    {
        _suppressOriginalDraftSave = true;
        try
        {
            _scene.Instruction.Text = text;
            if (placeCaretAtEnd) _scene.Instruction.PlaceCaretAtEnd();
        }
        finally { _suppressOriginalDraftSave = false; }
        _originalComposerText = text;
    }

    /// <summary>Restores canonical draft text only when the composer still carries the SAME edit
    /// revision and target the load admitted: newer typing (or another target) wins and a
    /// type-delete-back-to-empty is newer user intent, not a restore slot.</summary>
    private void RestoreOriginalDraftText(string content)
    {
        if (string.IsNullOrEmpty(content)) return;
        if (Volatile.Read(ref _originalComposerRevision) != _originalDraftRestoreRevision) return;
        if (_conversation.Id != _originalDraftRestoreConversationId) return;
        if (!string.IsNullOrEmpty(_scene.Instruction.Text)) return;
        PublishOriginalComposerText(content, placeCaretAtEnd: true);
    }

    private void ClearOriginalComposerDraft()
    {
        if (string.IsNullOrEmpty(_scene.Instruction.Text)) return;
        PublishOriginalComposerText(string.Empty, placeCaretAtEnd: false);
    }

    /// <summary>Publishes text into the composer without queueing an autosave: used by the
    /// send path to restore NOT-sent text after a refused admission, never to resurrect text
    /// that an accepted user message already owns.</summary>
    private void RestoreUnsentOriginalInstruction(string instruction)
    {
        if (string.IsNullOrEmpty(instruction) || !string.IsNullOrEmpty(_scene.Instruction.Text)) return;
        PublishOriginalComposerText(instruction, placeCaretAtEnd: true);
    }

    private Task AssociatePendingAttachmentsWithUserMessageAsync(Guid userMessageId, OriginalAcceptedSend accepted, CancellationToken cancellationToken) =>
        accepted.Consumed.AttachmentIds.Length == 0 ? Task.CompletedTask :
            RunChatNestedOriginalAsync(() => AssociatePendingAttachmentsWithUserMessageOriginalBodyAsync(userMessageId, accepted, cancellationToken));

    private async Task AssociatePendingAttachmentsWithUserMessageOriginalBodyAsync(Guid userMessageId, OriginalAcceptedSend accepted, CancellationToken cancellationToken)
    {
        using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, RequireChatOriginal().Token);
        cancellationToken = originalCancellation.Token;
        if (_conversationProduction is null || accepted.Target.IsTemporary || userMessageId == Guid.Empty) return;

        // The EXACT ids the accepted send carried, captured before its response streamed: a newer
        // attachment set added since is never associated with an older user message. Draft
        // retirement is NOT done here: the genuine committed-input acknowledgement already
        // retired the consumed identity, and a late delete here would remove a newer draft.
        var acceptedIds = accepted.Consumed.AttachmentIds;
        if (acceptedIds.Length == 0) return;
        var attachments = await AwaitChatOriginalAsync(_conversationProduction.GetAttachmentsAsync(accepted.Target.Id, messageId: null, cancellationToken));
        var now = DateTimeOffset.UtcNow;
        foreach (var attachment in attachments.Where(item => acceptedIds.Contains(item.Id)))
        {
            await AwaitChatOriginalAsync(_conversationProduction.UpsertAttachmentAsync(
                attachment with { MessageId = userMessageId, UpdatedAt = now },
                cancellationToken));
        }
        ClearAcceptedPersistedAttachmentsFromComposer(acceptedIds);
    }

    /// <summary>Clears ONLY the accepted send's own pending attachments from the composer. A
    /// newer attachment set added while the response streamed stays intact. The consumed ids are
    /// the fence: the composer is cleared wholesale only when nothing newer was added.</summary>
    private void ClearAcceptedPersistedAttachmentsFromComposer(IReadOnlyList<Guid> acceptedIds)
    {
        if (acceptedIds.Count == _pendingAttachmentIds.Count && acceptedIds.All(_pendingAttachmentIds.Contains))
        {
            ClearPendingPersistedAttachmentsFromComposer();
            return;
        }
        foreach (var id in acceptedIds)
        {
            _pendingAttachmentIds.Remove(id);
            if (_attachmentSourcePaths.Remove(id, out var sourcePath)) _taskAttachments.RemoveFile(sourcePath);
        }
        _persistedAttachments.RemoveAll(item => acceptedIds.Contains(item.Id));
        RefreshAttachmentStatus();
    }

    private void ClearPendingPersistedAttachmentsFromComposer()
    {
        foreach (var path in _taskAttachments.Files.ToArray()) _taskAttachments.RemoveFile(path);
        _pendingAttachmentIds.Clear();
        _persistedAttachments.Clear();
        _attachmentSourcePaths.Clear();
        _attachedImages.Clear();
        _attachedContext.Remove("persisted-attachments");
        RefreshAttachmentStatus();
    }

    private static bool TryReadGuid(string value, string prefix, out Guid id)
    {
        id = Guid.Empty;
        return value.StartsWith(prefix, StringComparison.Ordinal)
               && Guid.TryParse(value[prefix.Length..], out id);
    }

    private static string AttachmentIconForKind(MessageAttachmentKind kind) => kind switch
    {
        MessageAttachmentKind.Image => "image",
        MessageAttachmentKind.Video => "play",
        MessageAttachmentKind.Audio => "mic",
        _ => "file"
    };

    private static string AttachmentIconForFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" => "image",
            ".mp4" or ".mov" or ".m4v" or ".webm" or ".avi" => "play",
            ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg" => "mic",
            _ => "file"
        };
    }
}
