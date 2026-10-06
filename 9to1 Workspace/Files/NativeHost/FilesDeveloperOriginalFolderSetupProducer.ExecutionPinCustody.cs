using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalFolderSetupProducer
    : IDeveloperWorkspaceOriginalExecutionPinCustodySource
{
    public bool IsOwnedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin)
    {
        lock (_gate) return samePin is ExecutionPin pin && ReferenceEquals(pin.Owner, this) &&
            ReferenceEquals(pin.Binding, sameBinding) && _originalExecutionPins.Contains(pin) &&
            _originalExecutionBindings.Contains(pin.Binding);
    }
}
