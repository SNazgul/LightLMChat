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
    var grounded = familyTool.NormalizeProposal("When was Mary born?",
        new JsonObject { ["person_name"] = "Mary Example" });
    Check(grounded?["person_name"]?.GetValue<string>() == "Mary",
        "The tool accepted a name that was not fully present in the current request.");
    grounded = familyTool.NormalizeProposal("When unt Marry Jozifer born?",
        new JsonObject { ["person_name"] = "Jozifer" });
    Check(grounded?["person_name"]?.GetValue<string>() == "Marry Jozifer",
        "The tool did not recover the full name from the user's question.");
    Check(familyTool.NormalizeProposal("When was Alice born?",
        new JsonObject { ["person_name"] = "Mary Example" }) is null,
        "An unrelated person name was accepted.");
    var catalog = new ToolCatalog([familyTool]);
    var broker = new ToolBroker(catalog);
    var result = await broker.ExecuteAsync(familyTool.Name,
        new JsonObject { ["person_name"] = "Mary Example" }, CancellationToken.None);
    Check(result["result"]?["person"]?["birth"]?.GetValue<string>() == "14 May 1908",
        "Family birth value was not returned.");
    Check(result["result"]?["children"]?.AsArray().Count == 2,
        "Family children were not returned.");

    var ambiguous = await broker.ExecuteAsync(familyTool.Name,
        new JsonObject { ["person_name"] = "Mary" }, CancellationToken.None);
    Check(ambiguous["result"]?["match"]?.GetValue<string>() == "ambiguous",
        "Ambiguous person names must not be guessed.");

    var absent = await broker.ExecuteAsync(familyTool.Name,
        new JsonObject { ["person_name"] = "Marry Jozifer" }, CancellationToken.None);
    Check(absent["result"]?["match"]?.GetValue<string>() == "none",
        "An absent person must not acquire invented facts.");

    var invalidRejected = false;
    try
    {
        await broker.ExecuteAsync(familyTool.Name,
            new JsonObject { ["person_name"] = "Mary Example", ["unknown"] = "x" }, CancellationToken.None);
    }
    catch (ArgumentException) { invalidRejected = true; }
    Check(invalidRejected, "Unexpected model arguments were accepted.");

    invalidRejected = false;
    try
    {
        await broker.ExecuteAsync("unknown_tool",
            new JsonObject { ["person_name"] = "Mary Example" }, CancellationToken.None);
    }
    catch (ArgumentException) { invalidRejected = true; }
    Check(invalidRejected, "An unregistered tool was accepted.");

    var write = new TestWriteTool();
    var writeCatalog = new ToolCatalog([write]);
    var denied = await new ToolBroker(writeCatalog).ExecuteAsync(write.Name,
        new JsonObject { ["value"] = "test" }, CancellationToken.None);
    Check(denied["status"]?.GetValue<string>() == "denied" && write.CallCount == 0,
        "A write ran without approval.");
    await new ToolBroker(writeCatalog, new AllowApproval()).ExecuteAsync(write.Name,
        new JsonObject { ["value"] = "test" }, CancellationToken.None);
    Check(write.CallCount == 1, "Approved write did not run.");

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
    var noteCatalog = new ToolCatalog([noteTool]);
    var noteDenied = await new ToolBroker(noteCatalog).ExecuteAsync(noteTool.Name,
        new JsonObject { ["person_name"] = "Mary Example", ["note"] = "test note" }, CancellationToken.None);
    Check(noteDenied["status"]?.GetValue<string>() == "denied" && notes.Count == 0,
        "A write note ran without approval.");
    await new ToolBroker(noteCatalog, new AllowApproval()).ExecuteAsync(noteTool.Name,
        new JsonObject { ["person_name"] = "Mary Example", ["note"] = "test note" }, CancellationToken.None);
    Check(notes.Count == 1, "An approved write note did not run.");
    var ambiguousNote = await new ToolBroker(noteCatalog, new AllowApproval()).ExecuteAsync(noteTool.Name,
        new JsonObject { ["person_name"] = "Mary", ["note"] = "test note" }, CancellationToken.None);
    Check(ambiguousNote["result"]?["display_text"]?.GetValue<string>()!.Contains("several", StringComparison.Ordinal) == true,
        "A write note must not guess an ambiguous person.");

    var badMetadataRejected = false;
    try
    {
        _ = new ToolCatalog([new BadOutputSchemaTool()]);
    }
    catch (ArgumentException) { badMetadataRejected = true; }
    Check(badMetadataRejected, "A tool with an invalid output schema was accepted.");

    // ToolCatalog construction validation.
    var catalogRejected = false;
    try { _ = new ToolCatalog([familyTool, new FamilySummaryTool(new SampleFamilyTreeApi())]); }
    catch (ArgumentException) { catalogRejected = true; }
    Check(catalogRejected, "Duplicate tool names were accepted.");

    catalogRejected = false;
    try { _ = new ToolCatalog([new BadNameTool()]); }
    catch (ArgumentException) { catalogRejected = true; }
    Check(catalogRejected, "An invalid tool name was accepted.");

    catalogRejected = false;
    try { _ = new ToolCatalog([new BadInputSchemaTool()]); }
    catch (ArgumentException) { catalogRejected = true; }
    Check(catalogRejected, "A tool with a non-object input schema was accepted.");

    catalogRejected = false;
    try { _ = new ToolCatalog([new BadExampleTool()]); }
    catch (ArgumentException) { catalogRejected = true; }
    Check(catalogRejected, "A tool with an invalid usage example was accepted.");

    // ToolBroker argument-validation branches.
    var argTool = new TestTool
    {
        Name = "validate_args",
        Description = "Argument validation test tool.",
        ParametersSchemaJson = """
            {"type":"object","properties":{"text":{"type":"string","maxLength":3},"mode":{"type":"string","enum":["a","b"]},"count":{"type":"integer"}},"required":["text"],"additionalProperties":false}
            """
    };
    var argBroker = new ToolBroker(new ToolCatalog([argTool]));

    var argRejected = false;
    try { await argBroker.ExecuteAsync("validate_args", new JsonObject { ["mode"] = "a" }, CancellationToken.None); }
    catch (ArgumentException) { argRejected = true; }
    Check(argRejected, "A missing required argument was accepted.");

    argRejected = false;
    try { await argBroker.ExecuteAsync("validate_args", new JsonObject { ["text"] = 123 }, CancellationToken.None); }
    catch (ArgumentException) { argRejected = true; }
    Check(argRejected, "A wrong-typed argument was accepted.");

    argRejected = false;
    try { await argBroker.ExecuteAsync("validate_args", new JsonObject { ["text"] = "ok", ["mode"] = "c" }, CancellationToken.None); }
    catch (ArgumentException) { argRejected = true; }
    Check(argRejected, "An out-of-enum argument was accepted.");

    argRejected = false;
    try { await argBroker.ExecuteAsync("validate_args", new JsonObject { ["text"] = "toolong" }, CancellationToken.None); }
    catch (ArgumentException) { argRejected = true; }
    Check(argRejected, "An over-length argument was accepted.");

    var bigTool = new TestTool
    {
        Name = "big_result",
        Description = "Returns an oversized result.",
        ParametersSchemaJson = """{"type":"object","properties":{},"additionalProperties":false}""",
        Handler = _ => new JsonObject { ["blob"] = new string('x', 9000) }
    };
    var oversized = false;
    try { await new ToolBroker(new ToolCatalog([bigTool])).ExecuteAsync("big_result", new JsonObject(), CancellationToken.None); }
    catch (InvalidOperationException) { oversized = true; }
    Check(oversized, "An oversized tool result was not rejected.");

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

    // Write-note edge case: unknown person must not mutate.
    var zeroMatch = await new ToolBroker(noteCatalog, new AllowApproval()).ExecuteAsync(noteTool.Name,
        new JsonObject { ["person_name"] = "Nobody Here", ["note"] = "n" }, CancellationToken.None);
    Check(notes.Count == 1 && zeroMatch["result"]?["display_text"]?.GetValue<string>()!.Contains("could not find", StringComparison.Ordinal) == true,
        "A write note for an unknown person mutated state or reported success.");

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

