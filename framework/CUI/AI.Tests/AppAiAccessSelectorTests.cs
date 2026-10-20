using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using NineToOne.Cui.AI;
using Xunit;

namespace NineToOne.Cui.AI.Tests;

/// <summary>
/// Causal tests for the reusable canonical AI access-mode selector. These exercise the real
/// <see cref="FloatingAiBarState"/>, <see cref="AppAiCoordinator"/> and
/// <see cref="AppAiContracts"/> over the new <see cref="AppAiAccessSelector"/> adapter, so the
/// selected mode is verified to affect the actual request path rather than a mirror snapshot.
/// </summary>
public sealed class AppAiAccessSelectorTests
{
    [Fact]
    public void DefaultAccessModeIsReadOnlyAndSelectorReflectsIt()
    {
        using var state = CreateState();
        using var selector = new AppAiAccessSelector(state);

        Assert.Equal(AppAiAccessMode.ReadOnly, state.AccessMode);
        Assert.Equal(AppAiAccessMode.ReadOnly, selector.AccessMode);
        Assert.True(selector.IsReadOnly);
        Assert.False(selector.IsWriteMode);
        Assert.Equal("Read-only", selector.CurrentModeLabel);
        Assert.True(selector.CanChangeMode);
        Assert.False(selector.HasStatus);
    }

    [Fact]
    public async Task SelectingWriteThenReadOnlyRoundtripsIntoActualRequestAccessMode()
    {
        var dulche = new FakeDulche();
        using var state = CreateState(dulche);
        using var selector = new AppAiAccessSelector(state);

        selector.SelectWrite();
        Assert.True(selector.IsWriteMode);
        Assert.Equal(AppAiAccessMode.Write, state.AccessMode);

        state.Prompt = "Summarise";
        await state.SubmitAsync();
        Assert.Equal(AppAiAccessMode.Write, dulche.LastPrompt!.AccessMode);

        selector.SelectReadOnly();
        Assert.True(selector.IsReadOnly);
        Assert.Equal(AppAiAccessMode.ReadOnly, state.AccessMode);

        state.Prompt = "Explain";
        await state.SubmitAsync();
        Assert.Equal(AppAiAccessMode.ReadOnly, dulche.LastPrompt!.AccessMode);
    }

    [Fact]
    public void DeniedObservationIsPresentedWhileOwnerModeStateIsPreserved()
    {
        var status = new FakeStatusSource
        {
            Current = new AppAiAccessStatus(AppAiAccessStatusKind.Denied, "Home did not approve this action.")
        };
        using var state = CreateState();
        using var selector = new AppAiAccessSelector(state, status);

        // The denial is presented as a genuine observation...
        Assert.True(selector.HasStatus);
        Assert.Equal(AppAiAccessStatusKind.Denied, selector.Status!.Kind);
        Assert.Contains("did not approve", selector.Status.Message, StringComparison.Ordinal);

        // ...but it does not manufacture or flip the actual owner mode/state.
        Assert.Equal(AppAiAccessMode.ReadOnly, selector.AccessMode);
        Assert.Equal(AppAiAccessMode.ReadOnly, state.AccessMode);
        Assert.False(selector.IsWriteMode);
    }

    [Fact]
    public void InvalidEnumSelectionIsRejectedByTheRealAuthority()
    {
        using var state = CreateState();
        using var selector = new AppAiAccessSelector(state);

        Assert.Throws<ArgumentOutOfRangeException>(() => selector.SetMode((AppAiAccessMode)999));

        // The real owner state is untouched by a rejected selection.
        Assert.Equal(AppAiAccessMode.ReadOnly, state.AccessMode);
        Assert.Equal(AppAiAccessMode.ReadOnly, selector.AccessMode);
    }

