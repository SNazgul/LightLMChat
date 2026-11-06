# Family tree integration prototype

The first supported question is: “When was Mary Example born and who are her children?” The name and facts in `SampleFamilyTreeApi` are fictional. The intended real application API has `find_people`, `get_person`, and `get_relatives`; the exact request and response shapes are not available yet.

## Adapter contract

The `LightLMChat.FamilyTree` DLL defines `IFamilyTreeApi`:

```csharp
ValueTask<IReadOnlyList<FamilyPersonMatch>> FindPeopleAsync(string name, CancellationToken ct);
ValueTask<FamilyPerson?> GetPersonAsync(string personId, CancellationToken ct);
ValueTask<IReadOnlyList<FamilyRelative>> GetRelativesAsync(string personId, CancellationToken ct);
```

A host adapter maps `find_people` results to `FamilyPersonMatch`, uses the returned stable `Id` for `get_person` and `get_relatives`, and maps a child relation to `Relationship == "child"`. The host owns authorization and may filter records before returning them. Birth values are strings so partial and approximate genealogical dates remain intact.

The sample registers a read-only `FamilySummaryTool` and a write `AddPersonNoteTool`. These are concrete examples of the generic host contract; each also declares an `OutputSchemaJson` and usage `Examples` so the model sees the operation's purpose and shape. The summary tool asks the model for a `person_name`, then calls those three API operations in order. No match is reported; multiple matches produce candidate IDs and birth hints for clarification. A unique match returns the person's birth and recorded children. The tool also provides `display_text` derived from the API response; the chat displays it directly to avoid a small model rewriting names or dates. The note tool looks up a unique person by full name and, after host approval, stores a note through `IFamilyTreeNotes`.

“Aunt” in a question is treated as a name qualifier only. This tool does not verify the user's relationship to the person. Relationship resolution needs a known reference person and an appropriate `get_relatives` contract.

The other reported API operations (`tree_summary`, `get_ancestors`, `get_descendants`, `list_sources`, `get_search_context`, and the data-quality searches) are candidates for later, separately scoped read-only tools. Their argument and result formats should be defined before exposing them to the model.

## Current limits

- The automated integration check uses a simulated llama-server response. A separate [real-model smoke probe](MODEL_PROBE.md) checks three representative questions, but does not establish general accuracy.
- The sample data is fictional. No real family tree API is connected.
- Source citations from `list_sources` are not yet included in answers.
- The broker requires approval for any tool classified as a write. The sample now exposes one write tool, `add_person_note`, backed by `IFamilyTreeNotes`; the sample host shows a modal approve/deny dialog for it. The note tool refuses to guess when the name matches zero or several people.
