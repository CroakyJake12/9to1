using System.Text.Json;
using NineToOne.Web.Accounts;
using NineToOne.Web.Services;

namespace Haven.Desktop.Tests;

// Presentation counterexamples only. Scripted wire records do not prove live CAKE auth.
public sealed class NativeAccountObservationFenceTests
{
    [Fact]
    public async Task Cleared_generation_rejects_original_delayed_read_and_creates_no_followup()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        await using var view = new AccountBrowserBindings(new Transport(action =>
        {
            calls.Add(action); reached.TrySetResult(); return raw.Task;
        }), write => { write(); return Task.CompletedTask; });
        var original = view.DispatchAsync("Refresh", null, TestContext.Current.CancellationToken).AsTask();
        await reached.Task;
        view.ClearAccountObservations("The actual native session expired.");
        raw.SetResult(Current()); // Original backend ignored cancellation/generation retirement.
        await original;
        Assert.Equal(new[] { "GetCurrent" }, calls);
        Assert.True(view.TryGetValue("HasAccount", out var visible)); Assert.Equal(false, visible);
        Assert.True(view.TryGetValue("Sessions", out var rows)); Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<JsonElement>>(rows));
    }

    [Fact]
    public async Task Clearing_during_original_queued_presentation_prevents_old_command_from_using_new_generation()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action? originalPublication = null; var first = true; var reads = 0;
        await using var view = new AccountBrowserBindings(new Transport(_ =>
        { reads++; return Task.FromResult(Current()); }), write =>
        {
            if (first) { first = false; originalPublication = write; reached.TrySetResult(); return queued.Task; }
            write(); return Task.CompletedTask;
        });
        var original = view.DispatchAsync("Refresh", null, TestContext.Current.CancellationToken).AsTask();
        await reached.Task;
        view.ClearAccountObservations("The actual native session changed.");
        Assert.NotNull(originalPublication); originalPublication(); queued.SetResult();
        await original; // SAME queued callback and dispatch settle; neither is abandoned.
        Assert.Equal(0, reads);
        Assert.True(view.TryGetValue("HasAccount", out var visible)); Assert.Equal(false, visible);
        Assert.True(view.TryGetValue("CanEdit", out var edit)); Assert.Equal(false, edit);
    }

    [Fact]
    public async Task Clear_removes_profile_draft_session_selection_and_confirmation_but_allows_explicit_fresh_read()
    {
        await using var view = new AccountBrowserBindings(new Transport(action => Task.FromResult(action switch
        {
            "GetCurrent" => Current(),
            "GetProfile" => Ok(new { profile = new { accountId = AccountId, name = "Synthetic", username = "synthetic", icon = "", pronouns = "", job = "", revision = 1 } }),
            "ListSessions" => Ok(new { sessions = new[] { new { accountId = AccountId, sessionId = SessionId, deviceName = "Synthetic" } } }),
            _ => throw new InvalidOperationException("An observation control must not perform an auth mutation.")
        })), write => { write(); return Task.CompletedTask; });
        await view.DispatchAsync("Refresh", null, TestContext.Current.CancellationToken);
        Assert.True(view.TrySetValue("Draft.name", "Unsaved synthetic draft"));
        await view.DispatchAsync("SelectSession", JsonSerializer.SerializeToElement(new { sessionId = SessionId }), TestContext.Current.CancellationToken);
        Assert.True(view.TryGetValue("HasConfirmation", out var confirmation)); Assert.Equal(true, confirmation);
        view.ClearAccountObservations("The genuine session changed.");
        foreach (var path in new[] { "HasAccount", "HasProfile", "HasChanges", "HasConfirmation", "CanSessionActions" })
        { Assert.True(view.TryGetValue(path, out var value)); Assert.Equal(false, value); }
        Assert.True(view.TryGetValue("Draft.name", out var draft)); Assert.Equal("", draft);
        await view.DispatchAsync("Refresh", null, TestContext.Current.CancellationToken); // New explicit command, rather than resuming old work.
        Assert.True(view.TryGetValue("HasAccount", out var current)); Assert.Equal(true, current);
    }

    private const string AccountId = "12345678-1234-4234-8234-123456789abc";
    private const string SessionId = "87654321-4321-4321-8321-cba987654321";
    private static JsonElement Current() => Ok(new { accountId = AccountId, displayName = "Synthetic" });
    private static JsonElement Ok(object body) => JsonSerializer.SerializeToElement(new { ok = true, status = 200, body });
    private sealed class Transport(Func<string, Task<JsonElement>> original) : IAccountBrowserTransport
    {
        public Task<JsonElement> InvokeAsync(string action, JsonElement? arguments, CancellationToken cancellationToken) => original(action);
    }
}
