using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Detached display data for a canonical Data record, never an access grant.</summary>
public sealed record FormDataReferenceChoice(Guid RecordID, string Label);

/// <summary>Success with no choices is a genuine empty result; denied/unavailable reads never masquerade as empty.</summary>
public sealed record FormDataReferenceChoices(bool Success, string Code,
    IReadOnlyList<FormDataReferenceChoice>? Choices, bool HasMore, Guid SourceRevisionID);

/// <summary>Optional owning read session over one original, actually authorized form response/table column.
/// Locators and these metadata properties grant no Data access. Implementations privately retain original
/// actor, configured store/workbook/table and form/version association; every actual read and selection
/// rechecks that original authority/lifetime. Native hosts deny when this port is absent.
/// Local personal owner lookup does not confer anonymous, public respondent, cloud or AI read access.</summary>
public interface IFormDataReferenceLookupSession : IDisposable
{
    Guid FormID { get; }
    Guid FormVersionID { get; }
    Guid ResponseID { get; }
    Guid FieldID { get; }
    Guid ColumnID { get; }
    Guid TableID { get; }
    ValueTask<FormDataReferenceChoices> ReadChoicesAsync(string query, int offset, int maximum,
        CancellationToken cancellationToken = default);
    /// <summary>The owning session accepts only its current actually issued choice object after fresh
    /// original source/actor checks. Returned canonical reference is answer data, not a Data write grant.</summary>
    ValueTask<JsonElement?> SelectAsync(FormDataReferenceChoice originalChoice,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional actual owning provider. The original actor and lifetime constrain a read;
/// form/response/field locators never grant access or establish trusted Data configuration.
/// Native hosts must retain the original response association and deny when this port is absent.</summary>
public interface IFormDataReferenceLookupSource
{
    Task<IFormDataReferenceLookupSession?> OpenForOriginalResponseAsync(Guid formID, Guid responseID,
        Guid fieldID, Guid columnID, AuthenticatedResourceActor originalActor,
        Func<bool> pureOriginalLifetime, CancellationToken token = default);
}
