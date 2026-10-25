using MenuToOrder.Api.Intake;
using MenuToOrder.Api.Shared;
using MenuToOrder.Tests.Verification;

namespace MenuToOrder.Tests.Api;

public class OwnerPiecesTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Köşe Lokantası", "^kose-lokantasi-[0-9a-f]{6}$")]
    [InlineData("Çiğ Köfte & Ayran!", "^cig-kofte-ayran-[0-9a-f]{6}$")]
    [InlineData("!!!", "^menu-[0-9a-f]{6}$")]
    public void Slugs_are_readable_ascii_with_a_random_tail(string name, string pattern) =>
        Assert.Matches(pattern, Slug.For(name));

    [Fact]
    public void Two_restaurants_with_the_same_name_get_different_slugs() =>
        Assert.NotEqual(Slug.For("Sahil Kafe"), Slug.For("Sahil Kafe"));

    [Fact]
    public void The_owner_link_is_never_stored_in_the_clear()
    {
        var token = OwnerToken.New();

        Assert.Equal(43, token.Length);
        Assert.Equal(OwnerToken.Hash(token), OwnerToken.Hash(token));
        Assert.NotEqual(OwnerToken.Hash(token), OwnerToken.Hash(OwnerToken.New()));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C }, null)]
    [InlineData(new byte[0], null)]
    public void Photos_are_judged_by_their_bytes(byte[] head, string? expected) =>
        Assert.Equal(expected, PhotoCheck.MediaType(head));

    [Fact]
    public void An_iphone_heic_photo_is_recognised_so_the_owner_gets_a_useful_message()
    {
        byte[] heic = [0, 0, 0, 24, .. "ftypheic"u8];

        Assert.Null(PhotoCheck.MediaType(heic));
        Assert.True(PhotoCheck.IsHeic(heic));
        Assert.False(PhotoCheck.IsHeic(File.ReadAllBytes(Repo.At("samples", "menus", "kose-lokantasi.jpg"))));
    }

    [Theory]
    [InlineData("185.5", "185,50 ₺")]
    [InlineData("1250", "1.250 ₺")]
    [InlineData("20", "20 ₺")]
    public void Prices_print_the_turkish_way(string amount, string expected) =>
        Assert.Equal(expected, Money.Lira(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task One_restaurant_cannot_answer_another_restaurants_flags()
    {
        var (owner, draft) = await Samples.Save(postgres, "mangal-evi", Ct);
        var (stranger, _) = await Samples.Save(postgres, "kose-lokantasi", Ct);
        var flag = draft.Flags[0].Id;

        await using var db = postgres.CreateDb();

        Assert.True(await db.OwnsFlagAsync(owner.Id, flag, Ct));
        Assert.False(await db.OwnsFlagAsync(stranger.Id, flag, Ct));
    }
}
