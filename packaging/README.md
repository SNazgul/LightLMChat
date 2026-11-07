# Offline distribution bundle

This folder describes how the LightLMChat example is turned into a single, self-contained
folder (or zip) that can be copied to an offline Windows x64 PC and run by double-clicking —
no internet, no environment variables, no manual model setup.

## What the bundle contains

```
dist/
  LightLMChat.ExampleApp.exe      the app (plus its .NET assemblies)
  runtime/
    llama-server.exe              the local inference engine (+ its DLLs)
    granite-4.0-350m-Q8_0.gguf    the language model (~378 MB)
  licenses/
    MIT-LightLMChat.txt           this kit's license (MIT)
    MIT-llama.cpp.txt             llama.cpp license (MIT)
    Apache-2.0.txt                Granite model license (Apache-2.0)
  model-and-runtime-manifest.json pinned versions + SHA-256 hashes
```

The app looks for its engine and model in the `runtime/` folder next to itself first, and only
falls back to the `LIGHTLMCHAT_SERVER_PATH` / `LIGHTLMCHAT_MODEL_PATH` environment variables. So the
bundled app works by double-click; developers can still override it with environment variables.

## How to build the bundle

The large third-party files (engine and model) are never committed to Git. Building the bundle is
a two-step process on a machine with internet access:

```powershell
# 1. Download and verify the pinned engine and model (one-time).
.\scripts\prepare-runtime.ps1

# 2. Assemble the bundle (dist/) and optionally zip it.
.\scripts\pack.ps1 -Zip
```

For a truly offline target (no .NET installed either), publish with the .NET runtime bundled:

```powershell
.\scripts\pack.ps1 -Zip -SelfContained
```

`scripts/pack.ps1` re-verifies the model's SHA-256 hash (and the engine archive's, when still
present) against `docs/model-and-runtime-manifest.json` before copying anything, so a corrupt or
tampered file fails the build rather than shipping.

## Notes

- The default bundle is framework-dependent: the target PC needs the **.NET 10 Desktop Runtime**.
  Use `-SelfContained` to bundle that runtime too (larger folder).
- The bundle targets **Windows x64** with CPU inference. A dedicated GPU is not required.
- The app never downloads anything at runtime; after installation it works with the network
  disconnected.