    [Fact]
    public void PendingAndUnavailableStatusAreExposedFromTypedObservations()
    {
        Assert.Null(AppAiAccessStatusMapper.FromOutcome(AppAiApprovalOutcome.Approved));

        var pending = AppAiAccessStatusMapper.FromOutcome(AppAiApprovalOutcome.Pending, "Waiting");
        Assert.NotNull(pending);
        Assert.Equal(AppAiAccessStatusKind.Pending, pending.Kind);
        Assert.Equal("Waiting", pending.Message);

        var unavailable = AppAiAccessStatusMapper.FromOutcome(AppAiApprovalOutcome.Unavailable);
        Assert.NotNull(unavailable);
        Assert.Equal(AppAiAccessStatusKind.Unavailable, unavailable.Kind);

        var deniedFromAction = AppAiAccessStatusMapper.FromActionResult(
            AppAiActionResult.Rejected("Denied", "approval-denied"));
        Assert.NotNull(deniedFromAction);
        Assert.Equal(AppAiAccessStatusKind.Denied, deniedFromAction.Kind);
        Assert.Equal("Denied", deniedFromAction.Message);

        var pendingFromAction = AppAiAccessStatusMapper.FromActionResult(
            AppAiActionResult.Rejected("Pending", "approval-pending"));
        Assert.Equal(AppAiAccessStatusKind.Pending, pendingFromAction!.Kind);

        Assert.Null(AppAiAccessStatusMapper.FromActionResult(AppAiActionResult.Success("ok")));
        Assert.Null(AppAiAccessStatusMapper.FromActionResult(null!));
    }

    [Fact]
    public void DisposeUnsubscribesFromStateSoLateChangesAreNotPublished()
    {
        using var state = CreateState();
        var selector = new AppAiAccessSelector(state);
        var published = 0;
        selector.Changed += (_, _) => published++;

        state.SetWriteMode();
        Assert.Equal(1, published);

        selector.Dispose();
        state.SetReadOnly();
        Assert.Equal(1, published); // no publication after dispose

        Assert.Throws<ObjectDisposedException>(() => selector.SetMode(AppAiAccessMode.Write));
        Assert.Throws<ObjectDisposedException>(() => selector.SelectWrite());
    }

    [Fact]
    public void SelectorModeStaysDistinctFromHostEditingState()
    {
        var context = new FakeContext { HostState = "Edit" };
        using var state = CreateState(context);
        using var selector = new AppAiAccessSelector(state);

        // The host surface is in Edit state, but the AI access mode is an orthogonal persistent
        // setting: default Read-only.
        Assert.Equal("Edit", context.HostState);
        Assert.Equal(AppAiAccessMode.ReadOnly, selector.AccessMode);

        selector.SelectWrite();
        Assert.Equal(AppAiAccessMode.Write, selector.AccessMode);
        Assert.Equal(AppAiAccessMode.Write, state.AccessMode);
        // Switching AI mode does NOT alter the host editing state.
        Assert.Equal("Edit", context.HostState);

        selector.SelectReadOnly();
        Assert.Equal(AppAiAccessMode.ReadOnly, selector.AccessMode);
        Assert.Equal("Edit", context.HostState);
    }

