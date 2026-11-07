using System.Text.Json.Nodes;
using LightLMChat.Core;
using LightLMChat.PcParts;

namespace LightLMChat.UnitTests;

public class SearchPartsToolTests
{
    private static ToolBroker Broker() => new(new ToolCatalog([new SearchPartsTool(new PcPartsCatalog())]));

    [Fact]
    public async Task Returns_single_match_with_details()
    {
        var result = await Broker().ExecuteAsync("search_parts",
            new JsonObject { ["query"] = "80486DX4" }, CancellationToken.None);
        Assert.Equal("one", result["result"]?["match"]?.GetValue<string>());
        Assert.Contains("80486DX4", result["result"]?["display_text"]?.GetValue<string>() ?? "");
    }

    [Fact]
    public async Task Returns_multiple_matches()
    {
        var result = await Broker().ExecuteAsync("search_parts",
            new JsonObject { ["query"] = "Intel 486" }, CancellationToken.None);
        Assert.Equal("many", result["result"]?["match"]?.GetValue<string>());
    }

    [Fact]
    public async Task Reports_no_match()
    {
        var result = await Broker().ExecuteAsync("search_parts",
            new JsonObject { ["query"] = "zx spectrum" }, CancellationToken.None);
        Assert.Equal("none", result["result"]?["match"]?.GetValue<string>());
    }
}

public class ListPartTypesToolTests
{
    [Fact]
    public async Task Lists_available_types()
    {
        var result = await new ToolBroker(new ToolCatalog([new ListPartTypesTool(new PcPartsCatalog())]))
            .ExecuteAsync("list_part_types", new JsonObject(), CancellationToken.None);
        var text = result["result"]?["display_text"]?.GetValue<string>() ?? "";
        Assert.Contains("CPU", text);
        Assert.Contains("Motherboard", text);
    }
}
