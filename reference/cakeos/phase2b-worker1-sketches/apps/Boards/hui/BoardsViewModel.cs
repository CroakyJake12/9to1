using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Platform;

namespace CakeOS.Apps.Boards.Hui;

public sealed class BoardsViewModel : INotifyPropertyChanged
{
    private readonly IHavenBoardCommandSink _commandSink;
    private HavenBoardSnapshot? _currentSnapshot;

    public BoardsViewModel(IHavenBoardCommandSink commandSink)
    {
        _commandSink = commandSink;
        Groups = new ObservableCollection<BoardGroupViewModel>();
    }

    public ObservableCollection<BoardGroupViewModel> Groups { get; }

    public HavenBoardSnapshot? CurrentSnapshot => _currentSnapshot;

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(string boardId = "board-main", CancellationToken ct = default)
    {
        try
        {
            var result = await _commandSink.ExecuteAsync(new CreateCardCommand("todo", "init", "Initializing..."), ct).ConfigureAwait(false);
            _currentSnapshot = result;
            UpdateGroups(result);
        }
        catch
        {
            _currentSnapshot = HavenBoardSnapshot.CreateDefault();
            UpdateGroups(_currentSnapshot);
        }
    }

    public async Task AddGroupAsync(string title, CancellationToken ct = default)
    {
        if (_currentSnapshot is null) return;

        var newGroupId = $"group-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var command = new CreateCardCommand(newGroupId, $"card-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", title);
        // Note: The contract uses CreateCardCommand for adding cards, groups are implicit
        // For a full implementation, we'd need a dedicated AddGroupCommand

        var result = await _commandSink.ExecuteAsync(command, ct).ConfigureAwait(false);
        _currentSnapshot = result;
        UpdateGroups(result);
    }

    public async Task AddCardAsync(string groupId, string title, CancellationToken ct = default)
    {
        if (_currentSnapshot is null) return;

        var command = new CreateCardCommand(groupId, $"card-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", title);
        var result = await _commandSink.ExecuteAsync(command, ct).ConfigureAwait(false);
        _currentSnapshot = result;
        UpdateGroups(result);
    }

    public async Task MoveCardAsync(string fromGroupId, int fromIndex, string toGroupId, int toIndex, CancellationToken ct = default)
    {
        if (_currentSnapshot is null) return;

        var command = new MoveCardCommand(fromGroupId, fromIndex, toGroupId, toIndex);
        var result = await _commandSink.ExecuteAsync(command, ct).ConfigureAwait(false);
        _currentSnapshot = result;
        UpdateGroups(result);
    }

    public async Task MoveGroupAsync(int fromIndex, int toIndex, CancellationToken ct = default)
    {
        if (_currentSnapshot is null) return;

        var command = new MoveGroupCommand(fromIndex, toIndex);
        var result = await _commandSink.ExecuteAsync(command, ct).ConfigureAwait(false);
        _currentSnapshot = result;
        UpdateGroups(result);
    }

    private void UpdateGroups(HavenBoardSnapshot snapshot)
    {
        Groups.Clear();
        foreach (var group in snapshot.Groups)
        {
            var vm = new BoardGroupViewModel(group);
            Groups.Add(vm);
        }
        OnPropertyChanged(nameof(Groups));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class BoardGroupViewModel : INotifyPropertyChanged
{
    private readonly HavenBoardGroup _group;

    public BoardGroupViewModel(HavenBoardGroup group)
    {
        _group = group;
        Cards = new ObservableCollection<BoardCardViewModel>(
            group.Cards.Select(c => new BoardCardViewModel(c)));
    }

    public string Id => _group.Id;
    public string Title => _group.Title;
    public ObservableCollection<BoardCardViewModel> Cards { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class BoardCardViewModel : INotifyPropertyChanged
{
    private readonly HavenBoardCard _card;

    public BoardCardViewModel(HavenBoardCard card)
    {
        _card = card;
    }

    public string Id => _card.Id;
    public string Title => _card.Title;
    public string? ParentCardId => _card.ParentCardId;
    public IReadOnlyList<HavenBoardAttachment>? Attachments => _card.Attachments;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}