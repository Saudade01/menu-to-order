using System.Globalization;
using MenuToOrder.Data;

namespace MenuToOrder.Tests.Data;

public class EntityRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_draft_becomes_published_with_its_publish_time()
    {
        var version = new MenuVersion(Guid.CreateVersion7(), Now);

        version.Publish(Now.AddMinutes(5));

        Assert.Equal(MenuStatus.Published, version.Status);
        Assert.Equal(Now.AddMinutes(5), version.PublishedAt);
    }

    [Fact]
    public void A_published_version_cannot_be_published_again()
    {
        var version = new MenuVersion(Guid.CreateVersion7(), Now);
        version.Publish(Now);

        Assert.Throws<InvalidOperationException>(() => version.Publish(Now.AddMinutes(1)));
        Assert.Equal(Now, version.PublishedAt);
    }

    [Fact]
    public void An_unreadable_price_stays_missing_instead_of_being_guessed()
    {
        var item = Item(price: null);

        Assert.Null(item.Price);
    }

    [Fact]
    public void A_two_decimal_price_is_kept_exactly() =>
        Assert.Equal(180.50m, Item(price: 180.50m).Price);

    [Theory]
    [InlineData("-1")]
    [InlineData("18.005")]
    public void Prices_that_cannot_be_real_are_rejected(string price) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Item(decimal.Parse(price, CultureInfo.InvariantCulture)));

    [Fact]
    public void An_item_needs_a_name() =>
        Assert.Throws<ArgumentException>(() =>
            new MenuItem(Guid.CreateVersion7(), 0, " ", "Kebaplar", null, 180m));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_run_is_either_the_first_or_the_second_pass(int pass) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(pass, rawResponse: "{}", error: null));

    [Fact]
    public void A_run_without_a_response_has_to_say_why() =>
        Assert.Throws<ArgumentException>(() => Run(1, rawResponse: null, error: null));

    [Fact]
    public void A_failed_run_is_kept_with_its_error()
    {
        var run = Run(2, rawResponse: null, error: "429 Too Many Requests");

        Assert.Equal("429 Too Many Requests", run.Error);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Confidence_stays_between_zero_and_one(double confidence) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Flag(Guid.CreateVersion7(), MenuField.Price, FlagReason.PassesDisagree, "180", "18", confidence));

    [Fact]
    public void A_restaurant_has_at_least_one_table() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Restaurant("Köşe Lokantası", "kose-lokantasi", new byte[32], tableCount: 0, Now));

    private static MenuItem Item(decimal? price) =>
        new(Guid.CreateVersion7(), 0, "Adana kebap", "Kebaplar", null, price);

    private static ExtractionRun Run(int pass, string? rawResponse, string? error) =>
        new(Guid.CreateVersion7(), pass, "v1", "gemini-flash", "e3b0c442", rawResponse, error,
            TimeSpan.FromSeconds(2), inputTokens: null, outputTokens: null, Now);
}
