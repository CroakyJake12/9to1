using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Application.NodeGraph;

namespace HavenOS.Home.Core;

/// <summary>Same-host privately issued canonical graph observation. Receipt fields are metadata;
/// only the original issuer can validate this retained reference against genuine raw Home state.
/// It grants no graph write, SQL mutation, run admission or node action.</summary>
public sealed class HomeGraphPublicationObservation
{
    internal HomeGraphPublicationObservation(HomeGraphPublicationOwner issuer, HomeGraphPublicationOwner.Review review,
        AuthenticatedResourceActor actor, GraphPublicationReceipt receipt, HomeCoreStateRecord record)
    {
        Issuer = issuer; Review = review; Actor = actor; Receipt = receipt; RecordId = record.RecordId;
        RecordFingerprint = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record));
    }
    internal HomeGraphPublicationOwner Issuer { get; }
    internal HomeGraphPublicationOwner.Review Review { get; }
    internal AuthenticatedResourceActor Actor { get; }
    internal string RecordId { get; }
    internal byte[] RecordFingerprint { get; }
    public GraphPublicationReceipt Receipt { get; }
}
