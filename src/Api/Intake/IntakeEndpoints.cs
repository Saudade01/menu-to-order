using MenuToOrder.Api.Shared;
using MenuToOrder.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MenuToOrder.Api.Intake;

public static class IntakeEndpoints
{
    public static void MapIntake(this WebApplication app)
    {
        app.MapGet("/", () => new RazorComponentResult<Upload>());

        app.MapPost("/restaurants", async ([FromForm] string? name, [FromForm] string? tables, IFormFile? photo,
            MenuDb db, IServiceProvider services, TimeProvider time, CancellationToken ct) =>
        {
            IResult Retry(string error) => new RazorComponentResult<Upload>(new { Error = error, Name = name, Tables = tables })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };

            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 80)
                return Retry("Restoran adını yazın (en fazla 80 karakter).");
            if (!int.TryParse(tables, out var tableCount) || tableCount is < 1 or > 100)
                return Retry("Masa sayısı 1 ile 100 arasında olmalı.");
            var (image, error) = await PhotoCheck.ReadAsync(photo, ct);
            if (image is null)
                return Retry(error!);

            var token = OwnerToken.New();
            var restaurant = new Restaurant(name, Slug.For(name), OwnerToken.Hash(token), tableCount, time.GetUtcNow());
            db.Restaurants.Add(restaurant);
            await db.SaveChangesAsync(ct);
            MenuIntake.Start(services, restaurant.Id, image);

            return Results.Redirect($"/o/{token}/review");
        }).WithMetadata(new RequestSizeLimitAttribute(PhotoCheck.MaxBytes + 64 * 1024));
    }
}
