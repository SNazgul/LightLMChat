using System.Text.Json.Nodes;

namespace LightLMChat.Abstractions;

/// <summary>The host decides whether a proposed mutation may run.</summary>
public interface IToolApprovalService
{
    ValueTask<bool> ApproveAsync(ToolActionProposal proposal, CancellationToken cancellationToken);
}

public sealed record ToolActionProposal(string ToolName, JsonObject Arguments);
