namespace MenuToOrder.Data;

public sealed class MenuItem
{
    private readonly List<Flag> _flags = [];

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid MenuVersionId { get; private set; }
    public int Position { get; private set; }
    public string Name { get; private set; }
    public string? Category { get; private set; }
    public string? Description { get; private set; }
    // Null means the price could not be read. Such an item is flagged, never published.
    public decimal? Price { get; private set; }
    public IReadOnlyList<Flag> Flags => _flags;

    public MenuItem(Guid menuVersionId, int position, string name, string? category, string? description, decimal? price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CheckPrice(price);

        MenuVersionId = menuVersionId;
        Position = position;
        Name = name;
        Category = category;
        Description = description;
        Price = price;
    }

    internal void Correct(MenuField field, string value)
    {
        switch (field)
        {
            case MenuField.Name:
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                Name = value;
                break;
            case MenuField.Category:
                Category = value;
                break;
            case MenuField.Description:
                Description = value;
                break;
            default:
                throw new ArgumentException("Prices are corrected with CorrectPrice.", nameof(field));
        }
    }

    internal void CorrectPrice(decimal price)
    {
        CheckPrice(price);
        Price = price;
    }

    private static void CheckPrice(decimal? price)
    {
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), price, "Price cannot be negative.");
        // numeric(10,2) would round a third decimal away silently; a price like that is a misread.
        if (price is { } p && decimal.Round(p, 2) != p)
            throw new ArgumentOutOfRangeException(nameof(price), price, "Price has more than two decimals.");
    }
}
