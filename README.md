# LightLMChat

LightLMChat is a planned, embeddable **offline chat component for .NET desktop applications**. It combines a WPF chat control with a managed SDK that lets users request a small set of application actions in natural language. The host application supplies and authorizes those actions through a defined tool interface; the model never receives unrestricted access to application data or the operating system.

## Design

- **UI:** reusable .NET 10 WPF chat control with streamed responses, cancellation, action display, and approval prompts.
- **SDK:** WPF-independent .NET DLL for chat sessions, inference, tool validation, and runtime management.
- **Inference:** bundled llama.cpp process, bound to localhost for offline operation.
- **Model:** IBM Granite 4.0 350M Instruct, GGUF Q8_0. The model file is about **378 MB**; installed size and runtime RAM use are higher.
- **Host integration:** applications register a small catalog of typed read and write tools. The SDK checks model-proposed calls against their schemas. The host controls permissions, executes tools, and approves writes.

The first release targets **Windows x64 PCs with 8 GB RAM and a modern 4-core CPU**. A dedicated GPU is not required. CPU inference is the baseline; integrated GPU acceleration is optional and will be enabled only when measurements show a benefit.

## Intended use and limits

LightLMChat is intended for defined, primarily single-step tasks such as finding a record, showing its details, or proposing a field update. A 350M-parameter model may not reliably handle open-ended conversation or complex multi-step instructions. The first implementation phase therefore measures tool and argument accuracy against representative host operations before release. Unclear requests should result in clarification, and writes require explicit approval.

The runtime is designed to work after installation with the network disconnected. No cloud inference or runtime model download is required. Model and engine binaries are pinned and verified for packaging; the model file is kept out of ordinary Git history.

## Project status

**Planning stage.** The requirements and build sequence are in [docs/SRS.md](docs/SRS.md) and [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md). The next task is to define the first 3–5 host application operations and benchmark the selected model on a minimum-spec PC. Source projects and installer will follow that validation.

## Licensing

LightLMChat source code and documentation are licensed under [MIT](LICENSE). The selected [IBM Granite model](https://huggingface.co/ibm-granite/granite-4.0-350m) remains under Apache 2.0, and [llama.cpp](https://github.com/ggml-org/llama.cpp) remains under MIT. Model weights and engine binaries are not included in this repository; future distribution packages will include their required license and attribution notices.

