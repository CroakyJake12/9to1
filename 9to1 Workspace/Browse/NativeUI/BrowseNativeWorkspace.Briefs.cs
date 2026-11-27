using Haven.Core;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseNativeWorkspace
{
    private sealed class NavigationBrief(Guid targetTabId, Guid sourceTabId, BrowseNavigationBrief brief,
        IBrowseAppContentSession source, IBrowseAppContentSession target)
    {
        internal Guid TargetTabId { get; } = targetTabId;
        internal Guid SourceTabId { get; } = sourceTabId;
        internal BrowseNavigationBrief Brief { get; } = brief;
        internal IBrowseAppContentSession? Source { get; set; } = source;
        internal IBrowseAppContentSession Target { get; } = target;
    }
    private readonly Dictionary<Guid, NavigationBrief> _navigationBriefs = [];

    private IBrowseAppContentSession? ObserveBriefOwner(Guid id) =>
        !_retiring && !_disposed && _chrome.State.Tabs.Any(tab => tab.Id == id) ? _chrome.ObserveOriginalAppContent(id) : null;

    private IBrowseAppContentSession? AdmitNavigationBrief(BrowseNavigationIntent intent)
    {
        if (intent.Brief is not { } brief) return null;
        if (!intent.OpenInNewTab || intent.Address.Namespace != AppContentNamespace.NativeApp ||
            intent.Address.ResourceSegments.Count != 0 || brief.ConversationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(brief.Text) || brief.Text.Length > 3000 ||
            brief.Text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            return null;
        var source = ObserveBriefOwner(intent.SourceTabId);
        return source is { IsPublicationCurrent: true } && source is IBrowseNavigationBriefSource owner &&
            owner.CanRestoreNavigationBrief(brief.ConversationId) ? source : null;
    }

    private bool IsNavigationBriefSourceCurrent(BrowseNavigationIntent intent, IBrowseAppContentSession? source) =>
        source is { IsPublicationCurrent: true } && ReferenceEquals(ObserveBriefOwner(intent.SourceTabId), source) &&
        source is IBrowseNavigationBriefSource owner && intent.Brief is { } brief && owner.CanRestoreNavigationBrief(brief.ConversationId);

    private void PublishNavigationBrief(BrowseNavigationIntent intent, IBrowseAppContentSession source, BrowseChromeSnapshot result)
    {
        if (intent.Brief is not { } brief || !IsOriginalPublicationCurrent || !IsNavigationBriefSourceCurrent(intent, source)) return;
        var target = ObserveBriefOwner(result.SelectedTabId);
        if (target is not { IsPublicationCurrent: true } || result.SelectedTabId == intent.SourceTabId ||
            !Equals(result.SelectedTab.Address, intent.Address) || !Equals(target.Address, intent.Address))
            throw new InvalidOperationException("The destination opened without a current owner for the original brief.");
        _navigationBriefs[result.SelectedTabId] = new(result.SelectedTabId, intent.SourceTabId, brief, source, target);
    }

    private NavigationBrief? CurrentNavigationBrief =>
        _navigationBriefs.TryGetValue(_snapshot.SelectedTabId, out var brief) &&
        ReferenceEquals(ObserveBriefOwner(brief.TargetTabId), brief.Target) && brief.Target.IsPublicationCurrent
            ? brief : null;

    private bool CanReturnToBrief(NavigationBrief brief) => brief.Source is { IsPublicationCurrent: true } source &&
        ReferenceEquals(ObserveBriefOwner(brief.SourceTabId), source) &&
        source is IBrowseNavigationBriefSource owner && owner.CanRestoreNavigationBrief(brief.Brief.ConversationId);

    private void RefreshNavigationBriefs()
    {
        foreach (var pair in _navigationBriefs.ToArray())
        {
            if (!ReferenceEquals(ObserveBriefOwner(pair.Key), pair.Value.Target)) _navigationBriefs.Remove(pair.Key);
            else if (!CanReturnToBrief(pair.Value)) pair.Value.Source = null;
        }
    }

    private bool TryGetNavigationBriefBinding(string path, out object? value)
    {
        var brief = CurrentNavigationBrief;
        value = path switch
        {
            "ExploreBriefVisible" => brief is not null,
            "ExploreBriefText" => brief?.Brief.Text ?? string.Empty,
            "ExploreBriefTarget" => brief,
            "ExploreBriefReturnAvailable" => brief is not null && CanReturnToBrief(brief),
            "ExploreBriefReturnHint" => brief is not null && CanReturnToBrief(brief) ? "Return to the original Explore conversation" :
                "The original Explore conversation is no longer open.",
            _ => null
        };
        return path is "ExploreBriefVisible" or "ExploreBriefText" or "ExploreBriefTarget" or
            "ExploreBriefReturnAvailable" or "ExploreBriefReturnHint";
    }

    private async Task<string?> ReturnToNavigationBriefAsync(object? parameter, CancellationToken token)
    {
        var current = CurrentNavigationBrief;
        if (current is null || !ReferenceEquals(current, parameter) || !CanReturnToBrief(current))
            return "The original Explore conversation is no longer available.";
        var source = current.Source!;
        await Operation(() => _chrome.SelectTabAsync(current.SourceTabId, token));
        if (!ReferenceEquals(ObserveBriefOwner(current.SourceTabId), source) || !CanReturnToBrief(current))
            throw new InvalidOperationException("The Explore conversation changed before it could be restored.");
        var restore = ((IBrowseNavigationBriefSource)source).RestoreNavigationBriefAsync(current.Brief.ConversationId, token)
            ?? throw new InvalidOperationException("Explore did not return its original restore task.");
        lock (_sourceGate) Retain(_originalOperations, restore);
        return await restore ? null : "The Explore view changed before it could be restored. Its current conversation is kept.";
    }

    private void DismissNavigationBrief(object? parameter)
    {
        var current = CurrentNavigationBrief;
        if (current is null || !ReferenceEquals(current, parameter))
        { _status = "Use the brief currently shown in this app."; return; }
        _navigationBriefs.Remove(current.TargetTabId);
    }
}
