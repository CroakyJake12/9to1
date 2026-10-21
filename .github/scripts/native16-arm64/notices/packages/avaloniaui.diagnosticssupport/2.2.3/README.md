# Avalonia Diagnostics Support

The Diagnostics Support package is responsible for establishing a connection bridge between the user app and AvaloniaUI Developer Tools process.

This package can be installed either in the executable project with your Program AppBuilder or shared project with your Application, depending on your application's architecture.

## Getting Started

For a complete guide on setting up and using Developer Tools with this package, see the [Developer Tools Getting Started Guide](https://docs.avaloniaui.net/tools/developer-tools/installation).

## Prerequisites

Support package requires **Avalonia 11.2.0** or newer, and built on **.NET Standard 2.0** compatible APIs.

This package is compatible with Browser and Android/iOS projects.

## Installation

Install the Diagnostics Support package in your project:

```bash
dotnet add package AvaloniaUI.DiagnosticsSupport
```

:::note

Old package `Avalonia.Diagnostics` can be safely removed. It's not used by new `Developer Tools`.

:::

## Configuration

Once the `DiagnosticsSupport` package is installed, you need to enable it in your `Application` class:

```csharp
public override void Initialize()
{
    AvaloniaXamlLoader.Load(this);

#if DEBUG
    this.AttachDeveloperTools();
#endif
}
```

These methods also accept `DeveloperToolsOptions` options class allowing to customize `Diagnostics Support` setup.

## Browser and Mobile Support

For special configuration requirements when working with browser or mobile platforms, see [Attaching to Browser or Mobile Applications](https://docs.avaloniaui.net/tools/developer-tools/attaching-applications).

## Usage

When your target app is running with the Diagnostics Support package configured, press F12 to initialize connection. The package will automatically establish a bridge with the AvaloniaUI Developer Tools if it's available on your system.

## Troubleshooting

If you encounter issues with the connection or setup, check the [Developer Tools FAQ](https://docs.avaloniaui.net/tools/faq) for common solutions.
