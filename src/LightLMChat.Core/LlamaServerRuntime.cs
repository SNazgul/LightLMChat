using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace LightLMChat.Core;

/// <summary>Owns one loopback-only llama-server child process.</summary>
public sealed class LlamaServerRuntime : IAsyncDisposable
{
    private readonly Process _process;
    private readonly HttpClient _client;
    private readonly Queue<string> _recentErrors = new();
    private bool _started;
    private bool _disposed;

    private LlamaServerRuntime(Process process, HttpClient client, Uri baseAddress, string apiKey, int gpuLayers)
    {
        _process = process;
        _client = client;
        BaseAddress = baseAddress;
        ApiKey = apiKey;
        EffectiveGpuLayers = gpuLayers;
    }

    public Uri BaseAddress { get; }
    public int ProcessId { get; private set; }
    /// <summary>GPU layers actually in use; 0 means CPU inference, possibly after a fallback.</summary>
    public int EffectiveGpuLayers { get; }
    internal string ApiKey { get; }
    internal HttpClient Client => _client;

    public static async Task<LlamaServerRuntime> StartAsync(
        LlamaServerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (options.GpuLayers > 0)
        {
            try
            {
                return await StartCoreAsync(options, options.GpuLayers, cancellationToken);
            }
            catch (Exception ex) when ((ex is InvalidOperationException or TimeoutException) &&
                                       !cancellationToken.IsCancellationRequested)
            {
                // GPU initialization failed; retry once on the CPU baseline (FR-09).
                return await StartCoreAsync(options, 0, cancellationToken);
            }
        }
        return await StartCoreAsync(options, options.GpuLayers, cancellationToken);
    }

    private static async Task<LlamaServerRuntime> StartCoreAsync(
        LlamaServerOptions options, int gpuLayers, CancellationToken cancellationToken)
    {
        var port = FindAvailableLoopbackPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}/");
        var apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var executable = Path.GetFullPath(options.ExecutablePath);
        var model = Path.GetFullPath(options.ModelPath);

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "--model", model,
            "--host", "127.0.0.1",
            "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ctx-size", options.ContextSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--gpu-layers", gpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--alias", "lightlmchat",
            "--jinja",
            "--no-webui"
        }) start.ArgumentList.Add(argument);
        if (options.ReasoningBudget is { } reasoningBudget)
        {
            start.ArgumentList.Add("--reasoning-budget");
            start.ArgumentList.Add(reasoningBudget.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        start.Environment["LLAMA_API_KEY"] = apiKey;

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = baseAddress,
            Timeout = Timeout.InfiniteTimeSpan
        };
        var runtime = new LlamaServerRuntime(process, client, baseAddress, apiKey, gpuLayers);
        process.OutputDataReceived += static (_, _) => { };
        process.ErrorDataReceived += (_, e) => runtime.RecordError(e.Data);

        try
        {
            if (!process.Start()) throw new InvalidOperationException("llama-server did not start.");
            runtime._started = true;
            runtime.ProcessId = process.Id;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await runtime.WaitForReadyAsync(options.StartupTimeout ?? TimeSpan.FromMinutes(2), cancellationToken);
            return runtime;
        }
        catch
        {
            await runtime.DisposeAsync();
            throw;
        }
    }

    private async Task WaitForReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (_process.HasExited)
                    throw new InvalidOperationException(
                        $"llama-server exited during startup (code {_process.ExitCode}). {RecentErrors()}");

                try
                {
                    using var response = await _client.GetAsync("health", deadline.Token);
                    if (response.StatusCode == HttpStatusCode.OK) return;
                }
                catch (HttpRequestException)
                {
                    // The server has not opened its listener yet.
                }

                await Task.Delay(250, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"llama-server was not ready within {timeout}. {RecentErrors()}");
        }
    }

    private void RecordError(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_recentErrors)
        {
            if (_recentErrors.Count == 8) _recentErrors.Dequeue();
            _recentErrors.Enqueue(line[..Math.Min(line.Length, 300)]);
        }
    }

    private string RecentErrors()
    {
        lock (_recentErrors) return string.Join(" | ", _recentErrors);
    }

    private static int FindAvailableLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();

        if (_started && !_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* The process exited meanwhile. */ }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { }
        }
        _process.Dispose();
    }
}
