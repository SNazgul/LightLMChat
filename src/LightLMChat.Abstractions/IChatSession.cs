namespace LightLMChat.Abstractions;

/// <summary>A conversation owned by the core or supplied by a test host.</summary>
public interface IChatSession
{
    IAsyncEnumerable<ChatEvent> SendAsync(string userMessage, CancellationToken cancellationToken);

    /// <summary>Starts a new conversation by dropping history. Default no-op for stateless sessions.</summary>
    void Reset() { }
}

public abstract record ChatEvent;
public sealed record TextDeltaEvent(string Text) : ChatEvent;
public sealed record StatusEvent(string Text) : ChatEvent;
public sealed record ToolActivityEvent(string ToolName, string Text) : ChatEvent;
