using System.Text.Json.Nodes;
using LightLMChat.Abstractions;

namespace LightLMChat.FamilyTree;

/// <summary>One write operation: add a short research note to a person found by full name.</summary>
/// <remarks>Requires host approval before any mutation is performed. It refuses to guess when
/// the name matches zero or several people, so the approval dialog always shows a concrete,
/// already-resolved person.</remarks>
public sealed class AddPersonNoteTool(IFamilyTreeApi familyTree, IFamilyTreeNotes notes) : IApplicationTool
{
    public string Name => "add_person_note";
    public string Description => "Add a short research note to a person found by full name in the family tree. Use for requests to record or remember something about a person.";
    public string ParametersSchemaJson => """
        {"type":"object","properties":{"person_name":{"type":"string","description":"The person's full name","maxLength":120},"note":{"type":"string","description":"The short note to store","maxLength":500}},"required":["person_name","note"],"additionalProperties":false}
        """;
    public ToolAccess Access => ToolAccess.RequiresApproval;

    public string? OutputSchemaJson => """
        {"type":"object","properties":{"display_text":{"type":"string"},"person_id":{"type":"string"},"note":{"type":"string"}}}
        """;

    public IReadOnlyList<ToolUsageExample> Examples => new[]
    {
        new ToolUsageExample("Add a note to Mary Example saying confirmed birth date",
            new JsonObject { ["person_name"] = "Mary Example", ["note"] = "confirmed birth date" })
    };

    public async ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var name = arguments["person_name"]!.GetValue<string>().Trim();
        var note = arguments["note"]!.GetValue<string>().Trim();
        if (name.Length == 0 || note.Length == 0)
            return new JsonObject { ["display_text"] = "A person's full name and a note are both required." };

        var matches = await familyTree.FindPeopleAsync(name, cancellationToken);
        if (matches.Count == 0)
            return new JsonObject { ["display_text"] = $"I could not find a person named {name}, so no note was added." };
        if (matches.Count > 1)
            return new JsonObject { ["display_text"] = $"{name} matches several people, so no note was added. Please give the full name." };

        var person = matches[0];
        var added = await notes.AddNoteAsync(person.Id, note, cancellationToken);
        if (!added)
            return new JsonObject { ["display_text"] = "The note could not be stored." };

        return new JsonObject
        {
            ["display_text"] = $"Added a note to {person.DisplayName}: {note}",
            ["person_id"] = person.Id,
            ["note"] = note
        };
    }
}
