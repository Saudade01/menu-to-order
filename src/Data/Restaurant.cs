namespace MenuToOrder.Data;

public sealed class Restaurant
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Name { get; private set; }
    public string Slug { get; private set; }
    public byte[] OwnerTokenHash { get; private set; }
    public int TableCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Restaurant(string name, string slug, byte[] ownerTokenHash, int tableCount, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tableCount);

        Name = name;
        Slug = slug;
        OwnerTokenHash = ownerTokenHash;
        TableCount = tableCount;
        CreatedAt = createdAt;
    }

    public void ChangeTableCount(int tableCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tableCount);
        TableCount = tableCount;
    }
}
