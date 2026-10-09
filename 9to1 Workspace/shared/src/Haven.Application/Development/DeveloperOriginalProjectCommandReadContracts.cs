using Haven.Core;

namespace Haven.Application;

/// <summary>A privately issued LIVE manual project READ for one actual command cohort.
/// Detached input, closed restoration and public project fields issue no permission. Short
/// Home/completion/native borrowers are closed before this object is published.</summary>
public interface IDeveloperOriginalProjectCommandRead : IAsyncDisposable
{
    IDeveloperOriginalCurrentProjectSelection OriginalSelection { get; }
    IDeveloperProjectOriginalReadAdmission OriginalReadAdmission { get; }
    IDeveloperOriginalCurrentProjectDescriptor OriginalDescriptor { get; }
    TaskRunColdProjectIdentity OriginalIdentity { get; }
    Task OriginalPreparation { get; }
    void RequestOriginalRetirement();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

/// <summary>The SAME source's new native capture under its actual READ driver ancestry.
/// The actual raw capture Task and returned read are retained independently of eligibility.
/// This does not hold a Home entry or authorize a Dev effect.</summary>
public interface IDeveloperOriginalProjectCommandNativeRead
{
    IDeveloperOriginalProjectCommandRead OriginalCommandRead { get; }
    Task<IDeveloperOriginalCurrentProjectNativeRead> OriginalCaptureTask { get; }
    IDeveloperOriginalCurrentProjectNativeRead OriginalRead { get; }
}

public interface IDeveloperOriginalProjectCommandReadSource
{
    Task<IDeveloperOriginalProjectCommandRead> AcquireOriginalCommandReadWithinSourceAsync(
        Conversation sameConversation, ContainerDefinition sameActualContainer,
        string exactProjectReferenceJson, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalCommandRead(IDeveloperOriginalProjectCommandRead sameCommandRead);
    // Historical private cleanup identity only; it never permits another productive read.
    bool IsOwnedOriginalCommandRead(IDeveloperOriginalProjectCommandRead sameCommandRead);
    Task ValidateOriginalCommandReadWithinSourceAsync(
        IDeveloperOriginalProjectCommandRead sameCommandRead, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    Task<IDeveloperOriginalProjectCommandNativeRead> CaptureOriginalCommandNativeReadWithinSourceAsync(
        IDeveloperOriginalProjectCommandRead sameCommandRead, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalCommandNativeRead(IDeveloperOriginalProjectCommandRead sameCommandRead,
        IDeveloperOriginalProjectCommandNativeRead sameNativeRead);
    bool IsOwnedOriginalCommandNativeRead(IDeveloperOriginalProjectCommandRead sameCommandRead,
        IDeveloperOriginalProjectCommandNativeRead sameNativeRead);
    Task ValidateOriginalCommandNativeReadWithinSourceAsync(
        IDeveloperOriginalProjectCommandRead sameCommandRead,
        IDeveloperOriginalProjectCommandNativeRead sameNativeRead,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsClosedOriginalCommandNativeRead(IDeveloperOriginalProjectCommandRead sameCommandRead,
        IDeveloperOriginalProjectCommandNativeRead sameNativeRead, Task sameActualNativeCloseTask);
}
