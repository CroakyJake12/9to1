namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantConversationPresentation
{
    /// <summary>Pure current projection identity; grants no command or resource permission.</summary>
    public bool IsCurrentMessage(Target? target, AssistantMessagePresentation message) =>
        target is not null && ReferenceEquals(target, _target) &&
        _messages.Any(actual => ReferenceEquals(actual, message));
}
