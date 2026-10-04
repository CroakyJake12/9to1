using CakeOS.Cui;
using CakeOS.Cui.Language;
using Xunit;

namespace NineToOne.Cui.Markup.Tests;

public sealed class CuiMarkupTests
{
    [Fact]
    public void ParserBuildsNativeCuiDocument()
    {
        var document = new CuiRichParser().Parse("<Cui><Stack gap=\"8\"><Text>Hello</Text></Stack></Cui>", "dashboard.cui");

        var stack = Assert.Single(document.Components);
        Assert.Equal("Stack", stack.Type);
        Assert.True(stack.TryGetLiteralAttribute("gap", out var gap));
        Assert.Equal("8", gap);
        Assert.Equal("Hello", Assert.Single(stack.Children).Text);
    }

    [Fact]
    public void ParserAcceptsCaseInsensitiveCoreKeywordsAndPageRoot()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(
            "<page><container Type=\"Grid\"><text>hello</text></container></page>",
            "page.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        var page = Assert.Single(document.Components);
        Assert.Equal("Page", page.Type);
        Assert.Equal("Container", Assert.Single(page.Children).Type);
        Assert.Equal("Text", Assert.Single(Assert.Single(page.Children).Children).Type);
    }

    [Fact]
    public void ParserKeepsGroupsCaseSensitiveAndAllowsMultipleGroupMembers()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(
            "<Cui><Page><Text ID=\"first\" Group=\"Labels, Important\"/><Text Name=\"second\" Group=\"Labels\"/></Page><Page><Text ID=\"first\"/></Page></Cui>",
            "addresses.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        var firstPage = document.Components[0];
        Assert.Equal(new[] { "Labels", "Important" }, firstPage.Children[0].Groups);
        Assert.Equal(new[] { "Labels" }, firstPage.Children[1].Groups);
        Assert.Equal("first", firstPage.Children[0].Name);
        Assert.Equal("second", firstPage.Children[1].Name);
    }

