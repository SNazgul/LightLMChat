using System.Text.Json.Nodes;
using LightLMChat.Abstractions;

namespace LightLMChat.PcParts;

/// <summary>Searches the IBM PC parts catalog (286–486 era) by keyword.</summary>
public sealed class SearchPartsTool(PcPartsCatalog catalog) : IApplicationTool
{
    public string Name => "search_parts";
    public string Description => "Search the IBM PC parts catalog (286 to 486 era) by keywords such as a part type, model, or company. Use it to answer questions about available CPUs, motherboards, memory, hard drives, displays, mice, keyboards, and other PC components.";
    public string ParametersSchemaJson => """
        {"type":"object","properties":{"query":{"type":"string","description":"Search keywords, e.g. \"Intel 486 CPU\" or \"VGA monitor\"","maxLength":80}},"required":["query"],"additionalProperties":false}
        """;
    public ToolAccess Access => ToolAccess.ReadOnly;

    public string? OutputSchemaJson => """
        {"type":"object","properties":{"match":{"type":"string"},"display_text":{"type":"string"},"parts":{"type":"array"}}}
        """;

    public IReadOnlyList<ToolUsageExample> Examples => new[]
    {
        new ToolUsageExample("What Intel 486 CPUs do you have?", new JsonObject { ["query"] = "Intel 486 CPU" }),
        new ToolUsageExample("Show me VGA monitors", new JsonObject { ["query"] = "VGA monitor" })
    };

    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = arguments["query"]!.GetValue<string>().Trim();
        var parts = catalog.Search(query);

        if (parts.Count == 0)
            return ValueTask.FromResult<JsonNode?>(new JsonObject
            {
                ["match"] = "none",
                ["display_text"] = $"I could not find any parts matching \"{query}\"."
            });

        if (parts.Count == 1)
        {
            var p = parts[0];
            return ValueTask.FromResult<JsonNode?>(new JsonObject
            {
                ["match"] = "one",
                ["display_text"] = $"{p.Subtype} ({p.Type}) by {p.Company} — ${p.PriceUsd}. {p.Characteristics}. {p.Description}",
                ["parts"] = ToJson(parts)
            });
        }

        var list = string.Join("\n", parts.Select(p => $"- {p.Subtype} ({p.Type}) — {p.Company} (${p.PriceUsd})"));
        return ValueTask.FromResult<JsonNode?>(new JsonObject
        {
            ["match"] = "many",
            ["display_text"] = $"Found {parts.Count} parts:\n{list}\n\nAsk about a specific one for details.",
            ["parts"] = ToJson(parts)
        });
    }

    private static JsonArray ToJson(IReadOnlyList<PcPart> parts) => new(parts.Select(p => (JsonNode?)new JsonObject
    {
        ["id"] = p.Id,
        ["type"] = p.Type,
        ["subtype"] = p.Subtype,
        ["company"] = p.Company,
        ["characteristics"] = p.Characteristics,
        ["description"] = p.Description,
        ["price_usd"] = p.PriceUsd
    }).ToArray());
}

/// <summary>Lists the part types available in the catalog.</summary>
public sealed class ListPartTypesTool(PcPartsCatalog catalog) : IApplicationTool
{
    public string Name => "list_part_types";
    public string Description => "List the part types available in the IBM PC parts catalog (286 to 486 era), such as CPU, motherboard, memory, and hard drive.";
    public string ParametersSchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";
    public ToolAccess Access => ToolAccess.ReadOnly;

    public string? OutputSchemaJson => """{"type":"object","properties":{"display_text":{"type":"string"},"types":{"type":"array"}}}""";

    public IReadOnlyList<ToolUsageExample> Examples => new[]
    {
        new ToolUsageExample("What kinds of parts do you have?", new JsonObject())
    };

    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var types = catalog.Types;
        return ValueTask.FromResult<JsonNode?>(new JsonObject
        {
            ["display_text"] = "Available part types: " + string.Join(", ", types) + ".",
            ["types"] = new JsonArray(types.Select(t => (JsonNode?)JsonValue.Create(t)!).ToArray())
        });
    }
}

/// <summary>Recommends the cheapest complete PC build: one lowest-priced part per category.</summary>
public sealed class RecommendBuildTool(PcPartsCatalog catalog) : IApplicationTool
{
    private static readonly string[] BuildTypes =
    [
        "CPU", "Motherboard", "Memory", "Hard drive", "Video card", "Display",
        "Keyboard", "Mouse", "Case", "Power supply", "Floppy drive", "I/O controller"
    ];

    public string Name => "recommend_build";
    public string Description => "Recommend the cheapest complete PC build by picking the single lowest-priced part in each required category (CPU, motherboard, memory, hard drive, video card, display, keyboard, mouse, case, power supply, floppy drive, and I/O controller). Use for requests like 'assemble the cheapest PC' or 'what is the cheapest build'. This picks by price only and does not check compatibility between parts.";
    public string ParametersSchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";
    public ToolAccess Access => ToolAccess.ReadOnly;

    public string? OutputSchemaJson => """
        {"type":"object","properties":{"display_text":{"type":"string"},"total_price_usd":{"type":"integer"},"parts":{"type":"array"}}}
        """;

    public IReadOnlyList<ToolUsageExample> Examples => new[]
    {
        new ToolUsageExample("Assemble the cheapest PC", new JsonObject())
    };

    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picks = BuildTypes.Select(catalog.Cheapest).OfType<PcPart>().ToArray();
        var total = picks.Sum(p => p.PriceUsd);
        var lines = picks.Select(p => $"- {p.Type}: {p.Subtype} ({p.Company}) — ${p.PriceUsd}");
        return ValueTask.FromResult<JsonNode?>(new JsonObject
        {
            ["display_text"] = "Cheapest complete build (one part per category):\n" + string.Join("\n", lines) + $"\n\nTotal: ${total}",
            ["total_price_usd"] = total,
            ["parts"] = new JsonArray(picks.Select(p => (JsonNode?)new JsonObject
            {
                ["id"] = p.Id,
                ["type"] = p.Type,
                ["subtype"] = p.Subtype,
                ["company"] = p.Company,
                ["price_usd"] = p.PriceUsd
            }).ToArray())
        });
    }
}
