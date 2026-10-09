namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Product labels for existing canonical HavenIconCatalog keys. These
/// packaged line icons are preferences, never files, remote URLs or resource grants.</summary>
internal static class AssistantAvatarCatalogue
{
    internal sealed record Choice(string Key, string Label, string Description);
    internal static readonly IReadOnlyList<Choice> Choices = Array.AsReadOnly(new[]
    {
        new Choice("chat", "Conversation", "A thoughtful everyday companion"),
        new Choice("study", "Learning", "Study, explain and explore"),
        new Choice("code", "Code", "Build and understand software"),
        new Choice("target", "Focus", "Keep a clear goal in view"),
        new Choice("palette", "Creative", "Ideas, design and expression"),
        new Choice("tasks", "Planning", "Organise work and next steps"),
        new Choice("rocket", "Discovery", "Explore a new direction"),
        new Choice("browse", "Research", "Connect ideas and information"),
        new Choice("notes", "Writing", "Shape and refine your words"),
        new Choice("calendar", "Calendar", "Make time for what matters"),
        new Choice("cpu", "Technical", "Understand systems and details"),
        new Choice("vision", "Perspective", "Look closely and notice more")
    });
    internal static string DisplayKey(string? saved) => string.IsNullOrWhiteSpace(saved) ? "chat" : saved;
    internal static string Label(string? saved) => string.IsNullOrWhiteSpace(saved) ? "Conversation · default"
        : Choices.FirstOrDefault(choice => choice.Key == saved)?.Label ?? "Saved icon";
    internal static bool IsListed(string? saved) => string.IsNullOrWhiteSpace(saved) || Choices.Any(choice => choice.Key == saved);
}
