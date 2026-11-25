using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>atlas:build-code-diagnostics / IX-BUILD-002 stale analysis: real semantic diagnostics preserve draft and source; older results are
/// labelled stale and cannot be presented as current. Proves a diagnostic for the current version is Current, one for an older version is Stale
/// and cannot overwrite newer analysis, and different-document staleness.</summary>
public sealed class DeveloperDiagnosticCurrencyTests
{
    private static readonly Guid Doc = Guid.NewGuid();

    private static DeveloperDiagnosticSnapshot Diagnostic(long version, DeveloperDiagnosticSeverity severity = DeveloperDiagnosticSeverity.Error, string message = "err") =>
        new(Doc, version, 1, 0, 1, 5, severity, message);

    [Fact]
    public void DiagnosticForCurrentVersionIsCurrent()
    {
        var buffer = new DeveloperDiagnosticBuffer(Doc, 4);
        Assert.Equal(DeveloperDiagnosticCurrency.Current, DeveloperDiagnosticCurrencyService.Evaluate(Diagnostic(4), buffer));
    }

    [Fact]
    public void DiagnosticForOlderVersionIsStale()
    {
        var buffer = new DeveloperDiagnosticBuffer(Doc, 5);
        // The analysis arrived out of order for an older buffer version.
        Assert.Equal(DeveloperDiagnosticCurrency.Stale, DeveloperDiagnosticCurrencyService.Evaluate(Diagnostic(4), buffer));
    }

    [Fact]
    public void DifferentDocumentIsStale()
    {
        var other = new DeveloperDiagnosticBuffer(Guid.NewGuid(), 4);
        Assert.Equal(DeveloperDiagnosticCurrency.Stale, DeveloperDiagnosticCurrencyService.Evaluate(Diagnostic(4), other));
    }

    [Fact]
    public void StaleDiagnosisDoesNotOverwriteNewerAnalysisInProjection()
    {
        var buffer = new DeveloperDiagnosticBuffer(Doc, 5);
        // A stale error from version 4 and a current warning from version 5 coexist; the current one is not replaced.
        var rows = DeveloperDiagnosticCurrencyService.Project([Diagnostic(4), Diagnostic(5, DeveloperDiagnosticSeverity.Warning, "warn")], buffer);

        Assert.Equal(2, rows.Count);
        // Current rows sort before stale rows.
        Assert.True(rows[0].IsCurrent);
        Assert.False(rows[1].IsCurrent);
        Assert.Equal("warn", rows[0].Diagnostic.Message);
    }

    [Fact]
    public void ProjectionOrdersErrorsBeforeWarnings()
    {
        var buffer = new DeveloperDiagnosticBuffer(Doc, 3);
        var rows = DeveloperDiagnosticCurrencyService.Project([
            Diagnostic(3, DeveloperDiagnosticSeverity.Warning, "w"),
            Diagnostic(3, DeveloperDiagnosticSeverity.Error, "e")], buffer);

        Assert.Equal(DeveloperDiagnosticSeverity.Error, rows[0].Diagnostic.Severity);
        Assert.Equal(DeveloperDiagnosticSeverity.Warning, rows[1].Diagnostic.Severity);
    }

    [Fact]
    public void InvalidDiagnosticAndBufferAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new DeveloperDiagnosticSnapshot(Guid.Empty, 1, 0, 0, 0, 1, DeveloperDiagnosticSeverity.Error, "m").Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperDiagnosticSnapshot(Doc, 0, 0, 0, 0, 1, DeveloperDiagnosticSeverity.Error, "m").Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperDiagnosticSnapshot(Doc, 1, 0, 0, 0, 1, DeveloperDiagnosticSeverity.Error, " ").Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperDiagnosticBuffer(Guid.Empty, 1).Capture());
    }
}
