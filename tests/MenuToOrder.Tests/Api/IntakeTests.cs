using System.Security.Cryptography;
using MenuToOrder.Api.Intake;
using MenuToOrder.Data;
using MenuToOrder.Extraction;
using MenuToOrder.Verification;
using Microsoft.EntityFrameworkCore;

namespace MenuToOrder.Tests.Api;

public class IntakeTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_read_saves_the_draft_and_both_runs_for_an_existing_restaurant()
    {
        var restaurant = new Restaurant("Köşe Lokantası", $"kose-{Guid.NewGuid():N}", RandomNumberGenerator.GetBytes(32), 5, DateTimeOffset.UtcNow);
        await using (var db = postgres.CreateDb())
        {
            db.Add(restaurant);
            await db.SaveChangesAsync(Ct);
        }

        var image = new MenuImage(await File.ReadAllBytesAsync(Repo.At("samples", "menus", "kose-lokantasi.jpg"), Ct), "image/jpeg");
        await using (var db = postgres.CreateDb())
        {
            var reader = new RecordedMenuReader(Repo.At("samples", "recorded"), TimeProvider.System);
            await new MenuIntake(reader, db, new VerificationOptions(), TimeProvider.System).ReadAsync(restaurant.Id, image, Ct);
        }

        await using var check = postgres.CreateDb();
        var version = await check.MenuVersions.Include(v => v.Items).SingleAsync(v => v.RestaurantId == restaurant.Id, Ct);
        Assert.Equal(11, version.Items.Count);
        Assert.Equal(2, await check.ExtractionRuns.CountAsync(r => r.MenuVersionId == version.Id, Ct));
    }
}
