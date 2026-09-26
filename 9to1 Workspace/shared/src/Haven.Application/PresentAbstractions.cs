using Haven.Core;

namespace Haven.Application;

public sealed record PresentDocumentSummary(
    Guid Id,
    string Title,
    DateTimeOffset UpdatedAt,
    int Version,
    int SlideCount,
    bool RecoveredFromBackup,
    bool Pinned = false);

public sealed record PresentSaveResult(
    Guid DocumentId,
    int Version,
    DateTimeOffset SavedAt,
    string CurrentPath,
    string BackupPath);

public sealed class PresentRevisionConflictException(Guid documentId, int expectedVersion, int actualVersion)
    : InvalidOperationException($"Presentation {documentId:D} changed since it was opened (expected revision {expectedVersion}, current revision {actualVersion}).")
{
    public string Code => "RevisionConflict";
    public Guid DocumentId { get; } = documentId;
    public int ExpectedVersion { get; } = expectedVersion;
    public int ActualVersion { get; } = actualVersion;
    public bool CanRetry { get; } = false;
}

public interface IPresentRepository
{
    Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken cancellationToken);
    Task<PresentDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken);
    Task<PresentSaveResult> SaveAsync(
        PresentDocument document,
        string reason,
        CancellationToken cancellationToken);
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken);
}

public interface IPresentExportService
{
    IReadOnlyList<string> ExportExtensions { get; }

    Task<string> ExportAsync(
        PresentDocument document,
        string destinationPath,
        CancellationToken cancellationToken);
}

public enum PresentCompatibilitySeverity
{
    Info,
    Warning,
    Error
}

public enum PresentCompatibilityDirection
{
    Import,
    Export
}

public enum PresentCompatibilityResolution
{
    Translated,
    Degraded,
    Omitted,
    Blocked
}

public sealed record PresentCompatibilityIssue(
    Guid IssueId,
    PresentCompatibilitySeverity Severity,
    PresentCompatibilityDirection Direction,
    string FeatureType,
    Guid? SourceTargetId,
    string Description,
    PresentCompatibilityResolution Resolution);

public sealed record PresentCompatibilityReport(
    string Format,
    PresentCompatibilityDirection Direction,
    IReadOnlyList<PresentCompatibilityIssue> Issues)
{
    public bool HasBlockingIssues => Issues.Any(issue =>
        issue.Severity == PresentCompatibilitySeverity.Error ||
        issue.Resolution == PresentCompatibilityResolution.Blocked);

    public int WarningCount => Issues.Count(issue => issue.Severity == PresentCompatibilitySeverity.Warning);
}

public sealed record PresentExportResult(string Path, PresentCompatibilityReport CompatibilityReport);

/// <summary>Provides a previewable, structured account of format degradation.</summary>
public interface IPresentReportExportService : IPresentExportService
{
    PresentCompatibilityReport PreviewExport(PresentDocument document, string destinationPath);

    Task<PresentExportResult> ExportWithReportAsync(
        PresentDocument document,
        string destinationPath,
        CancellationToken cancellationToken);
}

public sealed record PresentImportSupport(
    string Format,
    string Description,
    IReadOnlyList<string> PreservedFeatures,
    IReadOnlyList<string> UnsupportedFeatures);

public interface IPresentImportService
{
    IReadOnlyList<string> ImportExtensions { get; }
    PresentImportSupport Support { get; }

    Task<PresentDocument> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken);
}
