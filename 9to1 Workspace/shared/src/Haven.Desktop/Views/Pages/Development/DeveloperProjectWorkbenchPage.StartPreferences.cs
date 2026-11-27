namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    /// <summary>The session calls this only after publishing this exact open workbench.
    /// Its owning action retains the metadata error; this changes the existing visible
    /// operation status without reopening a project or changing source/process state.</summary>
    internal Task ReportOpenedMetadataFailureAsync(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        long generation; lock (_gate) generation = _generation;
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            original.DemandPublication();
            await PublishAsync(original, () => _status = message).ConfigureAwait(false);
        });
    }
}
