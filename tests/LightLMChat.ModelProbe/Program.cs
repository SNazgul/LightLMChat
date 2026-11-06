using System.Diagnostics;
using LightLMChat.Abstractions;
using LightLMChat.Core;
using LightLMChat.FamilyTree;

var server = Environment.GetEnvironmentVariable("LIGHTLMCHAT_SERVER_PATH")
    ?? throw new InvalidOperationException("Set LIGHTLMCHAT_SERVER_PATH.");
var model = Environment.GetEnvironmentVariable("LIGHTLMCHAT_MODEL_PATH")
    ?? throw new InvalidOperationException("Set LIGHTLMCHAT_MODEL_PATH.");

await using var runtime = await LlamaServerRuntime.StartAsync(new LlamaServerOptions(server, model));
var catalog = new ToolCatalog([new FamilySummaryTool(new SampleFamilyTreeApi())]);
var session = new LlamaChatSession(runtime, catalog, forceSingleTool: true);
using var process = Process.GetProcessById(runtime.ProcessId);

foreach (var prompt in new[]
{
    "When was Mary Example born and who are her children?",
    "When was Mary born and who are her children?",
    "When unt Marry Jozifer born and who are her children?"
})
{
    Console.WriteLine($"\nUser: {prompt}");
    var clock = Stopwatch.StartNew();
    var selectedTool = false;
    var answer = new System.Text.StringBuilder();
    await foreach (var chatEvent in session.SendAsync(prompt, CancellationToken.None))
    {
        if (chatEvent is ToolActivityEvent tool)
        {
            selectedTool = true;
            Console.WriteLine($"Tool: {tool.ToolName}");
        }
        if (chatEvent is TextDeltaEvent delta) answer.Append(delta.Text);
    }
    clock.Stop();
    process.Refresh();
    Console.WriteLine($"Assistant: {answer}");
    Console.WriteLine($"Tool selected: {selectedTool}; elapsed: {clock.Elapsed}; process working set: {process.WorkingSet64 / 1048576:N0} MiB");
}
