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

    [Fact]
    public async Task Asks_to_clarify_when_best_is_requested()
    {
        var catalog = new PcPartsCatalog();
        var result = await new ToolBroker(new ToolCatalog([new RecommendBuildTool(catalog)]))
            .ExecuteAsync("recommend_build", new JsonObject { ["criterion"] = "best" }, CancellationToken.None);
        var text = result["result"]?["display_text"]?.GetValue<string>() ?? "";
        Assert.Contains("can't judge", text);
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

public class CompareCpusToolTests
{
    private static async Task<string> Answer(string? criterion)
    {
        var tool = new CompareCpusTool(new PcPartsCatalog());
        var arguments = new JsonObject();
        if (criterion is not null) arguments["criterion"] = criterion;
        var result = await new ToolBroker(new ToolCatalog([tool]))
            .ExecuteAsync(tool.Name, arguments, CancellationToken.None);
        return result["result"]?["display_text"]?.GetValue<string>() ?? "";
    }

    [Fact]
    public async Task Best_cpu_requires_criteria()
    {
        Assert.Contains("cannot identify one overall best", await Answer("best"));
        Assert.Contains("Which criterion", await Answer(null));
    }

    [Fact]
    public async Task Cheapest_cpu_uses_catalog_price()
    {
        Assert.Contains("Cyrix Cx486DLC-33", await Answer("cheapest"));
    }

    [Fact]
    public async Task Highest_clock_is_not_called_fastest()
    {
        var answer = await Answer("highest_clock");
        Assert.Contains("100 MHz", answer);
        Assert.Contains("Clock speed alone", answer);
    }
}

public class SearchPartsTypeFilterTests
{
    [Theory]
    [InlineData("Intel 486", "CPU")]
    [InlineData("VGA", "Display")]
    public async Task Search_only_returns_requested_type(string query, string partType)
    {
        var tool = new SearchPartsTool(new PcPartsCatalog());
        var result = await new ToolBroker(new ToolCatalog([tool])).ExecuteAsync(tool.Name,
            new JsonObject { ["query"] = query, ["part_type"] = partType }, CancellationToken.None);
        var parts = result["result"]?["parts"]?.AsArray();
        Assert.NotNull(parts);
        Assert.NotEmpty(parts!);
        Assert.All(parts!, part => Assert.Equal(partType, part?["type"]?.GetValue<string>()));
    }

    [Fact]
    public void Normalizer_derives_monitor_type_from_user_request()
    {
        var tool = new SearchPartsTool(new PcPartsCatalog());
        var result = tool.NormalizeProposal("Show me VGA monitors", new JsonObject { ["query"] = "VGA" });
        Assert.Equal("Display", result?["part_type"]?.GetValue<string>());
    }
}

public class CatalogKeywordSearchTests
{
    [Fact]
    public void Multiple_keywords_must_all_match()
    {
        var parts = new PcPartsCatalog().Search("Intel 486", "CPU");
        Assert.Equal(4, parts.Count);
        Assert.All(parts, part => Assert.Equal("Intel", part.Company));
        Assert.All(parts, part => Assert.Contains("486", part.Subtype));
    }
}

public class SubjectiveCriterionGuardTests
{
    [Fact]
    public void Best_cpu_cannot_be_rewritten_as_cheapest()
    {
        var tool = new CompareCpusTool(new PcPartsCatalog());
        var result = tool.NormalizeProposal("choose best CPU",
            new JsonObject { ["criterion"] = "cheapest" });
        Assert.Equal("best", result?["criterion"]?.GetValue<string>());
    }

    [Fact]
    public void Best_build_cannot_default_to_cheapest()
    {
        var tool = new RecommendBuildTool(new PcPartsCatalog());
        var result = tool.NormalizeProposal("build the best PC", new JsonObject());
        Assert.Equal("best", result?["criterion"]?.GetValue<string>());
    }

    [Fact]
    public void Best_is_not_used_as_a_catalog_keyword()
    {
        var tool = new SearchPartsTool(new PcPartsCatalog());
        var result = tool.NormalizeProposal("choose best CPU", new JsonObject { ["query"] = "best CPU" });
        Assert.Equal("CPU", result?["query"]?.GetValue<string>());
    }
}
