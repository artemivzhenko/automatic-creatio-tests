using System.Text.Json;

namespace CreatioAutoTests.Session;

/// <summary>What the process engine answered when a process was started.</summary>
/// <param name="Process">Schema name of the process.</param>
/// <param name="ProcessId">Id of the process instance, or the empty GUID when it did not start.</param>
/// <param name="Success">Whether the engine ran the process.</param>
/// <param name="Error">The engine's message when it did not.</param>
/// <param name="Results">Result parameters as text, keyed by parameter code.</param>
public sealed record ProcessRunResult(
    string Process,
    string ProcessId,
    bool Success,
    string? Error,
    IReadOnlyDictionary<string, string> Results)
{
    /// <summary>
    /// Reads the <c>RunProcess</c> response. Numbers and booleans come back as JSON values and are
    /// rendered the way the DSL writes them, so a result compares with a script literal directly.
    /// </summary>
    public static ProcessRunResult Parse(string process, string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        var processId = root.TryGetProperty("processId", out var id) ? id.GetString() ?? string.Empty : string.Empty;

        string? error = null;
        if (!success && root.TryGetProperty("errorInfo", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            error = (info.TryGetProperty("message", out var m) ? m.GetString() : null) ?? info.GetRawText();
        }

        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("resultParameterValues", out var values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in values.EnumerateObject())
            {
                results[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => string.Empty,
                    _ => property.Value.GetRawText(),
                };
            }
        }

        return new ProcessRunResult(process, processId, success, error ?? (success ? null : "the engine reported no reason"), results);
    }
}
