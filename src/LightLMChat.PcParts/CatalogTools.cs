using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LightLMChat.Abstractions;

namespace LightLMChat.PcParts;

/// <summary>Searches the IBM PC parts catalog (286–486 era) by keyword.</summary>
public sealed class SearchPartsTool(PcPartsCatalog catalog) : IApplicationTool, IToolProposalNormalizer
{
    public string Name => "search_parts";
    public string Description => "Search the IBM PC parts catalog (286 to 486 era) by keywords and optional part type. Use it for available CPUs, monitors, motherboards, memory, hard drives, and other components. Use compare_cpus instead when the user asks to choose or rank a CPU.";
    public string ParametersSchemaJson => """
        {"type":"object","properties":{"query":{"type":"string","description":"Search keywords, e.g. \"Intel 486 CPU\" or \"VGA monitor\"","maxLength":80},"part_type":{"type":"string","description":"Optional exact catalog type, such as CPU or Display","maxLength":30}},"required":["query"],"additionalProperties":false}
        """;
    public ToolAccess Access => ToolAccess.ReadOnly;

    public string? OutputSchemaJson => """
        {"type":"object","properties":{"match":{"type":"string"},"display_text":{"type":"string"},"parts":{"type":"array"}}}
        """;

    public IReadOnlyList<ToolUsageExample> Examples => new[]
    {
        new ToolUsageExample("What Intel 486 CPUs do you have?", new JsonObject { ["query"] = "Intel 486 CPU", ["part_type"] = "CPU" }),
        new ToolUsageExample("Show me VGA monitors", new JsonObject { ["query"] = "VGA monitor", ["part_type"] = "Display" })
    };

    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = arguments["query"]!.GetValue<string>().Trim();
        var partType = arguments["part_type"]?.GetValue<string>();
        var parts = catalog.Search(query, partType);

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
        "please", "tell", "sell", "want", "need", "available", "list",
        "best", "fastest", "cheapest", "better", "worse", "choose", "pick"
    };

    public JsonObject? NormalizeProposal(string userMessage, JsonObject proposedArguments)
    {
        var proposed = proposedArguments["query"]?.GetValue<string>()?.Trim() ?? "";
        var userWords = ExtractWords(userMessage);
        var proposedWords = ExtractWords(proposed);
        var userWordSet = new HashSet<string>(userWords, StringComparer.OrdinalIgnoreCase);

        // Trust the model when its query overlaps the current request; otherwise ground the
        // query in the user's message so a stale or hallucinated query cannot repeat.
        var grounded = proposedWords.Length > 0 &&
            proposedWords.Any(w => userWordSet.Contains(w) && !StopWords.Contains(w));
        var queryWords = grounded ? proposedWords : userWords;
        var query = string.Join(" ", queryWords.Where(w => !StopWords.Contains(w)));
        if (string.IsNullOrWhiteSpace(query)) query = userMessage.Trim();
        if (query.Length > 80) query = query[..80];
        var result = new JsonObject { ["query"] = query };
        if (Regex.IsMatch(userMessage, @"\b(?:cpus?|processors?)\b", RegexOptions.IgnoreCase))
            result["part_type"] = "CPU";
        else if (Regex.IsMatch(userMessage, @"\b(?:monitors?|displays?|screens?)\b", RegexOptions.IgnoreCase))
            result["part_type"] = "Display";
        return result;
    }

    private static string[] ExtractWords(string text) =>
        Regex.Matches(text, @"[\p{L}\p{N}'-]+").Select(m => m.Value).ToArray();
}

