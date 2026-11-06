using System.Text.Json.Nodes;

namespace LightLMChat.Abstractions;

/// <summary>Optional host rule for grounding model arguments in the current user request.</summary>
public interface IToolProposalNormalizer
{
    /// <returns>Safe arguments to validate and execute, or null to reject the proposal.</returns>
    JsonObject? NormalizeProposal(string userMessage, JsonObject proposedArguments);
}
