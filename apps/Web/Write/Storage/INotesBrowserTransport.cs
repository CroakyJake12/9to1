using System.Text.Json;
namespace NineToOne.Web.Write.Storage;
public interface INotesBrowserTransport
{
    Task<JsonElement> InvokeAsync(string action, JsonElement arguments, CancellationToken cancellationToken);
}
/// <summary>Transport acknowledgment was lost; a mutation may have committed. Never automatically retry.</summary>
public sealed class NotesCommitOutcomeUnknownException(string message, Exception? inner = null) : IOException(message, inner)
{
    public string? Operation { get; init; }
    public Guid? DocumentId { get; init; }
    public long? ExpectedVersion { get; init; }
    public long? IntendedVersion { get; init; }
    public string? ContentSha256 { get; init; }
    public string? VersionId { get; init; }
}
