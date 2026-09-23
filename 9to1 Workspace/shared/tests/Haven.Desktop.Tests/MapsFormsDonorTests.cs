using System.Text.Json;
using Haven.Core;
using Haven.Desktop.Views.Pages.Forms;
using Haven.Desktop.Views.Pages.Maps;

namespace Haven.Desktop.Tests;

public sealed class MapsFormsDonorTests
{
    [Fact]
    public void MapsStructuredResponseBecomesAPlaceWithoutLosingProviderCoordinates()
    {
        var place = new MapPlace("osm-1", "Original result", "City centre", new GeoPoint(51.5, -0.12), "city");
        var values = JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["structured-form.input.displayName"] = "  My London place  ",
            ["structured-form.input.note"] = "  Visit in spring  "
        });
        var savedAt = DateTimeOffset.UtcNow;

        var succeeded = MapsPage.TryCreateSavedPlaceFromForm(place, values, savedAt, out var saved, out var error);

        Assert.True(succeeded, error);
        Assert.NotNull(saved);
        Assert.NotEqual(place.Id, saved.Id);
        Assert.Equal("My London place", saved.DisplayName);
        Assert.Equal("Visit in spring", saved.Note);
        Assert.Equal(place.Location, saved.Location);
        Assert.Equal(savedAt, saved.SavedAt);
    }

    [Fact]
    public void MapsFormRequiresAnEditableSavedName()
    {
        var place = new MapPlace("osm-1", "Original result", null, new GeoPoint(1, 2), null);
        var values = JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["structured-form.input.displayName"] = "   "
        });

        var succeeded = MapsPage.TryCreateSavedPlaceFromForm(place, values, DateTimeOffset.UtcNow, out var saved, out var error);

        Assert.False(succeeded);
        Assert.Null(saved);
        Assert.Contains("name", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormsSubmissionRequiresANameOrAnonymousChoiceAndMessage()
    {
        var missingMessage = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["structured-form.input.name"] = "Ada",
            ["structured-form.input.topic"] = "Idea",
            ["structured-form.input.message"] = " ",
            ["structured-form.input.anonymous"] = false
        });
        Assert.False(FormsPage.TryCreateFeedbackSubmission(missingMessage, DateTimeOffset.UtcNow, out var rejected, out _));
        Assert.Null(rejected);

        var validAnonymous = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["structured-form.input.name"] = "",
            ["structured-form.input.topic"] = "Issue",
            ["structured-form.input.message"] = "Saved through the feedback form",
            ["structured-form.input.anonymous"] = true
        });
        var succeeded = FormsPage.TryCreateFeedbackSubmission(validAnonymous, DateTimeOffset.UtcNow, out var saved, out var error);

        Assert.True(succeeded, error);
        Assert.NotNull(saved);
        Assert.Equal("feedback", saved.FormId);
        Assert.Equal("true", saved.Values["anonymous"]);
        Assert.Equal("Issue", saved.Values["topic"]);
    }
}
