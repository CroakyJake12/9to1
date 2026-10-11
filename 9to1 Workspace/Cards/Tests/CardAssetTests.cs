using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Cards.Tests;

public sealed class CardAssetTests
{
    private static CardAssetReference Asset() => new()
    {
        AssetId = Guid.NewGuid(),
        CanonicalFileId = "files:canonical-object-01",
        MediaType = "image/png",
        Sha256 = new string('a', 64),
        ByteLength = 1024,
    };

    [Fact]
    public void RegisteredAssetsPreserveIdentityAndUnknownMetadataAcrossExport()
    {
        CardSet set = CardSetOperations.Create("personal:owner", "Illustrations");
        using JsonDocument extra = JsonDocument.Parse("""{"version":"future","retain":true}""");
        CardAssetReference asset = Asset() with
        {
            Extensions = new Dictionary<string, JsonElement>
            {
                ["Provenance"] = extra.RootElement.Clone(),
            },
        };

        CardSet updated = CardSetOperations.RegisterAssets(set,
            [asset], set.Revision, CardInteractionMode.Edit);
        CardSet reopened = CardSetOperations.ImportJson(CardSetOperations.ExportJson(updated));

        Assert.Equal(set.SetId, reopened.SetId);
        Assert.Equal(set.Revision + 1, reopened.Revision);
        Assert.Single(reopened.Assets);
        Assert.Equal(asset.AssetId, reopened.Assets[0].AssetId);
        Assert.Equal(asset.CanonicalFileId, reopened.Assets[0].CanonicalFileId);
        Assert.Equal(asset.Sha256, reopened.Assets[0].Sha256);
        Assert.Equal("future",
            reopened.Assets[0].Extensions!["Provenance"].GetProperty("version").GetString());
    }

    [Fact]
    public void DuplicateAssetIdentityFailsAtomically()
    {
        CardSet empty = CardSetOperations.Create("personal:owner", "Illustrations");
        CardAssetReference original = Asset();
        CardSet populated = CardSetOperations.RegisterAssets(empty,
            [original], empty.Revision, CardInteractionMode.Edit);

        CardOperationException failure = Assert.Throws<CardOperationException>(() =>
            CardSetOperations.RegisterAssets(populated,
                [Asset(), original],
                populated.Revision, CardInteractionMode.Edit));
        Assert.Equal(CardFailureCode.DuplicateAsset, failure.Code);
        Assert.Single(populated.Assets);
        Assert.Equal(2, populated.Revision);
    }

    [Fact]
    public void InvalidAssetHashOrViewModeRefusesRegistration()
    {
        CardSet set = CardSetOperations.Create("personal:owner", "Illustrations");
        CardOperationException missingHash = Assert.Throws<CardOperationException>(() =>
            CardSetOperations.RegisterAssets(set,
                [Asset() with { Sha256 = "" }],
                set.Revision, CardInteractionMode.Edit));
        Assert.Equal(CardFailureCode.InvalidContent, missingHash.Code);

        CardOperationException view = Assert.Throws<CardOperationException>(() =>
            CardSetOperations.RegisterAssets(set,
                [Asset()], set.Revision, CardInteractionMode.View));
        Assert.Equal(CardFailureCode.ViewIsReadOnly, view.Code);
        Assert.Empty(set.Assets);
    }
}
