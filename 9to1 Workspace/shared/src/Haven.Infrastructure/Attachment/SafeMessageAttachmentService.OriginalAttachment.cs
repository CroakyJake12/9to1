using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class SafeMessageAttachmentService : IOriginalMessageAttachmentProcessingSource
{
    public void BindOriginalContentSource(ICanonicalAttachmentOriginalContentSource source) => inner.BindOriginalContentSource(source);
    public bool HasOriginalContentSource(ICanonicalAttachmentOriginalContentSource source) => inner.HasOriginalContentSource(source);
    public bool CanExtractOriginalLocalFileName(string originalName) => inner.CanExtractOriginalLocalFileName(originalName);
    public bool IsIssuedOriginalProcessing(ICanonicalAttachmentOriginalContentLease content, OriginalMessageAttachmentProcessingResult result) =>
        inner.IsIssuedOriginalProcessing(content, result);
    public Task<OriginalMessageAttachmentProcessingResult> ProcessOriginalWithinSourceAsync(
        ICanonicalAttachmentOriginalContentLease content, Action<Action> scope, Action<Task> retain,
        CancellationToken token, AttachmentProcessingOptions? options = null) =>
        inner.ProcessOriginalWithinSourceAsync(content, scope, retain, token, options);
    public bool HasOriginalProcessingComposition(MessageAttachmentService actualInner, IAppPaths actualPaths) =>
        ReferenceEquals(inner, actualInner) && ReferenceEquals(paths, actualPaths);
}
