using MenuToOrder.Api.Shared;
using MenuToOrder.Data;
using MenuToOrder.Extraction;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MenuToOrder.Api.Intake;

public static class IntakeEndpoints
{
    public static void MapIntake(this WebApplication app)
    {
        app.MapGet("/", () => new RazorComponentResult<Upload>());

        app.MapPost("/restaurants", async ([FromForm] string? name, [FromForm] string? tables, IFormFile? photo,
            MenuIntake intake, TimeProvider time, CancellationToken ct) =>
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
            if (photo is null || photo.Length == 0)
                return Retry("Menünün fotoğrafını seçin.");
            if (photo.Length > PhotoCheck.MaxBytes)
                return Retry("Fotoğraf 10 MB'tan büyük.");

            using var buffer = new MemoryStream();
            await photo.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            if (PhotoCheck.MediaType(bytes) is not { } mediaType)
                return Retry("Bu dosya JPEG, PNG ya da WebP bir görsel değil.");

            var token = OwnerToken.New();
            var restaurant = new Restaurant(name, Slug.For(name), OwnerToken.Hash(token), tableCount, time.GetUtcNow());
            await intake.ReadAsync(restaurant, new MenuImage(bytes, mediaType), ct);

            return Results.Redirect($"/o/{token}/review");
        }).WithMetadata(new RequestSizeLimitAttribute(PhotoCheck.MaxBytes + 64 * 1024));
    }
}
