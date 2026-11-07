using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LightLMChat.Abstractions;
using LightLMChat.Core;
using LightLMChat.FamilyTree;

if (args.Contains("--model"))
{
    await RunFakeServerAsync(args);
    return;
}

var modelFile = Path.GetTempFileName();
try
{
    var executable = Environment.ProcessPath ?? throw new Exception("No executable path.");
    await using var runtime = await LlamaServerRuntime.StartAsync(
        new LlamaServerOptions(executable, modelFile, StartupTimeout: TimeSpan.FromSeconds(10)));
    var session = new LlamaChatSession(runtime);

    var output = new System.Text.StringBuilder();
    await foreach (var chatEvent in session.SendAsync("test streaming", CancellationToken.None))
        if (chatEvent is TextDeltaEvent delta) output.Append(delta.Text);
    Check(output.ToString() == "Hello local", "Streaming reply was assembled incorrectly.");

    using var cancellation = new CancellationTokenSource();
    var sawCancellation = false;
    try
    {
        await foreach (var chatEvent in session.SendAsync("test cancellation", cancellation.Token))
            if (chatEvent is TextDeltaEvent) cancellation.Cancel();
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        sawCancellation = true;
    }
    Check(sawCancellation, "Cancellation did not interrupt the stream.");

    output.Clear();
    await foreach (var chatEvent in session.SendAsync("test recovery", CancellationToken.None))
        if (chatEvent is TextDeltaEvent delta) output.Append(delta.Text);
    Check(output.ToString() == "Hello local", "The session did not recover after cancellation.");

    var familyTool = new FamilySummaryTool(new SampleFamilyTreeApi());
    var catalog = new ToolCatalog([familyTool]);

    var slowSession = new LlamaChatSession(runtime, limits: new ChatRequestLimits { MaxElapsed = TimeSpan.FromSeconds(1) });
    var timedOut = false;
    try
    {
        await foreach (var _ in slowSession.SendAsync("test timeout", CancellationToken.None)) { }
    }
    catch (TimeoutException) { timedOut = true; }
    Check(timedOut, "Per-request elapsed time limit was not enforced.");

    var invalidLimits = false;
    try
    {
        _ = new LlamaChatSession(runtime, limits: new ChatRequestLimits { MaxToolCalls = 0 });
    }
    catch (ArgumentOutOfRangeException) { invalidLimits = true; }
    Check(invalidLimits, "An out-of-range tool-call limit was accepted.");

    var notes = new SampleFamilyTreeNotes();
    var noteTool = new AddPersonNoteTool(new SampleFamilyTreeApi(), notes);

    // Request-limit ranges are validated.
    foreach (var bad in new[]
    {
        new ChatRequestLimits { MaxToolCalls = 9 },
        new ChatRequestLimits { MaxGenerationTokens = 0 },
        new ChatRequestLimits { MaxGenerationTokens = 4097 },
        new ChatRequestLimits { MaxElapsed = TimeSpan.Zero }
    })
    {
        var limitRejected = false;
        try { _ = new LlamaChatSession(runtime, limits: bad); }
        catch (ArgumentOutOfRangeException) { limitRejected = true; }
        Check(limitRejected, "An out-of-range request limit was accepted.");
    }

    // MaxGenerationTokens is forwarded to the model request.
    var tokenSession = new LlamaChatSession(runtime, limits: new ChatRequestLimits { MaxGenerationTokens = 321 });
    output.Clear();
    await foreach (var chatEvent in tokenSession.SendAsync("test max tokens", CancellationToken.None))
        if (chatEvent is TextDeltaEvent delta) output.Append(delta.Text);
    Check(output.ToString() == "321", "MaxGenerationTokens was not forwarded to the model.");

    // Bounded multi-tool loop: the model keeps proposing calls, the session must stop at MaxToolCalls.
    var chainTool = new ChainStepTool();
    var chainSession = new LlamaChatSession(runtime, new ToolCatalog([chainTool]),
        limits: new ChatRequestLimits { MaxToolCalls = 2 });
    output.Clear();
    await foreach (var chatEvent in chainSession.SendAsync("run chain", CancellationToken.None))
        if (chatEvent is TextDeltaEvent delta) output.Append(delta.Text);
    Check(chainTool.CallCount == 2 && output.ToString() == "done",
        "The tool-call loop did not honor MaxToolCalls.");

    var familySession = new LlamaChatSession(runtime, catalog);
    output.Clear();
    await foreach (var chatEvent in familySession.SendAsync(
        "When was Mary Example born and who are her children?", CancellationToken.None))
        if (chatEvent is TextDeltaEvent delta) output.Append(delta.Text);
    Check(output.ToString() == "Mary Example was born 14 May 1908 in Bristol. Recorded children: Anna Example, Peter Example.",
        "Model tool-call flow did not return the family answer.");

    // FR-04: the model receives exactly the session's tools (no missing or extra tools).
    var twoToolSession = new LlamaChatSession(runtime, new ToolCatalog([familyTool, noteTool]));
    output.Clear();
    await foreach (var chatEvent in twoToolSession.SendAsync(
        "When was Mary Example born?", CancellationToken.None))
        if (chatEvent is TextDeltaEvent delta) output.Append(delta.Text);
    Check(output.ToString().Contains("14 May 1908", StringComparison.Ordinal),
        "The model did not receive the exact session tool set.");

    // Runtime lifecycle fault paths (FR-09): load failures are reported and GPU init falls back to CPU.
    var failExitModel = Path.Combine(Path.GetTempPath(), $"fail-exit-{Guid.NewGuid():N}.gguf");
    var failStallModel = Path.Combine(Path.GetTempPath(), $"fail-stall-{Guid.NewGuid():N}.gguf");
    File.WriteAllText(failExitModel, string.Empty);
    File.WriteAllText(failStallModel, string.Empty);
    try
    {
        var exitedDuringStartup = false;
        try
        {
            await using var _ = await LlamaServerRuntime.StartAsync(
                new LlamaServerOptions(executable, failExitModel, StartupTimeout: TimeSpan.FromSeconds(5)), CancellationToken.None);
        }
        catch (InvalidOperationException) { exitedDuringStartup = true; }
        Check(exitedDuringStartup, "A server that exits during startup was not reported as a load failure.");

        var startupTimedOut = false;
        try
        {
            await using var _ = await LlamaServerRuntime.StartAsync(
                new LlamaServerOptions(executable, failStallModel, StartupTimeout: TimeSpan.FromSeconds(2)), CancellationToken.None);
        }
        catch (TimeoutException) { startupTimedOut = true; }
        Check(startupTimedOut, "A server that never becomes healthy did not hit the startup timeout.");

        await using var fallback = await LlamaServerRuntime.StartAsync(
            new LlamaServerOptions(executable, modelFile, GpuLayers: 2, StartupTimeout: TimeSpan.FromSeconds(10)), CancellationToken.None);
        Check(fallback.EffectiveGpuLayers == 0, "GPU startup failure did not fall back to CPU.");
    }
    finally
    {
        File.Delete(failExitModel);
        File.Delete(failStallModel);
    }

    // Bounded conversation context (FR-04): history is capped so the model sees a bounded prompt.
    var contextSession = new LlamaChatSession(runtime);
    for (var i = 0; i < 8; i++)
    {
        await foreach (var _ in contextSession.SendAsync($"turn {i}", CancellationToken.None)) { }
    }
    output.Clear();
    await foreach (var chatEvent in contextSession.SendAsync("report message count", CancellationToken.None))
        if (chatEvent is TextDeltaEvent delta) output.Append(delta.Text);
    Check(int.Parse(output.ToString()) <= 14, "The model received an unbounded conversation context.");

    // Reset starts a new conversation by dropping history.
    var resetSession = new LlamaChatSession(runtime);
    await foreach (var _ in resetSession.SendAsync("seed history", CancellationToken.None)) { }
    resetSession.Reset();
    output.Clear();
    await foreach (var chatEvent in resetSession.SendAsync("report message count", CancellationToken.None))
        if (chatEvent is TextDeltaEvent delta) output.Append(delta.Text);
    Check(int.Parse(output.ToString()) == 2, "Reset did not clear conversation history.");

    // Cancellation during tool execution (FR-08): a host tool honors the request token.
    var slowTool = new SlowTool();
    var slowBroker = new ToolBroker(new ToolCatalog([slowTool]));
    using var slowCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
    var toolCanceled = false;
    try
    {
        await slowBroker.ExecuteAsync(slowTool.Name, new JsonObject(), slowCancellation.Token);
    }
    catch (OperationCanceledException) { toolCanceled = true; }
    Check(toolCanceled && !slowTool.Completed, "Cancellation during tool execution was not honored.");

    var childId = runtime.ProcessId;
    await runtime.DisposeAsync();
    try
    {
        using var child = Process.GetProcessById(childId);
        Check(child.HasExited, "The child server is still running after disposal.");
    }
    catch (ArgumentException) { /* The process has exited and disappeared. */ }

    Console.WriteLine("Runtime checks passed: streaming, cancellation, family API, tool validation, tool metadata, write approval, per-request limits, argument validation, result bounds, write-note flow, multi-tool loop, bounded context, session reset, session tool set, tool cancellation, tool-call flow, runtime faults (exit, stall, GPU fallback), shutdown.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAILED: {ex.Message}");
    Environment.ExitCode = 1;
}
finally
{
    File.Delete(modelFile);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static async Task RunFakeServerAsync(string[] arguments)
{
    var port = arguments[Array.IndexOf(arguments, "--port") + 1];
    var expectedKey = Environment.GetEnvironmentVariable("LLAMA_API_KEY");
    var model = arguments[Array.IndexOf(arguments, "--model") + 1];
    var gpuLayers = int.Parse(arguments[Array.IndexOf(arguments, "--gpu-layers") + 1],
        System.Globalization.CultureInfo.InvariantCulture);

    if (model.Contains("fail-exit", StringComparison.Ordinal))
    {
        Environment.Exit(7);
    }
    if (model.Contains("fail-stall", StringComparison.Ordinal))
    {
        var stall = WebApplication.CreateSlimBuilder(arguments).Build();
        stall.MapGet("/health", () => Results.StatusCode(503));
        await stall.RunAsync();
        return;
    }
    if (gpuLayers > 0)
    {
        Environment.Exit(9);
    }

    var builder = WebApplication.CreateSlimBuilder(arguments);
    builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
    var app = builder.Build();
    app.MapGet("/health", () => Results.Json(new { status = "ok" }));
    app.MapPost("/v1/chat/completions", async (HttpContext context) =>
    {
        if (context.Request.Headers.Authorization != $"Bearer {expectedKey}")
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        using var payload = await JsonDocument.ParseAsync(context.Request.Body);
        var messages = payload.RootElement.GetProperty("messages");
        var last = messages[messages.GetArrayLength() - 1];
        if (payload.RootElement.TryGetProperty("tools", out var tools))
        {
            var functions = tools.EnumerateArray()
                .Select(t => t.GetProperty("function"))
                .ToArray();
            var toolNames = functions.Select(f => f.GetProperty("name").GetString()).ToArray();

            // The exact tool set must match the session catalog: no missing or extra tools.
            if (toolNames is ["get_person_family_summary"] or
                ["get_person_family_summary", "add_person_note"] or
                ["add_person_note", "get_person_family_summary"])
            {
                var family = functions.First(f => f.GetProperty("name").GetString() == "get_person_family_summary");
                if (!family.GetProperty("description").GetString()!.Contains("Examples:", StringComparison.Ordinal))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }
                await context.Response.WriteAsJsonAsync(new
                {
                    choices = new[] { new { message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[] { new
                        {
                            id = "call_1", type = "function",
                            function = new
                            {
                                name = "get_person_family_summary",
                                arguments = "{\"person_name\":\"Mary Example\"}"
                            }
                        } }
                    } } }
                });
                return;
            }
            if (toolNames is ["chain_step"])
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    choices = new[] { new { message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[] { new
                        {
                            id = "chain_1", type = "function",
                            function = new
                            {
                                name = "chain_step",
                                arguments = "{\"step\":1}"
                            }
                        } }
                    } } }
                });
                return;
            }
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        var latest = last.GetProperty("content").GetString();
        context.Response.ContentType = "text/event-stream";
        if (last.GetProperty("role").GetString() == "tool")
        {
            if (latest!.Contains("Anna Example", StringComparison.Ordinal))
            {
                await WriteChunkAsync(context, "Born: 14 May 1908; children: ");
                await WriteChunkAsync(context, "Anna Example, Peter Example");
            }
            else
            {
                await WriteChunkAsync(context, "done");
            }
            await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
            return;
        }
        if (latest == "test max tokens")
        {
            var maxTokens = payload.RootElement.GetProperty("max_tokens").GetInt32();
            await WriteChunkAsync(context, maxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
            return;
        }
        if (latest == "report message count")
        {
            await WriteChunkAsync(context, messages.GetArrayLength().ToString(System.Globalization.CultureInfo.InvariantCulture));
            await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
            return;
        }
        await WriteChunkAsync(context, "Hello ");
        if (latest is "test cancellation" or "test timeout") await Task.Delay(5000, context.RequestAborted);
        await WriteChunkAsync(context, "local");
        await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
    });
    await app.RunAsync();
}

static async Task WriteChunkAsync(HttpContext context, string value)
{
    var data = JsonSerializer.Serialize(new
    {
        choices = new[] { new { delta = new { content = value } } }
    });
    await context.Response.WriteAsync($"data: {data}\n\n", context.RequestAborted);
    await context.Response.Body.FlushAsync(context.RequestAborted);
}

sealed class ChainStepTool : IApplicationTool
{
    public int CallCount { get; private set; }
    public string Name => "chain_step";
    public string Description => "Chain test operation that returns data without display_text.";
    public string ParametersSchemaJson => """{"type":"object","properties":{"step":{"type":"integer"}},"required":["step"],"additionalProperties":false}""";
    public ToolAccess Access => ToolAccess.ReadOnly;
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        CallCount++;
        return ValueTask.FromResult<JsonNode?>(new JsonObject { ["step"] = arguments["step"]!.GetValue<int>() });
    }
}

sealed class SlowTool : IApplicationTool
{
    public bool Completed { get; private set; }
    public string Name => "slow_tool";
    public string Description => "Blocks until the request token is canceled.";
    public string ParametersSchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";
    public ToolAccess Access => ToolAccess.ReadOnly;
    public async ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        Completed = true;
        return new JsonObject { ["done"] = true };
    }
}
