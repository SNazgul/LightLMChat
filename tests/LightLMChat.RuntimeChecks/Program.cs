using System.Diagnostics;
using System.Net;
using System.Text.Json;
using LightLMChat.Abstractions;
using LightLMChat.Core;

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

    var childId = runtime.ProcessId;
    await runtime.DisposeAsync();
    try
    {
        using var child = Process.GetProcessById(childId);
        Check(child.HasExited, "The child server is still running after disposal.");
    }
    catch (ArgumentException) { /* The process has exited and disappeared. */ }

    Console.WriteLine("Runtime checks passed: startup, streaming, cancellation, recovery, shutdown.");
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
        var latest = messages[messages.GetArrayLength() - 1].GetProperty("content").GetString();
        context.Response.ContentType = "text/event-stream";
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
