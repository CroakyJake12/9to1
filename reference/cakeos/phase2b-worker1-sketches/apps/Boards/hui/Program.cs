using CakeOS.Apps.Boards.Hui;

await BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<BoardsApp>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();