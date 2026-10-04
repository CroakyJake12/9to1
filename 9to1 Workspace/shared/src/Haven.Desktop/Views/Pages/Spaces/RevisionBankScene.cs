using Haven.Application;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Spaces;

/// <summary>Metadata-only native view. Every attached write rechecks its original page/owner
/// before and after synchronous native notification; source references grant no source access.</summary>
internal sealed class RevisionBankScene : IDisposable
{
    private readonly Func<bool> _isOriginalCurrent;
    private readonly Container _content;
    private readonly HavenText _status;
    private long _generation;
    private bool _disposed;
    private Guid? _selectedCategory;

    public RevisionBankScene(Func<bool> isOriginalCurrent)
    {
        _isOriginalCurrent = isOriginalCurrent ?? throw new ArgumentNullException(nameof(isOriginalCurrent));
        Root = new Page { Name = "RevisionBankRoot", Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(22)));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        _content = new Container { Name = "RevisionBankContent", Layout = HavenLayout.Vertical };
        _content.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _content.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _status = new HavenText { Name = "RevisionBankStatus", Content = "" };
        _status.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Root.Add(_content);
        Root.Add(_status);
    }

    public Page Root { get; }
    internal Container OriginalContent => _content;
    internal HavenText OriginalStatus => _status;
    public event EventHandler? RefreshRequested;
    public event EventHandler<RevisionBankMutation>? MutationRequested;

    public void Render(SpaceDefinition space, RevisionBankSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (space.Id != snapshot.SpaceId || space.Revision != snapshot.SpaceRevision)
            throw new InvalidOperationException("The native Bank frame does not belong to this exact Space revision.");
        if (_disposed || !_isOriginalCurrent()) return;
        var generation = ++_generation;
        foreach (var child in _content.Children.ToArray())
            if (!Write(() => _content.Remove(child), generation)) return;

        var heading = new HavenText { Content = space.Name + " · Revision Bank" };
        if (!Write(() => _content.Add(heading), generation)) return;
        var refresh = Button("Refresh", "Refresh Revision Bank");
        refresh.Invoked += (_, _) => { if (Current(generation)) RefreshRequested?.Invoke(this, EventArgs.Empty); };
        if (!Write(() => _content.Add(refresh), generation)) return;

        var categoryRow = new Container { Layout = HavenLayout.Horizontal };
        foreach (var category in snapshot.Data.Categories.OrderBy(item => item.Position))
        {
            var chosen = category.CategoryId;
            var select = Button(category.Name, "Show " + category.Name);
            select.Invoked += (_, _) =>
            {
                if (!Current(generation)) return;
                _selectedCategory = chosen;
                Render(space, snapshot);
            };
            categoryRow.Add(select);
        }
        if (!Write(() => _content.Add(categoryRow), generation)) return;

        var categoryName = new Input { Name = "RevisionBankCategoryName", Placeholder = "Category name" };
        var create = Button("Create category", "Create Revision Bank category");
        create.Invoked += (_, _) => Request(new(space.Id, snapshot.SpaceRevision, Guid.NewGuid(),
            RevisionBankMutationKind.CreateCategory, CategoryId: Guid.NewGuid(), CategoryName: categoryName.Text), generation);
        var createRow = new Container { Layout = HavenLayout.Horizontal };
        createRow.Add(categoryName);
        createRow.Add(create);
        if (!Write(() => _content.Add(createRow), generation)) return;

        foreach (var category in snapshot.Data.Categories.Where(item => !item.IsBuiltIn).OrderBy(item => item.Position))
        {
            var captured = category;
            var name = new Input { Text = captured.Name, Placeholder = "Rename category" };
            var rename = Button("Rename", "Rename " + captured.Name);
            rename.Invoked += (_, _) => Request(new(space.Id, snapshot.SpaceRevision, Guid.NewGuid(),
                RevisionBankMutationKind.RenameCategory, CategoryId: captured.CategoryId, CategoryName: name.Text), generation);
            var remove = Button("Delete category", "Delete " + captured.Name);
            remove.Invoked += (_, _) => Request(new(space.Id, snapshot.SpaceRevision, Guid.NewGuid(),
                RevisionBankMutationKind.RemoveCategory, CategoryId: captured.CategoryId), generation);
            var row = new Container { Layout = HavenLayout.Horizontal };
            row.Add(name); row.Add(rename); row.Add(remove);
            if (!Write(() => _content.Add(row), generation)) return;
        }

        var automatic = Button(snapshot.Data.AutomaticallyClassifyTypes
            ? "Automatic type classification: on" : "Automatic type classification: off",
            "Toggle automatic Revision Bank classification");
        automatic.Invoked += (_, _) => Request(new(space.Id, snapshot.SpaceRevision, Guid.NewGuid(),
            RevisionBankMutationKind.SetAutomaticClassification,
            AutomaticallyClassifyTypes: !snapshot.Data.AutomaticallyClassifyTypes), generation);
        if (!Write(() => _content.Add(automatic), generation)) return;

        var selected = _selectedCategory;
        if (selected is { } selectedId && !snapshot.Data.Categories.Any(item => item.CategoryId == selectedId))
            _selectedCategory = selected = null;
        var members = snapshot.Data.Members.Where(member => selected is null || selected == RevisionBankCategories.All ||
            member.CategoryIds.Contains(selected.Value)).ToArray();
        if (members.Length == 0 && !Write(() => _content.Add(new HavenText { Content = "No sources in this category." }), generation)) return;
        foreach (var member in members)
        {
            var reference = space.ContextReferences?.SingleOrDefault(item => item.ContextId == member.ResourceId)
                ?? throw new InvalidDataException("A Revision Bank member has no canonical Space source.");
            var row = new Container { Layout = HavenLayout.Vertical };
            row.Add(new HavenText { Content = reference.Kind + " · " + reference.OwnerAppId });
            row.Add(new HavenText { Content = reference.CanonicalEntityId });
            var remove = Button("Remove from Bank", "Remove Bank source " + member.ResourceId.ToString("D"));
            var resourceId = member.ResourceId;
            remove.Invoked += (_, _) => Request(new(space.Id, snapshot.SpaceRevision, Guid.NewGuid(),
                RevisionBankMutationKind.Remove, resourceId), generation);
            row.Add(remove);
            foreach (var category in snapshot.Data.Categories.Where(item => item.CategoryId != RevisionBankCategories.All &&
                item.CategoryId != RevisionBankCategories.Recommended))
            {
                var categoryId = category.CategoryId;
                var categories = member.CategoryIds.ToArray();
                var classify = Button((categories.Contains(categoryId) ? "Remove " : "Add ") + category.Name,
                    "Classify Bank source " + resourceId.ToString("D") + " as " + category.Name);
                classify.Invoked += (_, _) => Request(new(space.Id, snapshot.SpaceRevision, Guid.NewGuid(),
                    RevisionBankMutationKind.Classify, resourceId,
                    CategoryIds: categories.Contains(categoryId) ? categories.Where(id => id != categoryId).ToArray() : [.. categories, categoryId]), generation);
                row.Add(classify);
            }
            if (!Write(() => _content.Add(row), generation)) return;
        }

        var enrolled = snapshot.Data.Members.Select(item => item.ResourceId).ToHashSet();
        var availableHeading = new HavenText { Content = "Other sources already in this Space" };
        if (!Write(() => _content.Add(availableHeading), generation)) return;
        foreach (var reference in space.ContextReferences ?? [])
        {
            if (enrolled.Contains(reference.ContextId)) continue;
            var id = reference.ContextId;
            var add = Button("Add " + reference.Kind + " · " + reference.OwnerAppId, "Add Bank source " + id.ToString("D"));
            add.Invoked += (_, _) => Request(new(space.Id, snapshot.SpaceRevision, Guid.NewGuid(), RevisionBankMutationKind.Add, id), generation);
            if (!Write(() => _content.Add(add), generation)) return;
        }
    }

    public void SetStatus(string? message)
    {
        var generation = _generation;
        if (!Write(() => _status.Content = message ?? "", generation)) return;
        _ = Write(() => _status.SetValue(HavenProperties.Visibility,
            string.IsNullOrWhiteSpace(message) ? HavenVisibility.Collapsed : HavenVisibility.Visible), generation);
    }

    private void Request(RevisionBankMutation request, long generation)
    {
        if (Current(generation)) MutationRequested?.Invoke(this, request);
    }

    private bool Current(long generation) => !_disposed && generation == _generation && _isOriginalCurrent();

    private bool Write(Action write, long generation)
    {
        if (!Current(generation)) return false;
        write();
        return Current(generation);
    }

    public void WithdrawFrame() => ++_generation;

    private static HavenButton Button(string label, string accessibleName)
    {
        var button = new HavenButton { Content = label, Variant = ButtonVariant.Tertiary };
        button.Accessibility.AccessibleName = accessibleName;
        return button;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_generation;
        RefreshRequested = null;
        MutationRequested = null;
    }
}
