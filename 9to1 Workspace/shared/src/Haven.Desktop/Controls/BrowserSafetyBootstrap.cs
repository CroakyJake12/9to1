/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/BrowserSafetyBootstrap.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns BrowserSafetyBootstrap, AttachmentMarker. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents browser safety bootstrap and keeps its related state and behavior together.
/// </summary>
internal static class BrowserSafetyBootstrap
{
    /// <summary>
    /// Stores attached locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly ConditionalWeakTable<BrowserView, AttachmentMarker> Attached = new();
    /// <summary>
    /// Attach the existing safety surface when its BrowserView enters the visual tree.
    /// Native CUI routes never use BrowserView, so they must not pay for a full-window
    /// descendant scan on every layout pass. The weak marker keeps reattachment idempotent.
    /// </summary>
    internal static void Attach(BrowserView browserView)
    {
        if (Attached.TryGetValue(browserView, out _)) return;
        if (browserView.Content is not Grid grid) return;

        var button = new HavenButton
        {
            Content = "Browser safety",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 470, 0),
            Padding = new Thickness(9, 4),
            FontSize = 10,
            Flyout = new HavenAdaptivePopup { Content = new BrowserSafetyView() },
            ZIndex = 20
        };
        ToolTip.SetTip(button, "Review model-requested form submissions and downloads");
        button.Classes.Add("ghost");
        Grid.SetRow(button, 3);
        grid.Children.Add(button);
        Attached.Add(browserView, new AttachmentMarker());
    }

    /// <summary>
    /// Represents attachment marker and keeps its related state and behavior together.
    /// </summary>
    private sealed class AttachmentMarker
    {
    }
}
