using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
using CakeOS.HuiLinuxHost;
using Haven.UI.Components;

namespace Haven.Applications.Boards;

public sealed class BoardsRootProvider : IHuiRootProvider
{
    private readonly JsonFileHavenBoardStore _store;
    private readonly HavenBoardsHuiSession _session;

    public BoardsRootProvider()
    {
        var dataDirectory = Environment.GetEnvironmentVariable("CAKEOS_BOARDS_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "cakeos",
                "boards");
        }

        _store = new JsonFileHavenBoardStore(dataDirectory);
        _session = HavenBoardsHuiSession.OpenAsync(_store).GetAwaiter().GetResult();
        _session.Scene.CommandRequested += (_, command) =>
            Console.WriteLine($"BOARDS_HUI_COMMAND_REQUESTED type={command.GetType().Name}");
        Console.WriteLine($"BOARDS_HUI_ROOT_READY board={_session.Snapshot.Id} version={_session.Snapshot.Version}");
    }

    public Page CreateRoot() => _session.Scene.Root;
}
