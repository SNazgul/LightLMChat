namespace LightLMChat.Core;

/// <summary>Paths and bounded resource settings for a local llama-server process.</summary>
public sealed record LlamaServerOptions(
    string ExecutablePath,
    string ModelPath,
    int ContextSize = 2048,
    int GpuLayers = 0,
    TimeSpan? StartupTimeout = null,
    int? ReasoningBudget = null)
{
    internal void Validate()
    {
        if (!File.Exists(ExecutablePath))
            throw new FileNotFoundException("llama-server executable was not found.", ExecutablePath);
        if (!File.Exists(ModelPath))
            throw new FileNotFoundException("GGUF model was not found.", ModelPath);
        if (ContextSize is < 512 or > 32768)
            throw new ArgumentOutOfRangeException(nameof(ContextSize));
        if (GpuLayers < 0)
            throw new ArgumentOutOfRangeException(nameof(GpuLayers));
        if (StartupTimeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StartupTimeout));
        if (ReasoningBudget is < 0 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(ReasoningBudget));
    }
}
