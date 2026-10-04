using System.Reflection;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Xunit;
using NativeMailPage = Haven.Desktop.Views.Pages.Mail.MailPage;

namespace Haven.Desktop.Tests;

// Provider timing is controlled through the real service contract; commands execute on the owning view model.
public sealed class MailMutationAcknowledgementTests
{
    [AvaloniaTheory]
    [InlineData("read")]
    [InlineData("flag")]
    [InlineData("important")]
    [InlineData("all")]
    public async Task Actual_MailPage_preserves_primary_thread_and_secondary_bulk_selection_during_acknowledged_flags(string kind)
    {
        await using var fixture = await Fixture.CreateAsync(native: true);
        var view = Assert.IsType<NativeMailPage>(fixture.NativePage);
        var list = Assert.IsType<ListBox>(view.FindControl<ListBox>("MessageList"));
        var window = new Window { Content = view, Width = 1000, Height = 760 };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(Avalonia.Controls.SelectionMode.Multiple | Avalonia.Controls.SelectionMode.Toggle, list.SelectionMode);
            Assert.Same(fixture.Page.SelectedSummary, list.SelectedItem);
            list.SelectedItems!.Add(fixture.Page.Messages[1]);
            Assert.Equal("A", fixture.Page.SelectedSummary!.Id);
            Assert.Equal(2, fixture.Page.BulkSelectionCount);
            var others = fixture.Page.ThreadMessages[1];

            var kinds = kind == "all" ? new[] { "read", "flag", "important" } : [kind];
            var mutations = kinds.ToDictionary(value => value, fixture.Start);
            foreach (var value in kinds.Reverse()) { fixture.Complete(value, 0); await mutations[value]; }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var message = Assert.IsType<MailMessage>(fixture.Page.SelectedMessage);
            var summary = Assert.IsType<MailMessageSummary>(fixture.Page.SelectedSummary);
            Assert.Same(summary, list.SelectedItem);
            Assert.Equal("A", message.Id);
            Assert.Equal(!kinds.Contains("read"), message.IsRead);
            Assert.Equal(kinds.Contains("flag"), message.IsFlagged);
            Assert.Equal(kinds.Contains("important"), message.IsImportant);
            Assert.Equal((message.IsRead, message.IsFlagged, message.IsImportant), (summary.IsRead, summary.IsFlagged, summary.IsImportant));
            Assert.Same(message, fixture.Page.ThreadMessages[0]);
            Assert.Same(others, fixture.Page.ThreadMessages[1]);
            Assert.Equal(1, fixture.Service.ThreadReads);
            Assert.Equal(2, fixture.Page.BulkSelectionCount);
            Assert.Equal(new[] { "A", "B" }, list.SelectedItems.OfType<MailMessageSummary>().Select(row => row.Id).Order());
            Assert.Equal(kinds.Length, fixture.Service.Requests.Count);

            await fixture.Page.BulkFlagCommand.ExecuteAsync();
            Assert.Equal(new[] { "A", "B" }, Assert.Single(fixture.Service.BulkRequests).Order());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("read")]
    [InlineData("flag")]
    [InlineData("important")]
    public async Task Native_two_way_list_selection_preserves_acknowledged_flags_without_retiring_or_rereading_the_thread(string kind)
    {
        await using var fixture = await Fixture.CreateAsync();
        var list = new ListBox { DataContext = fixture.Page };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MailPageViewModel.Messages)));
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(MailPageViewModel.SelectedSummary)) { Mode = BindingMode.TwoWay });
        var window = new Window { Content = list, Width = 600, Height = 400 };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Same(fixture.Page.SelectedSummary, list.SelectedItem);
            var mutation = fixture.Start(kind);
            fixture.Complete(kind, 0); await mutation;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var message = Assert.IsType<MailMessage>(fixture.Page.SelectedMessage);
            var summary = Assert.IsType<MailMessageSummary>(fixture.Page.SelectedSummary);
            Assert.Same(summary, list.SelectedItem);
            Assert.Equal("A", message.Id);
            Assert.Equal(kind != "read", message.IsRead);
            Assert.Equal(kind == "flag", message.IsFlagged);
            Assert.Equal(kind == "important", message.IsImportant);
            Assert.Equal((message.IsRead, message.IsFlagged, message.IsImportant), (summary.IsRead, summary.IsFlagged, summary.IsImportant));
            Assert.Same(message, fixture.Page.ThreadMessages[0]);
            Assert.Equal(2, fixture.Page.ThreadMessages.Count);
            Assert.Equal(1, fixture.Service.ThreadReads);
            AssertRequest(fixture, kind);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Native_two_way_list_selection_keeps_simultaneous_flag_acknowledgements_on_original_selection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var list = new ListBox { DataContext = fixture.Page };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MailPageViewModel.Messages)));
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(MailPageViewModel.SelectedSummary)) { Mode = BindingMode.TwoWay });
        var window = new Window { Content = list, Width = 600, Height = 400 };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var read = fixture.Start("read"); var flag = fixture.Start("flag"); var important = fixture.Start("important");
            fixture.Complete("important", 0); await important;
            fixture.Complete("flag", 0); await flag;
            fixture.Complete("read", 0); await read;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var message = Assert.IsType<MailMessage>(fixture.Page.SelectedMessage);
            var summary = Assert.IsType<MailMessageSummary>(fixture.Page.SelectedSummary);
            Assert.Same(summary, list.SelectedItem);
            Assert.False(message.IsRead); Assert.True(message.IsFlagged); Assert.True(message.IsImportant);
            Assert.Equal((message.IsRead, message.IsFlagged, message.IsImportant), (summary.IsRead, summary.IsFlagged, summary.IsImportant));
            Assert.Same(message, fixture.Page.ThreadMessages[0]);
            Assert.Equal(2, fixture.Page.ThreadMessages.Count);
            Assert.Equal(1, fixture.Service.ThreadReads);
            Assert.Equal(3, fixture.Service.Requests.Count);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("read", false)]
    [InlineData("read", true)]
    [InlineData("flag", false)]
    [InlineData("flag", true)]
    [InlineData("important", false)]
    [InlineData("important", true)]
    public async Task Rejected_or_throwing_flag_mutation_preserves_all_displayed_flags(string kind, bool throws)
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = Snapshot.Capture(fixture.Page);
        var mutation = fixture.Start(kind);
        await fixture.Service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        before.AssertUnchanged(fixture.Page);

        fixture.Complete(kind, throws ? 2 : 1);
        await mutation;

        before.AssertMessagesUnchanged(fixture.Page);
        Assert.Equal(MailUiState.Error, fixture.Page.State);
        Assert.True(fixture.Page.IsStale);
        Assert.Equal(throws ? "The mail provider could not complete this operation." : "Provider rejected the mutation.", fixture.Page.Status);
        Assert.Equal(0, fixture.Service.MailboxReads);
        AssertRequest(fixture, kind);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("flag")]
    [InlineData("important")]
    public async Task Acknowledged_flag_mutation_updates_selected_message_summary_and_thread_only(string kind)
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = Snapshot.Capture(fixture.Page);
        var mutation = fixture.Start(kind);
        await fixture.Service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        before.AssertUnchanged(fixture.Page);

        fixture.Complete(kind, 0);
        await mutation;

        var message = Assert.IsType<MailMessage>(fixture.Page.SelectedMessage);
        var summary = Assert.IsType<MailMessageSummary>(fixture.Page.SelectedSummary);
        Assert.Equal(kind != "read", message.IsRead);
        Assert.Equal(kind == "flag", message.IsFlagged);
        Assert.Equal(kind == "important", message.IsImportant);
        Assert.Equal((message.IsRead, message.IsFlagged, message.IsImportant), (summary.IsRead, summary.IsFlagged, summary.IsImportant));
        Assert.Equal(summary, fixture.Page.Messages.Single(row => row.Id == message.Id));
        Assert.Same(message, fixture.Page.ThreadMessages.Single(row => row.Id == message.Id));
        Assert.Same(before.Messages[1], fixture.Page.Messages[1]);
        Assert.Same(before.ThreadMessages[1], fixture.Page.ThreadMessages[1]);
        Assert.Equal(before.State, fixture.Page.State);
        Assert.Equal("Provider acknowledged the mutation.", fixture.Page.Status);
        Assert.Equal(kind == "read" ? "Mark read" : "Mark unread", fixture.Page.ReadActionLabel);
        Assert.Equal(kind == "flag" ? "Unflag" : "Flag", fixture.Page.FlagActionLabel);
        Assert.Equal(kind == "important" ? "Not important" : "Important", fixture.Page.ImportantActionLabel);
        Assert.Equal(0, fixture.Service.MailboxReads);
        Assert.Equal(1, fixture.Service.ThreadReads);
        AssertRequest(fixture, kind);
    }

    [Theory]
    [InlineData("read", false, 0)]
    [InlineData("read", false, 1)]
    [InlineData("read", false, 2)]
    [InlineData("read", true, 0)]
    [InlineData("read", true, 1)]
    [InlineData("read", true, 2)]
    [InlineData("flag", false, 0)]
    [InlineData("flag", false, 1)]
    [InlineData("flag", false, 2)]
    [InlineData("flag", true, 0)]
    [InlineData("flag", true, 1)]
    [InlineData("flag", true, 2)]
    [InlineData("important", false, 0)]
    [InlineData("important", false, 1)]
    [InlineData("important", false, 2)]
    [InlineData("important", true, 0)]
    [InlineData("important", true, 1)]
    [InlineData("important", true, 2)]
    public async Task Retired_selection_refuses_success_failure_and_exception_even_after_return_to_same_summary(string kind, bool returnToOriginal, int outcome)
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = fixture.Page.SelectedSummary;
        var mutation = fixture.Start(kind);
        await fixture.SelectAsync("B");
        if (returnToOriginal)
        {
            await fixture.SelectAsync("A");
            Assert.Same(original, fixture.Page.SelectedSummary); // Same account, ID and summary object, but a new lifetime.
        }
        var current = Snapshot.Capture(fixture.Page);
        fixture.Complete(kind, outcome);
        await mutation;
        current.AssertUnchanged(fixture.Page);
        Assert.Equal(0, fixture.Service.MailboxReads);
        AssertRequest(fixture, kind);
    }

    [Theory]
    [InlineData("read", 0)]
    [InlineData("read", 1)]
    [InlineData("read", 2)]
    [InlineData("flag", 0)]
    [InlineData("flag", 1)]
    [InlineData("flag", 2)]
    [InlineData("important", 0)]
    [InlineData("important", 1)]
    [InlineData("important", 2)]
    public async Task Original_account_acknowledgement_cannot_mutate_same_message_ID_in_replacement_account(string kind, int outcome)
    {
        await using var fixture = await Fixture.CreateAsync();
        var mutation = fixture.Start(kind);
        fixture.Page.SelectedAccount = Account();
        await fixture.Page.MailboxLoad;
        fixture.AddSummaries();
        await fixture.SelectAsync("A");
        var current = Snapshot.Capture(fixture.Page);
        fixture.Complete(kind, outcome);
        await mutation;
        current.AssertUnchanged(fixture.Page);
        Assert.Equal(0, fixture.Service.MailboxReads);
        AssertRequest(fixture, kind);
    }

    [Theory]
    [InlineData("read", 0)]
    [InlineData("read", 1)]
    [InlineData("read", 2)]
    [InlineData("flag", 0)]
    [InlineData("flag", 1)]
    [InlineData("flag", 2)]
    [InlineData("important", 0)]
    [InlineData("important", 1)]
    [InlineData("important", 2)]
    public async Task Disposed_reader_refuses_in_flight_mutation_presentation(string kind, int outcome)
    {
        await using var fixture = await Fixture.CreateAsync();
        var mutation = fixture.Start(kind);
        fixture.Page.Dispose();
        var current = Snapshot.Capture(fixture.Page);
        fixture.Complete(kind, outcome);
        await mutation;
        current.AssertUnchanged(fixture.Page);
        Assert.Null(fixture.Page.SelectedMessage);
        Assert.Empty(fixture.Page.ThreadMessages);
        Assert.Equal(0, fixture.Service.MailboxReads);
        AssertRequest(fixture, kind);
    }

    [Theory]
    [InlineData("archive", 0)]
    [InlineData("archive", 1)]
    [InlineData("archive", 2)]
    [InlineData("delete", 0)]
    [InlineData("delete", 1)]
    [InlineData("delete", 2)]
    public async Task Retired_archive_or_delete_does_not_reload_new_selection_or_replace_its_status(string kind, int outcome)
    {
        await using var fixture = await Fixture.CreateAsync();
        var mutation = fixture.Start(kind);
        await fixture.SelectAsync("B");
        var current = Snapshot.Capture(fixture.Page);
        fixture.Complete(kind, outcome);
        await mutation;
        current.AssertUnchanged(fixture.Page);
        Assert.Equal(0, fixture.Service.MailboxReads);
        AssertRequest(fixture, kind);
    }

    [Fact]
    public async Task Concurrent_acknowledged_flags_keep_each_other_on_the_same_selection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var read = fixture.Start("read");
        var flag = fixture.Start("flag");
        var important = fixture.Start("important");
        fixture.Complete("important", 0); await important;
        fixture.Complete("flag", 0); await flag;
        fixture.Complete("read", 0); await read;
        var message = Assert.IsType<MailMessage>(fixture.Page.SelectedMessage);
        var summary = Assert.IsType<MailMessageSummary>(fixture.Page.SelectedSummary);
        Assert.False(message.IsRead); Assert.True(message.IsFlagged); Assert.True(message.IsImportant);
        Assert.Equal((message.IsRead, message.IsFlagged, message.IsImportant), (summary.IsRead, summary.IsFlagged, summary.IsImportant));
        Assert.Same(message, fixture.Page.ThreadMessages[0]);
        Assert.Equal(summary, fixture.Page.Messages[0]);
        Assert.Equal(0, fixture.Service.MailboxReads); Assert.Equal(1, fixture.Service.ThreadReads);
        Assert.Equal(3, fixture.Service.Requests.Count);
    }

    private static void AssertRequest(Fixture fixture, string kind)
    {
        var request = Assert.Single(fixture.Service.Requests);
        Assert.Equal((fixture.OriginalAccount.AccountId, "A", kind), (request.Account, request.Message, request.Kind));
        Assert.Equal(kind == "read" ? false : kind is "flag" or "important" ? true : (bool?)null, request.Target);
    }

    private static MailAccount Account() => new(Guid.NewGuid(), CalendarProviderKind.Google, "Fixture", "fixture@example.test",
        MailProviderCapabilities.ReadState | MailProviderCapabilities.Flag | MailProviderCapabilities.Important | MailProviderCapabilities.Archive | MailProviderCapabilities.Delete);
    private static MailMessageSummary Summary(string id) => new(id, id, new("Sender", "sender@example.test"), [], id, id,
        DateTimeOffset.UnixEpoch, true, false, false, false, []);
    private static MailMessage Message(string id, Guid account) => new(id, id, null, new("Sender", "sender@example.test"), [], [], [], id,
        "Body in " + account, null, DateTimeOffset.UnixEpoch, true, false, false, [], []);

    private sealed record Snapshot(MailMessage? SelectedMessage, MailMessageSummary? SelectedSummary,
        MailMessageSummary[] Messages, MailMessage[] ThreadMessages, string Status, MailUiState State, bool IsStale)
    {
        public static Snapshot Capture(MailPageViewModel page) => new(page.SelectedMessage, page.SelectedSummary,
            page.Messages.ToArray(), page.ThreadMessages.ToArray(), page.Status, page.State, page.IsStale);
        public void AssertMessagesUnchanged(MailPageViewModel page)
        {
            Assert.Same(SelectedMessage, page.SelectedMessage); Assert.Same(SelectedSummary, page.SelectedSummary);
            Assert.Equal(Messages.Length, page.Messages.Count); Assert.Equal(ThreadMessages.Length, page.ThreadMessages.Count);
            for (var index = 0; index < Messages.Length; index++) Assert.Same(Messages[index], page.Messages[index]);
            for (var index = 0; index < ThreadMessages.Length; index++) Assert.Same(ThreadMessages[index], page.ThreadMessages[index]);
        }
        public void AssertUnchanged(MailPageViewModel page)
        {
            AssertMessagesUnchanged(page);
            Assert.Equal(Status, page.Status); Assert.Equal(State, page.State); Assert.Equal(IsStale, page.IsStale);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public MailPageViewModel Page { get; }
        public NativeMailPage? NativePage { get; }
        public ControlledMutations Service { get; }
        public MailAccount OriginalAccount { get; } = Account();
        private readonly List<Task> _commands = [];
        private Fixture(IMailService service, ControlledMutations control, bool native)
        {
            Service = control;
            var models = DispatchProxy.Create<IProviderModelClient, UnusedModels>();
            if (native)
            {
                NativePage = new(service, models);
                Page = Assert.IsType<MailPageViewModel>(NativePage.DataContext);
            }
            else Page = new(service, models);
        }
        public static async Task<Fixture> CreateAsync(bool native = false)
        {
            var service = DispatchProxy.Create<IMailService, ControlledMutations>();
            var fixture = new Fixture(service, (ControlledMutations)(object)service, native);
            await fixture.Page.Initialization;
            fixture.Page.SelectedAccount = fixture.OriginalAccount;
            await fixture.Page.MailboxLoad;
            fixture.AddSummaries(); await fixture.SelectAsync("A");
            return fixture;
        }
        public void AddSummaries() { Page.Messages.Add(Summary("A")); Page.Messages.Add(Summary("B")); }
        public async Task SelectAsync(string id)
        {
            Page.SelectedSummary = Page.Messages.Single(summary => summary.Id == id);
            await Page.SelectedMessageLoad;
        }
        public Task Start(string kind)
        {
            var command = kind switch
            {
                "read" => Page.ToggleReadCommand,
                "flag" => Page.ToggleFlagCommand,
                "important" => Page.ToggleImportantCommand,
                "archive" => Page.ArchiveCommand,
                "delete" => Page.DeleteCommand,
                _ => throw new InvalidOperationException(kind)
            };
            var task = command.ExecuteAsync(); _commands.Add(task); return task;
        }
        public void Complete(string kind, int outcome)
        {
            var pending = Service.Pending[kind];
            if (outcome == 2) pending.SetException(new IOException("Controlled transport failure."));
            else pending.SetResult(new(outcome == 0, outcome == 0 ? "Provider acknowledged the mutation." : "Provider rejected the mutation.",
                outcome == 0 ? MailFailureKind.None : MailFailureKind.ProviderError));
        }
        public async ValueTask DisposeAsync()
        {
            NativePage?.Dispose();
            Page.Dispose();
            foreach (var pending in Service.Pending.Values) pending.TrySetResult(new(false, "Fixture cleanup."));
            await Task.WhenAll(_commands);
        }
    }

    public class UnusedModels : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("No model operation belongs to a Mail mutation.");
    }

    public class ControlledMutations : DispatchProxy
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Dictionary<string, TaskCompletionSource<MailOperationResult>> Pending { get; } = [];
        public List<(Guid Account, string Message, string Kind, bool? Target)> Requests { get; } = [];
        public List<string[]> BulkRequests { get; } = [];
        public int MailboxReads { get; private set; }
        public int ThreadReads { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case nameof(IMailService.GetAccountsAsync): return Task.FromResult<IReadOnlyList<MailAccount>>([]);
                case nameof(IMailService.CheckAccessAsync): return Task.FromResult(new MailOperationResult(false, "Automatic mailbox loading is outside this controlled fixture."));
                case nameof(IMailService.GetThreadAsync):
                    ThreadReads++;
                    var account = (Guid)args![0]!; var id = (string)args[1]!;
                    return Task.FromResult<IReadOnlyList<MailMessage>>([Message(id, account), Message(id + "-other", account)]);
                case nameof(IMailService.GetMessagesAsync):
                    MailboxReads++;
                    return Task.FromResult(new Haven.Application.MailPage([], null, DateTimeOffset.UnixEpoch));
                case nameof(IMailService.SetReadAsync): return Hold("read", args!, (bool)args![2]!);
                case nameof(IMailService.SetFlaggedAsync): return Hold("flag", args!, (bool)args![2]!);
                case nameof(IMailService.SetImportantAsync): return Hold("important", args!, (bool)args![2]!);
                case nameof(IMailService.ArchiveAsync): return Hold("archive", args!, null);
                case nameof(IMailService.DeleteAsync): return Hold("delete", args!, null);
                case nameof(IMailService.ExecuteBulkAsync):
                    var ids = ((IReadOnlyCollection<string>)args![1]!).ToArray(); BulkRequests.Add(ids);
                    return Task.FromResult(new MailBulkOperationResult(ids.Length, ids.Length, 0, "Controlled bulk acknowledgement."));
                default: throw new InvalidOperationException("Unexpected Mail service operation: " + method?.Name);
            }
        }
        private Task<MailOperationResult> Hold(string kind, object?[] args, bool? target)
        {
            var pending = new TaskCompletionSource<MailOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(kind, pending); Requests.Add(((Guid)args[0]!, (string)args[1]!, kind, target));
            Entered.TrySetResult(true); return pending.Task;
        }
    }
}
