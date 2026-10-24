using MenuToOrder.Data;
using MenuToOrder.Extraction;
using MenuToOrder.Verification;

namespace MenuToOrder.Tests.Verification;

public class DraftTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_clean_menu_read_the_same_way_twice_raises_no_flags()
    {
        var draft = await Samples.Draft(Guid.CreateVersion7(), "kose-lokantasi", Ct);

        Assert.Equal(11, draft.Items.Count);
        Assert.Empty(draft.Flags);
        Assert.Equal(20m, draft.Items.Single(i => i.Name == "Çay").Price);
    }

    [Fact]
    public async Task Prices_the_model_called_unclear_are_flagged_and_nothing_else_is()
    {
        var draft = await Samples.Draft(Guid.CreateVersion7(), "mangal-evi", Ct);

        Assert.Equal(
            [("Haydari", MenuField.Price, FlagReason.LowLegibility),
             ("Acılı Ezme", MenuField.Price, FlagReason.LowLegibility),
             ("Humus", MenuField.Price, FlagReason.LowLegibility)],
            draft.Described());
    }

    [Fact]
    public async Task Every_trap_on_the_hard_menu_is_caught()
    {
        var draft = await Samples.Draft(Guid.CreateVersion7(), "sahil-kafe", Ct);

        Assert.Equal(
            new HashSet<(string, MenuField, FlagReason)>
            {
                ("Köfte", MenuField.Name, FlagReason.DuplicateName),
                ("Köfte", MenuField.Price, FlagReason.PriceUnreadable),
                ("Tavuk Şiş", MenuField.Price, FlagReason.CurrencyMissing),
                ("Günün Balığı", MenuField.Price, FlagReason.PassesDisagree),
                ("Günün Balığı", MenuField.Price, FlagReason.PriceUnreadable),
                ("Limonata", MenuField.Price, FlagReason.PriceOutOfRange),
                ("Limonata", MenuField.Price, FlagReason.ForeignCurrency),
            },
            draft.Described().ToHashSet());
        Assert.Equal(2, draft.Flags.Count(f => f.Reason == FlagReason.DuplicateName));
        Assert.Equal(1250.00m, draft.Items.Single(i => i.Name.StartsWith("Serpme")).Price);
    }

    [Fact]
    public void When_the_passes_read_different_prices_both_are_kept_for_the_owner()
    {
        var draft = Build(Read(1, Item("Adana Kebap", "180 ₺")), Read(2, Item("Adana Kebap", "18 ₺")));

        var flag = Assert.Single(draft.Flags);
        Assert.Equal((MenuField.Price, FlagReason.PassesDisagree), (flag.Field, flag.Reason));
        Assert.Equal(("180 ₺", "18 ₺"), (flag.FirstPassValue, flag.SecondPassValue));
        Assert.True(flag.Confidence < new VerificationOptions().PublishThreshold);
    }

    [Fact]
    public void A_dish_only_one_pass_saw_is_kept_and_flagged()
    {
        var draft = Build(Read(1, Item("Ayran", "35 ₺")), Read(2, Item("Ayran", "35 ₺"), Item("Şalgam", "40 ₺")));

        var salgam = draft.Items.Single(i => i.Name == "Şalgam");
        Assert.Contains(draft.Flags, f => f.MenuItemId == salgam.Id && f.Reason == FlagReason.PassesDisagree);
        Assert.DoesNotContain(draft.Flags, f => f.MenuItemId == draft.Items.Single(i => i.Name == "Ayran").Id);
    }

    [Fact]
    public void A_failed_pass_leaves_every_field_unconfirmed()
    {
        var failed = Read(2) with { Menu = null, RawResponse = null, Error = "HTTP 503" };

        var draft = Build(Read(1, Item("Ayran", "35 ₺")), failed);

        Assert.Single(draft.Items);
        Assert.All(Enum.GetValues<MenuField>(), field =>
            Assert.Contains(draft.Flags, f => f.Field == field && f.Reason == FlagReason.PassesDisagree));
        Assert.Equal("HTTP 503", draft.Runs.Single(r => r.Pass == 2).Error);
    }

    [Fact]
    public void A_category_the_menu_does_not_have_is_flagged()
    {
        var invented = Item("Ayran", "35 ₺") with { Category = "KAMPANYA" };
        var first = Read(1, invented) with { Menu = new ExtractedMenu(["İÇECEKLER"], [invented]) };

        var draft = Build(first, first with { Pass = 2 });

        Assert.Equal(("Ayran", MenuField.Category, FlagReason.UnknownCategory), Assert.Single(draft.Described()));
    }

    private static MenuDraft Build(MenuRead first, MenuRead second) =>
        Drafts.Build(Guid.CreateVersion7(), first, second, new VerificationOptions(), DateTimeOffset.UtcNow);

    private static ExtractedItem Item(string name, string price) => new(name, "İÇECEKLER", null, price, []);

    private static MenuRead Read(int pass, params ExtractedItem[] items) => new(
        pass, $"test-{pass}", "test-model", "hash", "{}",
        new ExtractedMenu(items.Select(i => i.Category!).Distinct().ToList(), items),
        null, TimeSpan.Zero, null, null, DateTimeOffset.UtcNow);
}
