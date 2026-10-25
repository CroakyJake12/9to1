using Avalonia.Controls;
using Avalonia.Threading;

namespace HavenOS.Apps.Canvas;

/// <summary>Stable Board-facing Canvas identity. It carries no Files ID, provider path, read grant,
/// or write grant; the Canvas owner resolves its current Files source before opening.</summary>
public sealed record CanvasEmbeddedArtifactTarget(Guid ArtifactId, Guid ExpectedRevisionId)
{
    public CanvasEmbeddedArtifactTarget Validate()
    {
        if (ArtifactId == Guid.Empty) throw new ArgumentException("A canonical Canvas artifact ID is required.", nameof(ArtifactId));
        if (ExpectedRevisionId == Guid.Empty) throw new ArgumentException("The expected Canvas revision is required.", nameof(ExpectedRevisionId));
        return this;
    }
}

/// <summary>Container-level editing affordance only; Edit never grants a Canvas/Home permission.</summary>
public enum CanvasEmbeddedArtifactMode { View, Edit }

/// <summary>Limits the actual owner launcher surface to the operation requested by the
/// containing Board's Select or Create action.</summary>
public enum CanvasEmbeddedArtifactChoiceMode { SelectExisting, CreateNew, SelectOrCreate }

/// <summary>Retained same-Canvas-app view opened against one exact canonical owner revision.
/// The returned view includes Canvas' existing Home approval surface and uses its existing
/// Files owner, editor, mutation intents, and close/drain lifecycle.</summary>
public sealed class CanvasEmbeddedArtifactSession
{
    private readonly CanvasHostSurface _owner;
    private CanvasEmbeddedArtifactSession(CanvasHostSurface owner, CanvasEmbeddedArtifactTarget target,
        CanvasEmbeddedArtifactMode mode)
    {
        _owner = owner;
        Target = target;
        Mode = mode;
    }

    /// <summary>The stable target supplied by the containing Board when this session opened.</summary>
    public CanvasEmbeddedArtifactTarget Target { get; }
    public CanvasEmbeddedArtifactMode Mode { get; }
    /// <summary>Latest revision reopened by the same Canvas owner after an acknowledged Canvas
    /// mutation. This reports the revision to persist back to the containing Board; it grants no access.</summary>
    public CanvasEmbeddedArtifactTarget CurrentTarget => _owner.CurrentEmbeddedArtifactTarget;
    public Control View => _owner;
    public bool IsAlive => _owner.IsAlive;

    /// <summary>Resolve one stable identity, run the ordinary original Canvas Home/Files source
    /// checks, and retain that same Canvas host as the embedded view.</summary>
    public static async Task<CanvasEmbeddedArtifactSession> OpenAsync(CanvasHostSurface sameCanvasOwner,
        CanvasEmbeddedArtifactTarget target, CancellationToken cancellationToken = default)
        => await OpenAsync(sameCanvasOwner, target, CanvasEmbeddedArtifactMode.View, cancellationToken);

    public static async Task<CanvasEmbeddedArtifactSession> OpenAsync(CanvasHostSurface sameCanvasOwner,
        CanvasEmbeddedArtifactTarget target, CanvasEmbeddedArtifactMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sameCanvasOwner);
        target = (target ?? throw new ArgumentNullException(nameof(target))).Validate();
        Dispatcher.UIThread.VerifyAccess();
        var selection = await sameCanvasOwner.InitializeEmbeddedArtifactAsync(target, mode, cancellationToken);
        if (selection.Opened.Artifact.ArtifactId != target.ArtifactId ||
            selection.Opened.Artifact.RevisionId != target.ExpectedRevisionId)
            throw new InvalidOperationException("Canvas did not retain the expected canonical artifact revision.");
        return new(sameCanvasOwner, target, mode);
    }

    /// <summary>Recheck the exact current actor, Files store/item CAS revision, artifact identity,
    /// and owning-app revision before a containing Board publishes or resumes this view.</summary>
    public Task RevalidateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAlive) throw new ObjectDisposedException(nameof(CanvasEmbeddedArtifactSession));
        var currentTarget = CurrentTarget;
        if (currentTarget.ArtifactId != Target.ArtifactId)
            throw new InvalidOperationException("The retained Canvas session differs from its stable Board artifact identity.");
        return _owner.RevalidateEmbeddedArtifactAsync(cancellationToken);
    }

    /// <summary>Prepare the same reversible close gate as the standalone Canvas owner. Pending
    /// Home requests or native edits can veto close while preserving this exact embedded view.</summary>
    public Task<CanvasHostSurface.ClosePreparation?> PrepareCloseAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAlive) return Task.FromResult<CanvasHostSurface.ClosePreparation?>(null);
        return _owner.PrepareCloseAsync(cancellationToken);
    }

    /// <summary>Retire the exact Canvas view and its owned native children. Home and Files remain borrowed.</summary>
    public Task CloseAndDrainAsync() => _owner.CloseAndDrainAsync();
}

/// <summary>Retained same-Canvas-owner launcher used to choose or create a Board reference.
/// Selection returns only stable Canvas owner identity; Files row/store/revision details remain
/// private and are rechecked by this same owner before a Board publishes the reference.</summary>
public sealed class CanvasEmbeddedArtifactChoiceSession
{
    private readonly CanvasHostSurface _owner;
    private CanvasEmbeddedArtifactChoiceSession(CanvasHostSurface owner, CanvasEmbeddedArtifactChoiceMode mode)
    { _owner = owner; Mode = mode; }

    public Control View => _owner;
    public CanvasEmbeddedArtifactChoiceMode Mode { get; }
    public bool IsAlive => _owner.IsAlive;
    public Task<CanvasEmbeddedArtifactTarget?> Choice => _owner.EmbeddedChoice;

    /// <summary>Show the actual Canvas launch/controller surface in reference-choice mode.
    /// Its rows are current Files metadata rows, and its create action retains Canvas' existing
    /// Home reviewed request path.</summary>
    public static async Task<CanvasEmbeddedArtifactChoiceSession> OpenAsync(
        CanvasHostSurface sameCanvasOwner, CanvasEmbeddedArtifactChoiceMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sameCanvasOwner);
        Dispatcher.UIThread.VerifyAccess();
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var session = new CanvasEmbeddedArtifactChoiceSession(sameCanvasOwner, mode);
        await sameCanvasOwner.InitializeEmbeddedChooserAsync(mode, cancellationToken);
        return session;
    }

    /// <summary>Revalidate the same owner-issued choice against the current original actor,
    /// Files store, canonical item revision and Canvas app revision. This reads metadata only.</summary>
    public Task RevalidateAsync(CanvasEmbeddedArtifactTarget choice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (!IsAlive) throw new ObjectDisposedException(nameof(CanvasEmbeddedArtifactChoiceSession));
        return _owner.RevalidateEmbeddedChoiceAsync(choice, cancellationToken);
    }

    public Task<CanvasHostSurface.ClosePreparation?> PrepareCloseAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAlive) return Task.FromResult<CanvasHostSurface.ClosePreparation?>(null);
        return _owner.PrepareCloseAsync(cancellationToken);
    }

    /// <summary>Resolve an unanswered picker as cancelled, then drain the same owner and all
    /// children. The returned choice is never converted into an authorization grant.</summary>
    public Task CloseAndDrainAsync()
    {
        _owner.CancelEmbeddedChoice();
        return _owner.CloseAndDrainAsync();
    }
}
