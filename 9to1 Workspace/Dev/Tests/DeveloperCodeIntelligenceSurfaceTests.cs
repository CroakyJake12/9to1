using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

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

        await surface.ApplyCodeActionAsync(document, callerCopy, "user:1");

        var applied = Assert.Single(language.Applied!);
        Assert.Equal("Sample.cs", applied.RelativePath);
        Assert.Equal("Safe edit", language.LastAppliedTitle);
    }

    private sealed class RecordingMutationGate(DeveloperMutationAuthorization authorization) : IDeveloperWorkspaceMutationGate
    {
        public DeveloperMutationRequest? LastRequest { get; private set; }
        public Task<DeveloperMutationAuthorization> AuthorizeAsync(DeveloperMutationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(authorization);
        }
    }

    private sealed class RecordingAdvancedService : IAdvancedCodeIntelligenceService
    {
        private readonly CodeActionProposal _proposal = new(Guid.NewGuid(), "Safe edit", "quickfix", true,
            [new CodeFileMutation("Sample.cs", "before", "after")], null);
        public CodeActionProposal Proposal => _proposal;
        public int ApplyCalls { get; private set; }
        public IReadOnlyList<CodeFileMutation>? Applied { get; private set; }
        public string? LastAppliedTitle { get; private set; }
        public Task<LanguageServerCapabilities> GetCapabilitiesAsync(string root, string path, CancellationToken ct) => Task.FromResult(LanguageServerCapabilities.None);
        public Task<IReadOnlyList<CodeLocation>> GetDefinitionAsync(string root, string path, string text, CodePosition position, CancellationToken ct) => Task.FromResult<IReadOnlyList<CodeLocation>>([]);
        public Task<IReadOnlyList<CodeLocation>> FindReferencesAsync(string root, string path, string text, CodePosition position, CancellationToken ct) => Task.FromResult<IReadOnlyList<CodeLocation>>([]);
        public Task<CodeWorkspaceMutationResult> RenameSymbolAsync(string root, string path, string text, CodePosition position, string name, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LanguageServerCompletion>> GetCompletionsAsync(string root, string path, string text, CodePosition position, CancellationToken ct) => Task.FromResult<IReadOnlyList<LanguageServerCompletion>>([]);
        public Task<IReadOnlyList<CodeActionProposal>> GetCodeActionsAsync(string root, string path, string text, CodeRange range, CancellationToken ct) => Task.FromResult<IReadOnlyList<CodeActionProposal>>([_proposal]);
        public Task<CodeWorkspaceMutationResult> ApplyCodeActionAsync(string root, CodeActionProposal action, CancellationToken ct)
        {
            ApplyCalls++;
            Applied = action.Files;
            LastAppliedTitle = action.Title;
            return Task.FromResult(new CodeWorkspaceMutationResult(Guid.NewGuid(), action.Title, action.Files));
        }
        public Task<IReadOnlyList<CodeSemanticToken>> GetSemanticTokensAsync(string root, string path, string text, CancellationToken ct) => Task.FromResult<IReadOnlyList<CodeSemanticToken>>([]);
    }
}
