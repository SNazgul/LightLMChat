using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LightLMChat.Abstractions;

namespace LightLMChat.FamilyTree;

/// <summary>One read-only application operation for birth and children questions.</summary>
public sealed class FamilySummaryTool(IFamilyTreeApi familyTree) : IApplicationTool, IToolProposalNormalizer
{
    public string Name => "get_person_family_summary";
    public string Description => "Find a person by full name in the family tree and return their recorded birth and children. Use for questions about when a person was born and who their children are. A relationship such as aunt is not verified by this tool.";
    public string ParametersSchemaJson => """
        {"type":"object","properties":{"person_name":{"type":"string","description":"The person's full name without relationship words such as aunt","maxLength":120}},"required":["person_name"],"additionalProperties":false}
        """;
    public ToolAccess Access => ToolAccess.ReadOnly;

    public string? OutputSchemaJson => """
        {"type":"object","properties":{"match":{"type":"string"},"display_text":{"type":"string"},"person":{"type":"object"},"children":{"type":"array"},"candidates":{"type":"array"}}}
        """;

    public IReadOnlyList<ToolUsageExample> Examples => new[]
    {
        new ToolUsageExample("When was Mary Example born and who are her children?",
            new JsonObject { ["person_name"] = "Mary Example" }),
        new ToolUsageExample("When was Mary born?", new JsonObject { ["person_name"] = "Mary" })
    };

    public JsonObject? NormalizeProposal(string userMessage, JsonObject proposedArguments)
    {
        if (proposedArguments["person_name"] is not JsonValue value ||
            !value.TryGetValue<string>(out var proposedName)) return null;
        var words = Regex.Matches(proposedName, @"[\p{L}\p{M}\p{N}'-]+")
            .Select(match => match.Value).Take(12).ToArray();
        string? groundedName = null;
        for (var length = words.Length; length > 0; length--)
        {
            for (var start = 0; start + length <= words.Length; start++)
            {
                var candidate = string.Join(" ", words.Skip(start).Take(length));
                if (Regex.IsMatch(userMessage,
                    $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(candidate)}(?![\p{{L}}\p{{N}}])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    groundedName = candidate;
                    break;
                }
            }
            if (groundedName is not null) break;
        }
        if (groundedName is null) return null;

        // A tiny model may return only the surname; recover adjacent, capitalized name words
        // from the user's text, never from conversation history or generated output.
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "when", "who", "what", "where", "which", "aunt", "uncle", "my", "our" };
        foreach (Match match in Regex.Matches(userMessage,
            @"\p{Lu}[\p{L}\p{M}'-]*(?:\s+\p{Lu}[\p{L}\p{M}'-]*)*"))
        {
            var nameWords = match.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(stopWords.Contains).ToArray();
            var fullName = string.Join(" ", nameWords);
            if (fullName.Length is > 0 and <= 120 && fullName.Length > groundedName.Length &&
                Regex.IsMatch(fullName,
                    $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(groundedName)}(?![\p{{L}}\p{{N}}])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                groundedName = fullName;
        }
        return new JsonObject { ["person_name"] = groundedName };
    }

    public async ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var name = arguments["person_name"]!.GetValue<string>().Trim();
        if (name.Length == 0) return new JsonObject
        {
            ["match"] = "none",
            ["display_text"] = "Please give the person's full name."
        };

        var matches = await familyTree.FindPeopleAsync(name, cancellationToken);
        if (matches.Count == 0) return new JsonObject
        {
            ["match"] = "none",
            ["display_text"] = $"I could not find a person named {name} in the family tree."
        };
        if (matches.Count > 1)
        {
            var options = string.Join("; ", matches.Take(5).Select(p =>
                $"{p.DisplayName} (ID {p.Id}, birth {p.BirthHint ?? "not recorded"})"));
            return new JsonObject
            {
                ["match"] = "ambiguous",
                ["display_text"] = $"I found several matches: {options}. Which person do you mean?",
                ["candidates"] = new JsonArray(matches.Take(5).Select(p =>
                    (JsonNode?)new JsonObject
                    {
                        ["id"] = p.Id,
                        ["name"] = p.DisplayName,
                        ["birth_hint"] = p.BirthHint
                    }).ToArray())
            };
        }

        var person = await familyTree.GetPersonAsync(matches[0].Id, cancellationToken);
        if (person is null) return new JsonObject
        {
            ["match"] = "none",
            ["display_text"] = "That person is no longer available in the family tree."
        };
        var relatives = await familyTree.GetRelativesAsync(person.Id, cancellationToken);
        var children = relatives.Where(r => r.Relationship == "child").Take(30).ToArray();
        var born = person.Birth is null ? "has no recorded birth date" :
            $"was born {person.Birth}" + (person.BirthPlace is null ? "" : $" in {person.BirthPlace}");
        var childText = children.Length == 0 ? "No children are recorded" :
            $"Recorded children: {string.Join(", ", children.Select(c => c.DisplayName))}";
        return new JsonObject
        {
            ["match"] = "one",
            ["display_text"] = $"{person.DisplayName} {born}. {childText}.",
            ["person"] = new JsonObject
            {
                ["id"] = person.Id,
                ["name"] = person.DisplayName,
                ["birth"] = person.Birth,
                ["birth_place"] = person.BirthPlace
            },
            ["children"] = new JsonArray(children.Select(p =>
                (JsonNode?)new JsonObject
                {
                    ["id"] = p.PersonId,
                    ["name"] = p.DisplayName,
                    ["birth"] = p.Birth
                }).ToArray())
        };
    }
}