sealed class TestWriteTool : IApplicationTool
{
    public int CallCount { get; private set; }
    public string Name => "test_write";
    public string Description => "Test-only write operation.";
    public string ParametersSchemaJson => """
        {"type":"object","properties":{"value":{"type":"string","maxLength":10}},"required":["value"],"additionalProperties":false}
        """;
    public ToolAccess Access => ToolAccess.RequiresApproval;
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        CallCount++;
        return ValueTask.FromResult<JsonNode?>(new JsonObject { ["changed"] = true });
    }
}

sealed class AllowApproval : IToolApprovalService
{
    public ValueTask<bool> ApproveAsync(ToolActionProposal proposal, CancellationToken cancellationToken)
        => ValueTask.FromResult(true);
}

sealed class BadOutputSchemaTool : IApplicationTool
{
    public string Name => "bad_output";
    public string Description => "Test tool with a malformed output schema.";
    public string ParametersSchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";
    public ToolAccess Access => ToolAccess.ReadOnly;
    public string? OutputSchemaJson => "42";
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<JsonNode?>(null);
}

sealed class BadNameTool : IApplicationTool
{
    public string Name => "BadName";
    public string Description => "Tool with an invalid name.";
    public string ParametersSchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";
    public ToolAccess Access => ToolAccess.ReadOnly;
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<JsonNode?>(null);
}

sealed class BadInputSchemaTool : IApplicationTool
{
    public string Name => "bad_input";
    public string Description => "Tool with a non-object input schema.";
    public string ParametersSchemaJson => """{"type":"string"}""";
    public ToolAccess Access => ToolAccess.ReadOnly;
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<JsonNode?>(null);
}

sealed class BadExampleTool : IApplicationTool
{
    public string Name => "bad_example";
    public string Description => "Tool with an invalid usage example.";
    public string ParametersSchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";
    public ToolAccess Access => ToolAccess.ReadOnly;
    public IReadOnlyList<ToolUsageExample> Examples => new[] { new ToolUsageExample("", new JsonObject()) };
    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<JsonNode?>(null);
}

sealed class TestTool : IApplicationTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ParametersSchemaJson { get; init; }
    public ToolAccess Access { get; init; } = ToolAccess.ReadOnly;
    public string? OutputSchemaJson { get; init; }
    public IReadOnlyList<ToolUsageExample> Examples { get; init; } = Array.Empty<ToolUsageExample>();
    public Func<JsonObject, JsonNode?>? Handler { get; init; }

    public ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<JsonNode?>(Handler?.Invoke(arguments));
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
