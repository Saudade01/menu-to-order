namespace MenuToOrder.Data;

public enum MenuField { Name, Category, Description, Price }

public enum FlagReason
{
    PassesDisagree,
    PriceUnreadable,
    PriceOutOfRange,
    CurrencyMissing,
    DuplicateName,
    UnknownCategory,
    LowLegibility,
}

public sealed class Flag
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid MenuItemId { get; private set; }
    public MenuField Field { get; private set; }
    public FlagReason Reason { get; private set; }
    public string? FirstPassValue { get; private set; }
    public string? SecondPassValue { get; private set; }
    public double Confidence { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public Flag(Guid menuItemId, MenuField field, FlagReason reason, string? firstPassValue, string? secondPassValue, double confidence)
    {
        if (confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence is a score in [0, 1].");

        MenuItemId = menuItemId;
        Field = field;
        Reason = reason;
        FirstPassValue = firstPassValue;
        SecondPassValue = secondPassValue;
        Confidence = confidence;
    }
}
