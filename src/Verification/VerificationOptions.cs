namespace MenuToOrder.Verification;

// Bound from the "Verification" config section. Tune these against the correction log, not by feel.
public sealed record VerificationOptions
{
    public decimal MinPrice { get; init; } = 10m;
    public decimal MaxPrice { get; init; } = 10_000m;
    public double AgreementWeight { get; init; } = 0.4;
    public double RulesWeight { get; init; } = 0.4;
    public double LegibilityWeight { get; init; } = 0.2;
    public double PublishThreshold { get; init; } = 0.9;
}
