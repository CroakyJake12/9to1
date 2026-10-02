using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.Stacks.NativeUI;

/// <summary>Exact captured owner edit; no caller value supplies an authenticated actor or grants permission.</summary>
public sealed class StackNativeWriteIntent
{
    private readonly JsonElement _arguments;
    private StackNativeWriteIntent(StackNativeWorkspaceBinding binding, string projectRevision, StackDomainSnapshot domain,
        string operation, string text, IReadOnlyList<string> paths)
    {
        if (binding.ProjectId == Guid.Empty || binding.FilesFolderId == Guid.Empty || domain.ProjectId != binding.ProjectId ||
            string.IsNullOrWhiteSpace(binding.FolderRevision) || projectRevision.Length != 64 ||
            projectRevision.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A canonical Files/project/domain revision binding is required.");
        Binding = binding; ProjectRevision = projectRevision; DomainId = domain.Id;
        HeadRevisionId = domain.HeadRevisionId ?? domain.BaseRevisionId; Operation = operation;
        Text = text; Paths = Array.AsReadOnly(paths.Select(StackPath.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        ActionId = operation switch { "create-domain" => "stacks.domain.create", "set-active" => "stacks.domain.set-active", "commit" => "stacks.source.commit", _ => throw new ArgumentException("Unknown Stack owner operation.") };
        Scopes = Array.AsReadOnly(new[]
        {
            new ResourceScope("files.item", binding.FilesFolderId.ToString("D"), binding.FolderRevision, ResourceAccess.Write),
            new ResourceScope("stacks.project", binding.ProjectId.ToString("D"), projectRevision, ResourceAccess.Write),
            new ResourceScope("stacks.domain", DomainId.ToString("D"), $"{binding.ProjectId:D}:{HeadRevisionId:D}:{projectRevision}", ResourceAccess.Write)
        });
        _arguments = JsonSerializer.SerializeToElement(new { operation, projectId = binding.ProjectId, projectRevision, domainId = DomainId,
            headRevisionId = HeadRevisionId, filesFolderId = binding.FilesFolderId, folderRevision = binding.FolderRevision, text, paths = Paths });
    }
    internal StackNativeWorkspaceBinding Binding { get; }
    internal string Text { get; }
    internal IReadOnlyList<string> Paths { get; }
    public string ProjectRevision { get; }
    public Guid DomainId { get; }
    public Guid HeadRevisionId { get; }
    public string Operation { get; }
    public string ActionId { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    public static StackNativeWriteIntent CreateDomain(StackNativeWorkspaceBinding binding, string revision, StackDomainSnapshot parent, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _ = StackHierarchy.ChildKind(parent.Kind);
        return new(binding, revision, parent, "create-domain", name.Trim(), []);
    }
    public static StackNativeWriteIntent SetActive(StackNativeWorkspaceBinding binding, string revision, StackDomainSnapshot domain) =>
        new(binding, revision, domain, "set-active", "", []);
    public static StackNativeWriteIntent Commit(StackNativeWorkspaceBinding binding, string revision, StackDomainSnapshot domain,
        string message, IReadOnlyList<string> selectedPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (selectedPaths.Count == 0) throw new ArgumentException("Select exact canonical working changes before reviewing a commit.", nameof(selectedPaths));
        return new(binding, revision, domain, "commit", message.Trim(), selectedPaths);
    }
}
