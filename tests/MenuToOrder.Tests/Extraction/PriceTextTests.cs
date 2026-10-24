using System.Globalization;
using MenuToOrder.Extraction;

namespace MenuToOrder.Tests.Extraction;

public class PriceTextTests
{
    [Theory]
    [InlineData("95 ₺", "95", Currency.Lira)]
    [InlineData("1.250,00 TL", "1250.00", Currency.Lira)]
    [InlineData("185,50 TL", "185.50", Currency.Lira)]
    [InlineData("₺1.250", "1250", Currency.Lira)]
    [InlineData("12.5 TL", "12.5", Currency.Lira)]
    [InlineData("40 try", "40", Currency.Lira)]
    [InlineData("180.-", "180", Currency.Missing)]
    [InlineData("290", "290", Currency.Missing)]
    [InlineData("$4", "4", Currency.Other)]
    public void Prices_printed_the_usual_ways_are_read(string text, string amount, Currency currency) =>
        Assert.Equal(new ParsedPrice(decimal.Parse(amount, CultureInfo.InvariantCulture), currency), PriceText.Parse(text));

    [Theory]
    [InlineData("Yarım 180 / Tam 320")]
    [InlineData("Sorunuz")]
    [InlineData("1,250 TL")]
    [InlineData("12.3456 TL")]
    [InlineData("1.25.0 TL")]
    [InlineData("")]
    [InlineData(null)]
    public void Ambiguous_or_missing_prices_get_no_amount(string? text) =>
        Assert.Null(PriceText.Parse(text).Amount);

    [Fact]
    public void Currency_is_reported_even_when_the_amount_is_ambiguous() =>
        Assert.Equal(Currency.Lira, PriceText.Parse("1,250 TL").Currency);
}
