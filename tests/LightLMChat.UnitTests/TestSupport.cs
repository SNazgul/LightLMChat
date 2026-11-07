using System.Text.Json.Nodes;
using LightLMChat.Abstractions;

namespace LightLMChat.UnitTests;

internal sealed class TestTool : IApplicationTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ParametersSchemaJson { get; init; }
    public ToolAccess Access { get; init; } = ToolAccess.ReadOnly;
    public string? OutputSchemaJson { get; init; }
    public IReadOnlyList<ToolUsageExample> Examples { get; init; } = Array.Empty<ToolUsageExample>();
    public Func<JsonObject, JsonNode?>? Handler { get; init; }

    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<JsonNode?>(Handler?.Invoke(arguments));
}

internal sealed class TestWriteTool : IApplicationTool
{
    public int CallCount { get; private set; }
    public string Name => "test_write";
    public string Description => "Test-only write operation.";
    public string ParametersSchemaJson => """
        {"type":"object","properties":{"value":{"type":"string","maxLength":10}},"required":["value"],"additionalProperties":false}
        """;
    public ToolAccess Access => ToolAccess.RequiresApproval;
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        CallCount++;
        return ValueTask.FromResult<JsonNode?>(new JsonObject { ["changed"] = true });
    }
}

internal sealed class AllowApproval : IToolApprovalService
{
    public ValueTask<bool> ApproveAsync(ToolActionProposal proposal, CancellationToken cancellationToken)
        => ValueTask.FromResult(true);
}

internal sealed class BadNameTool : IApplicationTool
{
    public string Name => "BadName";
    public string Description => "Tool with an invalid name.";
    public string ParametersSchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";
    public ToolAccess Access => ToolAccess.ReadOnly;
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<JsonNode?>(null);
}

internal sealed class BadInputSchemaTool : IApplicationTool
{
    public string Name => "bad_input";
    public string Description => "Tool with a non-object input schema.";
    public string ParametersSchemaJson => """{"type":"string"}""";
    public ToolAccess Access => ToolAccess.ReadOnly;
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<JsonNode?>(null);
}

internal sealed class BadOutputSchemaTool : IApplicationTool
{
    public string Name => "bad_output";
    public string Description => "Tool with a malformed output schema.";
    public string ParametersSchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";
    public ToolAccess Access => ToolAccess.ReadOnly;
    public string? OutputSchemaJson => "42";
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<JsonNode?>(null);
}

internal sealed class BadExampleTool : IApplicationTool
{
    public string Name => "bad_example";
    public string Description => "Tool with an invalid usage example.";
    public string ParametersSchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";
    public ToolAccess Access => ToolAccess.ReadOnly;
    public IReadOnlyList<ToolUsageExample> Examples => new[] { new ToolUsageExample("", new JsonObject()) };
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<JsonNode?>(null);
}
