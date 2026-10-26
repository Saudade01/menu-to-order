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
    public async Task A_busy_answer_is_tried_again_and_every_call_is_logged()
    {
        var reader = new BusyOnce(new RecordedMenuReader(Repo.At("samples", "recorded"), TimeProvider.System), "HTTP 503: busy");

        var (version, runs) = await Read(reader);

        Assert.Equal(11, version.Items.Count);
        Assert.Equal([(1, null), (1, "HTTP 503: busy"), (2, null)], runs.Select(r => (r.Pass, r.Error)).Order());
    }

    [Fact]
    public async Task An_answer_that_is_wrong_rather_than_busy_is_not_tried_again()
    {
        var reader = new BusyOnce(new RecordedMenuReader(Repo.At("samples", "recorded"), TimeProvider.System), "The model output does not match the menu schema.");

        var (_, runs) = await Read(reader);

        Assert.Equal(2, runs.Count);
    }

    private async Task<(MenuVersion Version, List<ExtractionRun> Runs)> Read(IMenuReader reader)
    {
        var restaurant = new Restaurant("Köşe Lokantası", $"kose-{Guid.NewGuid():N}", RandomNumberGenerator.GetBytes(32), 5, DateTimeOffset.UtcNow);
        await using (var db = postgres.CreateDb())
        {
            db.Add(restaurant);
            await db.SaveChangesAsync(Ct);
        }

        var image = new MenuImage(await File.ReadAllBytesAsync(Repo.At("samples", "menus", "kose-lokantasi.jpg"), Ct), "image/jpeg");
        await using (var db = postgres.CreateDb())
            await new MenuIntake(reader, db, new VerificationOptions(), TimeProvider.System, [TimeSpan.Zero, TimeSpan.Zero]).ReadAsync(restaurant.Id, image, Ct);

        await using var check = postgres.CreateDb();
        var version = await check.MenuVersions.Include(v => v.Items).SingleAsync(v => v.RestaurantId == restaurant.Id, Ct);
        return (version, await check.ExtractionRuns.Where(r => r.MenuVersionId == version.Id).ToListAsync(Ct));
    }

    // Fails the first pass-1 call with the given error, then reads normally.
    private sealed class BusyOnce(IMenuReader inner, string error) : IMenuReader
    {
        private int _failed;

        public async Task<MenuRead> ReadAsync(MenuImage image, int pass, CancellationToken ct)
        {
            var read = await inner.ReadAsync(image, pass, ct);
            return pass == 1 && Interlocked.Exchange(ref _failed, 1) == 0 ? read with { Menu = null, Error = error } : read;
        }
    }

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
