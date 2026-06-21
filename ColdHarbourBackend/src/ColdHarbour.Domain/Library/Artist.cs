namespace ColdHarbour.Domain.Library;

public class Artist
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;

    private Artist() { }

    public static Artist Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Artist name must not be null or whitespace.", nameof(name));

        return new Artist
        {
            Id = Guid.NewGuid(),
            Name = name.Trim()
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Artist name must not be null or whitespace.", nameof(name));

        Name = name.Trim();
    }
}
