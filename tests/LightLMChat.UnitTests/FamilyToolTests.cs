using System.Text.Json.Nodes;
using LightLMChat.Core;
using LightLMChat.FamilyTree;

namespace LightLMChat.UnitTests;

public class FamilyToolTests
{
    private readonly FamilySummaryTool _summary = new(new SampleFamilyTreeApi());

    [Fact]
    public void Normalizer_grounds_name_to_request()
    {
        var grounded = _summary.NormalizeProposal("When was Mary born?",
            new JsonObject { ["person_name"] = "Mary Example" });
        Assert.Equal("Mary", grounded?["person_name"]?.GetValue<string>());
    }

    [Fact]
    public void Normalizer_recovers_full_name()
    {
        var grounded = _summary.NormalizeProposal("When unt Marry Jozifer born?",
            new JsonObject { ["person_name"] = "Jozifer" });
        Assert.Equal("Marry Jozifer", grounded?["person_name"]?.GetValue<string>());
    }

    [Fact]
    public void Normalizer_rejects_unrelated_name() =>
        Assert.Null(_summary.NormalizeProposal("When was Alice born?",
            new JsonObject { ["person_name"] = "Mary Example" }));

    [Fact]
    public async Task Returns_birth_and_children()
    {
        var broker = new ToolBroker(new ToolCatalog([_summary]));
        var result = await broker.ExecuteAsync(_summary.Name,
            new JsonObject { ["person_name"] = "Mary Example" }, CancellationToken.None);
        Assert.Equal("14 May 1908", result["result"]?["person"]?["birth"]?.GetValue<string>());
        Assert.Equal(2, result["result"]?["children"]?.AsArray()?.Count);
    }

    [Fact]
    public async Task Reports_ambiguous_match()
    {
        var broker = new ToolBroker(new ToolCatalog([_summary]));
        var result = await broker.ExecuteAsync(_summary.Name,
            new JsonObject { ["person_name"] = "Mary" }, CancellationToken.None);
        Assert.Equal("ambiguous", result["result"]?["match"]?.GetValue<string>());
    }

    [Fact]
    public async Task Reports_absent_person()
    {
        var broker = new ToolBroker(new ToolCatalog([_summary]));
        var result = await broker.ExecuteAsync(_summary.Name,
            new JsonObject { ["person_name"] = "Marry Jozifer" }, CancellationToken.None);
        Assert.Equal("none", result["result"]?["match"]?.GetValue<string>());
    }
}

public class NoteToolTests
{
    private static (SampleFamilyTreeNotes Notes, AddPersonNoteTool Tool) Create()
    {
        var notes = new SampleFamilyTreeNotes();
        return (notes, new AddPersonNoteTool(new SampleFamilyTreeApi(), notes));
    }

    [Fact]
    public async Task Note_requires_approval()
    {
        var (notes, tool) = Create();
        var denied = await new ToolBroker(new ToolCatalog([tool]))
            .ExecuteAsync(tool.Name, new JsonObject { ["person_name"] = "Mary Example", ["note"] = "n" }, CancellationToken.None);
        Assert.Equal("denied", denied["status"]?.GetValue<string>());
        Assert.Equal(0, notes.Count);
    }

    [Fact]
    public async Task Approved_note_runs()
    {
        var (notes, tool) = Create();
        await new ToolBroker(new ToolCatalog([tool]), new AllowApproval())
            .ExecuteAsync(tool.Name, new JsonObject { ["person_name"] = "Mary Example", ["note"] = "n" }, CancellationToken.None);
        Assert.Equal(1, notes.Count);
    }

    [Fact]
    public async Task Ambiguous_note_is_refused()
    {
        var (notes, tool) = Create();
        var result = await new ToolBroker(new ToolCatalog([tool]), new AllowApproval())
            .ExecuteAsync(tool.Name, new JsonObject { ["person_name"] = "Mary", ["note"] = "n" }, CancellationToken.None);
        Assert.Contains("several", result["result"]?["display_text"]?.GetValue<string>() ?? "");
        Assert.Equal(0, notes.Count);
    }

    [Fact]
    public async Task Unknown_person_note_is_refused()
    {
        var (notes, tool) = Create();
        var result = await new ToolBroker(new ToolCatalog([tool]), new AllowApproval())
            .ExecuteAsync(tool.Name, new JsonObject { ["person_name"] = "Nobody Here", ["note"] = "n" }, CancellationToken.None);
        Assert.Contains("could not find", result["result"]?["display_text"]?.GetValue<string>() ?? "");
        Assert.Equal(0, notes.Count);
    }
}
