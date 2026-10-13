using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private async Task<bool?> ChooseTemporaryMemoryReadAsync(CancellationToken actualSendToken)
    {
        var original = RequireChatOriginal();
        bool? answer = null;
        Task Choose(bool allow)
        {
            original.DemandPublication();
            actualSendToken.ThrowIfCancellationRequested();
            answer = allow;
            return Task.CompletedTask;
        }
        var prompt = new CuiChoicePrompt(new CuiChoicePromptOptions(
            "Memory for this message",
            "May this Temporary chat read your existing Memory for this message? It will not create or reinforce persistent Memory. Your existing permissions still apply.",
            "Read Memory", "Don't read Memory", "Cancel",
            () => Choose(true), () => Choose(false), () => Task.CompletedTask), "Spaces");
        _isAwaitingTemporaryMemory = true;
        try
        {
            await AwaitChatOriginalAsync(prompt.ShowAsync(_choiceOverlay, actualSendToken));
            return answer;
        }
        finally
        {
            // Closing joins the exact selected callback, including a callback whose
            // acknowledgement faults. A changed/retired page cannot skip that join.
            try { await RequireChatOriginal().AwaitAsync(prompt.CloseAsync()); }
            finally { _isAwaitingTemporaryMemory = false; }
        }
    }
}
