using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

internal sealed partial class NativePersonalTaskRecoveryStore
{
    // Physical identity only. Files selection and Home READ are independently
    // required by the caller; no task authentication key is created or consulted.
    internal void ValidateOriginalAttachmentReadHandle(SafeFileHandle actualReader)
    {
        ArgumentNullException.ThrowIfNull(actualReader); Validate();
        if (_windows is { } windows) windows.ValidateOriginalMiniComputerReadHandle(actualReader);
        else if (!Observe(actualReader, directory: false).SameObject(_expectedDatabase))
            throw new UnauthorizedAccessException("The attachment reader is not the held original file.");
        Validate();
    }
}
