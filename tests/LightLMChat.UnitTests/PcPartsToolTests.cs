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

public class RecommendBuildToolTests
{
    [Fact]
    public async Task Picks_one_cheapest_part_per_category()
    {
        var catalog = new PcPartsCatalog();
        var result = await new ToolBroker(new ToolCatalog([new RecommendBuildTool(catalog)]))
            .ExecuteAsync("recommend_build", new JsonObject(), CancellationToken.None);

        var text = result["result"]?["display_text"]?.GetValue<string>() ?? "";
        var parts = result["result"]?["parts"]?.AsArray();
        var total = result["result"]?["total_price_usd"]?.GetValue<int>() ?? 0;

        Assert.Contains("Cheapest complete build", text);
        Assert.Contains("Total: $", text);
        Assert.Equal(12, parts?.Count);
        Assert.True(total > 0);

        // Exactly one CPU (not several), because the tool picks one part per category.
        var cpuCount = text.Split('\n').Count(line => line.StartsWith("- CPU:", StringComparison.Ordinal));
        Assert.Equal(1, cpuCount);
    }

    [Fact]
    public async Task Bases_build_on_requested_cpu()
    {
        var catalog = new PcPartsCatalog();
        var result = await new ToolBroker(new ToolCatalog([new RecommendBuildTool(catalog)]))
            .ExecuteAsync("recommend_build", new JsonObject { ["cpu"] = "286" }, CancellationToken.None);

        var text = result["result"]?["display_text"]?.GetValue<string>() ?? "";
        Assert.Contains("Intel 80286", text);
        Assert.Equal(1, text.Split('\n').Count(line => line.StartsWith("- CPU:", StringComparison.Ordinal)));
    }
}

public class SearchPartsToolNormalizerTests
{
    private readonly SearchPartsTool _tool = new(new PcPartsCatalog());

    [Fact]
    public void Keeps_query_that_overlaps_the_request()
    {
        var result = _tool.NormalizeProposal("What Intel 486 CPUs do you have?",
            new JsonObject { ["query"] = "Intel 486" });
        Assert.Equal("Intel 486", result?["query"]?.GetValue<string>());
    }

    [Fact]
    public void Grounds_query_when_it_does_not_overlap()
    {
        var result = _tool.NormalizeProposal("get father",
            new JsonObject { ["query"] = "FDDASDASSDA" });
        Assert.Equal("father", result?["query"]?.GetValue<string>());
    }
}
