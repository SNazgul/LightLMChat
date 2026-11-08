# LightLMChat

LightLMChat is an early-stage, embeddable **offline chat component for .NET desktop applications**. It combines a WPF chat control with a managed SDK that will let users request a small set of application actions in natural language. The host application supplies and authorizes those actions through a defined tool interface; the model never receives unrestricted access to application data or the operating system.

## Design

- **UI:** reusable .NET 10 WPF chat control with streamed responses, cancellation, activity display, and a clear-chat action.
- **SDK:** WPF-independent .NET DLLs for chat sessions, tool contracts, catalog validation, and runtime management.
- **Inference:** a managed launcher for `llama-server`, bound to localhost for offline operation. Engine binaries will be packaged later.
- **Models:** IBM Granite 4.0 350M Instruct (default, ~378 MB) and Qwen2.5 1.5B Instruct (~1.9 GB), both GGUF Q8_0. Pick one with `prepare-runtime.ps1 -Model`.
- **Host integration:** applications register a small catalog of typed read and write operations, each declaring its purpose, input/output JSON schemas, and usage examples. The model selects and calls them dynamically; the SDK validates every proposed call against the schema. The host controls permissions, executes tools, and approves writes.

The first release targets **Windows x64 PCs with 8 GB RAM and a modern 4-core CPU**. A dedicated GPU is not required. CPU inference is the baseline; integrated GPU acceleration is optional and will be enabled only when measurements show a benefit.

## Intended use and limits

LightLMChat is intended for defined, primarily single-step tasks such as finding a record, showing its details, or proposing a field update. Small local models (350M–1.5B parameters) may not reliably handle open-ended conversation or complex multi-step instructions. The first implementation phase therefore measures tool and argument accuracy against representative host operations before release. Unclear requests should result in clarification, and writes require explicit approval.

The runtime is designed to work after installation with the network disconnected. No cloud inference or runtime model download is required. Model and engine binaries are pinned and verified for packaging; the model file is kept out of ordinary Git history.

## Project status

**Embeddable local chat prototype.** The kit contains the host tool contract, a WPF chat control, a `llama-server` process launcher, an OpenAI-compatible streaming chat client, and a broker that validates proposed tool calls before dispatch, with per-request limits on tool calls, generation length, and elapsed time. The [`LightLMChat.ExampleApp`](samples/LightLMChat.ExampleApp) sample registers its own tools for a read-only IBM PC parts catalog (286–486) and embeds the chat control. Broader quality and minimum-hardware measurements are still needed, and the offline distribution bundle is not yet packaged. See the [integration guide](docs/INTEGRATION_GUIDE.md), [SRS](docs/SRS.md), and [implementation plan](docs/IMPLEMENTATION_PLAN.md).

## Getting started

### Prerequisites

- Windows x64.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Internet access once, to download the engine and a model. The app itself works offline afterward.

### Option A — set up by script (recommended)

`scripts/prepare-runtime.ps1` downloads the pinned llama.cpp engine and a model, verifies their SHA-256 hashes against [the manifest](docs/model-and-runtime-manifest.json), and writes the chosen paths into `lightlmchat.config.json` so the app uses that model. Two models are available (`-Model`, default `granite-350m`):

```powershell
.\scripts\prepare-runtime.ps1                      # IBM Granite 4.0 350M (~378 MB)
.\scripts\prepare-runtime.ps1 -Model qwen2.5-1.5b  # Qwen2.5 1.5B (~1.9 GB), smarter
```

### Option B — set up manually

1. Download a Windows CPU `llama-server` build from [llama.cpp releases](https://github.com/ggml-org/llama.cpp/releases) and unpack it.
2. Download a GGUF model, e.g. [Granite 4.0 350M Q8_0](https://huggingface.co/ibm-granite/granite-4.0-350m-GGUF) or [Qwen2.5 1.5B Q8_0](https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF).
3. Put their paths in `samples/LightLMChat.ExampleApp/lightlmchat.config.json`:

```json
{ "serverPath": "C:\\path\\to\\llama-server.exe", "modelPath": "C:\\path\\to\\model.gguf" }
```

Leaving either value empty (`""`) makes the app auto-discover it: a `runtime/` folder next to the app, or the repo's `models/` and `artifacts/` folders.

### Run the example

```powershell
dotnet run --project samples/LightLMChat.ExampleApp
```

It opens a read-only browser of IBM PC parts (286–486 era: CPUs, motherboards, memory, drives, displays, mice, keyboards, and more) with the AI chat control beside it. The catalog works even without a model; the app and chat control show a clear message when no model is found. Try asking: “What Intel 486 CPUs do you have?” or “Show me VGA monitors.”

### Build and test

```powershell
dotnet build LightLMChat.sln
dotnet test                                      # unit tests (no model needed)
dotnet run --project tests/LightLMChat.RuntimeChecks   # model-free integration checks
```

To assemble a single offline folder (app + engine + model + licenses) that runs by double-click, see [packaging/README.md](packaging/README.md) and run `.\scripts\pack.ps1 -Zip`.

### The launcher

The launcher uses CPU inference, a 2048-token context, a random local API key, and an available `127.0.0.1` port; if GPU initialization fails it falls back to CPU. The application itself does not download model or engine files at runtime.

## Licensing

LightLMChat source code and documentation are licensed under [MIT](LICENSE). The selected [IBM Granite model](https://huggingface.co/ibm-granite/granite-4.0-350m) remains under Apache 2.0, and [llama.cpp](https://github.com/ggml-org/llama.cpp) remains under MIT. Model weights and engine binaries are not included in this repository; future distribution packages will include their required license and attribution notices. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

