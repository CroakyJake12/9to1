using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    internal DeveloperManualGitObservation? OriginalManualGitObservation { get; private set; }
    private readonly List<DeveloperManualGitObservation> _originalManualGit = [];

    private bool CanInspectManualGit() => _manual && _manualProcessOwner is { SupportsGitInspection: true } &&
        _project.Root.EnvironmentId == "local" && _project.Repository is { ProviderId: "git" };
    private bool TryGetGitInspectionValue(string path, out object? value)
    {
        if (path != "CanInspectGit") { value = null; return false; }
        value = _project.Repository is { ProviderId: "git" } && IsActionAvailable("dev.git-status") == true; return true;
    }

    private ValueTask InspectManualGitAsync(string command, CancellationToken token)
    {
        long generation; DeveloperProjectGitInspectionKind kind; string digest;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true || !CanInspectManualGit())
                throw new InvalidOperationException("Git inspection is unavailable for this project.");
            kind = command switch { "dev.git-status" => DeveloperProjectGitInspectionKind.Status,
                "dev.git-diff" => DeveloperProjectGitInspectionKind.Diff, "dev.git-branches" => DeveloperProjectGitInspectionKind.Branches,
                _ => throw new ArgumentOutOfRangeException(nameof(command)) };
            var prepared = DeveloperGitCommand.Prepare(_project, kind);
            digest = prepared.Value?.DefinitionSha256 ?? throw new InvalidOperationException(prepared.Error?.Message);
            generation = _generation; _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                try
                {
                    token.ThrowIfCancellationRequested();
                    var project = await ResolveProjectAsync(original, token).ConfigureAwait(false);
                    if (DeveloperGitCommand.Prepare(project, kind).Value?.DefinitionSha256 != digest)
                        throw new InvalidOperationException("This repository changed. Refresh the project before inspecting Git.");
                    using var stop = CancellationTokenSource.CreateLinkedTokenSource(_businessToken);
                    lock (_gate) _manualRunStop = stop;
                    try
                    {
                        await PublishAsync(original, () => _status = "Waiting for permission to inspect Git. Git sees saved files; unsaved editor changes are kept.").ConfigureAwait(false);
                        var owner = _manualProcessOwner ?? throw new InvalidOperationException("Git inspection is unavailable.");
                        var observed = await original.AwaitAsync(InvokeOriginalSource(() => owner.InspectGitAsync(project, kind, digest,
                            body => InvokeOriginalSource(() => { body(); return true; }), stop.Token))).ConfigureAwait(false);
                        // Actual terminal/lifecycle evidence is retained before any UI callback.
                        lock (_gate) { OriginalManualGitObservation = observed; _originalManualGit.Add(observed); }
                        await PublishAsync(original, () =>
                        {
                            _processStatus = observed.ProcessSucceeded ? "Git inspection finished."
                                : observed.Process is { TimedOut: true } ? "Git timed out. Its output is kept below."
                                : observed.Process is { } process ? $"Git exited with code {process.ExitCode}. Check the output below."
                                : "Git did not start. Check permissions and project setup.";
                            _processOutput = observed.Process is { } output ? output.StandardOutput +
                                (string.IsNullOrEmpty(output.StandardError) ? "" : "\n[standard error]\n" + output.StandardError) : "";
                            if (observed.OriginalErrors.Count != 0)
                                _processOutput += "\n[problems]\n" + string.Join(Environment.NewLine, observed.OriginalErrors.Select(error => error.Message));
                            _status = observed.ProcessSucceeded ? "Git reads saved files. Your unsaved drafts are unchanged." : "Git inspection did not finish cleanly. Output and problems are kept below.";
                        }).ConfigureAwait(false);
                        foreach (var error in observed.OriginalErrors) original.Retain(error);
                    }
                    finally { lock (_gate) if (ReferenceEquals(_manualRunStop, stop)) _manualRunStop = null; }
                }
                catch (Exception error) { original.Retain(error); }
                finally
                {
                    lock (_gate) _pending = false;
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () => { }).ConfigureAwait(false); }
                        catch (Exception error) { original.Retain(error); }
                    original.ThrowRetained();
                }
            }));
        }
        catch { lock (_gate) _pending = false; throw; }
    }
}
