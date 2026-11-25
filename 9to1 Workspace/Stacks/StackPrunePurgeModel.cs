namespace HavenOS.Apps.Stacks;

/// <summary>A named blocker explaining why a prune/purge/restore cannot proceed. Never a silent failure.</summary>
public enum StackLifecycleBlocker { None, MainCannotBePruned, TwoFactorRequired, ActiveDescendantsRemain, NotDeleted, RetentionElapsed, BackingObjectsMissing, ActiveDependenciesRetained, ReleaseReferencesRetained, AuditDependenciesRetained }

/// <summary>The outcome of a restore attempt: identity reuse where backing objects survive, re-creation where they do not, or refusal.</summary>
public enum StackRestoreMode { ReuseIdentity, RecreateRequired, Refused }

/// <summary>
/// Pure prune/purge/restore lifecycle model (ED-STACKS-004). Prune is recoverable and gated; permanent purge is a separate operation blocked by
/// retained active/release/audit dependencies; restore reuses stable identity only where backing objects survive within retention. No engine or
/// store mutation occurs here — this decides eligibility so the owning engine can act on an explicit, auditable verdict.
/// </summary>
public static class StackPrunePurgeModel
{
    /// <summary>Whether a domain may be pruned (recoverable Deleted). Main is never prunable; 2FA is required when configured; active descendants must be swept/pruned first.</summary>
    public static StackLifecycleBlocker CanPrune(StackDomainRecord domain, bool requiresTwoFactor, bool actorHasTwoFactor, bool hasActiveDescendants)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (domain.Kind == StackDomainKind.Main) return StackLifecycleBlocker.MainCannotBePruned;
        if (requiresTwoFactor && !actorHasTwoFactor) return StackLifecycleBlocker.TwoFactorRequired;
        if (hasActiveDescendants) return StackLifecycleBlocker.ActiveDescendantsRemain;
        return StackLifecycleBlocker.None;
    }

    /// <summary>Enter the recoverable Deleted state with a timestamp. Refuses to prune Main or an already-deleted domain.</summary>
    public static void EnterDeleted(StackDomainRecord domain, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (domain.Kind == StackDomainKind.Main) throw new InvalidOperationException("Main cannot be pruned.");
        if (domain.IsDeleted) throw new InvalidOperationException("The domain is already deleted.");
        domain.IsDeleted = true;
        domain.DeletedAt = now;
        domain.IsActive = false;
    }

    /// <summary>Permanent purge is separate from prune and blocked while any retained dependency exists.</summary>
    public static StackLifecycleBlocker CanPurge(StackDomainRecord domain, bool hasActiveDependencies, bool hasReleaseReferences, bool hasAuditDependencies)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (!domain.IsDeleted) return StackLifecycleBlocker.NotDeleted;
        if (hasActiveDependencies) return StackLifecycleBlocker.ActiveDependenciesRetained;
        if (hasReleaseReferences) return StackLifecycleBlocker.ReleaseReferencesRetained;
        if (hasAuditDependencies) return StackLifecycleBlocker.AuditDependenciesRetained;
        return StackLifecycleBlocker.None;
    }

    /// <summary>Restore reuses stable identity only where backing objects survive within retention; after expiry it must be refused, and without backing objects it requires re-creation.</summary>
    public static StackRestoreMode CanRestore(StackDomainRecord domain, bool retentionElapsed, bool backingObjectsSurvive)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (!domain.IsDeleted) return StackRestoreMode.ReuseIdentity; // nothing to restore
        if (retentionElapsed) return StackRestoreMode.Refused;
        return backingObjectsSurvive ? StackRestoreMode.ReuseIdentity : StackRestoreMode.RecreateRequired;
    }
}
