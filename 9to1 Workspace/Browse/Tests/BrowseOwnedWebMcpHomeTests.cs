using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Browser;
using Haven.UI;
using HuiButton = Haven.UI.Components.Button;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

/// <summary>Actual physical Home/profile/private issuer and genuine Browser registry.
/// Native evaluations are controlled protocol observations, not an installed engine or external effect proof.</summary>
public sealed class BrowseOwnedWebMcpHomeTests
{
    [Fact]
    public async Task Actual_pending_review_emits_zero_effect_then_exact_approval_emits_once_and_lost_return_only_observes()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var workspace = await BrowseOwnedToolWorkspace.OpenAsync(fixture.Binding, fixture.Actor, Document, Tool());
        using var args = JsonDocument.Parse("{}");
        var requestID = await workspace.ReviewAsync(args.RootElement);
        var pending = await fixture.Permissions.ReadRequestObservationAsync(requestID);
        Assert.NotNull(pending); Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
        Assert.Equal(fixture.Actor.ActorId, pending.Caller.CallerId);
        Assert.Equal(WebMcpPreparedReviewState.PendingApproval, Assert.Single(workspace.Reviews).SubmissionState);
        Assert.False((await workspace.RunOrObserveAsync(requestID)).OutcomeKnown);
        Assert.Equal(0, fixture.Host.Effects);
        await fixture.Permissions.DecideAsync(requestID, HomeApprovalChoice.Accept);
        fixture.Host.LoseEffectReturn = true;
        Assert.False((await workspace.RunOrObserveAsync(requestID)).OutcomeKnown);
        Assert.Equal(1, fixture.Host.Effects);
        var observed = await workspace.RunOrObserveAsync(requestID);
        Assert.True(observed.OutcomeKnown); Assert.True(observed.AuditRecorded);
        Assert.Equal(1, fixture.Host.Effects); Assert.Equal(1, fixture.Host.OutcomeObservations);
        Assert.Equal(fixture.Host.DispatchedInvocation, fixture.Host.ObservedInvocation);
        workspace.Dispose();
        Assert.True((await workspace.FinishAuditAsync(requestID)).OutcomeKnown);
        Assert.Equal(1, fixture.Host.Effects); Assert.Equal(1, fixture.Host.OutcomeObservations);
    }
    [Fact]
    public async Task Genuine_Home_approved_original_review_cannot_emit_on_same_host_navigated_document_with_colliding_metadata()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var workspace = await BrowseOwnedToolWorkspace.OpenAsync(fixture.Binding, fixture.Actor, Document, Tool());
        using var args = JsonDocument.Parse("{}"); var request = await workspace.ReviewAsync(args.RootElement);
        await fixture.Permissions.DecideAsync(request, HomeApprovalChoice.Accept);
        var approvedBytes = await File.ReadAllBytesAsync(fixture.HomeFile);
        // Native provenance changes while the attached host and all page-controlled metadata remain identical.
        await fixture.Host.NavigateAsync(new Uri("https://example.test/replacement"), default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.RunOrObserveAsync(request));
        Assert.Equal(0, fixture.Host.Effects); Assert.Equal(0, fixture.Host.OutcomeObservations);
        Assert.Equal(approvedBytes, await File.ReadAllBytesAsync(fixture.HomeFile));
        Assert.Equal(request, Assert.Single(workspace.Reviews).RequestID);
        Assert.Equal(HomePermissionRequestState.Approved,
            (await fixture.Permissions.ReadRequestObservationAsync(request))!.State);
    }
    [Fact]
    public async Task Same_host_native_document_replacement_before_review_creates_no_Home_pending_or_effect()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var workspace = await BrowseOwnedToolWorkspace.OpenAsync(fixture.Binding, fixture.Actor, Document, Tool());
        var homeBytes = await File.ReadAllBytesAsync(fixture.HomeFile);
        await fixture.Host.NavigateAsync(new Uri("https://example.test/replacement"), default);
        using var args = JsonDocument.Parse("{}");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.ReviewAsync(args.RootElement));
        Assert.Empty(workspace.Reviews); Assert.Equal(0, fixture.Host.Effects);
        Assert.Equal(0, fixture.Host.OutcomeObservations);
        Assert.Equal(homeBytes, await File.ReadAllBytesAsync(fixture.HomeFile));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Navigation_or_declaration_replacement_after_actual_approval_never_emits(bool navigation)
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var workspace = await BrowseOwnedToolWorkspace.OpenAsync(fixture.Binding, fixture.Actor, Document, Tool());
        using var args = JsonDocument.Parse("{}"); var request = await workspace.ReviewAsync(args.RootElement);
        await fixture.Permissions.DecideAsync(request, HomeApprovalChoice.Accept);
        var homeBytes = await File.ReadAllBytesAsync(fixture.HomeFile);
        if (navigation) fixture.Browser.Attach(new Host());
        else fixture.Host.Schema = "{\"type\":\"object\",\"required\":[\"replacement\"]}";
        if (navigation) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.RunOrObserveAsync(request));
        else Assert.False((await workspace.RunOrObserveAsync(request)).OutcomeKnown);
        Assert.Equal(0, fixture.Host.Effects);
        // A resolver refusal may append an actual rejected admission audit. The original
        // approval is not silently converted to successful execution or a new request.
        var retained = await fixture.Permissions.ReadRequestObservationAsync(request);
        Assert.NotNull(retained); Assert.NotEqual(HomePermissionRequestState.Succeeded, retained.State);
        if (navigation) Assert.Equal(homeBytes, await File.ReadAllBytesAsync(fixture.HomeFile));
        Assert.Single(workspace.Reviews); Assert.Equal(request, Assert.Single(workspace.Reviews).RequestID);
    }
    [Fact]
    public async Task Disconnect_retains_actual_pending_request_for_exact_Home_decline_and_Finish_is_audit_only()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var workspace = await BrowseOwnedToolWorkspace.OpenAsync(fixture.Binding, fixture.Actor, Document, Tool());
        using var args = JsonDocument.Parse("{}"); var request = await workspace.ReviewAsync(args.RootElement);
        workspace.Dispose();
        Assert.True((await fixture.Permissions.DecideAsync(request, HomeApprovalChoice.Decline)).Succeeded);
        var homeBytes = await File.ReadAllBytesAsync(fixture.HomeFile);
        Assert.False((await workspace.FinishAuditAsync(request)).OutcomeKnown);
        Assert.Equal(homeBytes, await File.ReadAllBytesAsync(fixture.HomeFile));
        Assert.Equal(0, fixture.Host.Effects);
        Assert.Equal(HomePermissionRequestState.Denied, (await fixture.Permissions.ReadRequestObservationAsync(request))!.State);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Malicious_scope_count_cannot_enumerate_unbounded_proposals_or_create_Home_review(bool largeDeclaredCount)
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var display = await fixture.Binding.OpenDisplayedToolAsync(fixture.Actor, Document, Tool());
        var supplied = new EndlessScopes(display.Scope, largeDeclaredCount ? int.MaxValue : 1);
        using var arguments = JsonDocument.Parse("{}");
        var before = await File.ReadAllBytesAsync(fixture.HomeFile);
        var observations = fixture.Host.NativeObservations;
        if (largeDeclaredCount) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Binding.ReviewAsync(display, arguments.RootElement, supplied));
        else await Assert.ThrowsAsync<ArgumentException>(() => fixture.Binding.ReviewAsync(display, arguments.RootElement, supplied));
        Assert.Equal(largeDeclaredCount ? 0 : 2, supplied.Consumed);
        Assert.Equal(observations, fixture.Host.NativeObservations);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.HomeFile));
    }
    [Fact]
    public async Task Authored_controls_discover_review_and_observe_one_actual_Home_approved_invocation()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        string? openedHome = null;
        using var scene = new BrowseOwnedToolsScene(fixture.Registry, fixture.Binding, fixture.Actor,
            update => { update(); return Task.CompletedTask; }, id => { openedHome = id; return Task.CompletedTask; });
        Invoke(scene.DiscoverButton); await scene.WhenActionsIdleAsync();
        Invoke(Assert.IsType<HuiButton>(Assert.Single(scene.Tools.Children))); await scene.WhenActionsIdleAsync();
        Assert.Contains("actual-tool", scene.ToolDeclaration.Content);
        Assert.Contains("Untrusted page description", scene.ToolDeclaration.Content);
        Assert.Contains("\"type\":\"object\"", scene.ToolDeclaration.Content);
        Invoke(scene.ReviewButton); await scene.WhenActionsIdleAsync();
        var id = Assert.Single(scene.PendingReviewIDs);
        var row = Assert.IsType<Haven.UI.Components.Container>(Assert.Single(scene.Reviews.Children));
        var home = Assert.Single(row.Children.OfType<HuiButton>(), value => value.Name == "Browse.Tools.Home." + id);
        var run = Assert.Single(row.Children.OfType<HuiButton>(), value => value.Name == "Browse.Tools.Run." + id);
        Invoke(home); await scene.WhenActionsIdleAsync(); Assert.Equal(id, openedHome);
        Invoke(run); await scene.WhenActionsIdleAsync(); Assert.Equal(0, fixture.Host.Effects);
        Assert.Equal("This invocation is awaiting Home approval.", scene.Status.Content);
        Assert.Equal(HomePermissionRequestState.PendingApproval, (await fixture.Permissions.ReadRequestObservationAsync(id))!.State);
        Assert.True((await fixture.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        fixture.Host.LoseEffectReturn = true;
        Invoke(run); await scene.WhenActionsIdleAsync(); Assert.Equal(1, fixture.Host.Effects);
        Invoke(run); await scene.WhenActionsIdleAsync(); Assert.Equal(1, fixture.Host.Effects);
        Assert.Equal(1, fixture.Host.OutcomeObservations);
        Assert.Equal(fixture.Host.DispatchedInvocation, fixture.Host.ObservedInvocation);
        scene.Dispose();
        Assert.True((await scene.FinishAuditAsync(id)).OutcomeKnown);
        Assert.Equal(1, fixture.Host.Effects);
    }
    [Fact]
    public async Task Authored_old_catalogue_control_cannot_adopt_colliding_replacement_host_or_publish_review()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var scene = new BrowseOwnedToolsScene(fixture.Registry, fixture.Binding, fixture.Actor,
            update => { update(); return Task.CompletedTask; }, _ => Task.CompletedTask);
        Invoke(scene.DiscoverButton); await scene.WhenActionsIdleAsync();
        var selected = Assert.IsType<HuiButton>(Assert.Single(scene.Tools.Children));
        var homeBytes = await File.ReadAllBytesAsync(fixture.HomeFile);
        var replacement = new Host(); fixture.Browser.Attach(replacement);
        Invoke(selected); await scene.WhenActionsIdleAsync();
        Invoke(scene.ReviewButton); await scene.WhenActionsIdleAsync();
        Assert.Empty(scene.PendingReviewIDs); Assert.Empty(scene.Reviews.Children);
        Assert.Equal(0, replacement.NativeObservations); Assert.Equal(0, replacement.Effects);
        Assert.Equal(0, fixture.Host.Effects);
        Assert.Equal(homeBytes, await File.ReadAllBytesAsync(fixture.HomeFile));
    }
    [Fact]
    public async Task Newest_clicked_tool_remains_visible_and_in_actual_Home_intent_when_older_selection_read_finishes_last()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        fixture.Host.IncludeSecondTool = true; fixture.Host.HoldFirstToolRead = true;
        var newestPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BrowseOwnedToolsScene? actualScene = null;
        using var scene = new BrowseOwnedToolsScene(fixture.Registry, fixture.Binding, fixture.Actor,
            update =>
            {
                update();
                if (actualScene?.ToolDeclaration.Content.StartsWith("newest-tool\n", StringComparison.Ordinal) == true)
                    newestPublished.TrySetResult();
                return Task.CompletedTask;
            }, _ => Task.CompletedTask);
        actualScene = scene;
        Invoke(scene.DiscoverButton); await scene.WhenActionsIdleAsync();
        var first = Assert.Single(scene.Tools.Children.OfType<HuiButton>(), value => value.Name == "Browse.Tools.Select.actual-tool");
        var newest = Assert.Single(scene.Tools.Children.OfType<HuiButton>(), value => value.Name == "Browse.Tools.Select.newest-tool");
        Invoke(first);
        await fixture.Host.FirstToolReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Invoke(newest);
        // Snapshot includes the held original click; wait for the newer declaration's
        // actual UI publication instead of pretending the first pipeline has completed.
        try { await newestPublished.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { fixture.Host.ReleaseFirstToolRead.TrySetResult(); }
        await scene.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.StartsWith("newest-tool\n", scene.ToolDeclaration.Content);
        Invoke(scene.ReviewButton); await scene.WhenActionsIdleAsync();
        var id = Assert.Single(scene.PendingReviewIDs);
        var actual = await fixture.Permissions.ReadRequestObservationAsync(id);
        Assert.NotNull(actual); Assert.Equal(HomePermissionRequestState.PendingApproval, actual.State);
        using var arguments = JsonDocument.Parse("{}");
        var expected = new WebMcpInvocationRequest(Document.Origin, Document.DocumentID, Document.BrowserVersion,
            Document.Capability, Document.Supported, "newest-tool", Tool().InputSchema, arguments.RootElement);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.SerializeToElement(expected).GetRawText())));
        Assert.Equal(digest, actual.Impact.ArgumentsDigest);
        Assert.Equal(0, fixture.Host.Effects);
    }
    [Fact]
    public async Task Review_cannot_use_new_private_workspace_until_its_actual_UI_declaration_is_published()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(); fixture.Host.IncludeSecondTool = true;
        var uiEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refusedReviewPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = false; var held = 0; BrowseOwnedToolsScene? actualScene = null;
        using var scene = new BrowseOwnedToolsScene(fixture.Registry, fixture.Binding, fixture.Actor,
            async update =>
            {
                if (hold && Interlocked.CompareExchange(ref held, 1, 0) == 0)
                { uiEntered.TrySetResult(); await uiRelease.Task; }
                update();
                if (actualScene?.Status.Content.StartsWith("This action is unavailable.", StringComparison.Ordinal) == true)
                    refusedReviewPublished.TrySetResult();
            }, _ => Task.CompletedTask);
        actualScene = scene;
        Invoke(scene.DiscoverButton); await scene.WhenActionsIdleAsync();
        Invoke(Assert.Single(scene.Tools.Children.OfType<HuiButton>(), value => value.Name == "Browse.Tools.Select.actual-tool"));
        await scene.WhenActionsIdleAsync(); Assert.StartsWith("actual-tool\n", scene.ToolDeclaration.Content);
        var before = await File.ReadAllBytesAsync(fixture.HomeFile);
        hold = true;
        Invoke(Assert.Single(scene.Tools.Children.OfType<HuiButton>(), value => value.Name == "Browse.Tools.Select.newest-tool"));
        try
        {
            await uiEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.StartsWith("actual-tool\n", scene.ToolDeclaration.Content);
            Invoke(scene.ReviewButton);
            await refusedReviewPublished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(scene.PendingReviewIDs);
        }
        finally { uiRelease.TrySetResult(); }
        await scene.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(scene.PendingReviewIDs); Assert.Equal(before, await File.ReadAllBytesAsync(fixture.HomeFile));
        Assert.StartsWith("newest-tool\n", scene.ToolDeclaration.Content);
        Invoke(scene.ReviewButton); await scene.WhenActionsIdleAsync();
        var actual = await fixture.Permissions.ReadRequestObservationAsync(Assert.Single(scene.PendingReviewIDs));
        Assert.NotNull(actual); Assert.Equal(HomePermissionRequestState.PendingApproval, actual.State);
        using var arguments = JsonDocument.Parse("{}");
        var expected = new WebMcpInvocationRequest(Document.Origin, Document.DocumentID, Document.BrowserVersion,
            Document.Capability, Document.Supported, "newest-tool", Tool().InputSchema, arguments.RootElement);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.SerializeToElement(expected).GetRawText()))),
            actual.Impact.ArgumentsDigest);
        Assert.Equal(0, fixture.Host.Effects);
    }
    private static void Invoke(HuiButton button)
    {
        // Canonical CUI activation completes on KeyUp; this is not OS pointer injection.
        Assert.True(button.KeyDown(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.True(button.KeyUp(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
    }
    private sealed class EndlessScopes(ResourceScope value, int declared) : IReadOnlyList<ResourceScope>
    {
        public int Count => declared;
        public int Consumed { get; private set; }
        public ResourceScope this[int index] => value;
        public IEnumerator<ResourceScope> GetEnumerator()
        {
            while (true) { Consumed++; yield return value; }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static readonly WebMcpDocument Document = new("https://example.test", "actual-document", "controlled-browser",
        "navigator.modelContextTesting", true, false);
    private static WebMcpTool Tool() => new("actual-tool", "Untrusted page description",
        JsonSerializer.SerializeToElement(new { type = "object" }));
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("astra-browse-owning-home-").FullName;
        public string HomeFile => Path.Combine(_root, "home.json");
        public BrowserSessionService Browser { get; }
        public Host Host { get; } = new();
        public HomePermissionTrustService Permissions { get; }
        public BrowseOwnedWebMcpBinding Binding { get; }
        public BrowseOwnedDocumentRegistry Registry { get; }
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        private readonly HomeLocalProfileIdentity _profiles;
        public Fixture()
        {
            var store = new FileHomeCoreStateStore(HomeFile);
            _profiles = new(store, new OperatingSystemPrincipalSource());
            Browser = new(new Paths(_root)); Browser.Attach(Host);
            Registry = new BrowseOwnedDocumentRegistry(Browser, _profiles);
            var policy = new HomeWebMcpActionPolicySource();
            Permissions = new(store, policy.TryGet);
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(_profiles, [Registry]), Permissions);
            var issuer = new HomeWebMcpOriginalActorApproval(store, _profiles, broker, Permissions);
            Binding = new(Browser, _profiles, issuer, Registry);
        }
        public async Task InitializeAsync()
            => Actor = await _profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual OS/Home actor required.");
        public void Dispose() { Browser.Dispose(); Directory.Delete(_root, true); }
    }
    // Same supported owned-entry API and real private Home leases, controlled evaluation
    // results. No custom admission grant or fictional canonical access resolver.
    private sealed class Host : IOriginalSessionOwnedBrowserHost, IBrowserNativeEntryObservation
    {
        private long _documentGeneration;
        private sealed record NativeDocument(Host Issuer, long Generation) : IBrowserNativeDocumentSelection;
        public IBrowserNativeDocumentSelection CaptureDocumentSelection() => new NativeDocument(this, _documentGeneration);
        private void RequireDocument(IBrowserNativeDocumentSelection originalDocument)
        {
            if (originalDocument is not NativeDocument selected || !ReferenceEquals(selected.Issuer, this) || selected.Generation != _documentGeneration)
                throw new UnauthorizedAccessException("Original controlled native document changed.");
        }
        public string Schema { get; set; } = "{\"type\":\"object\"}";
        public bool LoseEffectReturn { get; set; }
        public bool IncludeSecondTool { get; set; }
        public bool HoldFirstToolRead { get; set; }
        private int _heldFirstToolRead;
        public TaskCompletionSource FirstToolReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstToolRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Effects { get; private set; }
        public string? DispatchedInvocation { get; private set; }
        public string? ObservedInvocation { get; private set; }
        public int OutcomeObservations { get; private set; }
        public int NativeObservations { get; private set; }
        public BrowserSnapshot State { get; } = new(null, "controlled", false, false, false, "controlled");
        public event EventHandler<BrowserSnapshot>? StateChanged { add { } remove { } }
        public async Task<string?> ExecuteOwnedScriptAsync(IBrowserNativeDocumentSelection originalDocument, string script, IBrowserOwnedScriptDispatchAdmission admission, CancellationToken token)
        {
            NativeObservations++;
            RequireDocument(originalDocument);
            await using var lease = await admission.AcquireAsync(this, token) ?? throw new UnauthorizedAccessException();
            if (!await lease.CheckAsync(token)) throw new UnauthorizedAccessException();
            RequireDocument(originalDocument);
            string result;
            if (script.Contains("Promise.resolve().then", StringComparison.Ordinal))
            {
                Effects++; DispatchedInvocation = ReadInvocation(script);
                if (LoseEffectReturn) throw new IOException("Actual controlled emission return lost after accepted dispatch.");
                result = "{\"started\":true}";
            }
            else if (script.Contains("status:'unobserved'", StringComparison.Ordinal))
            {
                OutcomeObservations++; ObservedInvocation = ReadInvocation(script);
                result = ObservedInvocation == DispatchedInvocation
                    ? "{\"status\":\"completed\",\"result\":{\"observed\":true}}"
                    : "{\"status\":\"unobserved\"}";
            }
            else if (script.Contains("Tools:supported", StringComparison.Ordinal))
            {
                using var schema = JsonDocument.Parse(Schema);
                var tools = new List<object> { new { name = "actual-tool", description = "Untrusted page description", inputSchema = schema.RootElement } };
                if (IncludeSecondTool) tools.Add(new { name = "newest-tool", description = "Newest displayed tool", inputSchema = schema.RootElement });
                result = JsonSerializer.Serialize(new { Document, Tools = tools });
            }
            else if (script.Contains("Document:{", StringComparison.Ordinal))
            {
                using var schema = JsonDocument.Parse(Schema);
                var name = script.Contains("\"newest-tool\"", StringComparison.Ordinal) ? "newest-tool" : "actual-tool";
                if (HoldFirstToolRead && name == "actual-tool" && Interlocked.CompareExchange(ref _heldFirstToolRead, 1, 0) == 0)
                {
                    FirstToolReadEntered.TrySetResult(); await ReleaseFirstToolRead.Task.WaitAsync(token);
                }
                result = JsonSerializer.Serialize(new { Document, ToolName = name, InputSchema = schema.RootElement });
            }
            else result = JsonSerializer.Serialize(Document);
            if (!await lease.CheckAsync(CancellationToken.None)) throw new InvalidOperationException("Observed outcome unconfirmed.");
            RequireDocument(originalDocument);
            return result;
        }
        private static string ReadInvocation(string actualScript)
        {
            const string marker = "const request = ";
            var start = actualScript.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var end = actualScript.IndexOf(';', start);
            using var request = JsonDocument.Parse(actualScript[start..end]);
            return request.RootElement.GetProperty("InvocationID").GetString()
                ?? throw new InvalidOperationException("Actual issuer correlation required.");
        }
        public Task<string?> EvaluateObservationAsync(string script, CancellationToken token)
        {
            using var schema = JsonDocument.Parse(Schema);
            return Task.FromResult<string?>(JsonSerializer.Serialize(new { Document.Origin,
                DocumentId = Document.DocumentID, Document.BrowserVersion, Document.Capability, Document.Supported,
                ToolName = "actual-tool", InputSchema = schema.RootElement }));
        }
        public Task<string?> ExecuteScriptGuardedAsync(string script, IBrowserScriptDispatchAdmission admission, CancellationToken token) => throw new NotSupportedException();
        public Task<string?> ExecuteScriptAsync(string script, CancellationToken token) => throw new InvalidOperationException("No legacy fallback.");
        public Task NavigateAsync(Uri address, CancellationToken token) { _documentGeneration++; return Task.CompletedTask; }
        public Task GoBackAsync(CancellationToken token) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken token) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public Task OpenDeveloperToolsAsync(CancellationToken token) => Task.CompletedTask;
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "actual.sqlite");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
