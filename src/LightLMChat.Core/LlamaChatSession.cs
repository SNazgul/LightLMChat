using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LightLMChat.Abstractions;

namespace LightLMChat.Core;

/// <summary>Text chat session backed by a running local llama-server.</summary>
public sealed class LlamaChatSession : IChatSession
{
    private const string SystemPrompt = "You are a concise assistant inside a desktop application. Answer only from information supplied in this conversation. If you cannot perform an application action, say so clearly.";
    private readonly LlamaServerRuntime _runtime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<(string Role, string Content)> _history = [];

    public LlamaChatSession(LlamaServerRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public async IAsyncEnumerable<ChatEvent> SendAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) yield break;
        await _gate.WaitAsync(cancellationToken);
        var answer = new StringBuilder();
        var completed = false;

        try
        {
            yield return new StatusEvent("Generating locally…");

            var messages = new List<object> { new { role = "system", content = SystemPrompt } };
            foreach (var (role, content) in _history.TakeLast(12))
                messages.Add(new { role, content });
            messages.Add(new { role = "user", content = userMessage });

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    model = "lightlmchat",
                    messages,
                    stream = true,
                    max_tokens = 256,
                    temperature = 0.2
                }), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _runtime.ApiKey);

            using var response = await _runtime.Client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Local inference failed ({(int)response.StatusCode}): {error[..Math.Min(error.Length, 500)]}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                var data = line[6..];
                if (data == "[DONE]")
                {
                    completed = true;
                    break;
                }

                using var chunk = JsonDocument.Parse(data);
                if (!chunk.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                var delta = choices[0].GetProperty("delta");
                if (!delta.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) continue;
                var text = content.GetString();
                if (string.IsNullOrEmpty(text)) continue;
                answer.Append(text);
                yield return new TextDeltaEvent(text);
            }

            if (!completed) throw new IOException("The local inference stream ended unexpectedly.");
            _history.Add(("user", userMessage));
            _history.Add(("assistant", answer.ToString()));
            if (_history.Count > 12) _history.RemoveRange(0, _history.Count - 12);
        }
        finally
        {
            _gate.Release();
        }
    }
}
