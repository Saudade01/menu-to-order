namespace MenuToOrder.Data;

public enum MenuStatus { Draft, Published }

public sealed class MenuVersion
{
    private readonly List<MenuItem> _items = [];

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RestaurantId { get; private set; }
    public MenuStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public IReadOnlyList<MenuItem> Items => _items;

    public MenuVersion(Guid restaurantId, DateTimeOffset createdAt)
    {
        RestaurantId = restaurantId;
        CreatedAt = createdAt;
    }

    // Internal on purpose: Verification is the only assembly allowed to call this.
    internal void Publish(DateTimeOffset at)
    {
        if (Status == MenuStatus.Published)
            throw new InvalidOperationException($"Menu version {Id} is already published.");

        Status = MenuStatus.Published;
        PublishedAt = at;
    }
}
