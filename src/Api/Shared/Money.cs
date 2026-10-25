using System.Globalization;

namespace MenuToOrder.Api.Shared;

public static class Money
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Whole prices print without kuruş; anything else gets both digits (185,50 ₺, never 185,5 ₺).
    public static string Lira(decimal amount) =>
        $"{amount.ToString(decimal.Truncate(amount) == amount ? "#,0" : "#,0.00", Turkish)} ₺";
}
