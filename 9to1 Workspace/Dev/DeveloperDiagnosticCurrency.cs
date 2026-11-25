namespace HavenOS.Apps.Dev;

/// <summary>Severity of a semantic diagnostic. Analysis failure is not '0 errors'; it surfaces as an explicit state, never silently empty.</summary>
public enum DeveloperDiagnosticSeverity { Hint, Info, Warning, Error }

/// <summary>Currency of a diagnostic relative to the current buffer version.</summary>
public enum DeveloperDiagnosticCurrency { Current, Stale }

/// <summary>A semantic diagnostic bound to the exact document identity and buffer version that produced it, plus its range and message. The producing
/// version is what lets an out-of-order/older result be recognised as stale rather than presented over newer text.</summary>
public sealed record DeveloperDiagnosticSnapshot(Guid DocumentId, long ProducedVersion, int StartLine, int StartCharacter, int EndLine, int EndCharacter,
    DeveloperDiagnosticSeverity Severity, string Message)
{
    public DeveloperDiagnosticSnapshot Capture()
    {
        if (DocumentId == Guid.Empty) throw new InvalidDataException("A diagnostic requires a document identity.");
        if (ProducedVersion < 1) throw new InvalidDataException("A diagnostic requires a positive producing version.");
        if (StartLine < 0 || StartCharacter < 0 || EndLine < 0 || EndCharacter < 0) throw new InvalidDataException("A diagnostic range must be non-negative.");
        if (string.IsNullOrWhiteSpace(Message)) throw new InvalidDataException("A diagnostic requires a message.");
        if (!Enum.IsDefined(Severity)) throw new InvalidDataException("The diagnostic severity is unsupported.");
        return this;
    }
}

/// <summary>The current buffer state used to judge a diagnostic's currency.</summary>
public sealed record DeveloperDiagnosticBuffer(Guid DocumentId, long Version)
{
    public DeveloperDiagnosticBuffer Capture()
    {
        if (DocumentId == Guid.Empty) throw new InvalidDataException("A diagnostic buffer requires a document identity.");
        if (Version < 1) throw new InvalidDataException("A diagnostic buffer requires a positive version.");
        return this;
    }
}

/// <summary>A labelled Problems row. Stale diagnostics remain visible but are marked so they cannot be mistaken for current analysis.</summary>
public sealed record DeveloperProblemRow(DeveloperDiagnosticSnapshot Diagnostic, DeveloperDiagnosticCurrency Currency)
{
    public bool IsCurrent => Currency == DeveloperDiagnosticCurrency.Current;
}

/// <summary>Pure diagnostic-currency contract. A diagnostic is Current only when it belongs to the same document and was produced for the current
/// buffer version; older results are Stale and never silently replace newer analysis.</summary>
public static class DeveloperDiagnosticCurrencyService
{
    public static DeveloperDiagnosticCurrency Evaluate(DeveloperDiagnosticSnapshot diagnostic, DeveloperDiagnosticBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(diagnostic); ArgumentNullException.ThrowIfNull(buffer);
        var captured = diagnostic.Capture(); var current = buffer.Capture();
        if (captured.DocumentId != current.DocumentId) return DeveloperDiagnosticCurrency.Stale;
        if (captured.ProducedVersion != current.Version) return DeveloperDiagnosticCurrency.Stale;
        return DeveloperDiagnosticCurrency.Current;
    }

    /// <summary>Project a set of diagnostics onto labelled Problems rows against the current buffer. Stale entries are kept but flagged, so an
/// out-of-order older result cannot be presented as current.</summary>
    public static IReadOnlyList<DeveloperProblemRow> Project(IEnumerable<DeveloperDiagnosticSnapshot> diagnostics, DeveloperDiagnosticBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var rows = diagnostics.Select(diagnostic => new DeveloperProblemRow(diagnostic.Capture(), Evaluate(diagnostic, buffer))).ToArray();
        // Current first, then by severity (errors before warnings) and range, for a stable Problems ordering.
        return Array.AsReadOnly(rows.OrderByDescending(r => r.IsCurrent)
            .ThenByDescending(r => (int)r.Diagnostic.Severity)
            .ThenBy(r => r.Diagnostic.StartLine)
            .ThenBy(r => r.Diagnostic.StartCharacter)
            .ToArray());
    }
}
