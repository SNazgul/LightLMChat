using System.Text.Json.Nodes;
using LightLMChat.Abstractions;

namespace LightLMChat.FamilyTree;

/// <summary>One read-only application operation for birth and children questions.</summary>
public sealed class FamilySummaryTool(IFamilyTreeApi familyTree) : IApplicationTool
{
    public string Name => "get_person_family_summary";
    public string Description => "Find a person by full name in the family tree and return their recorded birth and children. Use for questions about when a person was born and who their children are. A relationship such as aunt is not verified by this tool.";
    public string ParametersSchemaJson => """
        {"type":"object","properties":{"person_name":{"type":"string","description":"The person's full name without relationship words such as aunt","maxLength":120}},"required":["person_name"],"additionalProperties":false}
        """;
    public ToolAccess Access => ToolAccess.ReadOnly;

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
