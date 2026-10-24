using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

// Synthetic provider/authorization controls only; these do not exercise real Home permission
// or source mutation.
public sealed class DeveloperCodeIntelligenceSurfaceTests
{
    [Fact]
    public async Task ApplyCodeAction_RequiresHomeGrantBeforeMutation()
    {
        var language = new RecordingAdvancedService();
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Denied, null, "Denied by Home."));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));

        var result = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");

        Assert.False(result.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Equal(0, language.ApplyCalls);
        Assert.Equal("workspace.write", gate.LastRequest!.RequiredPermissionScope);
        Assert.Equal(document.WorkspaceRevision, gate.LastRequest.WorkspaceRevision);
    }

    [Fact]
    public async Task ApplyCodeAction_UsesPreparedProposalRatherThanCallerModifiedPayload()
    {
        var language = new RecordingAdvancedService();
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));
        var callerCopy = language.Proposal with { Title = "Caller supplied", Files = [new CodeFileMutation("evil.cs", "", "owned")] };

        var result = await surface.ApplyCodeActionAsync(document, callerCopy, "user:1");

        Assert.True(result.Succeeded, result.Error?.Message);
        var applied = Assert.Single(language.Applied!);
        Assert.Equal("Sample.cs", applied.RelativePath);
        Assert.Equal("Safe edit", language.LastAppliedTitle);
        Assert.True(Assert.IsAssignableFrom<IList<string>>(gate.LastRequest!.AffectedRelativePaths).IsReadOnly);
        Assert.True(Assert.IsAssignableFrom<IList<string>>(result.Value!.AffectedRelativePaths).IsReadOnly);
    }

    [Fact]
    public async Task ApplyCodeAction_AffectedPathUniquenessUsesHostFilesystemCaseSemantics()
    {
        var language = new RecordingAdvancedService
        {
            Proposal = new(Guid.NewGuid(), "Two-file edit", "quickfix", true,
            [new CodeFileMutation("Sample.cs", "before", "after"),
             new CodeFileMutation("sample.cs", "before", "after")], null)
        };
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));

        var result = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(DeveloperOperationErrorCode.InvalidInput, result.Error!.Code);
            Assert.Null(gate.LastRequest);
            Assert.Equal(0, language.ApplyCalls);
        }
        else
        {
            Assert.True(result.Succeeded, result.Error?.Message);
            var request = gate.LastRequest!;
            Assert.True(Assert.IsAssignableFrom<IList<string>>(result.Value!.AffectedRelativePaths).IsReadOnly);
            var expectedPaths = new[] { "Sample.cs", "sample.cs" };
            Assert.Equal(expectedPaths, request.AffectedRelativePaths);
        }
    }

    [Fact]
    public async Task ApplyCodeAction_RejectsDuplicateNormalizedPathsBeforeHomeAuthorization()
    {
        var language = new RecordingAdvancedService
        {
            Proposal = new(Guid.NewGuid(), "Aliased paths", "quickfix", true,
            [new CodeFileMutation("src/file.cs", "before", "one"),
             new CodeFileMutation("src//file.cs", "before", "two")], null)
        };
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "src/file.cs", 1);
        await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));

        var result = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");

        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, result.Error!.Code);
        Assert.Null(gate.LastRequest);
        Assert.Equal(0, language.ApplyCalls);
    }

    [Fact]
    public async Task ApplyCodeAction_RejectsUnsafePathsBeforeHomeAuthorization()
    {
        var invalidPaths = new List<string> { "bad\0path.cs", "../outside.cs", "/rooted.cs", new string('x', 4097) };
        if (OperatingSystem.IsWindows()) invalidPaths.Add("file.cs:stream"); // Refuse NTFS alternate streams.
        foreach (var invalidPath in invalidPaths)
        {
            var language = new RecordingAdvancedService
            {
                Proposal = new(Guid.NewGuid(), "Invalid path", "quickfix", true,
                    [new CodeFileMutation(invalidPath, "before", "after")], null)
            };
            var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
            var surface = new DeveloperCodeIntelligenceSurface(language, gate);
            var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
                Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
            await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));

            var result = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");

            Assert.Equal(DeveloperOperationErrorCode.InvalidInput, result.Error!.Code);
            Assert.Null(gate.LastRequest);
            Assert.Equal(0, language.ApplyCalls);
        }
    }

    [Fact]
    public async Task ApplyCodeAction_RejectsIncompleteProviderMutationBeforeHomeAuthorization()
    {
        var language = new RecordingAdvancedService
        {
            Proposal = new(Guid.NewGuid(), "Incomplete edit", "quickfix", true, [null!], null)
        };
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        var proposals = await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));

        var result = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");

        Assert.Equal(DeveloperOperationErrorCode.CapabilityUnavailable, proposals.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, result.Error!.Code);
        Assert.Null(gate.LastRequest);
        Assert.Equal(0, language.ApplyCalls);
    }

    [Fact]
    public async Task ApplyCodeAction_DocumentPathIdentityUsesHostFilesystemCaseSemantics()
    {
        var language = new RecordingAdvancedService();
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));

        var result = await surface.ApplyCodeActionAsync(document with { RelativePath = "sample.cs" },
            language.Proposal, "user:1");

        if (OperatingSystem.IsWindows())
        {
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(1, language.ApplyCalls);
        }
        else
        {
            Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, result.Error!.Code);
            Assert.Null(gate.LastRequest);
            Assert.Equal(0, language.ApplyCalls);
        }
    }

    [Fact]
    public async Task ApplyCodeAction_ReservesPreparedProposalAcrossPendingHomeAuthorization()
    {
        var language = new RecordingAdvancedService();
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.BeforeAuthorize = async () =>
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
        };
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));
        var first = surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");
            Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, duplicate.Error!.Code);
            Assert.Equal(1, gate.AuthorizeCalls);
            Assert.Equal(0, language.ApplyCalls);
        }
        finally { release.TrySetResult(); }

        Assert.True((await first).Succeeded);
        Assert.Equal(1, gate.AuthorizeCalls);
        Assert.Equal(1, language.ApplyCalls);
        var replay = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, replay.Error!.Code);
        Assert.Equal(1, language.ApplyCalls);
    }

    [Fact]
    public async Task ApplyCodeAction_CancellationDuringHomeAuthorizationDoesNotMutateAndCanRetry()
    {
        var language = new RecordingAdvancedService();
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.BeforeAuthorize = async () =>
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
        };
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));
        using var cancellation = new CancellationTokenSource();
        var pending = surface.ApplyCodeActionAsync(document, language.Proposal, "user:1", cancellation.Token);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, language.ApplyCalls);

        gate.BeforeAuthorize = null;
        var retry = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");
        Assert.True(retry.Succeeded, retry.Error?.Message);
        Assert.Equal(1, language.ApplyCalls);
    }

    [Fact]
    public async Task GetCodeActions_DetachesReturnedAndPreparedWorkspaceEditsFromProviderMutation()
    {
        var sourceFiles = new List<CodeFileMutation> { new("Sample.cs", "before", "after") };
        var language = new RecordingAdvancedService
        {
            Proposal = new(Guid.NewGuid(), "Detached edit", "quickfix", true, sourceFiles, null)
        };
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        var proposals = await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));
        Assert.True(proposals.Succeeded);
        var exact = Assert.Single(proposals.Value!);
        Assert.True(Assert.IsAssignableFrom<IList<CodeFileMutation>>(exact.Files).IsReadOnly);

        sourceFiles[0] = new("evil.cs", "", "caller-owned replacement");
        var applied = await surface.ApplyCodeActionAsync(document, exact, "user:1");

        Assert.True(applied.Succeeded, applied.Error?.Message);
        Assert.Equal("Sample.cs", Assert.Single(gate.LastRequest!.AffectedRelativePaths));
        Assert.Equal("Sample.cs", Assert.Single(language.Applied!).RelativePath);
    }

    [Fact]
    public async Task GetCodeActions_CancellationAfterProviderReplyCannotPublishPreparedMutation()
    {
        var language = new RecordingAdvancedService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        language.BeforeGetCodeActions = async () =>
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
        };
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        using var cancellation = new CancellationTokenSource();
        var pending = surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)), cancellation.Token);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        var apply = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, apply.Error!.Code);
        Assert.Null(gate.LastRequest);
        Assert.Equal(0, language.ApplyCalls);
    }

    [Fact]
    public async Task GetCodeActions_RejectsEmptyApplicableProposalIdentity()
    {
        var language = new RecordingAdvancedService { Proposal = new(Guid.Empty, "Unsafe identity", "quickfix", true,
            [new CodeFileMutation("Sample.cs", "before", "after")], null) };
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);

        var proposals = await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));
        var apply = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");

        Assert.Equal(DeveloperOperationErrorCode.CapabilityUnavailable, proposals.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, apply.Error!.Code);
        Assert.Null(gate.LastRequest);
        Assert.Equal(0, language.ApplyCalls);
    }

    [Fact]
    public async Task GetCodeActions_RejectsDuplicateProposalIdentityWhenOneIsUnavailable()
    {
        var identity = Guid.NewGuid();
        var language = new RecordingAdvancedService
        {
            Proposal = new(identity, "First", "quickfix", true, [new CodeFileMutation("one.cs", "a", "b")], null),
            ProposedActions = [
                new(identity, "First", "quickfix", true, [new CodeFileMutation("one.cs", "a", "b")], null),
                new(identity, "Different action", "quickfix", true, [new CodeFileMutation("two.cs", "c", "d")], "Unavailable")]
        };
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "one.cs", 1);

        var proposals = await surface.GetCodeActionsAsync(document, "a", new(new(0, 0), new(0, 1)));
        var apply = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");

        Assert.Equal(DeveloperOperationErrorCode.CapabilityUnavailable, proposals.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, apply.Error!.Code);
        Assert.Null(gate.LastRequest);
        Assert.Equal(0, language.ApplyCalls);
    }

    [Fact]
    public async Task GetCodeActions_DoesNotOverwriteAnActivePreparedIdentity()
    {
        var language = new RecordingAdvancedService();
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        var firstResponse = await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));
        var firstProposal = Assert.Single(firstResponse.Value!);
        language.Proposal = language.Proposal with
        {
            Title = "Changed action with reused ID",
            Files = [new CodeFileMutation("evil.cs", "", "unreviewed")]
        };

        var duplicateResponse = await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));
        Assert.Equal(DeveloperOperationErrorCode.CapabilityUnavailable, duplicateResponse.Error!.Code);
        var applied = await surface.ApplyCodeActionAsync(document, firstProposal, "user:1");

        Assert.True(applied.Succeeded, applied.Error?.Message);
        Assert.Equal("Sample.cs", Assert.Single(gate.LastRequest!.AffectedRelativePaths));
        Assert.Equal("Sample.cs", Assert.Single(language.Applied!).RelativePath);
    }

    [Fact]
    public async Task ApplyCodeAction_DeniedAuthorizationLeavesPreparedProposalRetryable()
    {
        var language = new RecordingAdvancedService();
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Denied, null, "Denied."));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));

        var denied = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");
        Assert.Equal(DeveloperOperationErrorCode.PermissionDenied, denied.Error!.Code);
        Assert.Equal(0, language.ApplyCalls);

        gate.Authorization = new(DeveloperAuthorizationDecision.Granted, "grant:retry", null);
        var retry = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");

        Assert.True(retry.Succeeded, retry.Error?.Message);
        Assert.Equal(1, language.ApplyCalls);
    }

    [Fact]
    public async Task ApplyCodeAction_UnknownPostGrantOutcomeCannotReplayPreparedMutation()
    {
        var language = new RecordingAdvancedService { ApplyFailure = new IOException("Outcome unavailable.") };
        var gate = new RecordingMutationGate(new(DeveloperAuthorizationDecision.Granted, "grant:1", null));
        var surface = new DeveloperCodeIntelligenceSurface(language, gate);
        var document = new DeveloperCodeDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hosted:sample",
            Path.GetFullPath(Path.GetTempPath()), "Sample.cs", 1);
        await surface.GetCodeActionsAsync(document, "before", new(new(0, 0), new(0, 6)));

        var unknown = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");
        Assert.Equal(DeveloperOperationErrorCode.LanguageServiceUnavailable, unknown.Error!.Code);
        Assert.Equal(1, language.ApplyCalls);
        language.ApplyFailure = null;

        var replay = await surface.ApplyCodeActionAsync(document, language.Proposal, "user:1");

        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, replay.Error!.Code);
        Assert.Equal(1, language.ApplyCalls);
    }

    private sealed class RecordingMutationGate(DeveloperMutationAuthorization authorization) : IDeveloperWorkspaceMutationGate
    {
        public DeveloperMutationRequest? LastRequest { get; private set; }
        public int AuthorizeCalls { get; private set; }
        public Func<Task>? BeforeAuthorize { get; set; }
        public DeveloperMutationAuthorization Authorization { get; set; } = authorization;
        public async Task<DeveloperMutationAuthorization> AuthorizeAsync(DeveloperMutationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            AuthorizeCalls++;
            if (BeforeAuthorize is not null) await BeforeAuthorize().ConfigureAwait(false);
            return Authorization;
        }
    }

    private sealed class RecordingAdvancedService : IAdvancedCodeIntelligenceService
    {
        public CodeActionProposal Proposal { get; set; } = new(Guid.NewGuid(), "Safe edit", "quickfix", true,
            [new CodeFileMutation("Sample.cs", "before", "after")], null);
        public int ApplyCalls { get; private set; }
        public IReadOnlyList<CodeFileMutation>? Applied { get; private set; }
        public string? LastAppliedTitle { get; private set; }
        public Exception? ApplyFailure { get; set; }
        public Func<Task>? BeforeGetCodeActions { get; set; }
        public IReadOnlyList<CodeActionProposal>? ProposedActions { get; set; }
        public Task<LanguageServerCapabilities> GetCapabilitiesAsync(string root, string path, CancellationToken ct) => Task.FromResult(LanguageServerCapabilities.None);
        public Task<IReadOnlyList<CodeLocation>> GetDefinitionAsync(string root, string path, string text, CodePosition position, CancellationToken ct) => Task.FromResult<IReadOnlyList<CodeLocation>>([]);
        public Task<IReadOnlyList<CodeLocation>> FindReferencesAsync(string root, string path, string text, CodePosition position, CancellationToken ct) => Task.FromResult<IReadOnlyList<CodeLocation>>([]);
        public Task<CodeWorkspaceMutationResult> RenameSymbolAsync(string root, string path, string text, CodePosition position, string name, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LanguageServerCompletion>> GetCompletionsAsync(string root, string path, string text, CodePosition position, CancellationToken ct) => Task.FromResult<IReadOnlyList<LanguageServerCompletion>>([]);
        public async Task<IReadOnlyList<CodeActionProposal>> GetCodeActionsAsync(string root, string path, string text, CodeRange range, CancellationToken ct)
        {
            if (BeforeGetCodeActions is not null) await BeforeGetCodeActions().ConfigureAwait(false);
            return ProposedActions ?? [Proposal];
        }
        public Task<CodeWorkspaceMutationResult> ApplyCodeActionAsync(string root, CodeActionProposal action, CancellationToken ct)
        {
            ApplyCalls++;
            if (ApplyFailure is { } failure) throw failure;
            Applied = action.Files;
            LastAppliedTitle = action.Title;
            return Task.FromResult(new CodeWorkspaceMutationResult(Guid.NewGuid(), action.Title, action.Files));
        }
        public Task<IReadOnlyList<CodeSemanticToken>> GetSemanticTokensAsync(string root, string path, string text, CancellationToken ct) => Task.FromResult<IReadOnlyList<CodeSemanticToken>>([]);
    }
}
