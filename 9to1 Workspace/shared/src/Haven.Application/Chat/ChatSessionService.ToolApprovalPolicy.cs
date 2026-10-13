using System.Text.Json;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    // This policy describes only finite operations implemented by the selected
    // first-party runtime below. It never interprets an MCP name/description as
    // proof of effects, nor claims that a host process has OS isolation.
    private static bool NeedsToolApproval(ChatToolApprovalMode mode, ToolRuntimeKind runtime, OllamaToolCall call)
    {
        if (mode == ChatToolApprovalMode.Ask) return true;
        if (mode == ChatToolApprovalMode.FullAccess) return true; // Refused at request admission.
        if (runtime == ToolRuntimeKind.Browser)
        {
            // Browser owns current-page capture and independent site/action gates.
            return !((call.Name is "browser_snapshot" or "browser_read_page") && call.Arguments.Count == 0);
        }
        if (runtime == ToolRuntimeKind.Computer)
        {
            // Actual ComputerToolPass still demands exact selected target admission.
            return !((call.Name is "computer_snapshot" or "computer_list_windows") && call.Arguments.Count == 0);
        }
        if (runtime != ToolRuntimeKind.Workspace) return true;
        // Arbitrary commands and tests run project/user code. Native owners expose
        // no OS-isolation proof; Run automatically therefore still asks for them.
        if (call.Name is "run_command" or "run_tests") return true;
        if (call.Name == "list_files")
            return !OnlyArguments(call, "path", "max_depth") || !OrdinaryRelativePath(call, required: false) ||
                !OptionalInteger(call, "max_depth", 1, 10);
        if (call.Name == "read_file")
            return !OnlyArguments(call, "path") || !OrdinaryRelativePath(call, required: true);
        // Searches may read sensitive files inside an otherwise ordinary folder.
        // Change sets contain multiple nested effects; unknown shapes always ask.
        if (mode != ChatToolApprovalMode.RunAutomatically) return true;
        if (call.Name == "write_file")
            return !OnlyArguments(call, "path", "content") || !OrdinaryRelativePath(call, required: true) || !StringArgument(call, "content");
        if (call.Name == "replace_in_file")
            return !OnlyArguments(call, "path", "old_text", "new_text", "replace_all") ||
                !OrdinaryRelativePath(call, required: true) || !StringArgument(call, "old_text") ||
                !StringArgument(call, "new_text") || !OptionalBoolean(call, "replace_all");
        return true;
    }

    private static bool OnlyArguments(OllamaToolCall call, params string[] names) =>
        call.Arguments.Keys.All(key => names.Contains(key, StringComparer.Ordinal));
    private static bool StringArgument(OllamaToolCall call, string key) =>
        call.Arguments.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String;
    private static bool OptionalInteger(OllamaToolCall call, string key, int minimum, int maximum) =>
        !call.Arguments.TryGetValue(key, out var value) || value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) && number >= minimum && number <= maximum;
    private static bool OptionalBoolean(OllamaToolCall call, string key) =>
        !call.Arguments.TryGetValue(key, out var value) || value.ValueKind is JsonValueKind.True or JsonValueKind.False;
    private static bool OrdinaryRelativePath(OllamaToolCall call, bool required)
    {
        if (!call.Arguments.TryGetValue("path", out var value)) return !required;
        if (value.ValueKind != JsonValueKind.String) return false;
        var path = value.GetString();
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || path.Contains(':') ||
            path.StartsWith('/') || path.StartsWith('\\')) return false;
        if (path == ".") return !required;
        var segments = path.Replace('\\', '/').Split('/');
        // Fail closed on traversal, hidden configuration and credential/persistence
        // locations. This classification never grants file/path access; the original
        // workspace source still resolves identities and holds its existing admission.
        return segments.All(segment => segment.Length != 0 && segment != "." && segment != ".." &&
            !segment.StartsWith('.') && !segment.Contains("secret", StringComparison.OrdinalIgnoreCase) &&
            !segment.Contains("credential", StringComparison.OrdinalIgnoreCase) && !segment.Contains("password", StringComparison.OrdinalIgnoreCase) &&
            !segment.Contains("token", StringComparison.OrdinalIgnoreCase) && !segment.Contains("key", StringComparison.OrdinalIgnoreCase) &&
            !segment.Contains("startup", StringComparison.OrdinalIgnoreCase) && !segment.Contains("autorun", StringComparison.OrdinalIgnoreCase) &&
            !segment.Contains("systemd", StringComparison.OrdinalIgnoreCase) && !segment.Contains("launchagent", StringComparison.OrdinalIgnoreCase) &&
            !segment.Contains("launchdaemon", StringComparison.OrdinalIgnoreCase) && !segment.Contains("cron", StringComparison.OrdinalIgnoreCase));
    }
}
