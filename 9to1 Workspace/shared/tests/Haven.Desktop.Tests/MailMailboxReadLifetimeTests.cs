using System.Reflection;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Xunit;

namespace Haven.Desktop.Tests;

// Controlled provider timing tests the actual public folder selection flow, not a transport or grant.
public sealed class MailMailboxReadLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Older_folder_return_or_failure_cannot_replace_newer_message_list(bool failOld)
    {
        var service = DispatchProxy.Create<IMailService, MailboxReads>();
        var reads = (MailboxReads)(object)service;
        using var page = new MailPageViewModel(service, DispatchProxy.Create<IProviderModelClient, MailSelectedThreadLifetimeTests.UnusedModels>());
        page.SelectedAccount = new(Guid.NewGuid(), CalendarProviderKind.Google, "Fixture", "fixture@example.test", MailProviderCapabilities.ReadState);
        await page.MailboxLoad;
        page.SelectedFolder = new("old", "Old folder", MailFolderKind.Inbox);
        var old = page.MailboxLoad;
        try
        {
            await reads.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.SelectedFolder = new("new", "New folder", MailFolderKind.Inbox);
            await page.MailboxLoad;
            await page.SelectedMessageLoad;
            var current = Assert.Single(page.Messages);
            var selected = page.SelectedMessage;
            var status = page.Status;
            var state = page.State;
            if (failOld) reads.Pending.TrySetException(new IOException("Old provider read failed."));
            else reads.Pending.TrySetResult(Result("old"));
            await old;
            Assert.Same(current, Assert.Single(page.Messages));
            Assert.Same(selected, page.SelectedMessage);
            Assert.Equal("new", current.Id);
            Assert.Equal(status, page.Status);
            Assert.Equal(state, page.State);
            Assert.False(page.IsBusy);
            Assert.Equal(new[] { "old", "new" }, reads.Queries.Select(q => q.FolderId));
        }
        finally
        {
            reads.Pending.TrySetResult(Result("cleanup"));
            try { await old; } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposed_or_cleared_account_refuses_completed_provider_read_that_ignored_cancellation(bool clearAccount)
    {
        var service = DispatchProxy.Create<IMailService, MailboxReads>();
        var reads = (MailboxReads)(object)service;
        using var page = new MailPageViewModel(service, DispatchProxy.Create<IProviderModelClient, MailSelectedThreadLifetimeTests.UnusedModels>());
        page.SelectedAccount = new(Guid.NewGuid(), CalendarProviderKind.Google, "Fixture", "fixture@example.test", MailProviderCapabilities.ReadState);
        await page.MailboxLoad;
        page.SelectedFolder = new("old", "Old folder", MailFolderKind.Inbox);
        var old = page.MailboxLoad;
        try
        {
            await reads.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (clearAccount) { page.SelectedAccount = null; Assert.False(page.IsBusy); }
            else page.Dispose();
            var status = page.Status; var state = page.State;
            reads.Pending.TrySetResult(Result("old"));
            await old;
            Assert.Empty(page.Messages);
            Assert.Null(page.SelectedMessage);
            Assert.Equal(status, page.Status); Assert.Equal(state, page.State);
            Assert.Equal(0, reads.ThreadReads);
        }
        finally
        {
            reads.Pending.TrySetResult(Result("cleanup"));
            try { await old; } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loaded_prior_account_rows_retire_before_clearing_or_denied_replacement(bool replace)
    {
        var service = DispatchProxy.Create<IMailService, MailboxReads>();
        var reads = (MailboxReads)(object)service; reads.AllowInitialAccountLoad = true;
        using var page = new MailPageViewModel(service, DispatchProxy.Create<IProviderModelClient, MailSelectedThreadLifetimeTests.UnusedModels>());
        var original = new MailAccount(Guid.NewGuid(), CalendarProviderKind.Google, "Original", "original@example.test", MailProviderCapabilities.ReadState);
        page.SelectedAccount = original; await page.MailboxLoad; await page.SelectedMessageLoad;
        Assert.Single(page.Folders); Assert.Single(page.Messages); Assert.NotNull(page.SelectedSummary);
        Assert.NotNull(page.SelectedFolder); Assert.NotNull(page.SelectedMessage);
        var threadReads = reads.ThreadReads; var queryCount = reads.Queries.Count;
        page.SelectedAccount = replace
            ? new(Guid.NewGuid(), CalendarProviderKind.Google, "Denied replacement", "replacement@example.test", MailProviderCapabilities.ReadState)
            : null;
        await page.MailboxLoad;
        Assert.Empty(page.Messages); Assert.Empty(page.Folders); Assert.Empty(page.ThreadMessages);
        Assert.Null(page.SelectedFolder); Assert.Null(page.SelectedSummary); Assert.Null(page.SelectedMessage);
        Assert.False(page.IsBusy);
        Assert.Equal(queryCount, reads.Queries.Count); Assert.Equal(threadReads, reads.ThreadReads);
        Assert.All(reads.Queries, query => Assert.Equal(original.AccountId, query.AccountId));
    }

    private static Haven.Application.MailPage Result(string id) => new(
        [new(id, id, new("Sender", "sender@example.test"), [], id, id, DateTimeOffset.UnixEpoch, true, false, false, false, [])],
        null, DateTimeOffset.UnixEpoch);

    public class MailboxReads : DispatchProxy
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Haven.Application.MailPage> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<MailQuery> Queries { get; } = [];
        public int ThreadReads { get; private set; }
        public bool AllowInitialAccountLoad { get; set; }
        private bool _initialAccessIssued;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case nameof(IMailService.GetAccountsAsync): return Task.FromResult<IReadOnlyList<MailAccount>>([]);
                case nameof(IMailService.CheckAccessAsync):
                    var allowed = AllowInitialAccountLoad && !_initialAccessIssued; _initialAccessIssued = true;
                    return Task.FromResult(new MailOperationResult(allowed, "Controlled original account access result."));
                case nameof(IMailService.GetFoldersAsync):
                    return Task.FromResult<IReadOnlyList<MailFolder>>([new("new", "Original inbox", MailFolderKind.Inbox)]);
                case nameof(IMailService.GetMessagesAsync):
                    var query = (MailQuery)args![0]!; Queries.Add(query);
                    if (query.FolderId == "old") { Entered.TrySetResult(true); return Pending.Task; }
                    return Task.FromResult(Result(query.FolderId ?? "none"));
                case nameof(IMailService.GetThreadAsync):
                    ThreadReads++;
                    var id = (string)args![1]!;
                    return Task.FromResult<IReadOnlyList<MailMessage>>([new(id, id, null, new("Sender", "sender@example.test"), [], [], [], id,
                        "Body", null, DateTimeOffset.UnixEpoch, true, false, false, [], [])]);
                default: throw new InvalidOperationException("Unexpected Mail operation: " + method?.Name);
            }
        }
    }
}
