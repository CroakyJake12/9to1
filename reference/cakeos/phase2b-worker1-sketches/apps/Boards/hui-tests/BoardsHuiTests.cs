using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
using CakeOS.Platform;
using Moq;
using Xunit;

namespace CakeOS.Apps.Boards.Hui.Tests;

public class FileSystemBoardStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSnapshot()
    {
        var mockStore = new Mock<IVersionedSettingsStore>();
        var savedJson = "";
        
        mockStore.Setup(s => s.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((k, v, _) => savedJson = v)
            .Returns(Task.CompletedTask);
        
        mockStore.Setup(s => s.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedJson);

        var store = new FileSystemBoardStore(mockStore.Object);
        var original = HavenBoardSnapshot.CreateDefault();
        
        await store.SaveAsync(original, CancellationToken.None);
        var loaded = await store.LoadAsync("board-main", CancellationToken.None);
        
        Assert.NotNull(loaded);
        Assert.Equal(original.Id, loaded!.Id);
        Assert.Equal(original.Title, loaded.Title);
        Assert.Equal(original.Version, loaded.Version);
        Assert.Equal(original.Groups.Count, loaded.Groups.Count);
    }

    [Fact]
    public async Task Load_NonExistent_ReturnsNull()
    {
        var mockStore = new Mock<IVersionedSettingsStore>();
        mockStore.Setup(s => s.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var store = new FileSystemBoardStore(mockStore.Object);
        var loaded = await store.LoadAsync("non-existent", CancellationToken.None);
        
        Assert.Null(loaded);
    }
}

public class BoardsViewModelTests
{
    [Fact]
    public async Task LoadAsync_InitializesGroups()
    {
        var mockSink = new Mock<IHavenBoardCommandSink>();
        mockSink.Setup(s => s.ExecuteAsync(It.IsAny<HavenBoardCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HavenBoardSnapshot.CreateDefault());

        var vm = new BoardsViewModel(mockSink.Object);
        await vm.LoadAsync("board-main", CancellationToken.None);
        
        Assert.Equal(3, vm.Groups.Count);
        Assert.Contains(vm.Groups, g => g.Id == "todo");
        Assert.Contains(vm.Groups, g => g.Id == "doing");
        Assert.Contains(vm.Groups, g => g.Id == "done");
    }
}