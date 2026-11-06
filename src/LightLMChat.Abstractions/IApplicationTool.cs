using System.Text.Json.Nodes;

namespace LightLMChat.Abstractions;

/// <summary>A host-owned operation that the assistant may propose.</summary>
public interface IApplicationTool
{
    string Name { get; }
    string Description { get; }
    string ParametersSchemaJson { get; }
    ToolAccess Access { get; }

    /// <summary>Runs under the host application's own authorization rules.</summary>
    ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken);
}

public enum ToolAccess
{
    ReadOnly,
    RequiresApproval
}