/// <summary>Answers CPU-only ranking requests using the criteria the catalog supports.</summary>
public sealed class CompareCpusTool(PcPartsCatalog catalog) : IApplicationTool, IToolProposalNormalizer
{
    public string Name => "compare_cpus";
    public string Description => "Use for CPU-only questions such as 'choose best CPU', 'cheapest CPU', or 'which CPU has the highest clock speed'. Do not use recommend_build unless the user asks for a complete PC. Overall 'best' or 'fastest' needs clarification because the catalog has no benchmarks or complete compatibility data.";
    public string ParametersSchemaJson => """
        {"type":"object","properties":{"criterion":{"type":"string","enum":["best","fastest","cheapest","highest_clock"],"description":"The user's comparison criterion. Use best when the user asks for best without a measurable criterion."}},"additionalProperties":false}
        """;
    public ToolAccess Access => ToolAccess.ReadOnly;
    public string? OutputSchemaJson => """{"type":"object","properties":{"display_text":{"type":"string"}}}""";
    public IReadOnlyList<ToolUsageExample> Examples => new[]
    {
        new ToolUsageExample("Choose best CPU", new JsonObject { ["criterion"] = "best" }),
        new ToolUsageExample("Which CPU is cheapest?", new JsonObject { ["criterion"] = "cheapest" })
    };

    public JsonObject? NormalizeProposal(string userMessage, JsonObject proposedArguments)
    {
        var result = (JsonObject)proposedArguments.DeepClone();
        if (Regex.IsMatch(userMessage, @"\bbest\b", RegexOptions.IgnoreCase)) result["criterion"] = "best";
        else if (Regex.IsMatch(userMessage, @"\bfastest\b", RegexOptions.IgnoreCase)) result["criterion"] = "fastest";
        else if (Regex.IsMatch(userMessage, @"\b(?:cheapest|lowest price)\b", RegexOptions.IgnoreCase)) result["criterion"] = "cheapest";
        else if (Regex.IsMatch(userMessage, @"\b(?:clock|mhz)\b", RegexOptions.IgnoreCase)) result["criterion"] = "highest_clock";
        return result;
    }

    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var criterion = arguments["criterion"]?.GetValue<string>()?.Trim().ToLowerInvariant();
        if (criterion is null or "best" or "fastest")
            return ValueTask.FromResult<JsonNode?>(new JsonObject
            {
                ["display_text"] = "I can compare the catalog CPUs by price or listed clock speed, but I cannot identify one overall best CPU: the catalog has no benchmark results or full compatibility data. Which criterion matters to you?"
            });

        var cpus = catalog.All.Where(p => p.Type == "CPU").ToArray();
        if (cpus.Length == 0)
            return ValueTask.FromResult<JsonNode?>(new JsonObject { ["display_text"] = "No CPUs are available in the catalog." });

        if (criterion == "cheapest")
        {
            var cpu = cpus.OrderBy(p => p.PriceUsd).First();
            return ValueTask.FromResult<JsonNode?>(new JsonObject
            {
                ["display_text"] = $"The cheapest listed CPU is {cpu.Subtype} ({cpu.Company}) at ${cpu.PriceUsd}. This is a price comparison, not an overall performance ranking."
            });
        }

        var clocked = cpus.Select(p => new
        {
            Cpu = p,
            Clock = int.TryParse(Regex.Match(p.Characteristics, @"\b(\d+)\s*MHz\b", RegexOptions.IgnoreCase).Groups[1].Value, out var mhz) ? mhz : 0
        }).OrderByDescending(x => x.Clock).First();
        return ValueTask.FromResult<JsonNode?>(new JsonObject
        {
            ["display_text"] = $"The highest listed CPU clock is {clocked.Cpu.Subtype} ({clocked.Cpu.Company}) at {clocked.Clock} MHz, priced ${clocked.Cpu.PriceUsd}. Clock speed alone does not establish overall performance."
        });
    }
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
public sealed class RecommendBuildTool(PcPartsCatalog catalog) : IApplicationTool, IToolProposalNormalizer
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

    public JsonObject? NormalizeProposal(string userMessage, JsonObject proposedArguments)
    {
        var result = (JsonObject)proposedArguments.DeepClone();
        if (Regex.IsMatch(userMessage, @"\bbest\b", RegexOptions.IgnoreCase)) result["criterion"] = "best";
        else if (Regex.IsMatch(userMessage, @"\bfastest\b", RegexOptions.IgnoreCase)) result["criterion"] = "fastest";
        return result;
    }

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
