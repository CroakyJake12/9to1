using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class MessageAttachmentService
{
    /// <summary>Pure formatting after the caller's original protected row/import
    /// validation. This formatter conveys no source or provider authority.</summary>
    public static AttachmentPromptContext BuildOriginalApprovedTextContext(IReadOnlyList<MessageAttachment> approved)
    {
        var selected = Array.AsReadOnly(approved.ToArray()); var text = new StringBuilder(); var notices = new List<string>();
        foreach (var attachment in selected)
        {
            notices.Add($"{attachment.OriginalName}: {NoticeFor(attachment)}");
            AppendAttachmentExtractedText(text, attachment);
        }
        return new(Array.Empty<string>(), Truncate(text.ToString(), new AttachmentProcessingOptions().MaxExtractedCharacters),
            notices.AsReadOnly(), selected);
    }
}
