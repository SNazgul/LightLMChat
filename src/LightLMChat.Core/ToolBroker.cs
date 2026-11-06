using System.Text.Json;
using System.Text.Json.Nodes;
using LightLMChat.Abstractions;

namespace LightLMChat.Core;

/// <summary>Validates one model proposal before it can reach a host-owned operation.</summary>
public sealed class ToolBroker
{
    private const int MaxResultCharacters = 8192;
    private readonly ToolCatalog _catalog;
    private readonly IToolApprovalService? _approval;

    public ToolBroker(ToolCatalog catalog, IToolApprovalService? approval = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _approval = approval;
    }

    public async ValueTask<JsonObject> ExecuteAsync(
        string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!_catalog.TryGet(name, out var tool) || tool is null)
            throw new ArgumentException($"Unknown application tool: {name}", nameof(name));

        ValidateArguments(tool.ParametersSchemaJson, arguments);
        cancellationToken.ThrowIfCancellationRequested();

        if (tool.Access == ToolAccess.RequiresApproval &&
            (_approval is null || !await _approval.ApproveAsync(
                new ToolActionProposal(name, (JsonObject)arguments.DeepClone()), cancellationToken)))
            return new JsonObject { ["status"] = "denied" };

        var result = await tool.ExecuteAsync((JsonObject)arguments.DeepClone(), cancellationToken);
        var serialized = result?.ToJsonString() ?? "null";
        if (serialized.Length > MaxResultCharacters)
            throw new InvalidOperationException($"Tool {name} returned more than {MaxResultCharacters} characters.");

        return new JsonObject
        {
            ["status"] = "ok",
            ["result"] = result?.DeepClone()
        };
    }

    private static void ValidateArguments(string schemaJson, JsonObject arguments)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        var root = schema.RootElement;
        if (!root.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Tool schema must declare properties.");

        if (root.TryGetProperty("required", out var required))
        {
            foreach (var property in required.EnumerateArray())
            {
                var key = property.GetString()!;
                if (!arguments.ContainsKey(key) || arguments[key] is null)
                    throw new ArgumentException($"Required argument is missing: {key}");
            }
        }

        foreach (var (key, node) in arguments)
        {
            if (!properties.TryGetProperty(key, out var propertySchema))
                throw new ArgumentException($"Unexpected argument: {key}");
            if (node is null) throw new ArgumentException($"Null argument is not allowed: {key}");

            var type = propertySchema.GetProperty("type").GetString();
            using var value = JsonDocument.Parse(node.ToJsonString());
            var element = value.RootElement;
            var valid = type switch
            {
                "string" => element.ValueKind == JsonValueKind.String,
                "integer" => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out _),
                "number" => element.ValueKind == JsonValueKind.Number,
                "boolean" => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => false
            };
            if (!valid) throw new ArgumentException($"Invalid type for argument: {key}");

            if (type == "string" && propertySchema.TryGetProperty("maxLength", out var maxLength) &&
                element.GetString()!.Length > maxLength.GetInt32())
                throw new ArgumentException($"Argument is too long: {key}");

            if (propertySchema.TryGetProperty("enum", out var allowed) &&
                !allowed.EnumerateArray().Any(item => item.GetRawText() == element.GetRawText()))
                throw new ArgumentException($"Argument is outside the allowed values: {key}");
        }
    }
}
