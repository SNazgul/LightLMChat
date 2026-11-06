# Local AI Chat Component — Software Requirements Specification

Version: 0.1 (draft)  
Date: 2026-11-06  
Status: Architecture agreed; host application operations pending

## 1. Purpose and scope

Build a reusable, fully local AI component for Windows desktop applications. A WPF chat control accepts natural-language requests. A managed DLL interprets requests and, through a narrow interface supplied by the host application, reads application data or proposes an application action. The host application remains the authority for permissions, data access, and mutations.

The first release supports a small, predefined set of single-step operations. Open-ended general assistant behavior, arbitrary code execution, and autonomous multi-step workflows are outside the first release.

## 2. Baseline decisions

| Area | Decision |
| --- | --- |
| UI and SDK | .NET 10, Windows x64; WPF control plus WPF-independent managed core DLL |
| Inference | Bundled llama.cpp `llama-server`, launched as a child process on loopback |
| Model | IBM Granite 4.0 350M Instruct, GGUF Q8_0, approximately 378 MB on disk |
| License | LightLMChat source and documentation: MIT; model: Apache 2.0; engine: MIT; distribute required notices |
| Minimum target | Windows x64, 8 GB RAM, modern 4-core CPU; integrated GPU optional |
| Execution | CPU first; optional Vulkan backend only after measurement shows a benefit |
| Connectivity | No cloud inference or runtime model download; operate with network disconnected |

The 378 MB figure is only the model file size. Installed size and runtime memory include the engine, context cache, application, and dependencies.

## 3. System components

1. `LocalAi.Abstractions`: public contracts for host tools, conversation events, approvals, and configuration.
2. `LocalAi.Core`: session orchestration, prompt and tool catalog construction, inference transport, tool-call validation, process lifecycle, and diagnostics.
3. `LocalAi.Wpf`: embeddable chat `UserControl` and view model; no direct access to host data.
4. `LocalAi.Runtime`: pinned llama.cpp binaries, a pinned model artifact, checksums, and license notices. These are packaged separately from source control as appropriate.
5. `LocalAi.SampleHost`: a small WPF sample application implementing representative read and write tools.

The public managed API must not expose llama.cpp-specific types. Host applications should be able to supply another inference provider in a future version without changing their tool implementations.

## 4. Functional requirements

| ID | Requirement | Acceptance evidence |
| --- | --- | --- |
| FR-01 | The WPF control can be embedded in a .NET 10 WPF host without modifying engine code. | Sample host references the control and runs. |
| FR-02 | The control shows user messages, streamed assistant output, busy/error state, cancel, and tool activity. | Interactive sample demonstration. |
| FR-03 | The host registers a bounded catalog of named tools. Each tool declares a description, JSON input schema, read/write classification, and async executor. | Sample read and write tools execute through the public interface. |
| FR-04 | The model receives only the current request, bounded conversation context, and the tools the host makes available for that session. | Request inspection test. |
| FR-05 | The core validates the proposed tool name and arguments against the registered schema before dispatch. Unknown or invalid calls are rejected and can trigger clarification. | Invalid-call tests. |
| FR-06 | A write tool requires explicit host approval for its exact action and arguments before execution. The host can deny the action. | Denial and approval tests; no mutation on denial. |
| FR-07 | The host executes tools under its own authorization and data rules, returning bounded results to the core. | Sample host verifies data filtering and result size limits. |
| FR-08 | The core enforces per-request limits on tool calls, generation length, elapsed time, and cancellation. | Limit and cancellation tests. |
| FR-09 | The runtime starts and stops with the host session, reports load failures, and supports CPU fallback when GPU initialization fails. | Startup, failure, and fallback tests. |
| FR-10 | The application works after installation with the network disconnected. The model and engine never upload application data. | Offline end-to-end test and network inspection. |
| FR-11 | The installer or runtime bundle identifies exact model and engine versions, hashes, and licenses. | Package inspection. |

## 5. Host integration contract

The initial contract is conceptually:

```csharp
public interface ILocalAiTool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }
    bool IsReadOnly { get; }
    Task<JsonElement> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}
```

The final interface will additionally provide a host approval callback, per-user authorization context, and result/error types. The core never calls host business objects directly. A host adapter handles any WPF dispatcher access. Tool results are treated as untrusted data, not as instructions to the model.

## 6. Quality and operational requirements

| ID | Requirement |
| --- | --- |
| NFR-01 | The process binds to `127.0.0.1` only, uses a per-session secret, and disables the engine's built-in web UI and unrelated tool/file capabilities. |
| NFR-02 | No arbitrary shell, filesystem, database, or network operation is exposed to the model. Only registered host tools can act on application data. |
| NFR-03 | Chat history is kept in memory by default. Persistence, if requested by a host, is opt-in and owned by that host. |
| NFR-04 | Model, engine, and application logs exclude sensitive prompt and tool-result contents by default. |
| NFR-05 | The baseline runs on an 8 GB RAM PC without a dedicated GPU alongside the sample host. Peak memory and response latency are measured on a named reference PC before release. |
| NFR-06 | The API is documented with an integration example and versioned contracts. |
| NFR-07 | The WPF control supports keyboard use, accessible labels, and cancellation of generation. |

## 7. Release validation

Before declaring the minimum tier usable, create a held-out test set with representative user phrasing, ambiguous requests, invalid identifiers, and requests for disallowed actions. Report exact tool/argument accuracy, clarification rate, unsafe execution count, latency, and peak RAM. Release requires zero unapproved writes and zero execution of unregistered tools. A numeric accuracy target will be set after the host's first operations and the baseline benchmark are known; tiny-model capability must be demonstrated rather than assumed.

## 8. Inputs needed from the host application

1. The host application's target .NET version and Windows support policy.
2. The first 3–5 operations, with example user requests, argument types, permission rules, and expected results.
3. Which operations mutate data and the host's approval/undo mechanism.
4. Representative minimum-spec PC and acceptable response time.
5. Whether the model must handle languages other than English.

## 9. References

- IBM model and tool-call format: https://huggingface.co/ibm-granite/granite-4.0-350m
- IBM GGUF sizes and license: https://huggingface.co/ibm-granite/granite-4.0-350m-GGUF
- llama.cpp server and tool calls: https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md
- llama.cpp MIT license: https://github.com/ggml-org/llama.cpp/blob/master/LICENSE

