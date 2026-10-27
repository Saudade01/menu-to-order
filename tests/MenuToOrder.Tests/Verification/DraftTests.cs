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
                ("Serpme Kahvaltı (2 kişilik)", MenuField.Name, FlagReason.PassesDisagree),
                ("Köfte", MenuField.Name, FlagReason.DuplicateName),
                ("Köfte", MenuField.Price, FlagReason.PriceUnreadable),
                ("Tavuk Şiş", MenuField.Price, FlagReason.CurrencyMissing),
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
    public void A_failed_pass_saves_no_dishes_for_the_owner_to_check()
    {
        var failed = Read(2) with { Menu = null, RawResponse = null, Error = "HTTP 503" };

        var draft = Build(Read(1, Item("Ayran", "35 ₺")), failed);

        Assert.Empty(draft.Items);
        Assert.Empty(draft.Flags);
        Assert.NotNull(draft.Runs.Single(r => r.Pass == 1).RawResponse);
        Assert.Equal("HTTP 503", draft.Runs.Single(r => r.Pass == 2).Error);
    }

    [Fact]
    public void A_pass_that_found_no_dishes_saves_none_either()
    {
        var draft = Build(Read(1, Item("Ayran", "35 ₺")), Read(2));

        Assert.Empty(draft.Items);
        Assert.Empty(draft.Flags);
    }

    [Fact]
    public void A_category_the_menu_does_not_have_is_flagged()
    {
        var invented = Item("Ayran", "35 ₺") with { Category = "KAMPANYA" };
        var first = Read(1, invented) with { Menu = new ExtractedMenu(["İÇECEKLER"], [invented]) };

        var draft = Build(first, first with { Pass = 2 });

        Assert.Equal(("Ayran", MenuField.Category, FlagReason.UnknownCategory), Assert.Single(draft.Described()));
    }

    [Fact]
    public void A_dish_filed_under_different_sections_stays_one_dish()
    {
        var draft = Build(
            Read(1, new ExtractedItem("Sosis Tabağı", "IZGARALAR", null, "280 TL", [])),
            Read(2, new ExtractedItem("Sosis Tabağı", null, null, "280 TL", [])) with
            {
                Menu = new ExtractedMenu(["IZGARALAR"], [new ExtractedItem("Sosis Tabağı", null, null, "280 TL", [])]),
            });

        Assert.Single(draft.Items);
        Assert.Equal(("Sosis Tabağı", MenuField.Category, FlagReason.PassesDisagree), Assert.Single(draft.Described()));
    }

    [Fact]
    public void A_name_misread_by_a_letter_stays_one_dish_with_both_readings()
    {
        var draft = Build(Read(1, Item("Serpme Kahvaltı (2 kişilik)", "1.250,00 TL")),
            Read(2, Item("Serpme Kahvaltı (2 kiőilik)", "1.250,00 TL")));

        var flag = Assert.Single(draft.Flags);
        Assert.Single(draft.Items);
        Assert.Equal((MenuField.Name, "Serpme Kahvaltı (2 kişilik)", "Serpme Kahvaltı (2 kiőilik)"),
            (flag.Field, flag.FirstPassValue, flag.SecondPassValue));
    }

    [Fact]
    public void The_same_letters_in_a_different_unicode_form_are_the_same_name()
    {
        var draft = Build(Read(1, Item("Tavuk \u015Ei\u015F", "290 TL")), Read(2, Item("Tavuk \u015Eis\u0327", "290 TL")));

        Assert.Empty(draft.Flags);
        Assert.Equal("Tavuk Şiş", Assert.Single(draft.Items).Name);
    }

    [Fact]
    public void Different_dishes_are_not_forced_together()
    {
        var draft = Build(Read(1, Item("Künefe", "240 TL")), Read(2, Item("Sufle", "175 TL")));

        Assert.Equal(2, draft.Items.Count);
    }

    [Fact]
    public void A_dish_printed_outside_any_heading_is_not_questioned()
    {
        var loose = new ExtractedItem("Menemen", null, null, "160 TL", []);
        var read = Read(1, loose) with { Menu = new ExtractedMenu(["İÇECEKLER"], [loose]) };

        Assert.Empty(Build(read, read with { Pass = 2 }).Flags);
    }

    [Theory]
    [InlineData("15 TL", false)]
    [InlineData("4 TL", true)]
    [InlineData("12.500 TL", true)]
    public void Prices_outside_ten_to_ten_thousand_lira_are_questioned(string price, bool flagged)
    {
        var read = Read(1, Item("Su", price));

        Assert.Equal(flagged, Build(read, read with { Pass = 2 }).Flags.Any(f => f.Reason == FlagReason.PriceOutOfRange));
    }

    [Theory]
    [InlineData("ÇORBA + ANAYEMEK + PİLAV", "ÇORBA + ANA YEMEK + PİLAV", false)]
    [InlineData("PORSİYON", "PORSIYON", false)]
    [InlineData("Büyük sandviç ekmek", "Buyuk sandvic ekmek", false)]
    [InlineData("Kaşarlı", "Kaşarsız", true)]
    [InlineData("Acılı", "Acısız", true)]
    public void Descriptions_that_differ_only_in_spacing_or_accents_are_not_questioned(string first, string second, bool flagged)
    {
        var draft = Build(Read(1, Item("Günün Menüsü", "170 TL") with { Description = first }),
            Read(2, Item("Günün Menüsü", "170 TL") with { Description = second }));

        Assert.Equal(flagged, draft.Flags.Any(f => f.Field == MenuField.Description));
    }

    [Theory]
    [InlineData("ÇORBA + ANAYEMEK + PİLAV", "ÇORBA+ANAYEMEK+PİLAV", false)]
    [InlineData("TEK ANAYEMEK", "TEK ANA YEMEK", false)]
    [InlineData("AMERİCANO", "AMERICANO", false)]
    [InlineData("GAZOZ", "GAZÖZ", false)]
    [InlineData("Kiremitte Köfte", "Kiremitte Köfle", true)]
    public void Names_that_differ_only_in_spacing_or_accents_are_one_dish_and_not_questioned(string first, string second, bool flagged)
    {
        var draft = Build(Read(1, Item(first, "170 TL")), Read(2, Item(second, "170 TL")));

        Assert.Single(draft.Items);
        Assert.Equal(flagged, draft.Flags.Any(f => f.Field == MenuField.Name));
    }

    private static MenuDraft Build(MenuRead first, MenuRead second) =>
        Drafts.Build(Guid.CreateVersion7(), first, second, new VerificationOptions(), DateTimeOffset.UtcNow);

    private static ExtractedItem Item(string name, string price) => new(name, "İÇECEKLER", null, price, []);

    private static MenuRead Read(int pass, params ExtractedItem[] items) => new(
        pass, $"test-{pass}", "test-model", "hash", "{}",
        new ExtractedMenu(items.Select(i => i.Category!).Distinct().ToList(), items),
        null, TimeSpan.Zero, null, null, DateTimeOffset.UtcNow);
}