    [Fact]
    public void AuthoredSelectorIsCanonicalCuiWithSelectedTrigAndStatusPresentation()
    {
        // Load the packaged authored selector from the AI assembly's embedded resource (the same
        // manifest identity `FloatingAiBarScene` uses), not an unverified deployment path.
        using var stream = typeof(FloatingAiBarScene).Assembly.GetManifestResourceStream(
                "NineToOne.Cui.AI.UI.AiAccessSelector.cui")
            ?? throw new InvalidOperationException("The packaged AI access selector is not present.");
        using var reader = new StreamReader(stream);
        var document = new CuiRichParser().Parse(reader.ReadToEnd(), "AiAccessSelector.cui");
        var all = document.Components.SelectMany(component => component.DescendantsAndSelf()).ToArray();

        var readOnly = Assert.Single(all, component => component.AuthoredId == "ai-access-readonly");
        var write = Assert.Single(all, component => component.AuthoredId == "ai-access-write");
        var status = Assert.Single(all, component => component.AuthoredId == "ai-access-status");

        // Read-only and Write selected states bind to the live mode (not a literal snapshot).
        Assert.Contains("Access.IsReadOnly", AllBindingPaths(readOnly), StringComparison.Ordinal);
        Assert.Contains("Access.IsWriteMode", AllBindingPaths(write), StringComparison.Ordinal);

        // The actions represent the real read-only/write mode change commands.
        Assert.Equal("access.select-readonly", readOnly.Actions["action"].Name);
        Assert.Equal("access.select-write", write.Actions["action"].Name);

        // Status line is present and bound to the observed-status projection.
        Assert.Contains("Access.HasStatus", AllBindingPaths(status), StringComparison.Ordinal);
        Assert.Contains("Access.StatusMessage", AllBindingPaths(status), StringComparison.Ordinal);

        // A persistent current-mode label is exposed.
        var label = Assert.Single(all, component => component.AuthoredId == "ai-access-current-mode");
        Assert.Contains("Access.CurrentModeLabel", AllBindingPaths(label), StringComparison.Ordinal);
    }

    private static string AllBindingPaths(CuiComponent component)
    {
        var paths = new List<string>();
        foreach (var value in component.Properties.Values)
            if (value is CuiBindingValue binding) paths.Add(binding.Path);
        return string.Join("|", paths);
    }

    private static FloatingAiBarState CreateState() => CreateState(new FakeDulche());

    private static FloatingAiBarState CreateState(FakeDulche dulche) => CreateState(new FakeContext(), dulche);

    private static FloatingAiBarState CreateState(FakeContext context, FakeDulche? dulche = null)
    {
        var coordinator = new AppAiCoordinator(
            context, new FakeActions(), new FakeApprovals(), dulche ?? new FakeDulche());
        return new FloatingAiBarState(coordinator);
    }

    private sealed class FakeContext : IAppAiContext
    {
        public string HostState { get; init; } = "Edit";

        public ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AppAiContextSnapshot(
                "write", "document", "doc-1", "Current document", null,
                new Dictionary<string, JsonElement>(), AppAiDataSensitivity.UserContent,
                DateTimeOffset.UtcNow, "rev-1", [], HostState));
    }

    private sealed class FakeActions : IAppAiActions
    {
        public IReadOnlyList<AppAiActionDescriptor> Actions =>
        [
            new("insert-paragraph", "Insert paragraph", "Adds reviewed text.",
                AppAiActionRisk.ReversibleChange, true, "{\"type\":\"object\"}",
                AffectedObjectIds: ["section-1"], ImpactUnknown: false, ImpactSummary: "Updates one section"),
        ];

        public ValueTask<AppAiActionResult> ExecuteAsync(
            AppAiActionRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AppAiActionResult.Success("Inserted"));
    }

    private sealed class FakeApprovals : IAppAiApprovalVerifier
    {
        public ValueTask<bool> VerifyAsync(
            string appId, string actionId, string approvalToken, CancellationToken cancellationToken) =>
            ValueTask.FromResult(approvalToken == "approved");
    }

    private sealed class FakeDulche : IDulcheAppClient
    {
        public AppAiPrompt? LastPrompt { get; private set; }

        public async IAsyncEnumerable<AppAiResponseChunk> StreamAsync(
            AppAiPrompt prompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            await Task.Yield();
            yield return new AppAiResponseChunk("Done", IsFinal: true);
        }
    }

    private sealed class FakeStatusSource : IAppAiAccessStatusSource
    {
        private AppAiAccessStatus? _current;
        public AppAiAccessStatus? Current { get => _current; set { _current = value; Changed?.Invoke(this, EventArgs.Empty); } }
        public event EventHandler? Changed;
    }
}
