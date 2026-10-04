using CakeOS.Images.Frontend;

await BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<ImagesApp>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();