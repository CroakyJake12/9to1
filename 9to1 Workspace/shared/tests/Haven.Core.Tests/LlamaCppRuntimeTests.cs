using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Core.Tests;

public sealed class LlamaCppRuntimeTests
{
    [Fact]
    public void Runtime_is_disabled_and_not_always_loaded_by_default()
    {
        var options = new LlamaCppRuntimeOptions();

        Assert.False(options.Enabled);
        Assert.False(options.AlwaysLoaded);
    }

    [Fact]
    public void Launch_plan_is_loopback_only_and_preserves_paths_as_arguments()
    {
        var executable = Path.GetFullPath(Path.Combine("llama cpp", "llama-server"));
        var model = Path.GetFullPath(Path.Combine("models", "Qwen Test.gguf"));
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: executable,
            ModelPath: model,
            Port: 18081,
            ContextSize: 65536,
            ParallelRequests: 2,
            AlwaysLoaded: true);

        var plan = LlamaCppLaunchPlanner.Create(options);

        Assert.Equal(executable, plan.FileName);
        Assert.NotNull(plan.Endpoint);
        Assert.Equal("127.0.0.1", plan.Endpoint.Host);
        Assert.Equal(18081, plan.Endpoint.Port);
        Assert.Equal("/v1/", plan.Endpoint.AbsolutePath);
        Assert.True(plan.AlwaysLoaded);
        Assert.Equal(
            new[] { "--model", model, "--host", "127.0.0.1", "--port", "18081", "--ctx-size", "65536", "--parallel", "2", "--no-webui" },
            plan.Arguments);
    }

    [Fact]
    public void Launch_plan_supports_current_unified_llama_cli_without_shell_parsing()
    {
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath("llama"),
            ModelPath: Path.GetFullPath("model.gguf"),
            UseUnifiedCli: true);

        var plan = LlamaCppLaunchPlanner.Create(options);

        Assert.Equal("serve", plan.Arguments[0]);
        Assert.Equal("--model", plan.Arguments[1]);
    }

    [Theory]
    [InlineData(80, 32768, 1)]
    [InlineData(18080, 256, 1)]
    [InlineData(18080, 32768, 0)]
    public void Launch_plan_rejects_unsafe_or_invalid_resource_bounds(int port, int contextSize, int parallelRequests)
    {
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath("llama-server"),
            ModelPath: Path.GetFullPath("model.gguf"),
            Port: port,
            ContextSize: contextSize,
            ParallelRequests: parallelRequests);

        Assert.ThrowsAny<ArgumentException>(() => LlamaCppLaunchPlanner.Create(options));
    }

    [Fact]
    public void Malformed_environment_numbers_remain_invalid_instead_of_falling_back_to_a_runnable_default()
    {
        var values = new Dictionary<string, string?>
        {
            ["HAVEN_LLAMA_CPP_ENABLED"] = "true",
            ["HAVEN_LLAMA_CPP_EXECUTABLE"] = Path.GetFullPath("llama-server"),
            ["HAVEN_LLAMA_CPP_MODEL"] = Path.GetFullPath("model.gguf"),
            ["HAVEN_LLAMA_CPP_PORT"] = "not-a-number"
        };
        var options = LlamaCppRuntimeConfiguration.FromEnvironment(key => values.GetValueOrDefault(key));

        Assert.True(options.Enabled);
        Assert.Throws<ArgumentOutOfRangeException>(() => LlamaCppLaunchPlanner.Create(options));
    }

    [Fact]
    public async Task Always_loaded_hook_fails_closed_when_the_executable_is_missing()
    {
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath($"missing-{Guid.NewGuid():N}"),
            ModelPath: Path.GetFullPath("model.gguf"),
            AlwaysLoaded: true);
        await using var runtime = new LlamaCppRuntime(options);

        var status = await runtime.StartIfAlwaysLoadedAsync(CancellationToken.None);

        Assert.False(status.Running);
        Assert.True(status.AlwaysLoaded);
        Assert.NotNull(status.Detail);
        Assert.Contains("does not exist", status.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Always_loaded_hook_is_a_no_op_when_not_enabled()
    {
        await using var runtime = new LlamaCppRuntime(new LlamaCppRuntimeOptions());

        var status = await runtime.StartIfAlwaysLoadedAsync(CancellationToken.None);

        Assert.False(status.Enabled);
        Assert.False(status.Running);
        Assert.Null(status.ProcessId);
    }

    [Fact]
    public void Launch_plan_binds_the_verified_unix_socket_without_loopback_when_a_socket_path_is_configured()
    {
        if (!OperatingSystem.IsLinux()) return;
        var executable = Path.GetFullPath(Path.Combine("llama cpp", "llama-server"));
        var model = Path.GetFullPath(Path.Combine("models", "Qwen Test.gguf"));
        const string socket = "/run/user/1000/haven/inference.sock";
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: executable,
            ModelPath: model,
            ModelAlias: "preview-model",
            SocketPath: socket,
            ContextSize: 32768,
            ParallelRequests: 1,
            AlwaysLoaded: true);

        var plan = LlamaCppLaunchPlanner.Create(options);

        Assert.Equal(executable, plan.FileName);
        Assert.Null(plan.Endpoint); // no loopback URI is claimed for the verified Unix socket
        Assert.Equal(socket, plan.UnixSocketPath);
        Assert.Contains("--alias", plan.Arguments);
        var aliasIndex = ArgumentIndex(plan.Arguments, "--alias");
        Assert.True(aliasIndex >= 0);
        Assert.Equal("preview-model", plan.Arguments[aliasIndex + 1]);
        Assert.Contains("--host", plan.Arguments);
        var hostIndex = ArgumentIndex(plan.Arguments, "--host");
        Assert.True(hostIndex >= 0);
        Assert.Equal(socket, plan.Arguments[hostIndex + 1]);
        Assert.DoesNotContain("--port", plan.Arguments);
        Assert.DoesNotContain("--no-webui", plan.Arguments);
    }

    [Fact]
    public void Bundled_preview_qwen_profile_emits_exact_jinja_reasoning_off()
    {
        if (!OperatingSystem.IsLinux()) return;
        const string socket = "/run/user/1000/haven/llamacpp-worker.sock";
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath("llama-server"),
            ModelPath: Path.GetFullPath("Qwen3-0.6B-Q6_K.gguf"),
            ModelAlias: "qwen3-06b-q6k",
            SocketPath: socket,
            ContextSize: 32768,
            ParallelRequests: 1,
            AlwaysLoaded: true,
            UseJinja: true,
            Reasoning: LlamaCppReasoningMode.Off);

        var plan = LlamaCppLaunchPlanner.Create(options);

        // The pinned supported engine switch: --jinja --reasoning off (disables thinking so a short
        // 32-token capability probe yields actual text rather than reasoning-only content).
        Assert.Contains("--jinja", plan.Arguments);
        var reasoningIndex = ArgumentIndex(plan.Arguments, "--reasoning");
        Assert.True(reasoningIndex >= 0);
        Assert.Equal("off", plan.Arguments[reasoningIndex + 1]);
    }

    [Fact]
    public void Default_and_explicit_callers_do_not_gain_template_or_reasoning_args()
    {
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath("llama-server"),
            ModelPath: Path.GetFullPath("model.gguf"));

        var plan = LlamaCppLaunchPlanner.Create(options);

        // Explicit/default callers that did not opt into the typed controls are unchanged.
        Assert.DoesNotContain("--jinja", plan.Arguments);
        Assert.DoesNotContain("--no-jinja", plan.Arguments);
        Assert.DoesNotContain("--reasoning", plan.Arguments);
    }

    [Theory]
    [InlineData(LlamaCppReasoningMode.On, "on")]
    [InlineData(LlamaCppReasoningMode.Auto, "auto")]
    [InlineData(LlamaCppReasoningMode.Unset, null)]
    public void Explicit_reasoning_preferences_are_mapped_and_validated(LlamaCppReasoningMode mode, string? expected)
    {
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath("llama-server"),
            ModelPath: Path.GetFullPath("model.gguf"),
            Reasoning: mode);

        var plan = LlamaCppLaunchPlanner.Create(options);

        if (expected is null)
        {
            Assert.DoesNotContain("--reasoning", plan.Arguments);
            return;
        }
        var reasoningIndex = ArgumentIndex(plan.Arguments, "--reasoning");
        Assert.True(reasoningIndex >= 0);
        Assert.Equal(expected, plan.Arguments[reasoningIndex + 1]);
    }

    [Fact]
    public void Explicit_jinja_off_emits_no_jinja()
    {
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath("llama-server"),
            ModelPath: Path.GetFullPath("model.gguf"),
            UseJinja: false);

        var plan = LlamaCppLaunchPlanner.Create(options);

        Assert.Contains("--no-jinja", plan.Arguments);
    }

    [Fact]
    public void Unsupported_reasoning_mode_throws_argument_out_of_range()
    {
        var options = new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: Path.GetFullPath("llama-server"),
            ModelPath: Path.GetFullPath("model.gguf"),
            Reasoning: (LlamaCppReasoningMode)999);

        Assert.Throws<ArgumentOutOfRangeException>(() => LlamaCppLaunchPlanner.Create(options));
    }

    private static int ArgumentIndex(IReadOnlyList<string> arguments, string value)
    {
        for (var i = 0; i < arguments.Count; i++)
            if (string.Equals(arguments[i], value, StringComparison.Ordinal)) return i;
        return -1;
    }
}
