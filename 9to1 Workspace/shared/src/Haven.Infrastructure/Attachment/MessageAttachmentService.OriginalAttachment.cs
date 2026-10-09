using System.Runtime.CompilerServices;
using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class MessageAttachmentService : IOriginalMessageAttachmentProcessingSource
{
    private readonly object _attachmentOriginalGate = new();
    private ICanonicalAttachmentOriginalContentSource? _attachmentOriginalContent;
    private readonly ConditionalWeakTable<OriginalMessageAttachmentProcessingResult, ICanonicalAttachmentOriginalContentLease> _attachmentOriginalResults = new();

    /// <summary>Pure one-time composition; does not read a file or issue permission.</summary>
    public void BindOriginalContentSource(ICanonicalAttachmentOriginalContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_attachmentOriginalGate)
        {
            if (_attachmentOriginalContent is not null && !ReferenceEquals(_attachmentOriginalContent, source))
                throw new InvalidOperationException("The original attachment content source is already configured.");
            _attachmentOriginalContent = source;
        }
    }
    public bool HasOriginalContentSource(ICanonicalAttachmentOriginalContentSource source)
    { lock (_attachmentOriginalGate) return ReferenceEquals(_attachmentOriginalContent, source); }
    /// <summary>Format availability only; this observation never grants file READ or import.</summary>
    public bool CanExtractOriginalLocalFileName(string originalName)
    {
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        return Classify(extension) is MessageAttachmentKind.PlainText or MessageAttachmentKind.SourceCode ||
            extension is ".docx" or ".pptx" or ".xlsx";
    }

    public bool IsIssuedOriginalProcessing(ICanonicalAttachmentOriginalContentLease sameContent,
        OriginalMessageAttachmentProcessingResult sameResult) =>
        _attachmentOriginalResults.TryGetValue(sameResult, out var content) && ReferenceEquals(content, sameContent);

    public async Task<OriginalMessageAttachmentProcessingResult> ProcessOriginalWithinSourceAsync(
        ICanonicalAttachmentOriginalContentLease sameContent, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token, AttachmentProcessingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sameContent); ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask); token.ThrowIfCancellationRequested();
        var source = new AttachmentOriginalSources(this, originalSynchronousScope, retainOriginalTask);
        OriginalMessageAttachmentProcessingResult? result = null; StreamReader? reader = null;
        try
        {
            ICanonicalAttachmentOriginalContentSource content;
            lock (_attachmentOriginalGate) content = _attachmentOriginalContent ??
                throw new InvalidOperationException("The original attachment content source is not configured.");
            source.Run(() =>
            {
                if (!content.IsIssuedOriginalContent(sameContent.OriginalSelection, sameContent.OriginalRead, sameContent))
                    throw new UnauthorizedAccessException("Retain the same configured source-issued attachment stream.");
            });
            await source.Read(() => sameContent.ValidateOriginalWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            var file = source.Invoke(() => sameContent.OriginalSelection.OriginalFile);
            var actualOptions = options ?? new AttachmentProcessingOptions();
            if (actualOptions.MaxExtractedCharacters is < 1 or > 500_000 || file.SizeBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Choose bounded local attachment extraction.");
            var extension = Path.GetExtension(file.OriginalName).ToLowerInvariant();
            var kind = Classify(extension);
            var openXml = extension is ".docx" or ".pptx" or ".xlsx";
            if (!openXml && kind is not (MessageAttachmentKind.PlainText or MessageAttachmentKind.SourceCode))
                throw new NotSupportedException("Choose a registered text, source, DOCX, PPTX or XLSX file.");
            if (file.SizeBytes > Math.Min(SizeLimit(kind, actualOptions), 50L * 1024 * 1024))
                throw new InvalidOperationException("This file exceeds the local text attachment limit.");
            string text;
            if (openXml)
            {
                Stream originalStream = null!;
                source.Run(() => originalStream = sameContent.OriginalContent);
                text = await ExtractOriginalOpenXmlTextAsync(source, originalStream, kind, actualOptions.MaxExtractedCharacters, token).ConfigureAwait(false);
            }
            else
            {
                source.Run(() => reader = new StreamReader(sameContent.OriginalContent, Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024, leaveOpen: true));
                text = await ReadBoundedTextCoreAsync(reader!, actualOptions.MaxExtractedCharacters,
                    next => source.Read(() => next().AsTask()), token).ConfigureAwait(false);
            }
            await source.Read(() => sameContent.ValidateOriginalWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            result = new(file, kind, AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted,
                MediaTypeFor(extension, kind), text, openXml
                    ? "Document text was extracted locally. Formatting and embedded media are not included."
                    : "Text was read directly from the attached file.");
        }
        catch (Exception error) { source.Remember(error); }
        // Only the decoder belongs to this call; the actual content lease remains
        // with its original source and is never replaced, copied or deleted here.
        if (reader is not null)
            try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { reader.Dispose(); return true; }); }
            catch (Exception error) { source.Remember(error); }
        await source.Join().ConfigureAwait(false);
        if (result is null) throw new InvalidOperationException("The original attachment produced no result.");
        _attachmentOriginalResults.Add(result, sameContent);
        return result;
    }
    /// <summary>Pure configured-owner identity, never file access or import permission.</summary>
    public bool HasOriginalProcessingComposition(IAppPaths actualPaths, IConversationProductionRepository actualRepository,
        ILocalMediaToolLocator actualTools) => ReferenceEquals(paths, actualPaths) &&
        ReferenceEquals(repository, actualRepository) && ReferenceEquals(tools, actualTools);
}
