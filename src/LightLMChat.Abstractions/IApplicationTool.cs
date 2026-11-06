using System.Text.Json.Nodes;

namespace LightLMChat.Abstractions;

/// <summary>A host-owned operation that the assistant may propose.</summary>
/// <remarks>
/// This is the integration point: a host implements one <see cref="IApplicationTool"/> per
/// operation it wants the local model to be able to reach, describing the operation's purpose,
/// its JSON input and output schemas, and optionally usage examples. The core then lets the model
/// select and call these operations dynamically; the host never exposes arbitrary code or data.
/// </remarks>
public interface IApplicationTool
{
    string Name { get; }
    string Description { get; }
    string ParametersSchemaJson { get; }
    ToolAccess Access { get; }

    /// <summary>Runs under the host application's own authorization rules.</summary>
    ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken);

    /// <summary>Optional JSON Schema describing the result shape; used for bounded-result checks.</summary>
    string? OutputSchemaJson => null;

    /// <summary>Optional request → arguments examples that improve model tool selection.</summary>
    IReadOnlyList<ToolUsageExample> Examples => Array.Empty<ToolUsageExample>();
}

public enum ToolAccess
{
    ReadOnly,
    RequiresApproval
}

/// <summary>A natural-language request paired with the arguments the tool should receive for it.</summary>
public sealed record ToolUsageExample(string Request, JsonObject Arguments);
