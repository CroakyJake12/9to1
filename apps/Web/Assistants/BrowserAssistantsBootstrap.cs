using CakeOS.Cui;
using HavenOS.Apps.Assistants.NativeUI;

namespace NineToOne.Web.Assistants;

/// <summary>Dedicated product setup state. It owns no Den, conversation, Task or execution authority.</summary>
public sealed class BrowserAssistantsBootstrap : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability
{
    public const string MissingOwnerCode = "AssistantBrowserOwnerUnavailable";
    public const string MissingOwnerMessage = "Assistants needs its authenticated Home connection in this browser. Your existing Assistants and conversations are preserved.";
    private readonly AssistantsCuiBindings _bindings = AssistantsCuiBindings.CreateUnavailable(MissingOwnerMessage);
    private bool _revoked;

    public CuiDocument Document { get; } = AssistantsCuiScenes.ReadDocument(AssistantsCuiScene.Home);
    public bool? IsActionAvailable(string command) => false;
    public void RevokePrivateContext() => _revoked = true;

    public bool TryGetValue(string path, out object? value)
    {
        value = null;
        if (_revoked) return false;
        return _bindings.TryGetValue(path, out value);
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException(new BrowserAssistantsHostUnavailableException(
            _revoked ? "The previous Assistants account context has been revoked." : MissingOwnerMessage));
    }
}

public sealed class BrowserAssistantsHostUnavailableException(string message) : InvalidOperationException(message);
