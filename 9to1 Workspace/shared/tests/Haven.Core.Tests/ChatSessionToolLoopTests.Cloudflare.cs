using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;
namespace Haven.Core.Tests;

public sealed partial class ChatSessionToolLoopTests
{
    [Fact] public async Task Ordinary_workspace_Send_with_configured_composite_never_acquires_Cloudflare_setup()
    {
        var model = new ModelDescriptor("ordinary-tools", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
        var source = new NeverOrdinaryCloudflareSource();
        // These canonical components are deliberately unavailable: ordinary execution
        // must preserve its existing concrete Workspace runtime without acquiring them.
        var composite = new CanonicalWorkspaceCloudflareToolActionOwner(null!, null!, null!, source);
        var service = new ChatSessionService(new FakeConversations(), new FakeOllama(model), new CapabilityPreflightService(), new PermitSafety(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()), taskToolOwner: composite);
        var now = DateTimeOffset.UtcNow; var conversation = new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.StudioChat, "ordinary", null, null, false, false, now, now);
        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(conversation, "Create the file", model, EffortLevel.Medium, [], "Default", "", DuoMode.Solo, _root, "", "", null, default)) events.Add(item);
        Assert.Equal("created", await File.ReadAllTextAsync(Path.Combine(_root, "tool-loop.txt")));
        Assert.Single(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted); Assert.Equal(0, source.Calls);
    }
    [Fact] public async Task Ordinary_MCP_Send_with_composite_preserves_real_MCP_admission_without_CF_acquisition()
    {
        var model = new ModelDescriptor("ordinary-mcp", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
        var source = new NeverOrdinaryCloudflareSource(); var composite = new CanonicalWorkspaceCloudflareToolActionOwner(null!, null!, null!, source);
        var connection = ChatMcpRepository.ReadyConnection(); var client = new ChatMcpClient();
        var runtime = new McpToolRuntime(new ChatMcpRepository(connection), client, new ExactMcpAdmission(connection.Id));
        var service = new ChatSessionService(new FakeConversations(), new SingleToolOllama(model, McpToolRuntime.LocalToolName(connection.Id, "write_item")),
            new CapabilityPreflightService(), new PermitSafety(), new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()), mcpTools: runtime, taskToolOwner: composite);
        var now = DateTimeOffset.UtcNow; var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "ordinary", null, null, false, true, now, now);
        var active = new ActiveCapability(ExternalConnectionNaming.CapabilityKey(connection.Id), ExternalConnectionNaming.PluginName(connection.Name), "connection", "Use connection", "connection.mcp", "haven.connections");
        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(conversation, "Write the item", model, EffortLevel.Medium, [active], "Default", "", DuoMode.Solo, null, "", "", null, default, commandPermission: PermissionMode.FullAccess)) events.Add(item);
        Assert.Equal(1, client.InvocationCount); Assert.Equal(0, source.Calls); Assert.Single(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }
    private sealed class NeverOrdinaryCloudflareSource : ICloudflareSavedServiceSource
    {
        internal int Calls;
        public Task<CloudflareSavedService> AcquireOriginalAsync(CancellationToken token) { Calls++; throw new InvalidOperationException("Ordinary caller must not acquire CF setup."); }
        public Task RevalidateOriginalAsync(CloudflareSavedService service, CancellationToken token) { Calls++; throw new InvalidOperationException("No ordinary CF original exists."); }
    }
}
