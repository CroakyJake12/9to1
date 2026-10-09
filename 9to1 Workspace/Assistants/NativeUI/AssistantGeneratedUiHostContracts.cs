using Avalonia.Controls;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Borrow the configured canonical renderer; message IDs and declarations grant no app/tool access.</summary>
public interface IAssistantGeneratedUiHost
{
    bool IsOriginalController(AssistantsWorkspaceController sameController);
    Task<IAssistantGeneratedUiMount?> CreateOriginalMessageAsync(AssistantConversationBinding sameBinding,
        AssistantMessagePresentation sameMessage, Func<bool> originalPresentationCurrent,
        Action<IAssistantGeneratedUiMount> retainOriginalMount, CancellationToken token = default);
    Task? OriginalClose { get; }
    void DemandExternalOriginalRetirementJoin();
    void RequestRetirement();
    Task CloseAndDrainAsync();
}

public interface IAssistantGeneratedUiMount
{
    Control View { get; }
    string OriginalSourceContent { get; }
    string DisplayContent { get; }
    string Status { get; }
}
