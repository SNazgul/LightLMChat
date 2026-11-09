# LightLMChat Integration Guide

LightLMChat is an embeddable, fully local chat component for .NET desktop applications. A host
application (1) implements one `IApplicationTool` per operation it wants the local model to be
able to reach, (2) registers them in a `ToolCatalog`, (3) starts the local inference runtime, and
(4) hands a `LlamaChatSession` to the WPF control — or drives the session headlessly.

The model never gets direct access to application data or the operating system. It can only
*propose* calls to the operations the host registered, and every proposal is validated against the
declared schema before it runs. Writes additionally require explicit host approval.

The PC-parts catalog example is one concrete instance of this pattern; the contract itself is generic.

## 1. Declare an operation

Implement `IApplicationTool` once per operation. `Description` is the operation's purpose as the
model sees it; `ParametersSchemaJson` is a JSON Schema describing the arguments; `Access` classifies
the operation as read-only or approval-required.

```csharp
using System.Text.Json.Nodes;
using LightLMChat.Abstractions;

public sealed class LookupContactTool(IContactStore contacts) : IApplicationTool
{
    public string Name => "lookup_contact";
    public string Description => "Find a contact by full name and return their phone and email.";
    public string ParametersSchemaJson => """
        {"type":"object","properties":{"name":{"type":"string","maxLength":120}},"required":["name"],"additionalProperties":false}
        """;
    public ToolAccess Access => ToolAccess.ReadOnly;

    // Optional: helps the small model map a natural-language request to arguments.
    public IReadOnlyList<ToolUsageExample> Examples => new[]
    {
        new ToolUsageExample("What is Alice's phone number?", new JsonObject { ["name"] = "Alice" })
    };

    public async ValueTask<JsonNode?> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var contact = await contacts.FindAsync(arguments["name"]!.GetValue<string>(), cancellationToken);
        if (contact is null)
            return new JsonObject { ["display_text"] = "No matching contact was found." };

        // By default, display_text is shown verbatim so a small model cannot rewrite the data.
        return new JsonObject
        {
            ["display_text"] = $"{contact.Name}: {contact.Phone}, {contact.Email}",
            ["phone"] = contact.Phone,
            ["email"] = contact.Email
        };
    }
}
```

A write operation is identical except `Access` is `RequiresApproval`; the broker will call the host
approval service before `ExecuteAsync` runs.

## 2. Register the catalog

`ToolCatalog` validates names, schemas, and access classes, and rejects duplicates.

```csharp
using LightLMChat.Core;

var catalog = new ToolCatalog([
    new LookupContactTool(contacts),
    new AddContactNoteTool(contacts)   // Access = RequiresApproval
]);
```

## 3. Start the runtime and create the session

```csharp
using LightLMChat.Core;

var runtime = await LlamaServerRuntime.StartAsync(new LlamaServerOptions(
    ExecutablePath: @"C:\runtime\llama-server.exe",
    ModelPath:      @"C:\runtime\granite-4.0-350m-Q8_0.gguf"),
    cancellationToken);

// approval is an IToolApprovalService that shows the host's approve/deny UI.
var session = new LlamaChatSession(runtime, catalog, approval);
```

`LlamaServerRuntime` binds only to `127.0.0.1` on a free port with a random per-session API key,
disables the engine's web UI, and falls back to CPU inference if GPU initialization fails. Dispose
it when the host session ends:

```csharp
await runtime.DisposeAsync();
```

## 4. Drive the session headlessly

```csharp
using LightLMChat.Abstractions;

await foreach (var update in session.SendAsync("What is Alice's phone number?", cancellationToken))
{
    switch (update)
    {
        case TextDeltaEvent delta:       Console.Write(delta.Text); break;
        case StatusEvent status:         Console.WriteLine($"[{status.Text}]"); break;
        case ToolActivityEvent tool:     Console.WriteLine($"[{tool.ToolName}: {tool.Text}]"); break;
    }
}
```

## 5. Approve writes

Implement `IToolApprovalService` in the host. It receives the exact action and arguments and decides
whether it may run; the broker aborts the call with `status: "denied"` when it returns `false`.

```csharp
using LightLMChat.Abstractions;

public sealed class ApprovalService(Window owner) : IToolApprovalService
{
    public ValueTask<bool> ApproveAsync(ToolActionProposal proposal, CancellationToken cancellationToken)
    {
        // Show a modal dialog listing proposal.ToolName and proposal.Arguments (Approve/Deny).
        return ValueTask.FromResult(ShowDialog(proposal));
    }
}
```

The host can also implement the optional `IToolProposalNormalizer` on a tool to ground model-argued
values in the current user request before validation.

## 6. Embed the WPF control

```xml
<Window xmlns:chat="clr-namespace:LightLMChat.Wpf;assembly=LightLMChat.Wpf">
    <chat:LightLmChatControl x:Name="Chat" />
</Window>
```

```csharp
Chat.Session = session;   // any IChatSession
```

The control renders streamed text, tool activity, busy/error state, cancel, and a clear-chat action.
Call `session.Reset()` (or the control's **Clear chat**) to start a new conversation.

## 7. Thinking models

The example app enables thinking automatically when the selected filename starts with `Qwen3-` or
`SmolLM3-`. To opt in from another host, use a bounded reasoning budget and a larger completion
limit. The model file and `llama-server` must support thinking mode.

```csharp
var runtime = await LlamaServerRuntime.StartAsync(new LlamaServerOptions(
    serverPath, modelPath, ContextSize: 4096, ReasoningBudget: 256));
var session = new LlamaChatSession(runtime, catalog, approval,
    limits: new ChatRequestLimits { MaxGenerationTokens = 1024 },
    enableThinking: true);
```

`ReasoningBudget` caps thinking tokens in `llama-server`. Without a cap, a small model can use
the whole completion allowance on its internal reasoning and return no answer. When thinking is
enabled, the session uses Qwen3's recommended sampling values. The default keeps thinking off
for models that do not support it.

Tool `display_text` is shown directly by default. This protects exact application data from
being rewritten by a small model. Hosts with a capable model can opt into
`reasonAboutToolResults: true` to have the model compose an answer from structured tool results;
validate that behavior on real host data before enabling it.

## 8. Limits

`LlamaChatSession` accepts optional `ChatRequestLimits` to bound each request: `MaxToolCalls`
(sequential tool calls, default 1), `MaxGenerationTokens` (default 256), and `MaxElapsed` (optional
wall-clock deadline). A deadline breach surfaces as `TimeoutException`.

## Security model at a glance

- The model sees only the current request, a bounded slice of history, and the session's tools.
- Proposed calls are checked against the registered name and JSON schema; unknown/invalid calls are
  rejected before they reach host code.
- `RequiresApproval` operations need an explicit host "approve"; no mutation runs on denial.
- Tool results are treated as data, never as instructions to the model.
- The engine runs on loopback with a per-session secret; no cloud inference or runtime download.

See [SRS.md](SRS.md) for the full requirements and acceptance criteria.
