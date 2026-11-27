using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

internal sealed partial class NativeAppContentRegistry
{
    private sealed partial class RegisteredSession
    {
        private sealed class RegisteredConversationPickerSource(RegisteredSession owner,
            SpacesBrowseAppContentSession spaces, AppContentAddress address,
            SpacesBrowseAppContentSession.OriginalModelTarget target, Guid tabId, string profileId,
            AuthenticatedResourceActor actor) : IHomeModelPickerConversationSource
        {
            private bool IsConversationOwnerCurrent()
            {
                Dispatcher.UIThread.VerifyAccess();
                using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(owner);
                lock (owner._gate)
                    return owner._request.TabId == tabId &&
                        string.Equals(owner._request.ProfileId, profileId, StringComparison.Ordinal) &&
                        string.Equals(actor.ProfileId, profileId, StringComparison.Ordinal) &&
                        owner.IsExactOwnerCurrent(spaces, address);
            }

            public Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> ReadAsync(CancellationToken cancellationToken = default) =>
                ReadConversationAsync(null, cancellationToken);

            public Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> SelectSpecialistAsync(
                HomeModelPickerSpecialistSelection selection, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(selection);
                return ReadConversationAsync(selection, cancellationToken);
            }

            private Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> ReadConversationAsync(
                HomeModelPickerSpecialistSelection? selection, CancellationToken token) => OnUiAsync(async () =>
            {
                Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> actual;
                using (CloudflareOriginalExecutionGuard.EnterOriginal(owner))
                {
                    lock (owner._gate)
                    {
                        if (!IsConversationOwnerCurrent())
                            return new HomeCoreOperationResult<HomeModelPickerConversationSnapshot>(false,
                                "ConversationUnavailable", "The originating registered Chat session is no longer current.");
                        actual = owner.WithinOriginal(() => spaces.ReadConversationPickerAsync(target, actor,
                            IsConversationOwnerCurrent, selection, token));
                        // The outer owner retains the SAME page task, including its actor and
                        // catalogue reads. Closing the shared panel cannot detach that work.
                        owner.Retain(actual);
                    }
                }
                return await actual ?? new HomeCoreOperationResult<HomeModelPickerConversationSnapshot>(false,
                    "ConversationUnavailable", "The originating Chat retired while its read settled.");
            });
        }
    }
}
