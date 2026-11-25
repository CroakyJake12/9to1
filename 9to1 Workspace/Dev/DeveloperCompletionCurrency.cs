namespace HavenOS.Apps.Dev;

/// <summary>The currency of a completion proposal relative to the current buffer.</summary>
public enum DeveloperCompletionCurrency { Current, Stale }

/// <summary>A completion candidate bound to the exact document identity and buffer version it was computed against, plus the replacement span
/// and the original text observed at that span. Applying later revalidates both the version and the span content so an out-of-order result
/// cannot overwrite newly typed text or mark a stale proposal current.</summary>
public sealed record DeveloperCompletionProposal(Guid DocumentId, long BufferVersion, int SpanStart, int SpanLength,
    string OriginalSpanText, string DisplayText, string ReplacementText)
{
    public DeveloperCompletionProposal Capture()
    {
        if (DocumentId == Guid.Empty) throw new InvalidDataException("A completion proposal requires a document identity.");
        if (BufferVersion < 1) throw new InvalidDataException("A completion proposal requires a positive buffer version.");
        if (SpanStart < 0 || SpanLength < 0) throw new InvalidDataException("A completion replacement span must be non-negative.");
        ArgumentNullException.ThrowIfNull(OriginalSpanText); ArgumentNullException.ThrowIfNull(DisplayText); ArgumentNullException.ThrowIfNull(ReplacementText);
        return this;
    }
}

/// <summary>The current buffer state a proposal is validated against.</summary>
public sealed record DeveloperCompletionBuffer(Guid DocumentId, long BufferVersion, string Text)
{
    public DeveloperCompletionBuffer Capture()
    {
        if (DocumentId == Guid.Empty) throw new InvalidDataException("A completion buffer requires a document identity.");
        if (BufferVersion < 1) throw new InvalidDataException("A completion buffer requires a positive version.");
        ArgumentNullException.ThrowIfNull(Text);
        return this;
    }
}

/// <summary>Outcome of attempting to apply a completion. A refusal never mutates the buffer.</summary>
public sealed record DeveloperCompletionApplyResult(bool Applied, DeveloperCompletionCurrency Currency, string? Text, string? Reason)
{
    public static DeveloperCompletionApplyResult Success(string text) => new(true, DeveloperCompletionCurrency.Current, text, null);
    public static DeveloperCompletionApplyResult Refused(DeveloperCompletionCurrency currency, string reason) => new(false, currency, null, reason);
}

/// <summary>Pure completion-currency contract. Results for older revisions are stale and cannot apply blindly; a proposal is applied only after
/// revalidating that the document identity, buffer version and exact replacement span content still match.</summary>
public static class DeveloperCompletionCurrencyService
{
    /// <summary>Classify a proposal against the current buffer without mutating anything.</summary>
    public static DeveloperCompletionCurrency Evaluate(DeveloperCompletionProposal proposal, DeveloperCompletionBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(proposal); ArgumentNullException.ThrowIfNull(buffer);
        var captured = proposal.Capture(); var current = buffer.Capture();
        if (captured.DocumentId != current.DocumentId) return DeveloperCompletionCurrency.Stale;
        if (captured.BufferVersion != current.BufferVersion) return DeveloperCompletionCurrency.Stale;
        // The identifier may have been edited after the request even at the same version boundary; revalidate the span content.
        if (!SpanStillMatches(captured, current.Text)) return DeveloperCompletionCurrency.Stale;
        return DeveloperCompletionCurrency.Current;
    }

    /// <summary>Apply a completion as one edit. Refuses (stale) when the buffer advanced or the target span changed since the request, so it never
/// overwrites newly typed text.</summary>
    public static DeveloperCompletionApplyResult Apply(DeveloperCompletionProposal proposal, DeveloperCompletionBuffer buffer)
    {
        var captured = proposal.Capture(); var current = buffer.Capture();
        if (captured.DocumentId != current.DocumentId) return DeveloperCompletionApplyResult.Refused(DeveloperCompletionCurrency.Stale, "The completion was computed for a different document.");
        if (captured.BufferVersion != current.BufferVersion) return DeveloperCompletionApplyResult.Refused(DeveloperCompletionCurrency.Stale, "The buffer changed after this completion was requested; it will not overwrite newer text.");
        if (!SpanStillMatches(captured, current.Text)) return DeveloperCompletionApplyResult.Refused(DeveloperCompletionCurrency.Stale, "The target range changed after this completion was requested; revalidate before applying.");
        var revised = string.Concat(current.Text.AsSpan(0, captured.SpanStart), captured.ReplacementText.AsSpan(), current.Text.AsSpan(captured.SpanStart + captured.SpanLength));
        return DeveloperCompletionApplyResult.Success(revised);
    }

    private static bool SpanStillMatches(DeveloperCompletionProposal proposal, string text)
    {
        if ((long)proposal.SpanStart + proposal.SpanLength > text.Length) return false;
        return string.CompareOrdinal(text.Substring(proposal.SpanStart, proposal.SpanLength), proposal.OriginalSpanText) == 0;
    }
}
