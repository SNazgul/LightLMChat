using System.Runtime.CompilerServices;
using LightLMChat.Abstractions;

namespace LightLMChat.SampleHost;

internal sealed class ScriptedChatSession : IChatSession
{
    public async IAsyncEnumerable<ChatEvent> SendAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new StatusEvent("Generating demo response…");
        var reply = $"This is a scripted UI preview. You asked: {userMessage}\n\nThe local model and application tools will be connected in the next implementation slices.";
        foreach (var word in reply.Split(' '))
        {
            await Task.Delay(65, cancellationToken);
            yield return new TextDeltaEvent(word + " ");
        }
    }
}
