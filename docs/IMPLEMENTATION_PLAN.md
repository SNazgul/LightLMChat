# Local AI Chat Component — Implementation Plan

Version: 0.1 (draft)  
Date: 2026-11-06  
Basis: [SRS.md](SRS.md)

## Delivery approach

Build a narrow vertical slice first: one read tool, one write tool, a single chat session, and CPU inference. Add packaging and GPU selection after the end-to-end behavior is reliable. Every phase has a concrete exit condition.

| Phase | Work | Exit condition |
| --- | --- | --- |
| 0. Tool contract and evaluation set | Obtain the host's first 3–5 operations. Define schemas, examples, permission rules, and a held-out set of requests. Measure Granite 350M Q8_0 on the reference PC for tool choice, argument extraction, latency, and RAM. | A written compatibility and quality report; go/no-go for the 350M baseline. |
| 1. Solution skeleton | Create `LightLMChat.sln`, `LightLMChat.Abstractions`, `LightLMChat.Core`, `LightLMChat.Wpf`, `LightLMChat.SampleHost`, and runtime checks; set .NET 10 and CI build. | Clean build on Windows x64; sample host loads control. **Done.** |
| 2. Engine lifecycle | Pin llama.cpp version and model hash. Launch local server on an available loopback port with a session key; wait for health, stream completions, cancel, stop, and recover from failure. | CPU local prompt and three family questions verified. Cleanup and cancellation pass protocol checks. Broader fault and hardware testing remain. |
| 3. Tool broker | Register host tools; provide schemas to model; parse and validate calls; enforce authorization, limits, approval, and bounded results. | Family tree read and write prototypes, broker checks, and a sample approve/deny write flow are in place. A real API adapter and real-model validation remain. |
| 4. WPF control | Implement message list, compose box, streaming text, tool activity, explicit write approval, cancel, errors, keyboard access, and host theming. | Message list, compose box, streaming, tool activity, cancel, and a modal write-approval dialog are in the sample host. Host theming polish and accessibility review remain. |
| 5. Runtime selection and packaging | Measure CPU and Vulkan on supported hardware, choose the faster stable backend, package runtime/model/licenses, and document fully offline installation. | Installer works without network on the minimum PC; fallback works. |
| 6. Release hardening | Run held-out task evaluation, resource measurements, offline/network checks, fault injection, and integration guide review. | SRS acceptance evidence recorded and release gates met. |

## Proposed repository layout

```text
LightLMChat/
  docs/
    SRS.md
    IMPLEMENTATION_PLAN.md
    model-and-runtime-manifest.json
  src/
    LightLMChat.Abstractions/
    LightLMChat.Core/
    LightLMChat.FamilyTree/
    LightLMChat.Wpf/
    LightLMChat.SampleHost/
  tests/
    LightLMChat.RuntimeChecks/
  packaging/
  LICENSES/
```

Do not commit the 378 MB model to ordinary Git history. Record its source revision and SHA-256 in the manifest; package verified bytes with the installer or a separate offline runtime bundle. CI can build and test the managed solution without downloading the model, while a release job uses the pinned artifact.

## Initial technical choices

- Use `HttpClient` against the bundled `llama-server` chat API. The current `LlamaChatSession` implements text streaming; tool calls will extend this path in Phase 3.
- Restrict the first model prompt to a small tool catalog and short conversation context. Prefer one tool call per turn; ask for clarification when required arguments are missing.
- Treat assistant output as a proposal. Only `IApplicationTool` implementations can access host data.
- Keep the engine process outside the WPF UI process. Stream and cancellation use async APIs; no blocking work on the dispatcher.
- Run a CPU reference build first. GPU support is an optimization informed by measured throughput, initialization time, and memory use.

## Main risks and responses

| Risk | Response |
| --- | --- |
| A 350M model misidentifies an action or arguments. | Phase 0 benchmark; small tool catalog; schema checks; clarification; approval before writes. If quality is insufficient, keep the same API and offer a larger optional model. |
| Model-specific tool format differs from server parsing. | Pin model and server versions; verify their chat template and run real tool-call integration tests. |
| Integrated GPU is slower or unstable. | CPU remains the baseline; enable GPU only after hardware-specific measurement. |
| Host application differs from the sample host. | Keep WPF control, core, and tool contracts separate; verify integration against one real host early. |
| Installer size exceeds the model's stated size. | Report model and full bundle sizes separately; keep optional GPU binaries in separate packages if useful. |

## Immediate next action

Map the real family tree API's `find_people`, `get_person`, and `get_relatives` responses to `IFamilyTreeApi`, then run Phase 0 with representative names, ambiguous matches, and missing facts. The other reported API operations can follow after this first read-only workflow is measured.

