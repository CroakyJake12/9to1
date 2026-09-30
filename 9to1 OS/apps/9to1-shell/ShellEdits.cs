namespace NineToOne.Os.Shell;

/// <summary>Settings, gestures and later authorised clients use the same typed edits, with no reserved layer categories.</summary>
public static class ShellEdits
{
    public static ShellConfiguration ChangeTaskbar(ShellConfiguration config, Func<TaskbarConfiguration, TaskbarConfiguration> edit) =>
        config with { Spaces = config.Spaces.Select(s => s.Id == config.ActiveSpaceId ? s with { Taskbar = edit(s.Taskbar) } : s).ToArray() };
    public static ShellConfiguration StepLayer(ShellConfiguration config, int direction) => ChangeTaskbar(config, bar =>
    {
        var current = bar.Layers.ToList().FindIndex(l => l.Id == bar.ActiveLayerId);
        return bar with { ActiveLayerId = bar.Layers[Math.Clamp(current + Math.Sign(direction), 0, bar.Layers.Count - 1)].Id };
    });
    public static ShellConfiguration AddLayer(ShellConfiguration config, string name) => ChangeTaskbar(config, bar =>
    {
        if (bar.Layers.Count == 5) throw new InvalidOperationException("A taskbar supports at most five layers.");
        var layer = new TaskbarLayer(Guid.NewGuid(), name, bar.Layers.Single(l => l.Id == bar.ActiveLayerId).Presentation, []);
        return bar with { ActiveLayerId = layer.Id, Layers = [.. bar.Layers, layer] };
    });
    public static ShellConfiguration RenameLayer(ShellConfiguration config, string name) => ChangeTaskbar(config, bar => bar with
    { Layers = bar.Layers.Select(l => l.Id == bar.ActiveLayerId ? l with { Name = name } : l).ToArray() });
    public static ShellConfiguration RemoveLayer(ShellConfiguration config) => ChangeTaskbar(config, bar =>
    {
        if (bar.Layers.Count == 1) throw new InvalidOperationException("Keep at least one taskbar layer.");
        var index = bar.Layers.ToList().FindIndex(l => l.Id == bar.ActiveLayerId);
        var remaining = bar.Layers.Where(l => l.Id != bar.ActiveLayerId).ToArray();
        return bar with { Layers = remaining, ActiveLayerId = remaining[Math.Min(index, remaining.Length - 1)].Id };
    });
    public static ShellConfiguration ReorderLayer(ShellConfiguration config, int direction) => ChangeTaskbar(config, bar =>
    {
        var layers = bar.Layers.ToList(); var index = layers.FindIndex(l => l.Id == bar.ActiveLayerId);
        var target = Math.Clamp(index + Math.Sign(direction), 0, layers.Count - 1);
        (layers[index], layers[target]) = (layers[target], layers[index]);
        return bar with { Layers = layers.ToArray() };
    });
    public static ShellConfiguration Presentation(ShellConfiguration config, int thickness, int spacing, int padding, int cornerRadius, double opacity) =>
        ChangeTaskbar(config, bar => bar with { Layers = bar.Layers.Select(l => l.Id != bar.ActiveLayerId ? l : l with
        { Presentation = l.Presentation with { Thickness = thickness, Spacing = spacing, Padding = padding, CornerRadius = cornerRadius, Opacity = opacity } }).ToArray() });
    public static ShellConfiguration DuplicateSpace(ShellConfiguration config, string name)
    {
        var original = config.ActiveSpace;
        var mapping = original.Taskbar.Layers.ToDictionary(l => l.Id, _ => Guid.NewGuid());
        var space = original with { Id = Guid.NewGuid(), Name = name, Taskbar = original.Taskbar with { Id = Guid.NewGuid(),
            ActiveLayerId = mapping[original.Taskbar.ActiveLayerId], Layers = original.Taskbar.Layers.Select(l => l with { Id = mapping[l.Id], Items = l.Items.Select(i => i with { Id = Guid.NewGuid() }).ToArray() }).ToArray() } };
        return config with { ActiveSpaceId = space.Id, Spaces = [.. config.Spaces, space] };
    }
    public static ShellConfiguration StepSpace(ShellConfiguration config, int direction)
    {
        var index = config.Spaces.ToList().FindIndex(s => s.Id == config.ActiveSpaceId);
        return config with { ActiveSpaceId = config.Spaces[Math.Clamp(index + Math.Sign(direction), 0, config.Spaces.Count - 1)].Id };
    }
    public static ShellConfiguration RenameSpace(ShellConfiguration config, string name) => config with
    { Spaces = config.Spaces.Select(s => s.Id == config.ActiveSpaceId ? s with { Name = name } : s).ToArray() };
    public static ShellConfiguration RemoveSpace(ShellConfiguration config)
    {
        if (config.Spaces.Count == 1) throw new InvalidOperationException("Keep at least one Desktop Space.");
        var index = config.Spaces.ToList().FindIndex(s => s.Id == config.ActiveSpaceId);
        var remaining = config.Spaces.Where(s => s.Id != config.ActiveSpaceId).ToArray();
        return config with { Spaces = remaining, ActiveSpaceId = remaining[Math.Min(index, remaining.Length - 1)].Id };
    }
    public static ShellConfiguration ResetTaskbar(ShellConfiguration config) => ChangeTaskbar(config, _ => ShellConfiguration.Default().ActiveSpace.Taskbar);
    public static ShellConfiguration PinApplication(ShellConfiguration config, Guid applicationId, string label) => ChangeTaskbar(config, bar => bar with
    { Layers = bar.Layers.Select(layer => layer.Id != bar.ActiveLayerId ? layer : layer with
        { Items = layer.Items.Any(item => item.Kind == TaskbarItemKind.Application && item.Target?.Owner == "Home" && item.Target.Kind == "os.installed-application" && item.Target.Id == applicationId.ToString("D")) ? layer.Items :
            [.. layer.Items, new(Guid.NewGuid(), TaskbarItemKind.Application, label, new("Home", "os.installed-application", applicationId.ToString("D")))] }).ToArray() });
    public static ShellConfiguration UnpinItem(ShellConfiguration config, Guid itemId) => ChangeTaskbar(config, bar => bar with
    { Layers = bar.Layers.Select(layer => layer.Id != bar.ActiveLayerId ? layer : layer with { Items = layer.Items.Where(item => item.Id != itemId).ToArray() }).ToArray() });
    public static ShellConfiguration ReorderSpace(ShellConfiguration config, int direction)
    {
        var spaces = config.Spaces.ToList(); var index = spaces.FindIndex(s => s.Id == config.ActiveSpaceId);
        var target = Math.Clamp(index + Math.Sign(direction), 0, spaces.Count - 1);
        (spaces[index], spaces[target]) = (spaces[target], spaces[index]);
        return config with { Spaces = spaces.ToArray() };
    }
}
