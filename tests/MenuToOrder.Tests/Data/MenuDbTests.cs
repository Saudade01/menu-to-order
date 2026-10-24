using System.Security.Cryptography;
using MenuToOrder.Data;
using Microsoft.EntityFrameworkCore;

namespace MenuToOrder.Tests.Data;

public class MenuDbTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("1249.99")]
    [InlineData("0.30")]
    public async Task A_price_comes_back_exactly_as_it_was_stored(string price)
    {
        var expected = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);
        var item = await SaveItem(expected);

        await using var db = postgres.CreateDb();
        var stored = await db.MenuItems.SingleAsync(i => i.Id == item.Id, Ct);

        Assert.Equal(expected, stored.Price);
    }

    [Fact]
    public async Task A_missing_price_is_stored_as_missing()
    {
        var item = await SaveItem(price: null);

        await using var db = postgres.CreateDb();
        var stored = await db.MenuItems.SingleAsync(i => i.Id == item.Id, Ct);

        Assert.Null(stored.Price);
    }

    [Fact]
    public async Task The_raw_model_response_is_stored_verbatim()
    {
        const string raw = "{ \"z\": 1,\n  \"items\" : [ {\"price\":\"180 ₺\",  \"name\":\"Adana\"} ] }";
        var item = await SaveItem(180m);
        var run = new ExtractionRun(item.MenuVersionId, 1, "v1", "gemini-flash", "e3b0c442", raw, null,
            TimeSpan.FromSeconds(3), 812, 240, Now);

        await using (var db = postgres.CreateDb())
        {
            db.ExtractionRuns.Add(run);
            await db.SaveChangesAsync(Ct);
        }

        await using var read = postgres.CreateDb();
        var stored = await read.ExtractionRuns.SingleAsync(r => r.Id == run.Id, Ct);

        Assert.Equal(raw, stored.RawResponse);
    }

    [Fact]
    public async Task Flag_reasons_are_stored_as_readable_text()
    {
        var item = await SaveItem(18m);
        var flag = new Flag(item.Id, MenuField.Price, FlagReason.PassesDisagree, "18", "180", 0.4);

        await using var db = postgres.CreateDb();
        db.Flags.Add(flag);
        await db.SaveChangesAsync(Ct);

        var reason = await db.Database
            .SqlQuery<string>($"""SELECT "Reason" AS "Value" FROM "Flags" WHERE "Id" = {flag.Id}""")
            .SingleAsync(Ct);

        Assert.Equal("PassesDisagree", reason);
    }

    [Fact]
    public async Task A_corrected_item_cannot_be_deleted_from_under_its_audit_trail()
    {
        var item = await SaveItem(18m);
        await using (var db = postgres.CreateDb())
        {
            db.Corrections.Add(new Correction(item.Id, MenuField.Price, "18", "180", Now));
            await db.SaveChangesAsync(Ct);
        }

        await using var delete = postgres.CreateDb();
        delete.MenuItems.Remove(await delete.MenuItems.SingleAsync(i => i.Id == item.Id, Ct));

        await Assert.ThrowsAsync<DbUpdateException>(() => delete.SaveChangesAsync(Ct));
    }

    private async Task<MenuItem> SaveItem(decimal? price)
    {
        var restaurant = new Restaurant("Köşe Lokantası", $"kose-{Guid.NewGuid():N}", RandomNumberGenerator.GetBytes(32), 4, Now);
        var version = new MenuVersion(restaurant.Id, Now);
        var item = new MenuItem(version.Id, 0, "Adana kebap", "Kebaplar", null, price);

        await using var db = postgres.CreateDb();
        db.AddRange(restaurant, version, item);
        await db.SaveChangesAsync(Ct);
        return item;
    }
}
