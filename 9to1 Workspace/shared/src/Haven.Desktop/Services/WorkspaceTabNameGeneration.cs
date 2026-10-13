using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Services;

/// <summary>
/// Executes the shell's original naming request against its captured presentation.
/// A title is not a progress indicator: the existing title stays intact until a
/// still-current result is acknowledged. Provider failures remain caller-owned.
/// </summary>
internal static class WorkspaceTabNameGeneration
{
    private sealed class Admission { internal bool Pending; }
    private static readonly ConditionalWeakTable<WorkspaceTabViewModel, Admission> Admissions = new();

    internal static async Task<bool> GenerateAsync(WorkspaceTabViewModel tab, IOllamaClient client,
        string? preferredModel, IExecutionEventSink? sink)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(client);
        var admission = Admissions.GetValue(tab, _ => new Admission());
        lock (admission)
        {
            if (admission.Pending) throw new InvalidOperationException("A name is already being generated for this tab.");
            admission.Pending = true;
        }

        PropertyChangedEventHandler? observer = null;
        try
        {
            var page = tab.Page;
            var originalTitle = tab.Title;
            var appKey = tab.AppKey;
            var surface = tab.Surface;
            var sessionId = tab.SessionId;
            var token = tab.LifetimeToken;
            token.ThrowIfCancellationRequested();
            var temporaryOrigin = page is NewChatPage { IsTemporary: true } or ChatPage { IsTemporary: true };
            var guard = page switch
            {
                NewChatPage chat => chat.CapturePresentationGuard(),
                ChatPage chat => chat.CapturePresentationGuard(),
                _ => static () => true
            };
            var context = page switch
            {
                NewChatPage chat => $"App: {appKey}. Existing title: {originalTitle}. Conversation title: {chat.CurrentConversation.Title}.",
                ChatPage chat => $"App: {appKey}. Existing title: {originalTitle}. Conversation title: {chat.ConversationTitle}.",
                _ => $"App: {appKey}. Existing title: {originalTitle}. Surface: {surface}."
            };
            var invalidated = false;
            var publishingTitle = false;
            observer = (_, change) =>
            {
                if (change.PropertyName is null or "" or nameof(WorkspaceTabViewModel.Page)
                    or nameof(WorkspaceTabViewModel.Key) or nameof(WorkspaceTabViewModel.AppKey)
                    or nameof(WorkspaceTabViewModel.Surface) or nameof(WorkspaceTabViewModel.SessionId)
                    || (change.PropertyName == nameof(WorkspaceTabViewModel.Title) && !publishingTitle))
                    invalidated = true;
            };
            tab.PropertyChanged += observer;
            bool IsCurrent() => !invalidated && !token.IsCancellationRequested
                && ReferenceEquals(page, tab.Page) && sessionId == tab.SessionId && guard();
            if (!IsCurrent()) return false;

            var executionId = Guid.NewGuid();
            var actionId = Guid.NewGuid();
            var started = DateTimeOffset.UtcNow;
            void Publish(ExecutionActionStatus status, string message, ExecutionFailure? failure = null)
            {
                // Privacy belongs to the original request, not its mutable current page.
                if (temporaryOrigin) return;
                var now = DateTimeOffset.UtcNow;
                sink?.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, null,
                    ExecutionOrigin.Haven, ExecutionActionType.ModelExecution, status,
                    "Generate tab name with AI", null, message, "tabs", now, started,
                    status == ExecutionActionStatus.Running ? null : now, TabId: sessionId, Failure: failure));
            }

            Publish(ExecutionActionStatus.Running, $"App: {appKey}; current title: {originalTitle}");
            Task? actualProviderTask = null;
            try
            {
                var modelsTask = client.GetModelsAsync(token);
                actualProviderTask = modelsTask;
                var models = await modelsTask;
                if (!IsCurrent())
                {
                    Publish(ExecutionActionStatus.Cancelled, "The original tab presentation changed before inference.");
                    return false;
                }
                var model = models.FirstOrDefault(item => item.Name.Equals(preferredModel, StringComparison.OrdinalIgnoreCase))
                    ?? models.FirstOrDefault();
                if (model is null) throw new InvalidOperationException("No configured model is available.");
                var completionTask = client.CompleteAsync(new OllamaChatRequest(model.Name,
                    [new OllamaMessage("user", context)], EffortLevel.Low,
                    "Return one concise tab name of at most six words. Return only the name."), token);
                actualProviderTask = completionTask;
                var answer = await completionTask;
                if (!IsCurrent())
                {
                    Publish(ExecutionActionStatus.Cancelled, "The original tab presentation changed before its name was applied.");
                    return false;
                }
                var generated = answer.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Trim().Trim('"', '\'', '.', ':');
                if (string.IsNullOrWhiteSpace(generated)) throw new InvalidOperationException("The model returned an empty name.");
                var title = generated.Length <= 80 ? generated : generated[..80].TrimEnd() + "…";
                publishingTitle = true;
                try { tab.Title = title; }
                finally { publishingTitle = false; }
                if (!IsCurrent() || !StringComparer.Ordinal.Equals(tab.Title, title))
                {
                    Publish(ExecutionActionStatus.Cancelled, "The original tab changed while its name was being applied.");
                    return false;
                }
                Publish(ExecutionActionStatus.Completed, title);
                return true;
            }
            catch (Exception error)
            {
                // Do not restore a captured title into a replacement page. Keep the
                // actual exception intact for the shell's original-work accounting.
                var detail = SensitiveTextRedactor.Redact(error.Message);
                var failure = actualProviderTask?.Exception is { InnerExceptions.Count: > 1 } compound ? compound : error;
                try
                {
                    Publish(actualProviderTask?.IsCanceled == true ? ExecutionActionStatus.Cancelled : ExecutionActionStatus.Failed, detail,
                        new ExecutionFailure("TAB_NAME_GENERATION_FAILED", "Name generation failed", detail));
                }
                catch (Exception reportingFailure)
                {
                    if (!ReferenceEquals(failure, reportingFailure))
                        throw new AggregateException("Tab naming and its execution report both failed.", failure, reportingFailure);
                }
                ExceptionDispatchInfo.Capture(failure).Throw();
                throw; // Unreachable; retains the original exception contract for compilers.
            }
        }
        finally
        {
            if (observer is not null) tab.PropertyChanged -= observer;
            lock (admission) admission.Pending = false;
        }
    }
}
