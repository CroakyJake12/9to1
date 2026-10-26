using Haven.Application;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Services;

/// <summary>
/// Connects a reviewed template payload to the existing manual-project lifecycle. The selected
/// folder is registered by the same issuer-recognized Home/Files setup journal; the returned
/// project is later populated through the workbench's existing reviewed absent-target CREATE owner.
/// This adapter creates no Files identity and never turns a template manifest into permission.
/// </summary>
public sealed class DeveloperTemplateProjectEntryAdapter(
    DeveloperManualProjectEntryAdapter manualEntry,
    DeveloperProjectTemplateMaterializer materializer)
{
    public Task<DeveloperProjectReference> RegisterExistingFolderAsync(
        Guid filesStoreId, Guid destinationFolderId, string existingProjectRoot,
        DeveloperProjectTemplateMaterialization materialization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (!materializer.IsIssued(materialization))
            throw new UnauthorizedAccessException("Use the exact provider materialization reviewed by this Build session.");
        if (!Path.IsPathFullyQualified(existingProjectRoot) ||
            !StringComparer.Ordinal.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(existingProjectRoot)), materialization.ProjectName))
            throw new InvalidOperationException("The registered folder must have the exact project name shown in the template review.");
        if (materialization.Files.IsDefaultOrEmpty || materialization.Files.Any(file =>
                string.IsNullOrWhiteSpace(file.RelativePath) || file.RelativePath.Contains('/') ||
                !StringComparer.Ordinal.Equals(file.RelativePath, Path.GetFileName(file.RelativePath))))
            throw new InvalidDataException("This reviewed starter only supports direct project-root files.");
        return manualEntry.RegisterExistingFolderWithCapturePreflightAsync(filesStoreId, destinationFolderId,
            existingProjectRoot, (capture, token) =>
            {
                DemandEmptyTarget(capture, token);
                return Task.CompletedTask;
            }, cancellationToken);
    }

    internal static void DemandEmptyTarget(IDeveloperProjectOriginalExistingSourceCapture capture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();
        // Only the configured Files/kernel capture, issued and revalidated by the manual owner,
        // reaches this check. Both files and nested directories make a target nonempty.
        if (capture.OriginalFiles.Length != 0 || capture.OriginalFolderPaths.Length != 0)
            throw new InvalidOperationException("Choose an empty project folder before creating a template project.");
    }
}
