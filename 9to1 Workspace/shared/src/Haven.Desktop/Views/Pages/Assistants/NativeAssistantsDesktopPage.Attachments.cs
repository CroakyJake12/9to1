#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Assistants;

internal sealed partial class NativeAssistantsDesktopPage
{
    private OriginalAssistantAttachmentPicker? _attachmentPicker;
    private void CaptureOriginalAttachmentPicker()
    {
        // The owning App acquired and passed this same attachment issuer into
        // the Core factory before this partial page/native construction began.
        if (_provider.GetService<IAssistantOriginalAttachmentOwner>() is not IAssistantOriginalAttachmentCommandSource source) return;
        if (!ReferenceEquals(_provider.GetRequiredService<IAssistantOriginalAttachmentCommandSource>(), source) ||
            source.OriginalClose is not null || !source.HasOriginalComposition(_provider.GetRequiredService<HomePersonalDenFactory>(),
                _provider.GetRequiredService<IConversationRepository>()))
            throw new UnauthorizedAccessException("Retain the SAME configured actual attachment issuer before binding its native picker.");
        _attachmentPicker = new(_originalWindow, _originalWindowLifetime); // Pure; no platform getter/IO.
    }
}
#endif
