using System.Diagnostics;
using LightLMChat.Abstractions;
using LightLMChat.Core;
using LightLMChat.PcParts;

var server = Environment.GetEnvironmentVariable("LIGHTLMCHAT_SERVER_PATH")
    ?? throw new InvalidOperationException("Set LIGHTLMCHAT_SERVER_PATH.");
var model = Environment.GetEnvironmentVariable("LIGHTLMCHAT_MODEL_PATH")
    ?? throw new InvalidOperationException("Set LIGHTLMCHAT_MODEL_PATH.");

var thinking = args.Contains("--thinking", StringComparer.OrdinalIgnoreCase);
var pcParts = args.Contains("--pc-parts", StringComparer.OrdinalIgnoreCase);
await using var runtime = await LlamaServerRuntime.StartAsync(new LlamaServerOptions(
    server, model, ContextSize: thinking ? 4096 : 2048,
    ReasoningBudget: thinking ? 256 : null));
var parts = new PcPartsCatalog();
var catalog = pcParts
    ? new ToolCatalog([new SearchPartsTool(parts), new CompareCpusTool(parts),
        new ListPartTypesTool(parts), new RecommendBuildTool(parts)])
    : new ToolCatalog([new SearchPartsTool(parts)]);
var session = new LlamaChatSession(runtime, catalog,
    forceSingleTool: !pcParts,
    limits: new ChatRequestLimits { MaxGenerationTokens = thinking ? 1024 : 256 },
    enableThinking: thinking, reasonAboutToolResults: false,
    systemPrompt: pcParts
        ? "You are a helpful assistant for an IBM PC parts catalog (286-486 era). " +
          "Use tools to look up catalog facts. For CPU-only questions, use compare_cpus; " +
          "use recommend_build only when the user asks for a complete PC. " +
          "The catalog contains CPUs, prices, and basic specifications, but no benchmarks. " +
          "If the user asks for the best CPU without a measurable criterion, explain " +
          "that you cannot rank overall performance and ask whether price or listed " +
          "clock speed matters. Never invent catalog facts. After a tool call, use " +
          "only the returned data and state when it cannot support a conclusion."
        : null);
using var process = Process.GetProcessById(runtime.ProcessId);

var prompts = pcParts
    ? new[] { "choose best CPU", "but I asked for best not for cheapest" }
    : new[] { "What Intel 486 CPUs do you have?", "Show me VGA monitors", "Tell me about the 80486DX4" };
foreach (var prompt in prompts)
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
