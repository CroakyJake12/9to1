using System.Text.Json;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private readonly CancellationTokenSource _toolApprovalLifetime = new();
    private readonly Dictionary<Guid, ChatToolApprovalSession> _toolApprovalSessions = [];
    private readonly Dictionary<ChatToolApprovalSession, Task> _toolApprovalCloses = [];
    private readonly Dictionary<Guid, ChatToolApprovalMode> _toolApprovalModes = [];
    private ChatToolApprovalMode CurrentToolApprovalMode => _toolApprovalModes.GetValueOrDefault(_conversation.Id, ChatToolApprovalMode.Ask);
    private bool _isAwaitingToolApproval;

    private ChatToolApprovalRequest CreateOriginalToolApprovalRequest(Conversation conversation,
        string instruction, CancellationToken actualSendToken)
    {
        var original = RequireChatOriginal();
        original.DemandPublication();
        if (!_toolApprovalSessions.TryGetValue(conversation.Id, out var session))
        {
            session = _sessions.CreateToolApprovalSession(conversation.Id, _toolApprovalLifetime.Token);
            _toolApprovalSessions.Add(conversation.Id, session);
        }
        return _sessions.CreateToolApprovalRequest(session, conversation, instruction, actualSendToken,
            CurrentToolApprovalMode, (lease, token) => PresentOriginalToolApprovalAsync(original, lease, token));
    }

    private async Task PresentOriginalToolApprovalAsync(DesktopOriginalWorkLifetime.Original original,
        ChatToolApprovalLease lease, CancellationToken actualApprovalToken)
    {
        CuiChoicePrompt? prompt = null;
        Task? choice = null;
        Task? presentation = null;
        var failures = new List<Exception>();
        OperationCanceledException? expectedCancellation = null;
        try
        {
            presentation = Dispatcher.UIThread.InvokeAsync(() =>
            {
                actualApprovalToken.ThrowIfCancellationRequested();
                original.RunAcceptedPublicationCallback(() =>
                {
                    if (lease.ConversationId != _conversation.Id || !lease.IsPending)
                        throw new InvalidOperationException("This tool approval no longer belongs to the active chat.");
                    Task Decide(ChatToolApprovalDecision decision)
                    {
                        actualApprovalToken.ThrowIfCancellationRequested();
                        original.DemandPublication();
                        if (!lease.TryResolve(decision))
                            throw new InvalidOperationException("This tool approval has already ended.");
                        return Task.CompletedTask;
                    }
                    Task Deny()
                    {
                        // Withdrawal is not a new user denial or an approval grant.
                        if (actualApprovalToken.IsCancellationRequested) return Task.CompletedTask;
                        return Decide(ChatToolApprovalDecision.Deny);
                    }
                    prompt = new CuiChoicePrompt(new CuiChoicePromptOptions(
                        "Allow " + lease.ToolName + "?",
                        "Review this tool call before it runs. Existing account, resource and platform permissions still apply.\n\n" +
                        JsonSerializer.Serialize(lease.Arguments, new JsonSerializerOptions { WriteIndented = true }),
                        "Allow once", "Always allow this tool in this chat", "Deny",
                        () => Decide(ChatToolApprovalDecision.Allow),
                        () => Decide(ChatToolApprovalDecision.AllowForSession), Deny), "Spaces",
                        App.Services?.GetService(typeof(CakeOS.Cui.Themes.CuiThemePreferenceContext)) as CakeOS.Cui.Themes.CuiThemePreferenceContext);
                    _isAwaitingToolApproval = true;
                    choice = prompt.ShowAsync(_choiceOverlay, actualApprovalToken);
                });
            }).GetTask();
            await presentation.ConfigureAwait(false);
            await (choice ?? throw new InvalidOperationException("The original approval choice did not start.")).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            var actual = choice ?? presentation;
            if (failure is OperationCanceledException canceled && canceled.CancellationToken == actualApprovalToken &&
                actualApprovalToken.IsCancellationRequested && actual is { IsCanceled: true })
                expectedCancellation = canceled;
            CaptureOriginalGeneratedCauses(failures, actual, failure);
        }
        finally
        {
            Task? close = null;
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Cleanup belongs to the original prompt even if its chat retired.
                    close = prompt?.CloseAsync();
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception failure) { AddOriginalGeneratedCause(failures, failure); }
            if (close is not null)
                try { await close.ConfigureAwait(false); }
                catch (Exception failure) { CaptureOriginalGeneratedCauses(failures, close, failure); }
            try { await Dispatcher.UIThread.InvokeAsync(() => _isAwaitingToolApproval = false).GetTask().ConfigureAwait(false); }
            catch (Exception failure) { AddOriginalGeneratedCause(failures, failure); }
        }
        if (expectedCancellation is not null && failures.Count == 1 && ReferenceEquals(failures[0], expectedCancellation))
            ExceptionDispatchInfo.Capture(expectedCancellation).Throw();
        ThrowIndependentCleanup(failures);
    }

    private Task ShowCuiToolApprovalModesAsync()
    {
        var original = CapturePresentationGuard();
        var availability = _sessions.GetToolApprovalAvailability();
        var choices = availability.Where(item => item.Available).Select(item =>
            (ToolApprovalLabel(item.Mode), (Func<Task>)(() => RunChatOriginalAsync(() =>
            {
                if (!original()) throw new InvalidOperationException("This approval setting belongs to an earlier chat.");
                var current = _sessions.GetToolApprovalAvailability().Single(value => value.Mode == item.Mode);
                if (!current.Available) throw new InvalidOperationException(current.Reason);
                if (item.Mode == ChatToolApprovalMode.FullAccess)
                    throw new InvalidOperationException("Full access requires the original host's separate session confirmation.");
                _toolApprovalModes[_conversation.Id] = item.Mode;
                _scene.SetApprovalLabel(ToolApprovalLabel(item.Mode));
                return Task.CompletedTask;
            })))).ToArray();
        _scene.ShowChoicePrompt("Tool approvals",
            "Controls approval for tool calls in this chat. Existing permissions remain required.\n\n" +
            string.Join("\n\n", availability.Select(item => ToolApprovalLabel(item.Mode) + ": " + item.Reason)), choices);
        return Task.CompletedTask;
    }
    private static string ToolApprovalLabel(ChatToolApprovalMode mode) => mode switch
    {
        ChatToolApprovalMode.Ask => "Ask for approval",
        ChatToolApprovalMode.ApproveForMe => "Approve for me",
        ChatToolApprovalMode.RunAutomatically => "Run automatically",
        ChatToolApprovalMode.FullAccess => "Full access",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private void StopOriginalToolApprovals(List<Exception> failures)
    {
        try { _toolApprovalLifetime.Cancel(); }
        catch (Exception failure) { AddOriginalGeneratedCause(failures, failure); }
        foreach (var session in _toolApprovalSessions.Values)
        {
            if (_toolApprovalCloses.ContainsKey(session)) continue;
            try { _toolApprovalCloses.Add(session, session.CloseAsync()); }
            catch (Exception failure) { AddOriginalGeneratedCause(failures, failure); }
        }
    }
    private async Task JoinOriginalToolApprovalsAsync(List<Exception> failures)
    {
        foreach (var close in _toolApprovalCloses.Values)
            try { await close.ConfigureAwait(false); }
            catch (Exception failure) { CaptureOriginalGeneratedCauses(failures, close, failure); }
        try { _toolApprovalLifetime.Dispose(); }
        catch (Exception failure) { AddOriginalGeneratedCause(failures, failure); }
    }
}
