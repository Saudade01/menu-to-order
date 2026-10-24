namespace MenuToOrder.Data;

public sealed class Correction
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid MenuItemId { get; private set; }
    public MenuField Field { get; private set; }
    public string? OldValue { get; private set; }
    public string NewValue { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Correction(Guid menuItemId, MenuField field, string? oldValue, string newValue, DateTimeOffset createdAt)
    {
        MenuItemId = menuItemId;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        CreatedAt = createdAt;
    }
}
