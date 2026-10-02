using System.Reflection;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Xunit;

namespace Haven.Desktop.Tests;

// Controlled Mail service responses exercise the actual owning view model; no transport or Home grant is fabricated as proof.
public sealed class MailSelectedThreadLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Older_thread_return_cannot_replace_newer_selection_including_A_B_A(bool returnToA)
    {
        var service = DispatchProxy.Create<IMailService, ControlledMailReads>();
        var reads = (ControlledMailReads)(object)service;
        using var page = NewPage(service);
        page.SelectedAccount = Account();
        var a = Summary("A");
        page.SelectedSummary = a;
        var old = page.SelectedMessageLoad;
        try
        {
            await reads.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.SelectedSummary = Summary("B");
            await page.SelectedMessageLoad;
            if (returnToA) { page.SelectedSummary = a; await page.SelectedMessageLoad; }
            var current = Assert.IsType<MailMessage>(page.SelectedMessage);
            Assert.Equal(returnToA ? "new A" : "new B", current.PlainTextBody);
            reads.Pending.SetResult([Message("A", "old A", false)]);
            await old;
            Assert.Same(current, page.SelectedMessage);
            Assert.Equal(current, Assert.Single(page.ThreadMessages));
            Assert.Empty(reads.ReadRequests);
        }
        finally
        {
            reads.Pending.TrySetResult([Message("A", "cleanup", false)]);
            try { await old; } catch { }
        }
    }

    [Fact]
    public async Task Older_thread_failure_does_not_replace_current_message_or_status()
    {
        var service = DispatchProxy.Create<IMailService, ControlledMailReads>();
        var reads = (ControlledMailReads)(object)service;
        using var page = NewPage(service);
        page.SelectedAccount = Account();
        page.SelectedSummary = Summary("A");
        var old = page.SelectedMessageLoad;
        try
        {
            await reads.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.SelectedSummary = Summary("B"); await page.SelectedMessageLoad;
            var current = page.SelectedMessage; var status = page.Status; var state = page.State;
            reads.Pending.SetException(new IOException("Old thread read failed."));
            await old;
            Assert.Same(current, page.SelectedMessage);
            Assert.Equal(status, page.Status); Assert.Equal(state, page.State);
            Assert.Empty(reads.ReadRequests);
        }
        finally
        {
            reads.Pending.TrySetResult([Message("A", "cleanup", false)]);
            try { await old; } catch { }
        }
    }

    [Fact]
    public async Task Disposed_reader_refuses_original_thread_and_does_not_request_mark_read()
    {
        var service = DispatchProxy.Create<IMailService, ControlledMailReads>();
        var reads = (ControlledMailReads)(object)service;
        using var page = NewPage(service);
        page.SelectedAccount = Account(); page.SelectedSummary = Summary("A");
        var old = page.SelectedMessageLoad;
        try
        {
            await reads.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            reads.Pending.SetResult([Message("A", "retired", false)]); await old;
            Assert.Null(page.SelectedMessage); Assert.Empty(page.ThreadMessages); Assert.Empty(reads.ReadRequests);
        }
        finally
        {
            reads.Pending.TrySetResult([Message("A", "cleanup", false)]);
            try { await old; } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_original_message_or_already_issued_read_acknowledgement_cannot_adopt_replacement_thread(bool fallback)
    {
        var service = DispatchProxy.Create<IMailService, ControlledMailReads>();
        var reads = (ControlledMailReads)(object)service;
        reads.HoldFallback = fallback; reads.HoldReadAcknowledgement = !fallback;
        using var page = NewPage(service);
        page.SelectedAccount = Account(); page.SelectedSummary = Summary("A");
        var old = page.SelectedMessageLoad;
        try
        {
            await reads.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.SelectedSummary = Summary("B"); await page.SelectedMessageLoad;
            var current = Assert.IsType<MailMessage>(page.SelectedMessage);
            Assert.Equal("B", current.Id);
            if (fallback) reads.PendingMessage.SetResult(Message("A", "retired fallback", false));
            else reads.PendingRead.SetResult(new MailOperationResult(true, "Original read acknowledged."));
            await old;
            Assert.Same(current, page.SelectedMessage); Assert.Same(current, Assert.Single(page.ThreadMessages));
            if (fallback) { Assert.Equal(1, reads.MessageReads); Assert.Empty(reads.ReadRequests); }
            else Assert.Equal("A", Assert.Single(reads.ReadRequests)); // Issued original effect is not canceled or replayed.
        }
        finally
        {
            reads.PendingMessage.TrySetResult(Message("A", "cleanup", false));
            reads.PendingRead.TrySetResult(new MailOperationResult(true, "cleanup"));
            try { await old; } catch { }
        }
    }

    [Fact]
    public async Task Original_account_thread_cannot_adopt_after_actual_owning_account_selection_changes()
    {
        var service = DispatchProxy.Create<IMailService, ControlledMailReads>();
        var reads = (ControlledMailReads)(object)service;
        using var page = NewPage(service);
        var originalAccount = Account(); var replacementAccount = Account();
        page.SelectedAccount = originalAccount; page.SelectedSummary = Summary("A");
        var old = page.SelectedMessageLoad;
        try
        {
            await reads.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.SelectedAccount = replacementAccount;
            Assert.Null(page.SelectedMessage); Assert.Empty(page.ThreadMessages);
            page.SelectedSummary = Summary("B"); await page.SelectedMessageLoad;
            var current = Assert.IsType<MailMessage>(page.SelectedMessage);
            reads.Pending.SetResult([Message("A", "old account", false)]); await old;
            Assert.Same(current, page.SelectedMessage); Assert.Same(current, Assert.Single(page.ThreadMessages));
            Assert.Equal(new[] { (originalAccount.AccountId, "A"), (replacementAccount.AccountId, "B") }, reads.ThreadRequests.ToArray());
            Assert.Empty(reads.ReadRequests);
        }
        finally
        {
            reads.Pending.TrySetResult([Message("A", "cleanup", false)]);
            try { await old; } catch { }
        }
    }

    private static MailPageViewModel NewPage(IMailService mail) => new(mail, DispatchProxy.Create<IProviderModelClient, UnusedModels>());
    private static MailAccount Account() => new(Guid.NewGuid(), CalendarProviderKind.Google, "Local fixture", "fixture@example.test", MailProviderCapabilities.ReadState);
    private static MailMessageSummary Summary(string id) => new(id, id, new("Sender", "sender@example.test"), [], id, id,
        DateTimeOffset.UnixEpoch, false, false, false, false, []);
    private static MailMessage Message(string id, string body, bool read) => new(id, id, null, new("Sender", "sender@example.test"), [], [], [], id,
        body, null, DateTimeOffset.UnixEpoch, read, false, false, [], []);

    public class UnusedModels : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("No model request is part of this read workflow.");
    }
    public class ControlledMailReads : DispatchProxy
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<MailMessage>> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> ReadRequests { get; } = [];
        public List<(Guid AccountId, string ThreadId)> ThreadRequests { get; } = [];
        public bool HoldFallback { get; set; }
        public bool HoldReadAcknowledgement { get; set; }
        public int MessageReads { get; private set; }
        public TaskCompletionSource<MailMessage> PendingMessage { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<MailOperationResult> PendingRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _held;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case nameof(IMailService.GetAccountsAsync): return Task.FromResult<IReadOnlyList<MailAccount>>([]);
                case nameof(IMailService.CheckAccessAsync): return Task.FromResult(new MailOperationResult(false, "Mailbox loading is outside this controlled thread-read fixture."));
                case nameof(IMailService.GetThreadAsync):
                    var id = (string)args![1]!;
                    ThreadRequests.Add(((Guid)args[0]!, id));
                    if (id == "A" && HoldFallback) return Task.FromResult<IReadOnlyList<MailMessage>>([]);
                    if (id == "A" && HoldReadAcknowledgement) return Task.FromResult<IReadOnlyList<MailMessage>>([Message("A", "original unread", false)]);
                    if (id == "A" && !_held) { _held = true; Entered.TrySetResult(true); return Pending.Task; }
                    return Task.FromResult<IReadOnlyList<MailMessage>>([Message(id, "new " + id, true)]);
                case nameof(IMailService.GetMessageAsync):
                    MessageReads++; Entered.TrySetResult(true); return PendingMessage.Task;
                case nameof(IMailService.SetReadAsync):
                    ReadRequests.Add((string)args![1]!);
                    if (HoldReadAcknowledgement) { Entered.TrySetResult(true); return PendingRead.Task; }
                    return Task.FromResult(new MailOperationResult(true, "Read acknowledgement."));
                default: throw new InvalidOperationException("Unexpected Mail service operation: " + method?.Name);
            }
        }
    }
}
