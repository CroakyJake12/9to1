namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>Borrowed commands from the SAME native coordinator. A returned Task
/// acknowledges the actual owner operation, not merely an event invocation.</summary>
internal sealed record NativeChatSidebarCuiActions(
    Action<string> Search,
    Func<Task> NewChat,
    Action NewGroup,
    Func<Task> SpacePicker,
    Func<ChatSidebarConversationRequest, Task> Conversation,
    Func<ChatSidebarGroupRequest, Task> Group,
    Func<Guid, Task> File,
    Func<Guid, Task> OpenSpace,
    Func<Task> ManageSpaces,
    Func<bool, Task> SetSidebarCollapsed)
{
    public bool CanManageSpaces { get; init; } = true;
    public bool CanChangeSidebarLayout { get; init; } = true;
    public Func<Task>? OpenOwnedSpecialists { get; init; }
    public Func<Task>? ConfigureNavigation { get; init; }
}

/// <summary>Read-only authorised navigation observation. It is neither a stored
/// pin nor a grant; only the real preference owner may supply the ordered pins.</summary>
internal sealed record NativeSpaceNavigationDestination(
    Guid SpaceId, string Name, string IconKey, bool Available, string AvailabilityDescription);

internal enum NativeSpaceSwitcherLayout { Rail, Dropdown }
