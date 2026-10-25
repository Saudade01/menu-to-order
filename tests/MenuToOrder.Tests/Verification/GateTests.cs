using MenuToOrder.Data;
using MenuToOrder.Verification;
using Microsoft.EntityFrameworkCore;

namespace MenuToOrder.Tests.Verification;

public class GateTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_clean_menu_goes_straight_through()
    {
        var draft = await Save("kose-lokantasi");

        await using var db = postgres.CreateDb();
        var outcome = await Gate(db).PublishAsync(draft.Version.Id, Ct);

        Assert.IsType<PublishOutcome.Published>(outcome);
        Assert.Equal(MenuStatus.Published, (await db.MenuVersions.SingleAsync(v => v.Id == draft.Version.Id, Ct)).Status);
    }

    [Fact]
    public async Task Open_flags_keep_a_menu_off_the_public_page()
    {
        var draft = await Save("sahil-kafe");

        await using var db = postgres.CreateDb();
        var outcome = await Gate(db).PublishAsync(draft.Version.Id, Ct);

        Assert.Equal(new PublishOutcome.Blocked("6 flagged field(s) still need the owner."), outcome);
        Assert.Equal(MenuStatus.Draft, (await db.MenuVersions.SingleAsync(v => v.Id == draft.Version.Id, Ct)).Status);
    }

    [Fact]
    public async Task The_hard_menu_publishes_once_the_owner_has_answered_everything()
    {
        var draft = await Save("sahil-kafe");
        var flags = draft.Described().Zip(draft.Flags, (d, f) => (d.Item, d.Field, d.Reason, f.Id)).ToList();
        Guid Flag(string item, FlagReason reason) => flags.First(f => f.Item == item && f.Reason == reason).Id;

        await using (var db = postgres.CreateDb())
        {
            var review = new Review(db, TimeProvider.System);
            Assert.IsType<ReviewOutcome.Done>(await review.CorrectAsync(Flag("Köfte", FlagReason.PriceUnreadable), "180", Ct));
            Assert.IsType<ReviewOutcome.Done>(await review.CorrectAsync(Flag("Günün Balığı", FlagReason.PriceUnreadable), "450", Ct));
            Assert.IsType<ReviewOutcome.Done>(await review.CorrectAsync(Flag("Limonata", FlagReason.ForeignCurrency), "120 TL", Ct));
            foreach (var duplicate in flags.Where(f => f.Reason == FlagReason.DuplicateName))
                Assert.IsType<ReviewOutcome.Done>(await review.ConfirmAsync(duplicate.Id, Ct));
            Assert.IsType<ReviewOutcome.Done>(await review.ConfirmLiraAsync(draft.Version.Id, Ct));
        }

        await using var check = postgres.CreateDb();
        Assert.IsType<PublishOutcome.Published>(await Gate(check).PublishAsync(draft.Version.Id, Ct));
        var kofte = ItemOf(draft, Flag("Köfte", FlagReason.PriceUnreadable));
        Assert.Equal(180m, (await check.MenuItems.SingleAsync(i => i.Id == kofte, Ct)).Price);
        var itemIds = draft.Items.Select(i => i.Id).ToList();
        Assert.Equal(3, await check.Corrections.CountAsync(c => itemIds.Contains(c.MenuItemId), Ct));
    }

    [Fact]
    public async Task A_published_menu_cannot_be_published_or_corrected_again()
    {
        var draft = await Save("mangal-evi");
        await using var db = postgres.CreateDb();
        var review = new Review(db, TimeProvider.System);
        foreach (var flag in draft.Flags)
            await review.ConfirmAsync(flag.Id, Ct);
        await Gate(db).PublishAsync(draft.Version.Id, Ct);

        Assert.IsType<PublishOutcome.AlreadyPublished>(await Gate(db).PublishAsync(draft.Version.Id, Ct));
        Assert.Equal(new ReviewOutcome.Rejected("A published menu cannot change."),
            await review.CorrectAsync(draft.Flags[0].Id, "999", Ct));
    }

    [Fact]
    public async Task An_unreadable_price_has_to_be_entered_not_waved_through()
    {
        var draft = await Save("sahil-kafe");
        var unreadable = draft.Flags.First(f => f.Reason == FlagReason.PriceUnreadable);

        await using var db = postgres.CreateDb();
        var review = new Review(db, TimeProvider.System);

        Assert.IsType<ReviewOutcome.Rejected>(await review.ConfirmAsync(unreadable.Id, Ct));
        Assert.IsType<ReviewOutcome.Rejected>(await review.CorrectAsync(unreadable.Id, "yarım porsiyon", Ct));
        Assert.Null((await db.Flags.SingleAsync(f => f.Id == unreadable.Id, Ct)).ResolvedAt);
    }

    [Fact]
    public async Task Confirming_a_field_answers_every_flag_on_it()
    {
        var draft = await Save("sahil-kafe");
        var limonata = draft.Items.Single(i => i.Name == "Limonata");
        var flags = draft.Flags.Where(f => f.MenuItemId == limonata.Id).ToList();

        await using var db = postgres.CreateDb();
        await new Review(db, TimeProvider.System).ConfirmAsync(flags[0].Id, Ct);

        var ids = flags.Select(f => f.Id).ToList();
        Assert.Equal(2, ids.Count);
        Assert.All(await db.Flags.Where(f => ids.Contains(f.Id)).ToListAsync(Ct), f => Assert.NotNull(f.ResolvedAt));
    }

    private static VerificationGate Gate(MenuDb db) => new(db, TimeProvider.System);

    private static Guid ItemOf(MenuDraft draft, Guid flagId) => draft.Flags.Single(f => f.Id == flagId).MenuItemId;

    private async Task<MenuDraft> Save(string sample) => (await Samples.Save(postgres, sample, Ct)).Draft;
}
