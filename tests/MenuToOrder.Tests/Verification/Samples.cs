using System.Security.Cryptography;
using MenuToOrder.Data;
using MenuToOrder.Extraction;
using MenuToOrder.Verification;

namespace MenuToOrder.Tests.Verification;

internal static class Samples
{
    private static readonly RecordedMenuReader Reader = new(Repo.At("samples", "recorded"), TimeProvider.System);

    public static async Task<MenuDraft> Draft(Guid restaurantId, string sample, CancellationToken ct)
    {
        var image = new MenuImage(await File.ReadAllBytesAsync(Repo.At("samples", "menus", $"{sample}.jpg"), ct), "image/jpeg");
        return Drafts.Build(restaurantId, await Reader.ReadAsync(image, 1, ct), await Reader.ReadAsync(image, 2, ct),
            new VerificationOptions(), DateTimeOffset.UtcNow);
    }

    public static async Task<(Restaurant Restaurant, MenuDraft Draft)> Save(PostgresFixture postgres, string sample, CancellationToken ct)
    {
        var restaurant = new Restaurant("Test", $"test-{Guid.NewGuid():N}", RandomNumberGenerator.GetBytes(32), 4, DateTimeOffset.UtcNow);
        var draft = await Draft(restaurant.Id, sample, ct);

        await using var db = postgres.CreateDb();
        db.Add(restaurant);
        db.Add(draft.Version);
        db.AddRange(draft.Items);
        db.AddRange(draft.Flags);
        db.AddRange(draft.Runs);
        await db.SaveChangesAsync(ct);
        return (restaurant, draft);
    }

    public static IEnumerable<(string Item, MenuField Field, FlagReason Reason)> Described(this MenuDraft draft) =>
        draft.Flags.Select(f => (draft.Items.Single(i => i.Id == f.MenuItemId).Name, f.Field, f.Reason));
}
