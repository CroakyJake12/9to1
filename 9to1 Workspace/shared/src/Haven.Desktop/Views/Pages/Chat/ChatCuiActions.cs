using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>Exact borrowed original operations. These callbacks do not grant model,
/// tool, resource or conversation authority; the actual NewChatPage owner retains it.</summary>
internal sealed record ChatCuiActions(
    Func<Task> Send,
    Func<Task> Stop,
    Func<Task> ResolveProblems,
    Func<AddMenu.AddMenuAction, Task> AddAction,
    Func<AddMenuSelection, Task> SelectCatalog,
    Func<ChatMessageActionRequest, Task> MessageAction,
    Func<ChatMarkdownCodeActionRequest, Task> CodeAction,
    Func<Guid, Task> RemoveContext,
    Func<string, Task> RemoveAttachment,
    Func<Task> ToggleDual,
    Func<Task> ChooseDualModel,
    Func<string, Task> SelectDualModel)
{
    public Func<Task>? ChooseTypeAndLocation { get; init; }
    public Func<Task>? ChooseModelAndReasoning { get; init; }
    public Func<Task>? ChooseToolApproval { get; init; }
}
