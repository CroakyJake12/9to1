using CakeOS.Cui.Runtime;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using NineToOne.Web.AI.Chat;

// Scripted interface boundary only. The controller, CUI resource, projection and
// adapter are actual source. No provider, durable store or access grant is tested.
var cases = new (string Name, Func<Task> Run)[]
{
    ("owner CUI and default registry report actual missing specialized controls", async () =>
    {
        using var adapter = new ChatSpaceBrowserReadAdapter(new ScriptedReadBackend());
        Check(adapter.Document.Components.Count > 0, "Actual embedded owner CUI must load.");
        Check(adapter.MissingElementTypes(new CuiControlRegistry()).Contains("AdaptiveSplit"), "No empty Panel substitution may hide missing AdaptiveSplit.");
        await Task.CompletedTask;
    }),
    ("actual controller projection preserves canonical conversation identity and owner draft", async () =>
    {
        var backend = new ScriptedReadBackend();
        using var adapter = new ChatSpaceBrowserReadAdapter(backend);
        await adapter.LoadAsync(backend.Conversation.Id);
        Check(adapter.OwnerState.Conversation?.Id == backend.Conversation.Id, "Owning ID changed.");
        Check(adapter.TryGetValue("composer.draft", out var draft) && Equals(draft, "owner draft"), "Owning draft did not project.");
        Check(adapter.OwnerState.Status.Tone == ChatSpaceStatusTone.Success, "Actual owner read did not finish.");
        Check(backend.Reads == 3 && backend.Mutations == 0, "Read adapter dispatched unexpected owning operations.");
    }),
    ("empty canonical ID fails before backend reads", async () =>
    {
        var backend = new ScriptedReadBackend(); using var adapter = new ChatSpaceBrowserReadAdapter(backend);
        await Throws<ArgumentException>(() => adapter.LoadAsync(Guid.Empty));
        Check(backend.Reads == 0 && backend.Mutations == 0, "Invalid ID reached backend.");
    }),
    ("owner error status survives without successful conversation or mutation", async () =>
    {
        var backend = new ScriptedReadBackend { ReadError = new UnauthorizedAccessException("scripted owner denial") };
        using var adapter = new ChatSpaceBrowserReadAdapter(backend);
        await adapter.LoadAsync(backend.Conversation.Id);
        Check(adapter.OwnerState.Status.Tone == ChatSpaceStatusTone.Error, "Actual owner denial was hidden.");
        Check(adapter.OwnerState.Status.Message == "scripted owner denial" && adapter.OwnerState.Conversation is null, "Denied read exposed successful conversation.");
        Check(backend.Mutations == 0, "Owner denial triggered mutation.");
    }),
    ("cancellation followed by deliberately late backend completion cannot expose private read", async () =>
    {
        var backend = new ScriptedReadBackend { Delay = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var adapter = new ChatSpaceBrowserReadAdapter(backend); using var caller = new CancellationTokenSource();
        var pending = adapter.LoadAsync(backend.Conversation.Id, caller.Token);
        try
        {
            await backend.ReadStarted.Task; caller.Cancel(); backend.Delay.SetResult(true);
            await Throws<OperationCanceledException>(() => pending);
            Check(adapter.OwnerState.Conversation is null && adapter.OwnerState.RecentChats.Count == 0, "Late cancelled owner data became visible.");
            Check(backend.Mutations == 0, "Cancelled read dispatched mutation.");
        }
        finally
        {
            caller.Cancel(); backend.Delay.TrySetResult(true);
            try { await pending; } catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
        }
    }),
    ("dispose hides completed state and rejects further reads", async () =>
    {
        var backend = new ScriptedReadBackend(); var adapter = new ChatSpaceBrowserReadAdapter(backend);
        await adapter.LoadAsync(backend.Conversation.Id); adapter.Dispose(); adapter.Dispose();
        Check(adapter.OwnerState.Conversation is null && !adapter.TryGetValue("conversation.title", out _), "Disposed private data remained readable.");
        await Throws<ObjectDisposedException>(() => adapter.LoadAsync(backend.Conversation.Id));
        Check(backend.Reads == 3 && backend.Mutations == 0, "Disposed adapter dispatched reads or mutations.");
    }),
    ("dispose during delayed read preserves cancellation and hides late owner completion", async () =>
    {
        var backend = new ScriptedReadBackend { Delay = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var adapter = new ChatSpaceBrowserReadAdapter(backend); var pending = adapter.LoadAsync(backend.Conversation.Id);
        try
        {
            await backend.ReadStarted.Task; adapter.Dispose(); backend.Delay.SetResult(true);
            await Throws<OperationCanceledException>(() => pending);
            Check(adapter.OwnerState.Conversation is null && !adapter.TryGetValue("composer.draft", out _), "Late disposed owner completion reopened context.");
            Check(backend.Mutations == 0, "Disposed read dispatched mutation.");
        }
        finally
        {
            adapter.Dispose(); backend.Delay.TrySetResult(true);
            try { await pending; } catch (OperationCanceledException) { }
        }
    }),
    ("concurrent read is refused without a hidden queue", async () =>
    {
        var backend = new ScriptedReadBackend { Delay = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var adapter = new ChatSpaceBrowserReadAdapter(backend);
        var pending = adapter.LoadAsync(backend.Conversation.Id);
        try
        {
            await backend.ReadStarted.Task;
            await Throws<InvalidOperationException>(() => adapter.LoadAsync(backend.Conversation.Id));
            Check(backend.Reads == 3, "Concurrent refusal started another owner read.");
            backend.Delay.SetResult(true); await pending;
            Check(adapter.OwnerState.Conversation?.Id == backend.Conversation.Id && backend.Mutations == 0, "Original read did not settle coherently.");
        }
        finally { backend.Delay.TrySetResult(true); await pending; }
    }),
    ("denied-refresh-preserves-owner-error-without-reexposing-prior-private-read", async () =>
    {
        var backend = new ScriptedReadBackend(); using var adapter = new ChatSpaceBrowserReadAdapter(backend);
        await adapter.LoadAsync(backend.Conversation.Id);
        Check(adapter.OwnerState.Conversation?.Id == backend.Conversation.Id
            && adapter.TryGetValue("composer.draft", out var beforeDraft) && Equals(beforeDraft, "owner draft"),
            "Actual initial owner read must expose the fixture before permission denial.");
        backend.ReadError = new UnauthorizedAccessException("scripted current owner denial");
        await adapter.LoadAsync(backend.Conversation.Id);
        var failed = adapter.OwnerState;
        Check(failed.Status.Tone == ChatSpaceStatusTone.Error && failed.Status.Message == "scripted current owner denial",
            "Actual owner denial diagnostic must survive; no fabricated successful read.");
        Check(failed.Conversation is null && failed.RecentChats.Count == 0 && failed.Models.Count == 0
            && failed.Composer.Draft == string.Empty && failed.Composer.Attachments.Count == 0,
            "Denied refresh cannot re-expose the previously authorized private conversation, draft or inventory.");
        Check(adapter.TryGetValue("composer.canSend", out var send) && Equals(send, false) && backend.Mutations == 0,
            "Denial cannot enable provider execution or dispatch mutation.");
    }),
    ("actual owner markup actions and composer cannot enable execution from a read", async () =>
    {
        var backend = new ScriptedReadBackend(); using var adapter = new ChatSpaceBrowserReadAdapter(backend);
        await adapter.LoadAsync(backend.Conversation.Id);
        var actions = new[] { "chat.new", "chat.open", "chat.send", "chat.attach", "chat.branch", "chat.regenerate", "chat.switch-branch", "chat.select-model", "chat.update-draft", "chat.remove-attachment" };
        Check(actions.All(action => adapter.IsActionAvailable(action) == false), "Read-only source enabled an owner mutation/executor.");
        Check(adapter.TryGetValue("composer.canSend", out var canSend) && Equals(canSend, false), "Read supplied executor authority.");
        Check(backend.Mutations == 0, "Read adapter invoked an unavailable mutation.");
    })
};
var failed = 0; var executed = 0; var passed = 0; var timedOut = false;
foreach (var (name, run) in cases)
{
    executed++;
    try { await run().WaitAsync(TimeSpan.FromSeconds(15)); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error)
    {
        failed++; Console.WriteLine("FAIL " + name + " " + error);
        if (error is TimeoutException) { timedOut = true; break; } // Original work is not cancelled by WaitAsync; external owned-family guard must drain it.
    }
}
foreach (var (name, _) in cases.Skip(executed)) Console.WriteLine("NOT_RUN " + name);
Console.WriteLine($"SUPPORTING_SCRIPTED_INTERFACE_UNIT discovered={cases.Length} executed={executed} pass={passed} fail={failed} skip=0 notRun={cases.Length - executed} timedOut={timedOut}");
return failed == 0 && executed == cases.Length ? 0 : 1;

static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}

sealed class ScriptedReadBackend : IChatSpaceBackend
{
    public Conversation Conversation { get; } = new(Guid.Parse("56a4ef89-f83c-4ec7-8c1c-96f7c18dfcb0"), HavenMode.Chat, ConversationKind.Chat,
        "Owner conversation", null, null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    public TaskCompletionSource<bool> ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool>? Delay { get; init; }
    public Exception? ReadError { get; set; }
    public int Reads { get; private set; }
    public int Mutations { get; private set; }
    public async Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken token)
    {
        Reads++; ReadStarted.TrySetResult(true);
        if (ReadError is not null) throw ReadError;
        if (Delay is not null) await Delay.Task; // Intentional negative control: ignores caller cancellation.
        return [Conversation];
    }
    public Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken token)
    { Reads++; return Task.FromResult(new ChatSpaceModelInventory([])); }
    public Task<ChatSpaceConversationData> GetConversationAsync(Guid id, CancellationToken token)
    {
        Reads++;
        if (id != Conversation.Id) throw new InvalidOperationException("Scripted read fixture ID mismatch.");
        return Task.FromResult(new ChatSpaceConversationData(Conversation, [], [], [], new(Conversation.Id, null, "owner draft", "[]", DateTimeOffset.UnixEpoch)));
    }
    private T Denied<T>() { Mutations++; throw new InvalidOperationException("Scripted read fixture has no provider/durable mutation implementation."); }
    public Task<Conversation> CreateChatAsync(CancellationToken token) => Denied<Task<Conversation>>();
    public Task SaveDraftAsync(Guid conversationId, Guid? branchId, string content, IReadOnlyList<Guid> attachmentIds, CancellationToken token) => Denied<Task>();
    public Task<MessageAttachment> ImportAttachmentAsync(Guid conversationId, Guid? branchId, string path, CancellationToken token) => Denied<Task<MessageAttachment>>();
    public Task RemoveAttachmentAsync(Guid attachmentId, CancellationToken token) => Denied<Task>();
    public Task SendAsync(ChatSpaceTurnRequest request, CancellationToken token) => Denied<Task>();
    public Task<ConversationBranch> CreateBranchAsync(Guid conversationId, Guid messageId, string? name, CancellationToken token) => Denied<Task<ConversationBranch>>();
    public Task SwitchBranchAsync(Guid conversationId, Guid branchId, CancellationToken token) => Denied<Task>();
    public Task RegenerateAsync(Guid conversationId, Guid assistantMessageId, ModelDescriptor model, CancellationToken token) => Denied<Task>();
}
