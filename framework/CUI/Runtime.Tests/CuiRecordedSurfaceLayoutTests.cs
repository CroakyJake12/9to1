using System.ComponentModel;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Pins the layout of the affected real Home Library and Spaces sidebar CUI branches. The tests
/// load branch fragments from the shipped documents, so their conditional roots, Repeat template,
/// bindings, and authored wrappers stay in sync with production markup.
/// </summary>
public sealed class CuiRecordedSurfaceLayoutTests
{
    [Fact]
    public async Task Home_library_labels_and_folder_name_field_flow_and_survive_live_branch_updates()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var document = ReadDocument("9T.W2.Home.cui");
            var library = document.Descendants("Container").Single(element => (string?)element.Attribute("id") == "library-page");
            var browse = Conditional(library, "{Binding IsLibraryBrowseMode}", element =>
                element.Descendants("Text").Any(text => (string?)text.Attribute("Text") == "Library"));
            var folder = Conditional(library, "{Binding IsBoardFolderSelection}");
            var markup = BranchHost(document, browse, folder);
            var state = new State();
            state.Set("IsLibraryBrowseMode", true);
            state.Set("IsBoardFolderSelection", false);
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(state);
            var loaded = loader.LoadMarkup(markup, "actual-home-library-branches.cui");
            Assert.DoesNotContain(loaded.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root);
            loader.WireBindings(root);
            var window = new Window { Width = 720, Height = 520, Content = root };
            try
            {
                window.Show(); Pump(window);
                var title = Text(root, "Library");
                var description = Text(root, "Authorised references to your artifacts. Items open in their owning app.");
                AssertBefore(window, title, description, "The Library description must follow its heading.");
                Assert.Empty(root.GetVisualDescendants().OfType<TextBlock>().Where(item => item.Text == "Choose Boards folder"));

                state.NotifyAll(); Pump(window);
                Assert.Same(title, Text(root, "Library"));
                Assert.Same(description, Text(root, "Authorised references to your artifacts. Items open in their owning app."));

                state.Set("IsLibraryBrowseMode", false);
                state.Set("IsBoardFolderSelection", true);
                state.NotifyAll(); Pump(window);
                var folderTitle = Text(root, "Choose Boards folder");
                var folderDescription = Text(root, "Select the configured Boards folder, then create the board with the name above.");
                var fieldLabel = Text(root, "New Board file name (.9to1board)");
                var name = Assert.Single(root.GetVisualDescendants().OfType<TextBox>());
                Assert.Equal("New board", name.Text);
                AssertBefore(window, folderTitle, folderDescription, "Folder instructions must follow their heading.");
                AssertBefore(window, folderDescription, fieldLabel, "The name label must follow the folder instructions.");
                AssertBefore(window, fieldLabel, name, "The name input must follow its label.");
                Assert.Empty(root.GetVisualDescendants().OfType<TextBlock>().Where(item => item.Text == "Library"));

                state.NotifyAll(); Pump(window);
                Assert.Same(name, Assert.Single(root.GetVisualDescendants().OfType<TextBox>()));
                state.Set("IsBoardFolderSelection", false);
                state.Set("IsLibraryBrowseMode", true);
                state.NotifyAll(); Pump(window);
                var returnedTitle = Text(root, "Library");
                var returnedDescription = Text(root, "Authorised references to your artifacts. Items open in their owning app.");
                AssertBefore(window, returnedTitle, returnedDescription, "The Library description must follow its heading after returning from folder selection.");
                Assert.Empty(root.GetVisualDescendants().OfType<TextBox>());
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Spaces_recent_chat_sidebar_flows_search_notice_and_multiple_keyed_rows()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var document = ReadDocument("9T.W2.SpacesHome.cui");
            var history = Conditional(document, "!{Binding spaces.isTasksSpace}");
            var markup = BranchHost(document, history);
            var state = new State();
            state.Set("spaces.isTasksSpace", false);
            state.Set("spaces.canSearchConversations", true);
            state.Set("spaces.hasConversations", true);
            state.Set("spaces.conversationHistoryHeading", "Recent chats");
            state.Set("spaces.conversationQuery", "");
            state.Set("spaces.conversationHistoryNotice", "Showing the 20 most recent saved chats.");
            state.Set("spaces.conversations", new[]
            {
                Conversation("one", "First saved chat", "Updated 10:00"),
                Conversation("two", "Second saved chat", "Updated 09:00")
            });
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(state);
            var loaded = loader.LoadMarkup(markup, "actual-spaces-history-branch.cui");
            Assert.DoesNotContain(loaded.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root);
            loader.WireBindings(root);
            var window = new Window { Width = 520, Height = 760, Content = root };
            try
            {
                window.Show(); Pump(window);
                AssertHistoryRowsFlow(window, root);
                var search = Assert.Single(root.GetVisualDescendants().OfType<TextBox>());
                var firstRow = RowButton(root, "First saved chat");
                var secondRow = RowButton(root, "Second saved chat");

                state.NotifyAll(); Pump(window);
                Assert.Same(search, Assert.Single(root.GetVisualDescendants().OfType<TextBox>()));
                Assert.Same(firstRow, RowButton(root, "First saved chat"));
                Assert.Same(secondRow, RowButton(root, "Second saved chat"));

                state.Set("spaces.isTasksSpace", true);
                state.NotifyAll(); Pump(window);
                Assert.Empty(root.GetVisualDescendants().OfType<TextBox>());
                Assert.Empty(root.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "open-space-conversation"));

                state.Set("spaces.isTasksSpace", false);
                state.NotifyAll(); Pump(window);
                AssertHistoryRowsFlow(window, root);
                Assert.Equal(2, root.GetVisualDescendants().OfType<Button>().Count(button => button.Name == "open-space-conversation"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Spaces_permission_status_and_review_action_flow_as_distinct_controls()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var document = ReadDocument("9T.W2.SpacesHome.cui");
            var permission = Conditional(document, "{Binding task.permission}");
            var markup = BranchHost(document, permission);
            var state = new State();
            state.Set("task.permission", true);
            state.Set("task.status", "Permission requested");
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(state);
            var loaded = loader.LoadMarkup(markup, "actual-spaces-permission-branch.cui");
            Assert.DoesNotContain(loaded.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root);
            loader.WireBindings(root);
            var window = new Window { Width = 520, Height = 240, Content = root };
            try
            {
                window.Show(); Pump(window);
                var status = Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "Permission requested");
                var review = Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "space-task-review");
                AssertBefore(window, status, review, "Permission review must follow its status action.");
                state.NotifyAll(); Pump(window);
                Assert.Same(status, Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "Permission requested"));
                Assert.Same(review, Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "space-task-review"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertHistoryRowsFlow(Window window, Control root)
    {
        var heading = Text(root, "Recent chats");
        var search = Assert.Single(root.GetVisualDescendants().OfType<TextBox>());
        var searchButton = Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "space-history-search-button");
        var notice = Text(root, "Showing the 20 most recent saved chats.");
        var divider = Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), control => control.Name == "space-recent-chats-divider");
        var rows = root.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "open-space-conversation").ToArray();
        Assert.Equal(2, rows.Length);
        AssertBefore(window, heading, search, "Search must follow the history heading.");
        AssertBefore(window, search, searchButton, "Search action must follow the query field.");
        AssertBefore(window, searchButton, notice, "The history notice must follow the search actions.");
        AssertBefore(window, notice, divider, "The divider must follow the history notice.");
        AssertBefore(window, divider, rows[0], "Saved chats must follow the sidebar header.");
        AssertBefore(window, rows[0], rows[1], "Saved chat rows must have distinct vertical positions.");
        AssertBefore(window, Text(root, "First saved chat"), Text(root, "Updated 10:00"), "Each saved-chat title must precede its date.");
        AssertBefore(window, Text(root, "Second saved chat"), Text(root, "Updated 09:00"), "Each saved-chat title must precede its date.");
    }

    private static Dictionary<string, object?> Conversation(string id, string title, string updated) => new()
    {
        ["id"] = id,
        ["title"] = title,
        ["updatedLabel"] = updated,
        ["openLabel"] = "Open " + title,
        ["isCurrent"] = false
    };

    private static XElement ReadDocument(string resourceName)
    {
        using var stream = typeof(CuiRecordedSurfaceLayoutTests).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"The shipped CUI resource '{resourceName}' is required.");
        return XElement.Load(stream);
    }

    private static XElement Conditional(XElement document, string condition, Func<XElement, bool>? predicate = null) => document.Descendants("If")
        .Single(element => string.Equals((string?)element.Attribute("Condition"), condition, StringComparison.Ordinal)
            && (predicate is null || predicate(element)));

    private static string BranchHost(XElement document, params XElement[] branches)
    {
        var root = new XElement("Cui");
        if (document.Element("Actions") is { } actions) root.Add(new XElement(actions));
        root.Add(new XElement("Theme", new XAttribute("Name", "user_preference"),
            new XElement("Page", new XElement("Container", new XAttribute("Type", "Vertical Stack"), new XAttribute("CellGap", "8"),
                branches.Select(branch => new XElement(branch))))));
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static TextBlock Text(Control root, string value) =>
        Assert.Single(root.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == value);

    private static Button RowButton(Control root, string title) =>
        Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "open-space-conversation"
            && button.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == title));

    private static void AssertBefore(Window window, Control before, Control after, string message)
    {
        var first = Bounds(window, before);
        var second = Bounds(window, after);
        Assert.True(first.Bottom <= second.Top + .5,
            $"{message} First={first}, second={second}.");
    }

    private static Rect Bounds(Window window, Control control) => new(
        control.TranslatePoint(default, window) ?? throw new InvalidOperationException("Expected control to be attached to the headless window."),
        control.Bounds.Size);

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private sealed class State : ICuiWritableBindingContext, INotifyPropertyChanged
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool TryGetValue(string path, out object? value) => _values.TryGetValue(path, out value);

        public bool TrySetValue(string path, object? value)
        {
            if (!_values.ContainsKey(path)) return false;
            _values[path] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(path));
            return true;
        }

        public void Set(string path, object? value) => _values[path] = value;

        public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
