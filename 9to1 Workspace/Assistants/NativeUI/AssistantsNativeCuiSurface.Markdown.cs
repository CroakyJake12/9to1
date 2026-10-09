using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private CuiSceneHost? _richConversationScene;

    private CuiControlRegistry CreateConversationRegistry()
    {
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("AssistantMessage", _ => CreateOriginalMessageView());
        return registry;
    }

    private bool IsOriginalMarkdownCurrent(CuiMarkdownView view, CuiMarkdownCodeActionRequest request,
        AssistantMessagePresentation message, AssistantConversationBinding binding, long generation) =>
        IsPresentationCurrent(binding, generation) && view.IsCurrentOriginalCodeAction(request) &&
        _conversation.IsCurrentMessage(_conversationTarget, message) && IsOriginalGeneratedMarkdownText(view, message) &&
        Bindings.TryGetItemValue(message, "Content", out var content) && content as string == message.Content &&
        view.DataContext is ICuiBindingContext context && context.TryGetValue("message", out var actual) &&
        ReferenceEquals(actual, message) && _richConversationScene is not null &&
        view.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, _richConversationScene));

    private void OnOriginalMarkdownAction(CuiMarkdownView view, CuiMarkdownCodeActionRequest request)
    {
        if (IsRetiring || _binding is not { } binding || view.DataContext is not ICuiBindingContext context ||
            !context.TryGetValue("message", out var item) || item is not AssistantMessagePresentation message) return;
        var generation = PresentationGeneration;
        if (!IsOriginalMarkdownCurrent(view, request, message, binding, generation)) return;
        // This SAME native owner publishes the original driver before callbacks.
        // The renderer itself performs no clipboard/provider/tool/OS action.
        _ = RunAsync(async () =>
        {
            if (!IsOriginalMarkdownCurrent(view, request, message, binding, generation)) return;
            if (request.Action == CuiMarkdownCodeAction.Copy)
            {
                Avalonia.Input.Platform.IClipboard? clipboard;
                // TopLevel delegates this getter to the actual platform feature
                // owner. Keep that synchronous callback under the SAME physical
                // guard, then recheck the presentation before accepting effects.
                using (EnterPhysical()) clipboard = TopLevel.GetTopLevel(view)?.Clipboard;
                if (!IsOriginalMarkdownCurrent(view, request, message, binding, generation)) return;
                if (clipboard is null)
                {
                    PublishSynchronous(() => Bindings.SetConversationStatus("A clipboard is not available in this window."));
                    return;
                }
                await SourceAsync(() => clipboard.SetTextAsync(request.Code));
                if (IsOriginalMarkdownCurrent(view, request, message, binding, generation))
                    PublishSynchronous(() => Bindings.SetConversationStatus("Code copied."));
                return;
            }
            if (request.Action is not (CuiMarkdownCodeAction.AskToRun or CuiMarkdownCodeAction.AskToApply))
                throw new InvalidOperationException("The rendered code action is not supported.");
            PublishSynchronous(() =>
            {
                if (!IsOriginalMarkdownCurrent(view, request, message, binding, generation) ||
                    !Bindings.TryGetValue("CanEditPrompt", out var editable) || editable is not true) return;
                var verb = request.Action == CuiMarkdownCodeAction.AskToRun ? "Run" : "Apply";
                var staged = $"{verb} this code safely and explain the result:\n```{request.Language}\n{request.Code}\n```";
                var prior = Bindings.OriginalPrompt;
                if (!Bindings.TrySetValue("Prompt", string.IsNullOrWhiteSpace(prior) ? staged : prior + "\n\n" + staged))
                    throw new InvalidOperationException("The current conversation could not retain the requested draft.");
                Bindings.SetConversationStatus("Code request added to your draft. Review it before sending.");
            });
        });
    }
}
