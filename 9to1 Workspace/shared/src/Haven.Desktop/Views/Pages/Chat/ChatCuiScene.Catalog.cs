using Haven.Core;
using Haven.Desktop.Prefabs;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatCuiScene
{
    private IReadOnlyList<AgentDefinition> _agents = [];
    private IReadOnlyList<CapabilityDefinition> _capabilities = [];
    private IReadOnlyList<PromptDefinition> _instructions = [];
    private IReadOnlyList<ModeDefinition> _apps = [];
    private readonly Dictionary<string, AddMenuSelection> _catalogItems = new(StringComparer.Ordinal);
    private AddMenu.AddMenuAction? _catalogCategory;
    private string _agentName = "Default agent";
    private ChatActionMode _actionMode = ChatActionMode.AllowBasicActions;
    private GenerativeUiResponseMode _visualMode = GenerativeUiResponseMode.Auto;
    public void SetCatalogue(IReadOnlyList<AgentDefinition> agents, IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions, IReadOnlyList<ModeDefinition> apps) => Publish(() =>
    {
        _agents = agents.Where(item => item.IsEnabled).ToArray();
        _capabilities = capabilities.Where(item => item.IsEnabled && item.IsAttachable).ToArray();
        _instructions = instructions.Where(item => item.IsEnabled).ToArray();
        _apps = apps.Where(item => item.IsEnabled).ToArray();
        RebuildCatalog();
    });
    public void SetResponseState(string agentName, ChatActionMode actionMode, GenerativeUiResponseMode visualMode) => Publish(() =>
    { _agentName = agentName; _actionMode = actionMode; _visualMode = visualMode; });
    public void ShowAddMenu() => Publish(() => { _catalogCategory = null; _values["catalog.query"] = ""; _values["catalog.visible"] = true; RebuildCatalog(); });
    public void HideAddMenu() => Publish(() => { _values["catalog.visible"] = false; _values["catalog.query"] = ""; _catalogCategory = null; });
    public void ShowMentionSearch(string query) => Publish(() => { _catalogCategory = null; _values["catalog.visible"] = true; _values["catalog.query"] = query; RebuildCatalog(); });
    public void FilterCatalogue(string query) => ShowMentionSearch(query);
    private void ShowCatalog(AddMenu.AddMenuAction category) => Publish(() =>
    { _catalogCategory = category; _values["catalog.query"] = ""; _values["catalog.visible"] = true; RebuildCatalog(); });
    private void RebuildCatalog()
    {
        var query = _values["catalog.query"] as string ?? "";
        var candidates = new List<(string Key, string Name, string Description, int Score, AddMenuSelection Selection)>();
        void Add<T>(AddMenu.AddMenuAction action, IEnumerable<T> items, Func<T, Guid> id, Func<T, string> name, Func<T, string> detail)
        {
            if (_catalogCategory is { } category && category != action) return;
            foreach (var item in items)
            {
                var score = ChatAddMenuSurface.SearchScore(name(item), detail(item), query);
                if (score >= 0) candidates.Add(($"{action}:{id(item):D}", name(item), detail(item), score, new(action, item!)));
            }
        }
        Add(AddMenu.AddMenuAction.Agent, _agents, item => item.Id, item => item.Name, item => item.Description);
        Add(AddMenu.AddMenuAction.Capability, _capabilities, item => item.Id, item => item.Name, item => item.Description);
        Add(AddMenu.AddMenuAction.Instruction, _instructions, item => item.Id, item => item.Name, item => item.Description);
        Add(AddMenu.AddMenuAction.App, _apps, item => item.Id, item => item.Name, item => item.Description);
        var matching = candidates.OrderByDescending(item => item.Score).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Take(30).ToArray();
        _catalogItems.Clear();
        _values["catalog.items"] = matching.Select(item =>
        {
            _catalogItems.Add(item.Key, item.Selection);
            return new Dictionary<string, object?> { ["key"] = item.Key, ["name"] = item.Name, ["description"] = item.Description };
        }).ToArray();
        _values["catalog.status"] = matching.Length == 0 ? "No matching available attachments" : candidates.Count > 30 ? $"Showing 30 of {candidates.Count} matches. Refine your search." : "";
    }
    private void ShowResponseSettings()
    {
        var choices = new List<(string Label, Func<Task> Action)>();
        foreach (var mode in new[] { ChatActionMode.AllowAllActions, ChatActionMode.AllowBasicActions, ChatActionMode.JustChat })
            choices.Add(($"Actions: {mode}" + (_actionMode == mode ? " (selected)" : ""), () => _actions.SelectCatalog(new(AddMenu.AddMenuAction.AllowActions, mode))));
        foreach (var mode in new[] { GenerativeUiResponseMode.Auto, GenerativeUiResponseMode.AlwaysVisual, GenerativeUiResponseMode.PreferVisual, GenerativeUiResponseMode.PreferText, GenerativeUiResponseMode.AlwaysText })
            choices.Add(($"Responses: {mode}" + (_visualMode == mode ? " (selected)" : ""), () => _actions.SelectCatalog(new(AddMenu.AddMenuAction.VisualResponses, mode))));
        ShowChoices("Chat response settings", "Current agent: " + _agentName, choices);
    }
}
