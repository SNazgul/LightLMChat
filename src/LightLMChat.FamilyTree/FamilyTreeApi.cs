namespace LightLMChat.FamilyTree;

/// <summary>Example adapter for the host's find_people, get_person, and get_relatives operations.</summary>
public interface IFamilyTreeApi
{
    ValueTask<IReadOnlyList<FamilyPersonMatch>> FindPeopleAsync(string name, CancellationToken cancellationToken);
    ValueTask<FamilyPerson?> GetPersonAsync(string personId, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<FamilyRelative>> GetRelativesAsync(string personId, CancellationToken cancellationToken);
}

public sealed record FamilyPersonMatch(string Id, string DisplayName, string? BirthHint);
/// <summary>Birth text preserves partial and approximate dates used in genealogy records.</summary>
public sealed record FamilyPerson(string Id, string DisplayName, string? Birth, string? BirthPlace = null);
public sealed record FamilyRelative(string PersonId, string Relationship, string DisplayName, string? Birth);

/// <summary>Fictional data for the sample host only.</summary>
public sealed class SampleFamilyTreeApi : IFamilyTreeApi
{
    private static readonly FamilyPerson[] People =
    [
        new("p1", "Mary Example", "14 May 1908", "Bristol"),
        new("p2", "Anna Example", "1932"),
        new("p3", "Peter Example", "1935"),
        new("p4", "Mary Other", "circa 1880", "Leeds")
    ];

    public ValueTask<IReadOnlyList<FamilyPersonMatch>> FindPeopleAsync(
        string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<FamilyPersonMatch> matches = People
            .Where(p => p.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Take(6).Select(p => new FamilyPersonMatch(p.Id, p.DisplayName, p.Birth)).ToArray();
        return ValueTask.FromResult(matches);
    }

    public ValueTask<FamilyPerson?> GetPersonAsync(string personId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(People.FirstOrDefault(p => p.Id == personId));
    }

    public ValueTask<IReadOnlyList<FamilyRelative>> GetRelativesAsync(
        string personId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<FamilyRelative> relatives = personId == "p1"
            ? People[1..3].Select(p => new FamilyRelative(p.Id, "child", p.DisplayName, p.Birth)).ToArray()
            : [];
        return ValueTask.FromResult(relatives);
    }
}
