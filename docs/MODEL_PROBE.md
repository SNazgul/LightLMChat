# Granite CPU probe

Date: 2026-11-06. Local Windows x64 development PC; this is a smoke probe, not a minimum-hardware benchmark. Engine: pinned llama.cpp `b12119` CPU archive. Model: IBM Granite 4.0 350M GGUF Q8_0. Downloads were verified against the SHA-256 values in [model-and-runtime-manifest.json](model-and-runtime-manifest.json). The model and engine files remain in Git-ignored local folders.

The probe started `llama-server` on `127.0.0.1` with a random API key, 2048-token context, and CPU inference. It used the fictional `SampleFamilyTreeApi` and forced selection of the single registered read tool. Each question used the same chat session.

| User question | Observed result | Elapsed | Server working set |
| --- | --- | ---: | ---: |
| “When was Mary Example born and who are her children?” | `get_person_family_summary`; birth and two children returned | 0.70 s | 482 MiB |
| “When was Mary born and who are her children?” | Same tool; two matches reported, clarification requested | 0.29 s | 482 MiB |
| “When unt Marry Jozifer born and who are her children?” | Same tool; no matching person reported | 0.35 s | 482 MiB |

The first probe exposed two model errors: it reused the prior full name for the ambiguous question and skipped tool selection for the misspelled question. The family tool now grounds proposed name words in the current user request, and the sample forces its one read tool. The table records the retest after those changes. The tool formats the final answer from API data, so the model does not rewrite dates or names.

These three examples do not establish general tool-selection accuracy or an 8 GB minimum. Startup time, total application memory, longer trees, multiple tools, languages, and integrated GPU performance remain unmeasured. A larger held-out evaluation is required before release.
