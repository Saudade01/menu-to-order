using MenuToOrder.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MenuToOrder.Api.Guest;

public static class GuestEndpoints
{
    public static void MapGuest(this WebApplication app)
    {
        var table = app.MapGroup("/m/{slug}/{table:int}");

        table.MapGet("/", async (string slug, int table, MenuDb db, CancellationToken ct) =>
            await Load(db, slug, table, ct) is { } open ? Menu(open, null) : Results.NotFound());

        table.MapPost("/cart", async (string slug, int table, HttpRequest request, MenuDb db, TimeProvider time, CancellationToken ct) =>
        {
            if (await Load(db, slug, table, ct) is not { } open)
                return Results.NotFound();

            var (order, error) = await Priced(open, request, time, ct);
            return order is null
                ? Menu(open, error)
                : new RazorComponentResult<CartPage>(new { open.Restaurant, Table = table, open.Menu, Order = order });
        });

        table.MapPost("/orders", async (string slug, int table, HttpRequest request, MenuDb db, TimeProvider time, CancellationToken ct) =>
        {
            if (await Load(db, slug, table, ct) is not { } open)
                return Results.NotFound();

            var (order, error) = await Priced(open, request, time, ct);
            if (order is null)
                return Menu(open, error);

            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            // Redirect so refreshing the receipt cannot send the same order twice.
            return Results.Redirect($"/m/{slug}/{table}/orders/{order.Id}");
        });

        table.MapGet("/orders/{id:guid}", async (string slug, int table, Guid id, MenuDb db, CancellationToken ct) =>
        {
            var order = await db.Orders.Include(o => o.Lines.OrderBy(l => l.Position)).SingleOrDefaultAsync(o => o.Id == id && o.TableNumber == table, ct);
            var restaurant = order is null ? null : await db.Restaurants.SingleOrDefaultAsync(r => r.Id == order.RestaurantId && r.Slug == slug, ct);
            return restaurant is null
                ? Results.NotFound()
                : new RazorComponentResult<ReceiptPage>(new { Restaurant = restaurant, Order = order });
        });
    }

    private sealed record OpenTable(Restaurant Restaurant, int Table, MenuVersion Menu);

    private static async Task<OpenTable?> Load(MenuDb db, string slug, int table, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.SingleOrDefaultAsync(r => r.Slug == slug, ct);
        if (restaurant is null || table < 1 || table > restaurant.TableCount)
            return null;

        // Only a published version is ever shown here; drafts stay with the owner.
        var menu = await db.MenuVersions.Include(v => v.Items)
            .Where(v => v.RestaurantId == restaurant.Id && v.Status == MenuStatus.Published)
            .OrderByDescending(v => v.PublishedAt)
            .FirstOrDefaultAsync(ct);
        return menu is null ? null : new OpenTable(restaurant, table, menu);
    }

    private static async Task<(Order?, string?)> Priced(OpenTable open, HttpRequest request, TimeProvider time, CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        var (cart, error) = Checkout.Read(form);
        if (cart is null)
            return (null, error);

        return Guid.TryParse(form["menu"], out var seen)
            ? Checkout.Price(open.Menu, open.Menu.Items, seen, cart, open.Table, form["note"], time.GetUtcNow())
            : (null, Checkout.MenuChanged);
    }

    private static IResult Menu(OpenTable open, string? notice) =>
        new RazorComponentResult<MenuPage>(new { open.Restaurant, open.Table, open.Menu, Notice = notice })
        {
            StatusCode = notice is null ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest,
        };
}
