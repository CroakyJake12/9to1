using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Prefabs;

/// <summary>Shared retained composer behaviour for Chat and Go; formatting edits the existing draft.</summary>
public sealed class ChatComposerController
{
    private static readonly ConditionalWeakTable<Prefab, ChatComposerController> Instances = new();
    private readonly Prefab _prefab;
    private readonly Container _row;
    private readonly Container _viewport;
    private readonly Input _input;
    private readonly Button _expand;
    private readonly Button _format;
    private readonly Button _emoji;
    private PopupMenu? _popup;
    public bool Expanded { get; private set; }
    public Action<Input>? FocusRequested { get; set; }

    public static ChatComposerController For(Prefab prefab) => Instances.GetValue(prefab, value => new(value));

    private ChatComposerController(Prefab prefab)
    {
        _prefab = prefab;
        _row = prefab.GetComponent<Container>("ComposerRow");
        _viewport = prefab.GetComponent<Container>("InstructionViewport");
        _input = prefab.GetComponent<Input>("Instruction");
        _expand = MakeButton("Expand composer", "↗");
        _format = MakeButton("Markdown formatting", "Aa");
        _emoji = MakeButton("Emoji picker", "☺");
        _row.Add(_expand); _row.Add(_format); _row.Add(_emoji);
        _expand.Invoked += (_, _) => SetExpanded(!Expanded);
        _format.Invoked += (_, _) => ShowFormatting();
        _emoji.Invoked += (_, _) => ShowEmoji();
        foreach (var element in _prefab.DescendantsAndSelf())
            element.Invalidated += (_, _) => Dispatcher.UIThread.Post(UpdateFocusVisibility);
        SetExpanded(false);
    }

    public void SetExpanded(bool expanded)
    {
        Expanded = expanded;
        _popup?.Dismiss();
        _row.Columns = expanded ? "44px 44px 44px 1fr 44px 44px 44px" : "44px 1fr 44px 44px";
        _row.Rows = expanded ? "124px 44px" : "44px";
        Place(_viewport, expanded ? 0 : 1, 0);
        _viewport.SetValue(HavenProperties.ColumnSpan, expanded ? 7 : 1);
        _viewport.SetValue(HavenProperties.Height, HavenLength.Px(expanded ? 124 : 44));
        _input.SetValue(HavenProperties.Height, HavenLength.Px(expanded ? 124 : 44));
        Place(_prefab.GetComponent<Button>("AddMenu"), 0, expanded ? 1 : 0);
        Place(_prefab.GetComponent<Icon>("AddMenuIcon"), 0, expanded ? 1 : 0);
        Place(_format, 1, 1); Place(_emoji, 2, 1);
        Place(_expand, expanded ? 4 : 1, expanded ? 1 : 0);
        _expand.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        _expand.SetValue(HavenProperties.ZIndex, 2);
        _input.SetValue(HavenProperties.Padding, HavenThickness.Parse(expanded ? "0px 12px" : "0px 56px 0px 12px"));
        Place(_prefab.GetComponent<Button>("ChatSettings"), expanded ? 5 : 2, expanded ? 1 : 0);
        Place(_prefab.GetComponent<Icon>("ChatSettingsIcon"), expanded ? 5 : 2, expanded ? 1 : 0);
        Place(_prefab.GetComponent<Button>("Send"), expanded ? 6 : 3, expanded ? 1 : 0);
        Place(_prefab.GetComponent<Icon>("SendIcon"), expanded ? 6 : 3, expanded ? 1 : 0);
        _expand.Content = expanded ? "↙" : "↗";
        _expand.Accessibility.AccessibleName = expanded ? "Collapse composer" : "Expand composer";
        UpdateFocusVisibility();
        FocusRequested?.Invoke(_input);
    }

