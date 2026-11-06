namespace LightLMChat.FamilyTree;

/// <summary>Host-owned write surface for research notes on family tree people.</summary>
public interface IFamilyTreeNotes
{
    /// <summary>Adds a note for an existing person and reports whether it was stored.</summary>
    ValueTask<bool> AddNoteAsync(string personId, string note, CancellationToken cancellationToken);
}

/// <summary>In-memory notes for the sample host and model-free checks.</summary>
public sealed class SampleFamilyTreeNotes : IFamilyTreeNotes
{
    private readonly List<(string PersonId, string Note)> _notes = [];

    public int Count => _notes.Count;

    public IReadOnlyList<(string PersonId, string Note)> Notes => _notes;

    public ValueTask<bool> AddNoteAsync(string personId, string note, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _notes.Add((personId, note));
        return ValueTask.FromResult(true);
    }
}
