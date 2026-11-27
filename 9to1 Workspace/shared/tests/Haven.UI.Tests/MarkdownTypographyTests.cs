using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class MarkdownTypographyTests
{
    [Fact]
    public void Unstyled_body_keeps_legacy_typography_after_class_application()
    {
        var markdown = new Markdown { Content = "Body" };

        HavenResourceSet.LoadEmbedded().ApplyClasses(markdown);

        AssertBodyTypography(markdown, 14d, 500);
    }

    [Theory]
    [InlineData("<Markdown Content='Body' FontSize='15' FontWeight='550' />")]
    [InlineData("<Markdown FontSize='15' FontWeight='550' Content='Body' />")]
    public void Authored_body_typography_is_independent_of_attribute_order(string markup)
    {
        var markdown = Assert.IsType<Markdown>(new HavenMarkupParser().Parse(markup));

        HavenResourceSet.LoadEmbedded().ApplyClasses(markdown);

        AssertBodyTypography(markdown, 15d, 550);
    }

    [Fact]
    public void Effective_typography_updates_retained_paragraphs_without_rebuilding()
    {
        var markdown = new Markdown { Content = "First paragraph\n\nSecond paragraph" };
        var paragraphs = markdown.Children.Cast<Text>().ToArray();
        var invalidations = new List<HavenInvalidationKinds>();
        foreach (var paragraph in paragraphs)
            paragraph.Invalidated += (_, _) => invalidations.Add(paragraph.LastInvalidationKinds);
        markdown.Invalidated += (_, _) => invalidations.Add(markdown.LastInvalidationKinds);

        markdown.SetValue(HavenProperties.FontSize, 16d, HavenValueSource.UserClass);
        markdown.SetValue(HavenProperties.FontWeight, 650, HavenValueSource.UserClass);
        AssertBodyTypography(markdown, 16d, 650);
        markdown.SetValue(HavenProperties.FontSize, 15d);
        markdown.SetValue(HavenProperties.FontWeight, 550);
        AssertBodyTypography(markdown, 15d, 550);

        // A lower-priority change must not replace an effective authored value.
        markdown.SetValue(HavenProperties.FontSize, 17d, HavenValueSource.UserClass);
        AssertBodyTypography(markdown, 15d, 550);
        markdown.ClearValue(HavenProperties.FontSize, HavenValueSource.Explicit);
        markdown.ClearValue(HavenProperties.FontWeight, HavenValueSource.Explicit);
        AssertBodyTypography(markdown, 17d, 650);
        markdown.ClearValue(HavenProperties.FontSize, HavenValueSource.UserClass);
        markdown.ClearValue(HavenProperties.FontWeight, HavenValueSource.UserClass);
        AssertBodyTypography(markdown, 14d, 500);

        Assert.Equal(paragraphs.Length, markdown.Children.Count);
        for (var index = 0; index < paragraphs.Length; index++)
            Assert.Same(paragraphs[index], markdown.Children[index]);
        Assert.NotEmpty(invalidations);
        Assert.All(invalidations, kinds =>
            Assert.Equal(HavenInvalidationKinds.Layout | HavenInvalidationKinds.Paint, kinds));
    }

    [Fact]
    public void Rebuilt_content_uses_current_typography_and_retires_old_paragraphs()
    {
        var markdown = new Markdown { Content = "Old body" };
        markdown.SetValue(HavenProperties.FontSize, 15d);
        markdown.SetValue(HavenProperties.FontWeight, 550);
        var oldParagraph = Assert.IsType<Text>(Assert.Single(markdown.Children));

        markdown.Content = "New body\n\nAnother paragraph";
        AssertBodyTypography(markdown, 15d, 550);
        markdown.SetValue(HavenProperties.FontSize, 17d);
        markdown.SetValue(HavenProperties.FontWeight, 650);

        AssertBodyTypography(markdown, 17d, 650);
        Assert.Null(oldParagraph.Parent);
        Assert.Equal(15d, oldParagraph.GetValue(HavenProperties.FontSize));
        Assert.Equal(550, oldParagraph.GetValue(HavenProperties.FontWeight));
    }

    [Fact]
    public void Body_typography_preserves_other_blocks_and_retained_code_actions()
    {
        var markdown = new Markdown
        {
            Content = "Body\n\n# Heading\n\n> Quote\n\n- List item\n\n" +
                "| Column |\n| --- |\n| Cell |\n\n```csharp\nConsole.WriteLine(1);\n```"
        };
        var blocks = markdown.Children.ToArray();
        var otherText = markdown.DescendantsAndSelf().OfType<Text>()
            .Where(text => text.Content != "Body")
            .Select(text => (Element: text, Size: text.GetValue(HavenProperties.FontSize),
                Weight: text.GetValue(HavenProperties.FontWeight), Family: text.GetValue(HavenProperties.FontFamily)))
            .ToArray();
        var copy = Assert.Single(markdown.DescendantsAndSelf().OfType<Button>(), button => button.Content == "Copy");
        var actions = new List<MarkdownCodeActionRequest>();
        markdown.CodeActionRequested += (_, request) => actions.Add(request);

        markdown.SetValue(HavenProperties.FontSize, 15d);
        markdown.SetValue(HavenProperties.FontWeight, 550);
        HavenResourceSet.LoadEmbedded().ApplyClasses(markdown);

        var body = Assert.Single(markdown.Children.OfType<Text>(), text => text.Content == "Body");
        Assert.Equal(15d, body.GetValue(HavenProperties.FontSize));
        Assert.Equal(550, body.GetValue(HavenProperties.FontWeight));
        Assert.Equal(blocks.Length, markdown.Children.Count);
        for (var index = 0; index < blocks.Length; index++)
            Assert.Same(blocks[index], markdown.Children[index]);
        Assert.All(otherText, original =>
        {
            Assert.Contains(original.Element, markdown.DescendantsAndSelf());
            Assert.Equal(original.Size, original.Element.GetValue(HavenProperties.FontSize));
            Assert.Equal(original.Weight, original.Element.GetValue(HavenProperties.FontWeight));
            Assert.Equal(original.Family, original.Element.GetValue(HavenProperties.FontFamily));
        });
        Assert.Same(copy, Assert.Single(markdown.DescendantsAndSelf().OfType<Button>(), button => button.Content == "Copy"));
        var keyboard = Assert.IsAssignableFrom<IHavenKeyboardInputTarget>(copy);
        Assert.True(keyboard.KeyDown(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.True(keyboard.KeyUp(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.Equal(new MarkdownCodeActionRequest(MarkdownCodeAction.Copy, "csharp", "Console.WriteLine(1);"),
            Assert.Single(actions));
    }

    private static void AssertBodyTypography(Markdown markdown, double size, int weight)
    {
        var paragraphs = markdown.Children.OfType<Text>().ToArray();
        Assert.NotEmpty(paragraphs);
        Assert.All(paragraphs, paragraph =>
        {
            Assert.Equal(size, paragraph.GetValue(HavenProperties.FontSize));
            Assert.Equal(weight, paragraph.GetValue(HavenProperties.FontWeight));
        });
    }
}
