using Haven.Core;
using Avalonia.Threading;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    internal bool IsOriginalVoiceTarget(Guid conversationId, ModelDescriptor model) =>
        !_disposed && !_originalWork.IsRetiring && !_safetyLocked && CanUseSelectedModel &&
        conversationId == _conversation.Id && _selectedModel == model;

    internal void BindOriginalComposerVoiceOwner(IChatComposerVoiceActions? actualOwner)
    {
        Dispatcher.UIThread.VerifyAccess();
        _scene.ComposerSource.VoiceActions = actualOwner;
        _scene.ComposerSource.VoiceTarget = () => !_disposed && !_originalWork.IsRetiring && !_safetyLocked && CanUseSelectedModel
            ? (_conversation.Id, _selectedModel) : (_conversation.Id, null);
        RefreshOriginalComposerVoiceAvailability();
    }
    internal void RefreshOriginalComposerVoiceAvailability()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_disposed && !_originalWork.IsRetiring) _scene.ComposerView.RefreshSurface();
    }
}
