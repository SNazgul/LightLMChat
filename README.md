# LightLMChat

LightLMChat is an early-stage, embeddable **offline chat component for .NET desktop applications**. It combines a WPF chat control with a managed SDK that will let users request a small set of application actions in natural language. The host application supplies and authorizes those actions through a defined tool interface; the model never receives unrestricted access to application data or the operating system.

## Design

- **UI:** reusable .NET 10 WPF chat control with streamed responses, cancellation, activity display, and a clear-chat action.
- **SDK:** WPF-independent .NET DLLs for chat sessions, tool contracts, catalog validation, and runtime management.
- **Inference:** a managed launcher for `llama-server`, bound to localhost for offline operation. Engine binaries will be packaged later.
- **Model:** IBM Granite 4.0 350M Instruct, GGUF Q8_0. The model file is about **378 MB**; installed size and runtime RAM use are higher.
- **Host integration:** applications register a small catalog of typed read and write operations, each declaring its purpose, input/output JSON schemas, and usage examples. The model selects and calls them dynamically; the SDK validates every proposed call against the schema. The host controls permissions, executes tools, and approves writes.

The first release targets **Windows x64 PCs with 8 GB RAM and a modern 4-core CPU**. A dedicated GPU is not required. CPU inference is the baseline; integrated GPU acceleration is optional and will be enabled only when measurements show a benefit.

## Intended use and limits

LightLMChat is intended for defined, primarily single-step tasks such as finding a record, showing its details, or proposing a field update. A 350M-parameter model may not reliably handle open-ended conversation or complex multi-step instructions. The first implementation phase therefore measures tool and argument accuracy against representative host operations before release. Unclear requests should result in clarification, and writes require explicit approval.

The runtime is designed to work after installation with the network disconnected. No cloud inference or runtime model download is required. Model and engine binaries are pinned and verified for packaging; the model file is kept out of ordinary Git history.

## Project status

**Family tree integration prototype.** The solution contains host tool contracts, a WPF chat control, a sample WPF host, a `llama-server` process launcher, an OpenAI-compatible streaming chat client, and a broker that validates proposed tool calls before dispatch. The `LightLMChat.FamilyTree` DLL provides a read-only adapter contract, a family-summary tool using `find_people`, `get_person`, and `get_relatives`, and a write `add_person_note` tool. The core enforces per-request limits on sequential tool calls, generation length, and elapsed time. The sample uses fictional data and scripted replies by default. If both runtime paths below are set, it starts the local server and exposes the read and write tools to the model, with a modal approve/deny dialog for writes. A [real Granite CPU smoke probe](docs/MODEL_PROBE.md) passed three representative questions; broader quality and minimum-hardware measurements are still needed. The real family API and offline distribution bundle are not yet connected. See [family tree integration](docs/FAMILY_TREE_INTEGRATION.md), [SRS](docs/SRS.md), and [implementation plan](docs/IMPLEMENTATION_PLAN.md).

On Windows with the .NET 10 SDK, run:

```powershell
dotnet build LightLMChat.sln
dotnet run --project src/LightLMChat.SampleHost
```

To try a locally installed `llama-server` and Granite GGUF model in the sample:

```powershell
$env:LIGHTLMCHAT_SERVER_PATH = 'C:\path\to\llama-server.exe'
$env:LIGHTLMCHAT_MODEL_PATH = 'C:\path\to\granite-4.0-350m-Q8_0.gguf'
dotnet run --project src/LightLMChat.SampleHost
```

For development, `scripts/prepare-runtime.ps1` downloads the pinned official CPU engine and model into Git-ignored folders and verifies their SHA-256 hashes against [the manifest](docs/model-and-runtime-manifest.json). It prints the two environment variable commands to use:

```powershell
.\scripts\prepare-runtime.ps1
# Paste the two environment-variable commands printed by the script.
dotnet run --project tests/LightLMChat.ModelProbe
```

The launcher uses CPU inference, a 2048-token context, a random local API key, and an available `127.0.0.1` port; if GPU initialization fails it falls back to CPU. The application itself does not download model or engine files at runtime. To run the model-free transport check:

```powershell
dotnet run --project tests/LightLMChat.RuntimeChecks
```

With a local model configured, try: “When was Mary Example born and who are her children?” `Mary Example` is a fictional sample record. The next tasks are to connect the real family tree API and measure Granite's tool-selection accuracy on a minimum-spec PC.

## Licensing

LightLMChat source code and documentation are licensed under [MIT](LICENSE). The selected [IBM Granite model](https://huggingface.co/ibm-granite/granite-4.0-350m) remains under Apache 2.0, and [llama.cpp](https://github.com/ggml-org/llama.cpp) remains under MIT. Model weights and engine binaries are not included in this repository; future distribution packages will include their required license and attribution notices.

