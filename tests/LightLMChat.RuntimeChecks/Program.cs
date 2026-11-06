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

    var familySession = new LlamaChatSession(runtime, catalog);
    output.Clear();
    await foreach (var chatEvent in familySession.SendAsync(
        "When was Mary Example born and who are her children?", CancellationToken.None))
        if (chatEvent is TextDeltaEvent delta) output.Append(delta.Text);
    Check(output.ToString() == "Mary Example was born 14 May 1908 in Bristol. Recorded children: Anna Example, Peter Example.",
        "Model tool-call flow did not return the family answer.");

    var childId = runtime.ProcessId;
    await runtime.DisposeAsync();
    try
    {
        using var child = Process.GetProcessById(childId);
        Check(child.HasExited, "The child server is still running after disposal.");
    }
    catch (ArgumentException) { /* The process has exited and disappeared. */ }

    Console.WriteLine("Runtime checks passed: streaming, cancellation, family API, tool validation, write approval, tool-call flow, shutdown.");
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
            if (tools[0].GetProperty("function").GetProperty("name").GetString() != "get_person_family_summary")
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

        var latest = last.GetProperty("content").GetString();
        context.Response.ContentType = "text/event-stream";
        if (last.GetProperty("role").GetString() == "tool")
        {
            if (!latest!.Contains("Anna Example", StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }
            await WriteChunkAsync(context, "Born: 14 May 1908; children: ");
            await WriteChunkAsync(context, "Anna Example, Peter Example");
            await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
            return;
        }
        await WriteChunkAsync(context, "Hello ");
        if (latest == "test cancellation") await Task.Delay(5000, context.RequestAborted);
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
