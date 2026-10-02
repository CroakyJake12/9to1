using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.Views.Pages.Mail;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class MailReadingPaneChoiceTests
{
    [AvaloniaFact]
    public void Actual_native_picker_remembers_bottom_across_narrow_single_pane_and_restores_right_and_off_geometry()
    {
        var mail = DispatchProxy.Create<IMailService, NoConnectedAccount>();
        var models = DispatchProxy.Create<IProviderModelClient, UnusedModel>();
        using var page = new MailPage(mail, models);
        var picker = page.FindControl<ComboBox>("ReadingPlacementPicker")!;
        var grid = page.FindControl<Grid>("MailboxGrid")!;
        var reading = page.FindControl<Border>("ReadingPanel")!;
        var messages = page.FindControl<Border>("MessagePanel")!;
        void Resize(double width)
        {
            page.Measure(new Size(width, 800)); page.Arrange(new Rect(0, 0, width, 800));
            page.ApplyResponsiveLayout(width);
        }
        Resize(1280); picker.SelectedIndex = 1;
        Assert.Equal(MailReadingPanePlacement.Bottom, page.ReadingPanePlacement);
        Assert.Equal(1, Grid.GetRow(reading)); Assert.Equal(2, Grid.GetColumnSpan(reading));
        Assert.True(grid.RowDefinitions[1].Height.IsStar); Assert.True(reading.IsVisible);
        Resize(500);
        Assert.Equal("narrow", page.ResponsiveMode); Assert.Equal(0, Grid.GetRow(reading));
        Assert.Equal(new GridLength(0), grid.RowDefinitions[1].Height);
        Assert.False(reading.IsVisible); Assert.True(messages.IsVisible); Assert.Equal(1, picker.SelectedIndex);
        Resize(1280); Assert.Equal(1, Grid.GetRow(reading)); Assert.True(reading.IsVisible);
        picker.SelectedIndex = 2;
        Assert.Equal(MailReadingPanePlacement.Off, page.ReadingPanePlacement);
        Assert.False(reading.IsVisible); Assert.True(messages.IsVisible); Assert.Equal(2, Grid.GetColumnSpan(messages));
        picker.SelectedIndex = 0;
        Assert.Equal(MailReadingPanePlacement.Right, page.ReadingPanePlacement);
        Assert.Equal(2, Grid.GetColumn(reading)); Assert.Equal(1, Grid.GetColumnSpan(messages));
        Assert.True(reading.IsVisible); Assert.Equal(new GridLength(0), grid.RowDefinitions[1].Height);
    }
    public class NoConnectedAccount : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IMailService.GetAccountsAsync)
            ? Task.FromResult<IReadOnlyList<MailAccount>>([])
            : throw new InvalidOperationException("Local layout fixture has no connected mailbox or provider operation.");
    }
    public class UnusedModel : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new InvalidOperationException("Manual reading layout does not require an AI model.");
    }
}
