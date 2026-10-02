using System.Text.Json;
using Haven.Application;
using Haven.Browser;
namespace HavenOS.Apps.Browse;

public interface IBrowseWebMcpDisplay : IDisposable { ResourceScope Scope { get; } }
public interface IBrowseWebMcpReview { string RequestID { get; } bool OriginAvailable { get; } WebMcpPreparedReviewState SubmissionState { get; } }
public sealed record BrowseWebMcpOwnerResult(bool OutcomeKnown, bool AuditRecorded, string Code, JsonElement? Result = null);

/// <summary>Optional actual owner path. No bool-approval adapter, ambient session fallback,
/// browser-profile UUID or public JSON capability is accepted. Composition is explicitly required.</summary>
public sealed class BrowseOwnedWebMcpBinding(BrowserSessionService browser,
    IAuthenticatedResourceActorSource actors, IWebMcpOriginalActorApproval home, BrowseOwnedDocumentRegistry documents)
{
    private sealed record Display(BrowseOwnedWebMcpBinding Issuer, AuthenticatedResourceActor Actor,
        IOriginalBrowserSessionSelection Session, WebMcpDocument Document, WebMcpTool Tool,
        IBrowseOwnedDocumentSelection DocumentSelection) : IBrowseWebMcpDisplay
    {
        private int _revoked;
        public bool Revoked => Volatile.Read(ref _revoked) != 0;
        public ResourceScope Scope => DocumentSelection.Scope;
        public void Dispose() { Interlocked.Exchange(ref _revoked, 1); DocumentSelection.Dispose(); }
    }
    private sealed class Review(BrowseOwnedWebMcpBinding issuer, Display display, WebMcpInvocationRequest request,
        IWebMcpOriginalActorReview issued) : IBrowseWebMcpReview
    {
        public BrowseOwnedWebMcpBinding Issuer { get; } = issuer;
        public Display Display { get; } = display;
        public WebMcpInvocationRequest Request { get; } = request;
        public IWebMcpOriginalActorReview Issued { get; } = issued;
        public string RequestID => Issued.RequestId;
        public bool OriginAvailable { get; set; } = true;
        public WebMcpPreparedReviewState SubmissionState { get; set; } = WebMcpPreparedReviewState.OutcomeUnconfirmed;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public IWebMcpOriginalDispatch? Dispatch { get; set; }
        public BrowseWebMcpInvocationScripts? Scripts { get; set; }
        public bool DispatchReserved { get; set; }
        public WebMcpObservedOutcome? Outcome { get; set; }
        public JsonElement? Result { get; set; }
    }
    private const string Detect = """
        (() => {
          const key = Symbol.for('9to1.webmcp.document');
          const api = navigator.modelContextTesting;
          return JSON.stringify({Origin:location.origin,DocumentID:document[key] ?? '',
            BrowserVersion:navigator.userAgent,Capability:'navigator.modelContextTesting',
            Supported:!!api && typeof api.listTools === 'function' && typeof api.executeTool === 'function',
            CancellationSupported:false});
        })()
        """;
    public async Task<IBrowseWebMcpDisplay> OpenDisplayedToolAsync(AuthenticatedResourceActor expectedActor,
        WebMcpDocument observedDocument, WebMcpTool observedTool, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor); ArgumentNullException.ThrowIfNull(observedDocument);
        ArgumentNullException.ThrowIfNull(observedTool);
        var capturedTool = observedTool with { InputSchema = observedTool.InputSchema.Clone() };
        // Registry captures the actual attached host before its first actor/native await.
        var documentSelection = await documents.LoadForDisplayAsync(expectedActor, observedDocument, capturedTool, token).ConfigureAwait(false);
        var session = documents.GetOriginalSession(documentSelection);
        var display = new Display(this, expectedActor, session, observedDocument,
            capturedTool, documentSelection);
        try { await RequireAsync(display, token).ConfigureAwait(false); return display; }
        catch { display.Dispose(); throw; }
    }
    /// <summary>Actual app entry from an issuer-owned displayed discovery catalogue.
    /// Tool selection can never adopt a newly attached host with colliding public metadata.</summary>
    public async Task<IBrowseWebMcpDisplay> OpenDisplayedToolAsync(IBrowseOwnedDocumentCatalogue originalCatalogue,
        string selectedToolName, CancellationToken token = default)
    {
        var selected = await documents.SelectDisplayedToolAsync(originalCatalogue, selectedToolName, token).ConfigureAwait(false);
        var original = documents.GetDisplayedMetadata(selected);
        var display = new Display(this, original.Actor, documents.GetOriginalSession(selected),
            original.Document, original.Tool, selected);
        try { await RequireAsync(display, token).ConfigureAwait(false); return display; }
        catch { display.Dispose(); throw; }
    }
    private async Task<Display> RequireAsync(IBrowseWebMcpDisplay selected, CancellationToken token)
    {
        if (selected is not Display display || !ReferenceEquals(display.Issuer, this) || display.Revoked)
            throw new UnauthorizedAccessException("Private original displayed browser selection required.");
        var actual = await browser.ExecuteOriginalSessionOwnedScriptAsync(display.Session, Detect,
            new ObservationAdmission(display, actors), token).ConfigureAwait(false);
        if (JsonSerializer.Deserialize<WebMcpDocument>(actual) != display.Document || display.Revoked
            || await actors.GetCurrentAsync(token).ConfigureAwait(false) != display.Actor || display.Revoked)
            throw new UnauthorizedAccessException("Original displayed browser document or principal changed.");
        return display;
    }
    // Used ONLY for the private fixed Detect/correlated read scripts below. This is an
    // original-principal observation, NEVER a Home execution grant or effect admission.
    private sealed class ObservationAdmission(Display display, IAuthenticatedResourceActorSource source) : IBrowserOwnedScriptDispatchAdmission
    {
        public ValueTask<IBrowserScriptDispatchLease?> AcquireAsync(IBrowserNativeEntryObservation entry, CancellationToken token)
            => ValueTask.FromResult<IBrowserScriptDispatchLease?>(display.Revoked ? null : new ObservationLease(display, source));
    }
    private sealed class ObservationLease(Display display, IAuthenticatedResourceActorSource source) : IBrowserScriptDispatchLease
    {
        private int _disposed;
        public async ValueTask<bool> CheckAsync(CancellationToken token)
        {
            if (display.Revoked || Volatile.Read(ref _disposed) != 0) return false;
            var current = await source.GetCurrentAsync(token).ConfigureAwait(false);
            return current == display.Actor && !display.Revoked && Volatile.Read(ref _disposed) == 0;
        }
        public ValueTask DisposeAsync() { Interlocked.Exchange(ref _disposed, 1); return ValueTask.CompletedTask; }
    }
    public async Task<IBrowseWebMcpReview> ReviewAsync(IBrowseWebMcpDisplay selected, JsonElement arguments,
        IReadOnlyList<ResourceScope> actualOwnerScopes, CancellationToken token = default)
    {
        // Caller JSON and scopes are proposals. The actual Home issuer must resolve owning access.
        var captured = arguments.Clone();
        if (selected is not Display display || !ReferenceEquals(display.Issuer, this))
            throw new UnauthorizedAccessException("Private original display required.");
        ArgumentNullException.ThrowIfNull(actualOwnerScopes);
        var declared = actualOwnerScopes.Count;
        if (declared != 1) throw new UnauthorizedAccessException("Exact singleton displayed document scope required.");
        var scopes = new List<ResourceScope>(); // Never allocate from untrusted declared Count.
        foreach (var scope in actualOwnerScopes)
        {
            if (scopes.Count >= declared) throw new ArgumentException("Scope enumeration exceeds declared count.", nameof(actualOwnerScopes));
            scopes.Add(scope);
        }
        if (scopes.Count != declared) throw new ArgumentException("Scope enumeration differs from declared count.", nameof(actualOwnerScopes));
        if (scopes.Count != 1 || scopes[0] != display.Scope)
            throw new UnauthorizedAccessException("Exact private displayed document scope required.");
        var request = new WebMcpInvocationRequest(display.Document.Origin, display.Document.DocumentID,
            display.Document.BrowserVersion, display.Document.Capability, display.Document.Supported,
            display.Tool.Name, display.Tool.InputSchema.Clone(), captured);
        if (!request.IsValid()) throw new ArgumentException("Observed tool schema rejected arguments.");
        await RequireAsync(display, token).ConfigureAwait(false);
        // Actual private preparation has NO durable authorization side effect. Retain the
        // original issuer handle/ID BEFORE the FIRST submission await, even if its return is lost.
        var issued = home.PrepareReview(display.Actor, request, scopes.ToArray());
        var retained = new Review(this, display, request, issued);
        try
        {
            retained.SubmissionState = (await issued.SubmitPreparedAsync(token).ConfigureAwait(false)).State;
            await RequireAsync(display, token).ConfigureAwait(false);
        }
        catch (Exception) { retained.OriginAvailable = false; }
        return retained;
    }
    public async Task<BrowseWebMcpOwnerResult> CommitOrObserveAsync(IBrowseWebMcpReview issuedReview, CancellationToken token = default)
    {
        if (issuedReview is not Review review || !ReferenceEquals(review.Issuer, this))
            throw new UnauthorizedAccessException("Private original Home review required.");
        await review.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (review.Outcome is not null)
            {
                var audit = await review.Dispatch!.FinishAuditAsync(token).ConfigureAwait(false);
                return new(true, audit.AuditRecorded, review.Outcome.Code, review.Result);
            }
            await RequireAsync(review.Display, token).ConfigureAwait(false);
            if (!review.DispatchReserved)
            {
                if (review.SubmissionState == WebMcpPreparedReviewState.OutcomeUnconfirmed)
                {
                    // Actual issuer only observes the FULL exact original durable intent here;
                    // it must never authorize again after the reserved first submission attempt.
                    var recovered = await review.Issued.SubmitPreparedAsync(token).ConfigureAwait(false);
                    review.SubmissionState = recovered.State;
                    if (recovered.State == WebMcpPreparedReviewState.OutcomeUnconfirmed)
                        return new(false, false, recovered.Code);
                }
                if (review.SubmissionState == WebMcpPreparedReviewState.Rejected)
                {
                    var rejected = await review.Issued.FinishAdmissionAuditAsync(token).ConfigureAwait(false);
                    return new(false, rejected.AuditRecorded, rejected.Code);
                }
                var begin = await review.Issued.BeginDispatchAsync(review.Request, token).ConfigureAwait(false);
                if (begin.State == WebMcpDispatchBeginState.PendingApproval) return new(false, false, begin.Code);
                if (begin.State != WebMcpDispatchBeginState.Ready || begin.Dispatch is null)
                {
                    var recovery = await review.Issued.FinishAdmissionAuditAsync(token).ConfigureAwait(false);
                    return new(false, recovery.AuditRecorded, begin.Code);
                }
                review.Dispatch = begin.Dispatch;
                review.Scripts = new(begin.Dispatch.InvocationId, review.Display.Document, review.Display.Tool, review.Request.Arguments);
                review.DispatchReserved = true; // BEFORE actual emission await. Never call Dispatch script again.
                try
                {
                    await browser.ExecuteOriginalSessionOwnedScriptAsync(review.Display.Session, review.Scripts.Dispatch,
                        new EffectAdmission(review.Display, review.Request, begin.Dispatch), token).ConfigureAwait(false);
                }
                catch (Exception) { return new(false, false, "WebMcpOutcomeUnconfirmed"); }
            }
            try
            {
                var json = await browser.ExecuteOriginalSessionOwnedScriptAsync(review.Display.Session, review.Scripts!.Observe,
                    new ObservationAdmission(review.Display, actors), token).ConfigureAwait(false);
                await RequireAsync(review.Display, token).ConfigureAwait(false); // Original observation cannot adopt a switched page/principal.
                using var parsed = JsonDocument.Parse(json);
                var status = parsed.RootElement.GetProperty("status").GetString();
                if (status is not ("completed" or "failed")) return new(false, false, "WebMcpOutcomeUnconfirmed");
                if (status == "completed") review.Result = parsed.RootElement.GetProperty("result").Clone();
                review.Outcome = new(review.Dispatch!.InvocationId,
                    status == "completed" ? WebMcpObservedOutcomeKind.Succeeded : WebMcpObservedOutcomeKind.Failed,
                    status == "completed" ? "WebMcpObservedCompleted" : "WebMcpObservedFailed", "Actual correlated originating page observation; no rollback claim.");
            }
            catch (Exception) { return new(false, false, "WebMcpOutcomeUnconfirmed"); }
            // First known outcome reserved BEFORE audit await. Audit loss cannot replay JS.
            var completed = await review.Dispatch!.CompleteObservedAsync(review.Outcome!, token).ConfigureAwait(false);
            return new(true, completed.AuditRecorded, review.Outcome!.Code, review.Result);
        }
        finally { review.Gate.Release(); }
    }
    /// <summary>Audit-only recovery for the retained issuer context, including after the
    /// original page/session was revoked. This never requests a new approval, enters native
    /// dispatch, observes a page or changes the first known operation outcome.</summary>
    public async Task<BrowseWebMcpOwnerResult> FinishAuditAsync(IBrowseWebMcpReview issuedReview,
        CancellationToken token = default)
    {
        if (issuedReview is not Review review || !ReferenceEquals(review.Issuer, this))
            throw new UnauthorizedAccessException("Private original Home review required.");
        await review.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (review.Dispatch is not null)
            {
                var audit = await review.Dispatch!.FinishAuditAsync(token).ConfigureAwait(false);
                return new(review.Outcome is not null, audit.AuditRecorded,
                    review.Outcome?.Code ?? "WebMcpOutcomeUnconfirmed", review.Result);
            }
            var retained = await review.Issued.FinishAdmissionAuditAsync(token).ConfigureAwait(false);
            return new(false, retained.AuditRecorded, retained.Code);
        }
        finally { review.Gate.Release(); }
    }
    private sealed class EffectAdmission(Display display, WebMcpInvocationRequest request, IWebMcpOriginalDispatch issued)
        : IBrowserOwnedScriptDispatchAdmission
    {
        public async ValueTask<IBrowserScriptDispatchLease?> AcquireAsync(IBrowserNativeEntryObservation entry, CancellationToken token)
        {
            if (display.Revoked) return null;
            var toolName = JsonSerializer.Serialize(request.ToolName);
            var script = $$"""
                (() => {
                  const api = navigator.modelContextTesting;
                  const tool = api && typeof api.listTools === 'function' ? api.listTools().find(value => value.name === {{toolName}}) : null;
                  const schema = tool && (typeof tool.inputSchema === 'string' ? JSON.parse(tool.inputSchema) : tool.inputSchema);
                  return JSON.stringify({Origin:location.origin,DocumentId:document[Symbol.for('9to1.webmcp.document')] ?? '',
                    BrowserVersion:navigator.userAgent,Capability:'navigator.modelContextTesting',
                    Supported:!!api && typeof api.listTools === 'function' && typeof api.executeTool === 'function',
                    ToolName:tool?.name ?? '',InputSchema:schema ?? null});
                })()
                """;
            // This is a DIRECT observation in the actual owning native turn, not recursive
            // Browser dispatch and not retained request data masquerading as a fresh binding.
            var raw = await entry.EvaluateObservationAsync(script, token).ConfigureAwait(false);
            if (display.Revoked || string.IsNullOrEmpty(raw)) return null;
            try { raw = JsonSerializer.Deserialize<string>(raw) ?? raw; }
            catch (JsonException) { } // Some supported hosts already return unwrapped JSON.
            using var observed = JsonDocument.Parse(raw);
            var value = observed.RootElement;
            var fresh = new WebMcpInvocationRequest(value.GetProperty("Origin").GetString()!,
                value.GetProperty("DocumentId").GetString()!, value.GetProperty("BrowserVersion").GetString()!,
                value.GetProperty("Capability").GetString()!, value.GetProperty("Supported").GetBoolean(),
                value.GetProperty("ToolName").GetString()!, value.GetProperty("InputSchema").Clone(), request.Arguments.Clone());
            if (!fresh.IsValid() || fresh.Origin != request.Origin || fresh.DocumentId != request.DocumentId
                || fresh.BrowserVersion != request.BrowserVersion || fresh.Capability != request.Capability
                || fresh.Supported != request.Supported || fresh.ToolName != request.ToolName
                || !JsonElement.DeepEquals(fresh.InputSchema, request.InputSchema) || display.Revoked) return null;
            var lease = await issued.EnterFinalDispatchAsync(fresh, token).ConfigureAwait(false);
            if (lease is null) return null; // Missing actual issuer lease NEVER falls back to old boolean check.
            if (display.Revoked) { await lease.DisposeAsync().ConfigureAwait(false); return null; }
            return new RetainedLease(display, lease);
        }
    }
    private sealed class RetainedLease(Display display, IWebMcpFinalDispatchLease actual) : IBrowserScriptDispatchLease
    {
        private int _disposed;
        public async ValueTask<bool> CheckAsync(CancellationToken token)
        {
            if (Volatile.Read(ref _disposed) != 0 || display.Revoked) return false;
            var current = await actual.CheckAsync(token).ConfigureAwait(false);
            return current && Volatile.Read(ref _disposed) == 0 && !display.Revoked;
        }
        public async ValueTask DisposeAsync()
        { if (Interlocked.Exchange(ref _disposed, 1) == 0) await actual.DisposeAsync().ConfigureAwait(false); }
    }
}
