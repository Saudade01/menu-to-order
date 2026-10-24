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

    public static IEnumerable<(string Item, MenuField Field, FlagReason Reason)> Described(this MenuDraft draft) =>
        draft.Flags.Select(f => (draft.Items.Single(i => i.Id == f.MenuItemId).Name, f.Field, f.Reason));
}