    [Theory]
    [InlineData("<Cui><Text ID=\"target\"/><Text ID=\"target\"/></Cui>", "CUI027")]
    [InlineData("<Cui><Text ID=\"target\"/><Text Group=\"target\"/></Cui>", "CUI028")]
    [InlineData("<Cui><Text ID=\"first\" Name=\"second\"/></Cui>", "CUI026")]
    public void ParserRejectsAmbiguousAddressDeclarations(string source, string diagnosticCode)
    {
        var parser = new CuiRichParser();

        _ = parser.Parse(source, "invalid-address.cui");

        Assert.Contains(parser.Diagnostics.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
    }

    [Fact]
    public void ParserRetainsDefinitionMetadataWithoutRenderingTheDeclarationAsAnotherType()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(
            "<Cui><Paragraph Definition=\"true\"><Text>Reusable</Text></Paragraph></Cui>",
            "definition.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        var definition = Assert.Single(document.Components);
        Assert.Equal("Paragraph", definition.Type);
        Assert.True(definition.IsDefinition);
        Assert.Equal("Reusable", Assert.Single(definition.Children).Text);
    }

    [Theory]
    [InlineData("<:Name:>", "Name", null, CuiBindingMode.OneWay)]
    [InlineData("<:Name:live>", "Name", null, CuiBindingMode.OneWay)]
    [InlineData("<:Name:static>", "Name", null, CuiBindingMode.OneTime)]
    [InlineData("<int:Age:>", "Age", "int", CuiBindingMode.OneWay)]
    [InlineData("<int:Age:static>", "Age", "int", CuiBindingMode.OneTime)]
    public void ValueParserSupportsLiveStaticAndTypedVariableReferences(
        string source,
        string expectedPath,
        string? expectedTargetType,
        CuiBindingMode expectedMode)
    {
        var value = CuiRichParser.ParseValue(source, CuiSourceSpan.At("reference.cui", 1, 1));

        var reference = Assert.IsType<CuiBindingValue>(value);
        Assert.Equal(expectedPath, reference.Path);
        Assert.Equal(expectedTargetType, reference.TargetType);
        Assert.Equal(expectedMode, reference.Mode);
    }

    [Fact]
    public void ValueParserSupportsLongTypedBindingForm()
    {
        var value = CuiRichParser.ParseValue(
            "{Binding Age, mode=TwoWay, type=int}",
            CuiSourceSpan.At("binding.cui", 1, 1));

        var binding = Assert.IsType<CuiBindingValue>(value);
        Assert.Equal("Age", binding.Path);
        Assert.Equal(CuiBindingMode.TwoWay, binding.Mode);
        Assert.Equal("int", binding.TargetType);
    }

    [Fact]
    public void ParserRepresentsLongFormPropertyRegionAsNonVisualSemanticScope()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(
            "<Cui><Property Property=\"Color\" Color=\"Green\" ID=\"greenText\" Group=\"labels\"><Text>Hello</Text></Property></Cui>",
            "property-region.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        var property = Assert.Single(document.Components);
        Assert.Equal("Property", property.Type);
        Assert.Equal("Color", property.PropertyRegion!.PropertyName);
        Assert.Equal("Green", Assert.IsType<CuiLiteralValue>(property.PropertyRegion.Value).Value);
        Assert.Equal("greenText", property.AuthoredId);
        Assert.Equal(new[] { "labels" }, property.Groups);
        Assert.Equal("Hello", Assert.Single(property.Children).Text);
    }

    [Fact]
    public void ParserRejectsIncompleteLongFormPropertyRegion()
    {
        var parser = new CuiRichParser();
        _ = parser.Parse("<Cui><Property Property=\"Color\"/></Cui>", "invalid-property-region.cui");

        Assert.Contains(parser.Diagnostics.Diagnostics, diagnostic => diagnostic.Code == "CUI039");
    }

    [Theory]
    [InlineData("{Binding Age, mode=TwowayTypo}", "CUI036")]
    [InlineData("{Binding Age, fallback=first, fallback=second}", "CUI035")]
    public void ParserDiagnosesInvalidBindingAndVariableReferenceOptions(string value, string diagnosticCode)
    {
        var parser = new CuiRichParser();
        _ = parser.Parse($"<Cui><Text Value=\"{value}\"/></Cui>", "invalid-binding.cui");

        Assert.Contains(parser.Diagnostics.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
    }

    [Fact]
    public void StableGeneratedIdsIgnoreSourceLocationsAndSiblingOrder()
    {
        var first = new CuiRichParser().Parse(
            "<Cui><Page><Text>Alpha</Text><Button Content=\"Save\"/></Page></Cui>",
            "first.cui");
        var second = new CuiRichParser().Parse(
            "<Cui>\n  <Page>\n    <Button Content=\"Save\"/>\n    <Text>Alpha</Text>\n  </Page>\n</Cui>",
            "second.cui");

        var firstPage = Assert.Single(first.Components);
        var secondPage = Assert.Single(second.Components);
        var firstText = firstPage.Children.Single(component => component.Type == "Text");
        var secondText = secondPage.Children.Single(component => component.Type == "Text");
        var firstButton = firstPage.Children.Single(component => component.Type == "Button");
        var secondButton = secondPage.Children.Single(component => component.Type == "Button");

        Assert.Equal(firstText.StableId, secondText.StableId);
        Assert.Equal(firstButton.StableId, secondButton.StableId);
        Assert.StartsWith("gen:v1:", firstText.StableId, StringComparison.Ordinal);
    }

    [Fact]
    public void StableIdUsesAuthoredIdAndDuplicateGeneratedIdsCarryBothLocations()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(
            "<Cui><Page><Text ID=\"CaseSensitive\">First</Text><Text>Same</Text>\n<Text>Same</Text></Page></Cui>",
            "identity.cui");

        var page = Assert.Single(document.Components);
        var authored = page.Children[0];
        Assert.Equal("CaseSensitive", authored.AuthoredId);
        Assert.Equal("id:CaseSensitive", authored.StableId);

        var duplicate = Assert.Single(parser.Diagnostics.Diagnostics, diagnostic => diagnostic.Code == "CUI029");
        Assert.NotNull(duplicate.RelatedSpan);
        Assert.True(duplicate.Span.Start.Line > duplicate.RelatedSpan!.Value.Start.Line);
    }

    [Fact]
    public void ValueParserRejectsUnknownCompactVariableReferenceMode()
    {
        var value = CuiRichParser.ParseValue(
            "<:Name:polling>",
            CuiSourceSpan.At("invalid-reference.cui", 1, 1));

        Assert.Equal("CUI031", Assert.IsType<CuiInvalidValue>(value).DiagnosticCode);
    }

    [Fact]
    public void StaticConditionalBindingIsMarkedSnapshotOnly()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(
            "<Cui><If Condition=\"{Binding LoggedIn, mode=OneTime}\"><Text>Yes</Text></If></Cui>",
            "static-condition.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        var conditional = Assert.Single(document.Components);
        Assert.False(conditional.Condition!.IsLive);
    }

    [Fact]
    public void ParserAssociatesIfAndElseBranchesAndTracksLiveCondition()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(
            "<Cui><If Condition=\"{Binding LoggedIn}\"><Text>Welcome</Text></If><Else><Text>Sign in</Text></Else></Cui>",
            "conditional.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        var conditional = Assert.Single(document.Components);
        Assert.Equal("If", conditional.Type);
        Assert.True(conditional.Condition!.IsLive);
        Assert.Equal("LoggedIn", Assert.IsType<CuiBindingValue>(conditional.Condition.Test).Path);
        Assert.Equal("Welcome", Assert.Single(conditional.Children).Text);
        Assert.Equal("Sign in", Assert.Single(conditional.ElseChildren).Text);
        Assert.Equal(3, conditional.DescendantsAndSelf().Count());
    }

    [Fact]
    public void ParserRetainsRepeatSourceItemNameAndStableKey()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(
            "<Cui><Repeat Source=\"{Binding Users}\" As=\"user\" Key=\"{Binding user.ID}\"><Text>{Binding user.Name}</Text></Repeat></Cui>",
            "repeat.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        var repeat = Assert.Single(document.Components);
        Assert.Equal("Repeat", repeat.Type);
        Assert.Equal("Users", Assert.IsType<CuiBindingValue>(repeat.Repeat!.Source).Path);
        Assert.Equal("user", repeat.Repeat.ItemName);
        Assert.Equal("user.ID", Assert.IsType<CuiBindingValue>(repeat.Repeat.Key).Path);
        Assert.Equal("{Binding user.Name}", Assert.Single(repeat.Children).Text);
    }

    [Fact]
    public void ParserNormalizesCompactBindingsConditionsRepeatsAndTextReferences()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(
            "<Page><Container Type:Vertical_Stack ColumnSizes:1fr_2fr_200><If Condition=<:LoggedIn:>><Text>Hello <:Name:></Text></If><Else><Text>Sign in</Text></Else><Repeat Source=<:Users:> As:user Key=<:user.ID:>><Text><:user.Name:></Text></Repeat></Container></Page>",
            "compact.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        var page = Assert.Single(document.Components);
        var container = Assert.Single(page.Children);
        Assert.True(container.TryGetLiteralAttribute("Type", out var type));
        Assert.Equal("Vertical Stack", type);
        Assert.True(container.TryGetLiteralAttribute("ColumnSizes", out var columns));
        Assert.Equal("1fr 2fr 200", columns);

        var conditional = container.Children[0];
        Assert.Equal("LoggedIn", Assert.IsType<CuiBindingValue>(conditional.Condition!.Test).Path);
        Assert.Equal("Sign in", Assert.Single(conditional.ElseChildren).Text);
        var greeting = Assert.Single(conditional.Children);
        Assert.Equal("Hello <:Name:>", greeting.Text);
        Assert.Collection(greeting.TextParts,
            part => Assert.Equal("Hello ", Assert.IsType<CuiLiteralTextPart>(part).Value),
            part => Assert.Equal("Name", Assert.IsType<CuiBindingValue>(Assert.IsType<CuiExpressionTextPart>(part).Value).Path));

        var repeat = container.Children[1];
        Assert.Equal("Users", Assert.IsType<CuiBindingValue>(repeat.Repeat!.Source).Path);
        Assert.Equal("user", repeat.Repeat.ItemName);
        Assert.Equal("user.ID", Assert.IsType<CuiBindingValue>(repeat.Repeat.Key).Path);
        Assert.Equal("user.Name", Assert.IsType<CuiBindingValue>(Assert.IsType<CuiExpressionTextPart>(Assert.Single(repeat.Children[0].TextParts)).Value).Path);
    }

    [Fact]
    public void ParserNormalizesCompactIdentityPropertyEventKeyframeAndComments()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(
            "<# ignored <Text> markup #><Page><!Text::submit:::actions>Save</Text><$Color:Light_Green><Text>Green</Text></$><Property=Opacity Opacity:0.5/><&Submit{::submit}/><@Fade Duration:250/><Text (Definition)>Shared</Text></Page>",
            "compact-declarations.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        var page = Assert.Single(document.Components);
        var button = page.Children[0];
        Assert.Equal("Text", button.Type);
        Assert.Equal("submit", button.AuthoredId);
        Assert.Equal(new[] { "actions" }, button.Groups);
        Assert.True(button.TryGetLiteralAttribute("ActiveIndependently", out var independent));
        Assert.Equal("True", independent);

        var property = page.Children[1];
        Assert.Equal("Color", property.PropertyRegion!.PropertyName);
        Assert.Equal("Light Green", Assert.IsType<CuiLiteralValue>(property.PropertyRegion.Value).Value);
        Assert.Equal("Green", Assert.Single(property.Children).Text);
        var opacityProperty = page.Children[2].PropertyRegion!;
        Assert.Equal("Opacity", opacityProperty.PropertyName);
        Assert.Equal("0.5", Assert.IsType<CuiLiteralValue>(opacityProperty.Value).Value);
        Assert.Equal("Submit", page.Children[3].TryGetLiteralAttribute("Type", out var eventName) ? eventName : null);
        Assert.Equal("submit", page.Children[3].TryGetLiteralAttribute("TargetID", out var targetId) ? targetId : null);
        Assert.True(page.Children[4].TryGetLiteralAttribute("Name", out var keyframeName));
        Assert.Equal("Fade", keyframeName);
        Assert.True(page.Children[5].IsDefinition);
    }

    [Theory]
    [InlineData("<Cui><Repeat Source=\"{Binding Users}\" As=\"user\"><Text>Item</Text></Repeat></Cui>", "CUI025")]
    [InlineData("<Cui><Else><Text>Orphan</Text></Else></Cui>", "CUI021")]
    [InlineData("<Cui><If><Text>Missing condition</Text></If></Cui>", "CUI022")]
    public void ParserReportsInvalidStructuralConditionalAndRepeatSyntax(string source, string diagnosticCode)
    {
        var parser = new CuiRichParser();

        _ = parser.Parse(source, "invalid-structure.cui");

        Assert.Contains(parser.Diagnostics.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
    }

    [Fact]
    public void LoaderLoadsCuiFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cui");
        try
        {
            File.WriteAllText(path, "<Cui><Text>Loaded</Text></Cui>");

            var document = new CuiRichParser().ParseFile(path);

            Assert.Equal(Path.GetFullPath(path), document.SourceName);
            Assert.Equal("Loaded", Assert.Single(document.Components).Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("legacy.axaml")]
    [InlineData("legacy.hui")]
    public void ParserAndLoaderRejectLegacyMarkupInputs(string sourceName)
    {
        var parserException = Assert.Throws<NotSupportedException>(
            () => new CuiRichParser().ParseFile(sourceName));
        var loaderException = Assert.Throws<NotSupportedException>(
            () => new CuiRichParser().ParseFile(Path.Combine(Path.GetTempPath(), sourceName)));

        Assert.Contains(".cui", parserException.Message, StringComparison.Ordinal);
        Assert.Contains(".cui", loaderException.Message, StringComparison.Ordinal);
    }
}
