using MenuToOrder.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MenuToOrder.Api.Guest;

public static class GuestEndpoints
{
    public static void MapGuest(this WebApplication app)
    {
        app.MapGet("/m/{slug}/{table:int}", async (string slug, int table, MenuDb db, CancellationToken ct) =>
        {
            var restaurant = await db.Restaurants.SingleOrDefaultAsync(r => r.Slug == slug, ct);
            if (restaurant is null || table < 1 || table > restaurant.TableCount)
                return Results.NotFound();

            // Only a published version is ever shown here; drafts stay with the owner.
            var menu = await db.MenuVersions.Include(v => v.Items)
                .Where(v => v.RestaurantId == restaurant.Id && v.Status == MenuStatus.Published)
                .OrderByDescending(v => v.PublishedAt)
                .FirstOrDefaultAsync(ct);
            if (menu is null)
                return Results.NotFound();

            return new RazorComponentResult<MenuPage>(new { Restaurant = restaurant, Table = table, Menu = menu });
        });
    }
}
