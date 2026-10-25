namespace MenuToOrder.Data;

public sealed class Order
{
    public const int MaxQuantity = 20;
    public const int MaxNoteLength = 200;

    private readonly List<OrderLine> _lines = [];

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RestaurantId { get; private set; }
    public Guid MenuVersionId { get; private set; }
    public int TableNumber { get; private set; }
    public string? Note { get; private set; }
    public decimal Total { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;

    // The total is worked out here from the menu's own prices, never taken from the guest.
    public Order(Guid restaurantId, Guid menuVersionId, int tableNumber, string? note,
        IEnumerable<(MenuItem Item, int Quantity)> lines, DateTimeOffset createdAt)
        : this(restaurantId, menuVersionId, tableNumber, note, 0m, createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tableNumber);
        if (note?.Length > MaxNoteLength)
            throw new ArgumentOutOfRangeException(nameof(note), $"A note is at most {MaxNoteLength} characters.");

        _lines.AddRange(lines.Select((l, i) => OrderLine.For(Id, i, l.Item, l.Quantity)));
        if (_lines.Count == 0)
            throw new ArgumentException("An order needs at least one dish.", nameof(lines));

        Total = _lines.Sum(l => l.UnitPrice * l.Quantity);
    }

    private Order(Guid restaurantId, Guid menuVersionId, int tableNumber, string? note, decimal total, DateTimeOffset createdAt)
    {
        RestaurantId = restaurantId;
        MenuVersionId = menuVersionId;
        TableNumber = tableNumber;
        Note = note;
        Total = total;
        CreatedAt = createdAt;
    }
}

public sealed class OrderLine
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid OrderId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public int Position { get; private set; }
    // Name and price are copied so an old order still reads correctly after the menu changes.
    public string Name { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }

    private OrderLine(Guid orderId, int position, Guid menuItemId, string name, decimal unitPrice, int quantity)
    {
        OrderId = orderId;
        Position = position;
        MenuItemId = menuItemId;
        Name = name;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    internal static OrderLine For(Guid orderId, int position, MenuItem item, int quantity)
    {
        if (quantity is < 1 or > Order.MaxQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, $"Quantity is between 1 and {Order.MaxQuantity}.");
        if (item.Price is not { } price)
            throw new ArgumentException($"{item.Name} has no price and cannot be ordered.", nameof(item));

        return new OrderLine(orderId, position, item.Id, item.Name, price, quantity);
    }
}
