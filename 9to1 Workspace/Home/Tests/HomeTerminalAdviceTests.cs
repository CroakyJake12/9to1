using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Cui.AI;
using Xunit;
namespace HavenOS.Home.Tests;
public sealed class HomeTerminalAdviceTests
{
    [Fact]
    public async Task Shared_route_advice_only_receives_selected_context_and_returns_inert_suggestions()
    {
        var client = new Client(); var service = new HomeTerminalAdviceService(client);
        var result = await service.AskAsync(Context(), "Explain the selected error");
        Assert.Null(result.Failure); Assert.Equal("Explanation", result.Explanation); Assert.Equal("git status", Assert.Single(result.SuggestedCommands));
        Assert.Equal(AppAiAccessMode.ReadOnly, client.Prompt!.AccessMode); Assert.Empty(client.Prompt.AvailableActions!);
        Assert.Null(client.Prompt.ModelSelection); Assert.Equal("terminal", client.Prompt.Context.AppId);
        Assert.Equal(AppAiDataSensitivity.Restricted, client.Prompt.Context.Sensitivity);
    }
    [Fact]
    public async Task Model_action_request_is_rejected_without_execution_or_approval()
    {
        var client = new Client { Action = true };
        var result = await new HomeTerminalAdviceService(client).AskAsync(Context(), "Explain");
        Assert.Equal("TerminalAdviceActionRejected", result.Failure!.Code); Assert.Empty(result.SuggestedCommands);
    }
    private static TerminalAdviceContext Context() => new(Guid.NewGuid(), new("local"), TerminalSessionMode.Ai, "bash", "/workspace", "git status", 0, "selected output only");
    private sealed class Client : IDulcheAppClient
    {
        public AppAiPrompt? Prompt;
        public bool Action;
        public async IAsyncEnumerable<AppAiResponseChunk> StreamAsync(AppAiPrompt prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Prompt = prompt; await Task.Yield();
            if (Action) yield return new("", RequestedAction: new("shell.execute", JsonSerializer.SerializeToElement(new { command = "echo unsafe" })));
            else yield return new("{\"explanation\":\"Explanation\",\"suggestedCommands\":[\"git status\"]}", true);
        }
    }
}
