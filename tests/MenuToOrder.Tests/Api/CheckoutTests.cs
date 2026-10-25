using MenuToOrder.Api.Guest;
using MenuToOrder.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace MenuToOrder.Tests.Api;

public class CheckoutTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly MenuVersion Menu = new(Guid.CreateVersion7(), Now);
    private static readonly MenuItem Menemen = new(Menu.Id, 0, "Menemen", "KAHVALTI", null, 185.50m);
    private static readonly MenuItem Soda = new(Menu.Id, 1, "Soda", "İÇECEK", null, 40m);
    private static readonly MenuItem[] Items = [Menemen, Soda];

    [Fact]
    public void The_total_comes_from_the_menu_prices()
    {
        var (order, error) = Price(new() { [Menemen.Id] = 3, [Soda.Id] = 2 });

        Assert.Null(error);
        Assert.Equal(636.50m, order!.Total);
        Assert.Equal([("Menemen", 185.50m, 3), ("Soda", 40m, 2)], order.Lines.Select(l => (l.Name, l.UnitPrice, l.Quantity)));
    }

    [Fact]
    public void A_cart_built_against_an_older_menu_is_refused() =>
        Assert.Equal(Checkout.MenuChanged, Price(new() { [Soda.Id] = 1 }, seenMenu: Guid.CreateVersion7()).Error);

    [Fact]
    public void A_dish_that_is_no_longer_on_the_menu_is_refused() =>
        Assert.Equal(Checkout.MenuChanged, Price(new() { [Guid.CreateVersion7()] = 1 }).Error);

    [Fact]
    public void Notes_are_trimmed_and_limited()
    {
        Assert.Equal("acısız", Price(new() { [Soda.Id] = 1 }, note: "  acısız ").Order!.Note);
        Assert.Null(Price(new() { [Soda.Id] = 1 }, note: "   ").Order!.Note);
        Assert.NotNull(Price(new() { [Soda.Id] = 1 }, note: new string('x', 201)).Error);
    }

    [Fact]
    public void Quantities_are_read_from_the_form_and_zeros_are_dropped()
    {
        var (cart, error) = Checkout.Read(Form(("qty." + Menemen.Id, "2"), ("qty." + Soda.Id, "0"), ("note", "x")));

        Assert.Null(error);
        Assert.Equal(new Dictionary<Guid, int> { [Menemen.Id] = 2 }, cart);
    }

    [Theory]
    [InlineData("21")]
    [InlineData("-1")]
    [InlineData("iki")]
    [InlineData("1.5")]
    public void Quantities_outside_one_to_twenty_are_refused(string quantity) =>
        Assert.NotNull(Checkout.Read(Form(("qty." + Soda.Id, quantity))).Error);

    [Fact]
    public void An_empty_cart_is_refused() =>
        Assert.Equal("Pick at least one dish.", Checkout.Read(Form(("qty." + Soda.Id, "0"))).Error);

    [Fact]
    public void A_dish_without_a_price_cannot_be_ordered() =>
        Assert.Throws<ArgumentException>(() =>
            new Order(Menu.RestaurantId, Menu.Id, 1, null, [(new MenuItem(Menu.Id, 2, "Günün Balığı", null, null, null), 1)], Now));

    private static (Order? Order, string? Error) Price(Dictionary<Guid, int> cart, Guid? seenMenu = null, string? note = null) =>
        Checkout.Price(Menu, Items, seenMenu ?? Menu.Id, cart, table: 3, note, Now);

    private static FormCollection Form(params (string Key, string Value)[] fields) =>
        new(fields.ToDictionary(f => f.Key, f => new StringValues(f.Value)));
}
