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
                return Retry("Give the restaurant a name of up to 80 characters.");
            if (!int.TryParse(tables, out var tableCount) || tableCount is < 1 or > 100)
                return Retry("The number of tables has to be between 1 and 100.");
            if (photo is null || photo.Length == 0)
                return Retry("Choose a photo of the menu.");
            if (photo.Length > PhotoCheck.MaxBytes)
                return Retry("The photo is larger than 10 MB.");

            using var buffer = new MemoryStream();
            await photo.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            if (PhotoCheck.MediaType(bytes) is not { } mediaType)
                return Retry("That file is not a JPEG, PNG or WebP image.");

            var token = OwnerToken.New();
            var restaurant = new Restaurant(name, Slug.For(name), OwnerToken.Hash(token), tableCount, time.GetUtcNow());
            await intake.ReadAsync(restaurant, new MenuImage(bytes, mediaType), ct);

            return Results.Redirect($"/o/{token}/review");
        }).WithMetadata(new RequestSizeLimitAttribute(PhotoCheck.MaxBytes + 64 * 1024));
    }
}
