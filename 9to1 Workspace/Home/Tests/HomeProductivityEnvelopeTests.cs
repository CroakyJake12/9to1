using System.Text.Json;
using System.Text.Json.Nodes;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeProductivityEnvelopeTests
{
    [Fact]
    public void Invalid_known_object_content_does_not_claim_paste_support_or_mint_replacement_ids()
    {
        var engine = new HomeProductivityEngine(); var (bundle, context) = Create(engine);
        var broken = bundle.Objects[0] with { Content = JsonSerializer.SerializeToElement(new { text = (string?)null }) };
        var invalid = bundle with { Objects = [broken] };
        Assert.False(engine.CanPaste(invalid, context, out var code)); Assert.Equal("ObjectContentInvalid", code);
        Assert.Throws<InvalidDataException>(() => engine.Paste(invalid, context));
        Assert.Equal(bundle.Objects[0].ObjectId, invalid.Objects[0].ObjectId);
        Assert.Equal(JsonValueKind.Null, invalid.Objects[0].Content.GetProperty("text").ValueKind);
    }

    [Fact]
    public void Unknown_bundle_metadata_round_trips_but_paste_cannot_silently_discard_it()
    {
        var engine = new HomeProductivityEngine(); var (bundle, context) = Create(engine);
        var raw = JsonNode.Parse(engine.SerializeBundle(bundle))!.AsObject();
        raw["future-owner-layout"] = JsonNode.Parse("{\"revision\":17,\"opaque\":[1,2,3]}");
        var parsed = engine.ParseBundle(raw.ToJsonString());
        var roundTrip = JsonNode.Parse(engine.SerializeBundle(parsed))!;
        Assert.True(JsonNode.DeepEquals(raw["future-owner-layout"], roundTrip["future-owner-layout"]));
        Assert.False(engine.CanPaste(parsed, context, out var code)); Assert.Equal("BundleExtensionsUnsupported", code);
        Assert.Throws<InvalidDataException>(() => engine.Paste(parsed, context));
        Assert.Equal(bundle.Objects[0].ObjectId, parsed.Objects[0].ObjectId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_style_or_source_metadata_is_explicitly_rejected_instead_of_lost(bool sourceMetadata)
    {
        var engine = new HomeProductivityEngine(); var (bundle, _) = Create(engine);
        var style = new HomeProductivityStyle("normal", 1, "Normal", "shared", JsonSerializer.SerializeToElement(new { color = "red" }))
        { Source = new("write", "document", Guid.NewGuid(), Guid.NewGuid(), "opaque-owner-revision") };
        var raw = JsonNode.Parse(engine.SerializeBundle(bundle with { Styles = [style] }))!.AsObject();
        var target = sourceMetadata ? raw["styles"]![0]!["source"]! : raw["styles"]![0]!;
        target["future-style-property"] = "must-not-disappear";
        Assert.Throws<InvalidDataException>(() => engine.ParseBundle(raw.ToJsonString()));
        Assert.Equal("must-not-disappear", target["future-style-property"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("null-list")]
    [InlineData("null-object")]
    [InlineData("duplicate-id")]
    [InlineData("null-assets")]
    public void Malformed_canonical_collections_fail_explicitly_without_creating_new_ids(string variant)
    {
        var engine = new HomeProductivityEngine(); var (bundle, _) = Create(engine);
        var raw = JsonNode.Parse(engine.SerializeBundle(bundle))!.AsObject();
        switch (variant)
        {
            case "null-list": raw["objects"] = null; break;
            case "null-object": raw["objects"] = new JsonArray((JsonNode?)null); break;
            case "duplicate-id": raw["objects"]!.AsArray().Add(raw["objects"]![0]!.DeepClone()); break;
            case "null-assets": raw["objects"]![0]!["assetReferences"] = null; break;
        }
        Assert.Throws<InvalidDataException>(() => engine.ParseBundle(raw.ToJsonString()));
    }

    [Fact]
    public void Case_aliases_cannot_ambiguously_replace_envelope_identity()
    {
        var engine = new HomeProductivityEngine(); var (bundle, _) = Create(engine);
        var raw = engine.SerializeBundle(bundle);
        Assert.Throws<InvalidDataException>(() => engine.ParseBundle("{\"FormatVersion\":1," + raw[1..]));
        var objectAlias = raw.Replace("\"objectId\":", "\"ObjectId\":\"" + Guid.NewGuid() + "\",\"objectId\":", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => engine.ParseBundle(objectAlias));
    }

    private static (HomeProductivityObjectBundle Bundle, HomeProductivityContext Context) Create(HomeProductivityEngine engine)
    {
        var item = engine.CreateObject("text.paragraph", Guid.NewGuid(), JsonSerializer.SerializeToElement(new { text = "Original source" }));
        var context = new HomeProductivityContext("write", "document", 1, [item.ObjectId], new HashSet<string> { "text.paragraph" });
        return (engine.SerializeSelection(context, [item]), context);
    }
}
