using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LightLMChat.Abstractions;

namespace LightLMChat.PcParts;

/// <summary>Searches the IBM PC parts catalog (286–486 era) by keyword.</summary>
public sealed class SearchPartsTool(PcPartsCatalog catalog) : IApplicationTool, IToolProposalNormalizer
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

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "find", "show", "me", "about", "a", "an", "the", "for", "of", "with", "to",
        "what", "which", "do", "does", "have", "has", "is", "are", "can", "could", "you",
        "please", "tell", "sell", "want", "need", "available", "list"
    };

    public JsonObject? NormalizeProposal(string userMessage, JsonObject proposedArguments)
    {
        var proposed = proposedArguments["query"]?.GetValue<string>()?.Trim() ?? "";
        var userWords = ExtractWords(userMessage);
        var proposedWords = ExtractWords(proposed);
        var userWordSet = new HashSet<string>(userWords, StringComparer.OrdinalIgnoreCase);

        // Trust the model when its query overlaps the current request; otherwise ground the
        // query in the user's message so a stale or hallucinated query cannot repeat.
        if (proposedWords.Length > 0 && proposedWords.Any(userWordSet.Contains))
            return proposedArguments;

        var query = string.Join(" ", userWords.Where(w => !StopWords.Contains(w)));
        if (string.IsNullOrWhiteSpace(query))
            query = userMessage.Trim();
        if (query.Length > 80)
            query = query[..80];
        return new JsonObject { ["query"] = query };
    }

    private static string[] ExtractWords(string text) =>
        Regex.Matches(text, @"[\p{L}\p{N}'-]+").Select(m => m.Value).ToArray();
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

/// <summary>Assembles a complete PC build: one lowest-priced part per category, optionally based on a CPU.</summary>
public sealed class RecommendBuildTool(PcPartsCatalog catalog) : IApplicationTool
{
    private static readonly string[] BuildTypes =
    [
        "Motherboard", "Memory", "Hard drive", "Video card", "Display",
        "Keyboard", "Mouse", "Case", "Power supply", "Floppy drive", "I/O controller"
    ];

    public string Name => "recommend_build";
    public string Description => "Assemble, build, or configure a complete PC by picking one part in each required category (CPU, motherboard, memory, hard drive, video card, display, keyboard, mouse, case, power supply, floppy drive, and I/O controller). Use this tool for any request to assemble, build, or put together a whole PC, such as 'assemble the cheapest PC' or 'build a PC around the 286 CPU'. It picks parts by a criterion: 'cheapest' (default) picks the lowest-priced part in each category; 'best' or 'fastest' cannot be judged from price and specifications alone, so it asks the user to clarify instead of guessing. Compatibility between parts is not checked.";
    public string ParametersSchemaJson => """
        {"type":"object","properties":{"cpu":{"type":"string","description":"Optional CPU to base the build on, e.g. 286, 386, or 486DX2","maxLength":40},"criterion":{"type":"string","description":"How to choose parts: cheapest (default), fastest, or best","enum":["cheapest","fastest","best"]}},"additionalProperties":false}
        """;
    public ToolAccess Access => ToolAccess.ReadOnly;

    public string? OutputSchemaJson => """
        {"type":"object","properties":{"display_text":{"type":"string"},"total_price_usd":{"type":"integer"},"parts":{"type":"array"}}}
        """;

    public IReadOnlyList<ToolUsageExample> Examples => new[]
    {
        new ToolUsageExample("Assemble the cheapest PC", new JsonObject()),
        new ToolUsageExample("Build a PC around the 286 CPU", new JsonObject { ["cpu"] = "286" })
    };

    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var criterion = arguments["criterion"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? "cheapest";
        if (criterion is "best" or "fastest")
        {
            return ValueTask.FromResult<JsonNode?>(new JsonObject
            {
                ["display_text"] = "I can't judge what is 'best' or 'fastest' because I only have each part's price and basic specifications — not enough to compare overall quality or speed. I can recommend the cheapest build, or list parts by their specifications. Which would you like?"
            });
        }

        var cpuArg = arguments["cpu"]?.GetValue<string>()?.Trim();
        var basedOnCpu = !string.IsNullOrWhiteSpace(cpuArg);

        PcPart? cpu = null;
        if (basedOnCpu)
        {
            cpu = catalog.All
                .Where(p => p.Type == "CPU" && p.Subtype.Contains(cpuArg!, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.PriceUsd)
                .FirstOrDefault();
            if (cpu is null)
                return ValueTask.FromResult<JsonNode?>(new JsonObject
                {
                    ["display_text"] = $"I could not find a CPU matching \"{cpuArg}\" in the catalog."
                });
        }
        cpu ??= catalog.Cheapest("CPU");
        if (cpu is null)
            return ValueTask.FromResult<JsonNode?>(new JsonObject { ["display_text"] = "No CPU is available in the catalog." });

        var picks = new List<PcPart> { cpu };
        foreach (var type in BuildTypes)
        {
            var cheapest = catalog.Cheapest(type);
            if (cheapest is not null) picks.Add(cheapest);
        }

        var total = picks.Sum(p => p.PriceUsd);
        var lines = picks.Select(p => $"- {p.Type}: {p.Subtype} ({p.Company}) — ${p.PriceUsd}");
        var header = basedOnCpu
            ? $"Cheapest complete build based on the {cpu.Subtype}:\n"
            : "Cheapest complete build (one part per category):\n";
        return ValueTask.FromResult<JsonNode?>(new JsonObject
        {
            ["display_text"] = header + string.Join("\n", lines) + $"\n\nTotal: ${total}",
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
