using MenuToOrder.Api.Shared;
using MenuToOrder.Data;
using MenuToOrder.Verification;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace MenuToOrder.Api.Owner;

public static class OwnerEndpoints
{
    public static void MapOwner(this WebApplication app)
    {
        var owner = app.MapGroup("/o/{token}");

        owner.MapGet("/review", async (string token, string? notice, MenuDb db, CancellationToken ct) =>
        {
            if (await db.FindOwnerAsync(token, ct) is not { } restaurant)
                return Results.NotFound();

            var version = await Latest(db, restaurant.Id, ct);
            var runs = await db.ExtractionRuns.Where(r => r.MenuVersionId == version.Id).OrderBy(r => r.Pass).ToListAsync(ct);
            return new RazorComponentResult<ReviewPage>(new { Token = token, Restaurant = restaurant, Version = version, Runs = runs, Notice = notice });
        });

        owner.MapPost("/flags/{id:guid}", async (string token, Guid id, [FromForm] string? action, [FromForm] string? value,
            MenuDb db, Review review, CancellationToken ct) =>
        {
            // The flag id comes from the form, so it has to belong to the restaurant behind this link.
            if (await db.FindOwnerAsync(token, ct) is not { } restaurant || !await db.OwnsFlagAsync(restaurant.Id, id, ct))
                return Results.NotFound();

            var outcome = action == "correct" ? await review.CorrectAsync(id, value ?? "", ct) : await review.ConfirmAsync(id, ct);
            return Back(token, (outcome as ReviewOutcome.Rejected)?.Reason);
        });

        owner.MapPost("/lira", async (string token, MenuDb db, Review review, CancellationToken ct) =>
        {
            if (await db.FindOwnerAsync(token, ct) is not { } restaurant)
                return Results.NotFound();

            var outcome = await review.ConfirmLiraAsync((await Latest(db, restaurant.Id, ct)).Id, ct);
            return Back(token, (outcome as ReviewOutcome.Rejected)?.Reason);
        });

        owner.MapPost("/publish", async (string token, MenuDb db, VerificationGate gate, CancellationToken ct) =>
        {
            if (await db.FindOwnerAsync(token, ct) is not { } restaurant)
                return Results.NotFound();

            return await gate.PublishAsync((await Latest(db, restaurant.Id, ct)).Id, ct) switch
            {
                PublishOutcome.Blocked blocked => Back(token, blocked.Reason),
                PublishOutcome.NotFound => Results.NotFound(),
                _ => Results.Redirect($"/o/{token}/qr"),
            };
        });

        owner.MapGet("/orders", async (string token, MenuDb db, CancellationToken ct) =>
        {
            if (await db.FindOwnerAsync(token, ct) is not { } restaurant)
                return Results.NotFound();

            var orders = await db.Orders.Include(o => o.Lines.OrderBy(l => l.Position))
                .Where(o => o.RestaurantId == restaurant.Id)
                .OrderByDescending(o => o.CreatedAt)
                .Take(100)
                .ToListAsync(ct);
            return new RazorComponentResult<OrdersPage>(new { Token = token, Restaurant = restaurant, Orders = orders });
        });

        owner.MapGet("/qr", async (string token, HttpRequest request, MenuDb db, CancellationToken ct) =>
        {
            if (await db.FindOwnerAsync(token, ct) is not { } restaurant)
                return Results.NotFound();
            if (!await db.MenuVersions.AnyAsync(v => v.RestaurantId == restaurant.Id && v.Status == MenuStatus.Published, ct))
                return Back(token, "Publish the menu first.");

            var codes = Enumerable.Range(1, restaurant.TableCount)
                .Select(table => $"{request.Scheme}://{request.Host}/m/{restaurant.Slug}/{table}")
                .Select((url, i) => new QrCode(i + 1, url, Png(url)))
                .ToList();
            return new RazorComponentResult<QrPage>(new { Token = token, Restaurant = restaurant, Codes = codes });
        });
    }

    private static Task<MenuVersion> Latest(MenuDb db, Guid restaurantId, CancellationToken ct) =>
        db.MenuVersions.Include(v => v.Items).ThenInclude(i => i.Flags)
            .Where(v => v.RestaurantId == restaurantId)
            .OrderByDescending(v => v.CreatedAt)
            .FirstAsync(ct);

    private static IResult Back(string token, string? notice) =>
        Results.Redirect($"/o/{token}/review" + (notice is null ? "" : $"?notice={Uri.EscapeDataString(notice)}"));

    private static string Png(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return $"data:image/png;base64,{Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(8))}";
    }
}

public sealed record QrCode(int Table, string Url, string Image);