    private void UpdateFocusVisibility()
    {
        var focused = _prefab.DescendantsAndSelf().Any(element => element.State.HasFlag(HavenElementState.Focused)) ||
            _popup is not null;
        var visibility = Expanded && focused ? HavenVisibility.Visible : HavenVisibility.Collapsed;
        // Avoid creating an invalidation loop when a focus update does not change visibility.
        if (_format.GetValue(HavenProperties.Visibility) != visibility) _format.SetValue(HavenProperties.Visibility, visibility);
        if (_emoji.GetValue(HavenProperties.Visibility) != visibility) _emoji.SetValue(HavenProperties.Visibility, visibility);
    }

    private void ShowFormatting()
    {
        ShowMenu(_format,
        [
            new("Bold", () => SurroundSelection("**", "**")),
            new("Italic", () => SurroundSelection("_", "_")),
            new("Inline code", () => SurroundSelection("`", "`")),
            new("Link", () => SurroundSelection("[", "](https://)")),
            new("Quote", () => SurroundSelection("> ", "")),
            new("Bullet", () => SurroundSelection("- ", ""))
        ], "Markdown formatting");
    }

    private void ShowEmoji()
    {
        ShowMenu(_emoji, [], "Search Unicode emoji");
        var popup = _popup!;
        var search = new Input { Placeholder = "Search emoji or category" };
        search.Accessibility.AccessibleName = "Search emoji names and categories";
        popup.Card.Add(search);
        var results = new Container { Layout = HavenLayout.Vertical };
        popup.Card.Add(results);
        var query = "";
        var page = 0;
        const int pageSize = 24;
        void Refresh()
        {
            foreach (var child in results.Children.ToArray()) results.Remove(child);
            var matches = ComposerEmojiCatalogue.Entries.Where(entry =>
                entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.Group.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Text == query).ToArray();
            foreach (var entry in matches.Skip(page * pageSize).Take(pageSize))
            {
                var button = new Button { Content = $"{entry.Text} {entry.Name}", Variant = ButtonVariant.Navigation };
                button.Accessibility.AccessibleName = $"Insert {entry.Name}; {entry.Group}";
                button.Invoked += (_, _) => { popup.Dismiss(); _input.InsertText(entry.Text); FocusRequested?.Invoke(_input); };
                results.Add(button);
            }
            if (page > 0)
            {
                var previous = new Button { Content = "Previous emoji", Variant = ButtonVariant.Navigation };
                previous.Invoked += (_, _) => { page--; Refresh(); };
                results.Add(previous);
            }
            if ((page + 1) * pageSize < matches.Length)
            {
                var next = new Button { Content = "More emoji", Variant = ButtonVariant.Navigation };
                next.Invoked += (_, _) => { page++; Refresh(); };
                results.Add(next);
            }
        }
        search.Invalidated += (_, _) => { if (search.Text == query) return; query = search.Text; page = 0; Refresh(); };
        Refresh();
        FocusRequested?.Invoke(search);
    }

    public void SurroundSelection(string before, string after)
    {
        var start = _input.SelectionStart;
        var selected = _input.SelectedText;
        _input.InsertText(before + selected + after);
        _input.SetSelection(start + before.Length, start + before.Length + selected.Length);
        FocusRequested?.Invoke(_input);
    }

    private void ShowMenu(Button anchor, IReadOnlyList<PopupMenuItem> items, string label)
    {
        _popup?.Dismiss();
        HavenElement root = _prefab;
        while (root.Parent is not null) root = root.Parent;
        var popup = new PopupMenu(anchor, root, items, 240, label);
        _popup = popup;
        popup.Dismissed += (_, _) => { if (ReferenceEquals(_popup, popup)) _popup = null; UpdateFocusVisibility(); };
        root.Add(popup);
    }

    private static Button MakeButton(string name, string content)
    {
        var button = new Button { Content = content, Variant = ButtonVariant.Icon };
        button.Accessibility.AccessibleName = name;
        button.SetValue(HavenProperties.Width, HavenLength.Px(44));
        button.SetValue(HavenProperties.Height, HavenLength.Px(44));
        return button;
    }

    private static void Place(HavenElement element, int column, int row)
    {
        element.SetValue(HavenProperties.Column, column);
        element.SetValue(HavenProperties.Row, row);
    }
}
