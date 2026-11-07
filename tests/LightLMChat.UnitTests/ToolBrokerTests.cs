using System.Text.Json.Nodes;
using LightLMChat.Core;

namespace LightLMChat.UnitTests;

public class ToolBrokerTests
{
    private static readonly ToolBroker ArgBroker = new(new ToolCatalog([
        new TestTool
        {
            Name = "validate_args",
            Description = "Argument validation test tool.",
            ParametersSchemaJson = """
                {"type":"object","properties":{"text":{"type":"string","maxLength":3},"mode":{"type":"string","enum":["a","b"]},"count":{"type":"integer"}},"required":["text"],"additionalProperties":false}
                """
        }
    ]));

    [Fact]
    public async Task Rejects_unknown_tool() =>
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ArgBroker.ExecuteAsync("unknown_tool", new JsonObject(), CancellationToken.None));

    [Fact]
    public async Task Rejects_missing_required_argument() =>
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ArgBroker.ExecuteAsync("validate_args", new JsonObject { ["mode"] = "a" }, CancellationToken.None));

    [Fact]
    public async Task Rejects_wrong_typed_argument() =>
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ArgBroker.ExecuteAsync("validate_args", new JsonObject { ["text"] = 123 }, CancellationToken.None));

    [Fact]
    public async Task Rejects_out_of_enum_argument() =>
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ArgBroker.ExecuteAsync("validate_args", new JsonObject { ["text"] = "ok", ["mode"] = "c" }, CancellationToken.None));

    [Fact]
    public async Task Rejects_over_length_argument() =>
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ArgBroker.ExecuteAsync("validate_args", new JsonObject { ["text"] = "toolong" }, CancellationToken.None));

    [Fact]
    public async Task Rejects_oversized_result()
    {
        var big = new TestTool
        {
            Name = "big_result",
            Description = "Returns an oversized result.",
            ParametersSchemaJson = """{"type":"object","properties":{},"additionalProperties":false}""",
            Handler = _ => new JsonObject { ["blob"] = new string('x', 9000) }
        };
        var broker = new ToolBroker(new ToolCatalog([big]));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await broker.ExecuteAsync("big_result", new JsonObject(), CancellationToken.None));
    }

    [Fact]
    public async Task Write_requires_approval()
    {
        var write = new TestWriteTool();
        var denied = await new ToolBroker(new ToolCatalog([write]))
            .ExecuteAsync(write.Name, new JsonObject { ["value"] = "test" }, CancellationToken.None);
        Assert.Equal("denied", denied["status"]?.GetValue<string>());
        Assert.Equal(0, write.CallCount);
    }

    [Fact]
    public async Task Approved_write_runs()
    {
        var write = new TestWriteTool();
        await new ToolBroker(new ToolCatalog([write]), new AllowApproval())
            .ExecuteAsync(write.Name, new JsonObject { ["value"] = "test" }, CancellationToken.None);
        Assert.Equal(1, write.CallCount);
    }
}
