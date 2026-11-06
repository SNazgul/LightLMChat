namespace LightLMChat.Core;

/// <summary>Per-request bounds enforced by <see cref="LlamaChatSession"/>.</summary>
/// <remarks>
/// These caps protect the host from runaway inference or tool usage (FR-08).
/// <see cref="MaxElapsed"/> covers the whole request after the session gate is
/// acquired, including tool selection, host tool execution, and generation.
/// </remarks>
public sealed record ChatRequestLimits
{
    /// <summary>Maximum sequential tool calls the model may propose in one turn. Default 1.</summary>
    public int MaxToolCalls { get; init; } = 1;

    /// <summary>Maximum tokens for the final text completion. Default 256.</summary>
    public int MaxGenerationTokens { get; init; } = 256;

    /// <summary>Optional wall-clock limit for one request. Null means no deadline.</summary>
    public TimeSpan? MaxElapsed { get; init; }

    internal void Validate()
    {
        if (MaxToolCalls is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(MaxToolCalls), "MaxToolCalls must be between 1 and 8.");
        if (MaxGenerationTokens is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(MaxGenerationTokens), "MaxGenerationTokens must be between 1 and 4096.");
        if (MaxElapsed is { } elapsed && elapsed <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxElapsed), "MaxElapsed must be positive when set.");
    }
}
