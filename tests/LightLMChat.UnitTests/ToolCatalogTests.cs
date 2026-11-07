using LightLMChat.Core;
using LightLMChat.FamilyTree;

namespace LightLMChat.UnitTests;

public class ToolCatalogTests
{
    [Fact]
    public void Rejects_duplicate_names() =>
        Assert.Throws<ArgumentException>(() => new ToolCatalog([
            new FamilySummaryTool(new SampleFamilyTreeApi()),
            new FamilySummaryTool(new SampleFamilyTreeApi())
        ]));

    [Fact]
    public void Rejects_invalid_name() =>
        Assert.Throws<ArgumentException>(() => new ToolCatalog([new BadNameTool()]));

    [Fact]
    public void Rejects_non_object_input_schema() =>
        Assert.Throws<ArgumentException>(() => new ToolCatalog([new BadInputSchemaTool()]));

    [Fact]
    public void Rejects_invalid_output_schema() =>
        Assert.Throws<ArgumentException>(() => new ToolCatalog([new BadOutputSchemaTool()]));

    [Fact]
    public void Rejects_invalid_example() =>
        Assert.Throws<ArgumentException>(() => new ToolCatalog([new BadExampleTool()]));
}
