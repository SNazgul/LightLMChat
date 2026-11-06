using System.Text.Json;
using System.Text.RegularExpressions;
using LightLMChat.Abstractions;

namespace LightLMChat.Core;

/// <summary>Immutable catalog of host operations available to a session.</summary>
public sealed class ToolCatalog
{
    private static readonly Regex ValidName = new("^[a-z][a-z0-9_]{0,63}$", RegexOptions.Compiled);
    private readonly Dictionary<string, IApplicationTool> _tools;

    public ToolCatalog(IEnumerable<IApplicationTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = new Dictionary<string, IApplicationTool>(StringComparer.Ordinal);

        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            Validate(tool);
            if (!_tools.TryAdd(tool.Name, tool))
                throw new ArgumentException($"Duplicate tool name: {tool.Name}", nameof(tools));
        }
    }

    public IReadOnlyCollection<IApplicationTool> Tools => _tools.Values;

    public bool TryGet(string name, out IApplicationTool? tool) => _tools.TryGetValue(name, out tool);

    private static void Validate(IApplicationTool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Name) || !ValidName.IsMatch(tool.Name))
            throw new ArgumentException("Tool names must start with a lowercase letter and contain only lowercase letters, digits, or underscores.", nameof(tool));

        if (string.IsNullOrWhiteSpace(tool.Description))
            throw new ArgumentException($"Tool {tool.Name} needs a description.", nameof(tool));

        if (!Enum.IsDefined(tool.Access))
            throw new ArgumentException($"Tool {tool.Name} has an invalid access classification.", nameof(tool));

        using var document = JsonDocument.Parse(tool.ParametersSchemaJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("type", out var type) ||
            type.GetString() != "object")
            throw new ArgumentException($"Tool {tool.Name} needs an object JSON input schema.", nameof(tool));

        if (tool.OutputSchemaJson is { } outputSchema)
        {
            using var output = JsonDocument.Parse(outputSchema);
            if (output.RootElement.ValueKind != JsonValueKind.Object ||
                !output.RootElement.TryGetProperty("type", out _))
                throw new ArgumentException($"Tool {tool.Name} needs an object JSON output schema.", nameof(tool));
        }

        foreach (var example in tool.Examples)
        {
            if (example is null || string.IsNullOrWhiteSpace(example.Request) || example.Arguments is null)
                throw new ArgumentException($"Tool {tool.Name} has an invalid usage example.", nameof(tool));
        }
    }
}
