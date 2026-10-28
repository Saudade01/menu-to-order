using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MenuToOrder.Api.Shared;
using MenuToOrder.Data;
using MenuToOrder.Extraction;
using MenuToOrder.Verification;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace MenuToOrder.Tests.Api;

// Drives the real app over HTTP: antiforgery, owner links and the order redirect only exist at this level.
public partial class HttpTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("ConnectionStrings:Menu", postgres.ConnectionString)
            // No key, so menus are read from the recorded responses instead of Gemini.
            .UseSetting("Gemini:ApiKey", ""));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Refreshing_the_receipt_does_not_place_the_order_twice()
    {
        var client = Client();
        var (owner, restaurant) = await Upload(client, "kose-lokantasi");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, $"{owner}/publish", [])).StatusCode);

        var table = $"/m/{restaurant.Slug}/2";
        var menu = await client.GetStringAsync(table, Ct);
        var dish = DishField().Match(menu).Groups[1].Value;
        var order = await Post(client, $"{table}/orders", [("menu", MenuId().Match(menu).Groups[1].Value), ($"qty.{dish}", "2")], from: table);
        Assert.Equal(HttpStatusCode.Redirect, order.StatusCode);

        var receipt = order.Headers.Location!.ToString();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(receipt, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(receipt, Ct)).StatusCode);

        await using var db = postgres.CreateDb();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.RestaurantId == restaurant.Id, Ct));
    }

    [Fact]
    public async Task Sending_the_same_cart_twice_places_one_order()
    {
        var client = Client();
        var (restaurant, table, order) = await Cart(client);

        var first = await Post(client, $"{table}/orders", order, from: table);
        var second = await Post(client, $"{table}/orders", order, from: table);

        Assert.Equal(first.Headers.Location, second.Headers.Location);
        await using var db = postgres.CreateDb();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.RestaurantId == restaurant.Id, Ct));
    }

    [Fact]
    public async Task Two_taps_arriving_together_still_place_one_order()
    {
        var client = Client();
        var (restaurant, table, order) = await Cart(client);

        var answers = await Task.WhenAll(
            Post(client, $"{table}/orders", order, from: table),
            Post(client, $"{table}/orders", order, from: table));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.Redirect, answer.StatusCode));
        Assert.Equal(answers[0].Headers.Location, answers[1].Headers.Location);
        await using var db = postgres.CreateDb();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.RestaurantId == restaurant.Id, Ct));
    }

    [Fact]
    public async Task An_old_or_wrong_qr_code_gets_a_plain_explanation()
    {
        var response = await Client().GetAsync("/m/boyle-bir-yer-yok/1", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("şu an kullanılamıyor", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_script_link_changes_whenever_the_script_does()
    {
        var page = await Client().GetStringAsync("/", Ct);

        var hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Repo.At("src", "Api", "wwwroot", "site.js"), Ct)));
        Assert.Contains($"/site.js?v={hash[..8]}", page);
    }

    [Fact]
    public async Task A_form_sent_without_the_antiforgery_token_is_refused()
    {
        using var form = new MultipartFormDataContent { { new StringContent("Sahte"), "name" }, { new StringContent("2"), "tables" } };

        var response = await Client().PostAsync("/restaurants", form, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task One_owner_link_cannot_answer_another_restaurants_flags()
    {
        var client = Client();
        var (_, victim) = await Upload(client, "sahil-kafe");
        var (stranger, _) = await Upload(client, "kose-lokantasi");

        await using var db = postgres.CreateDb();
        var flag = await db.Flags.FirstAsync(f => db.MenuItems.Any(i => i.Id == f.MenuItemId &&
            db.MenuVersions.Any(v => v.Id == i.MenuVersionId && v.RestaurantId == victim.Id)), Ct);

        var response = await Post(client, $"{stranger}/flags", [("ids", flag.Id.ToString()), ("action", "confirm")]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var check = postgres.CreateDb();
        Assert.Null((await check.Flags.SingleAsync(f => f.Id == flag.Id, Ct)).ResolvedAt);
    }

    [Fact]
    public async Task A_failed_read_names_only_the_pass_that_never_got_an_answer()
    {
        var client = Client();
        var (owner, restaurant) = await Upload(client, "kose-lokantasi");

        var at = DateTimeOffset.UtcNow.AddMinutes(1);
        MenuRead Busy(int pass, int second) => new(pass, "p", "m", "h", null, null, "HTTP 503: busy", TimeSpan.Zero, null, null, at.AddSeconds(second));
        var answered = Busy(2, 20) with { Menu = new ExtractedMenu([], []), RawResponse = "{}", Error = null };
        var draft = Drafts.Build(restaurant.Id, Busy(1, 20), answered, new VerificationOptions(), at.AddSeconds(30));
        await using (var db = postgres.CreateDb())
        {
            db.Add(draft.Version);
            db.AddRange(draft.Runs);
            db.AddRange(new[] { Busy(1, 0), Busy(2, 0) }.Select(r => Drafts.Run(draft.Version, r)));
            await db.SaveChangesAsync(Ct);
        }

        var page = await client.GetStringAsync($"{owner}/review", Ct);

        Assert.Contains("1. okuma: HTTP 503: busy", page);
        Assert.DoesNotContain("2. okuma", page);
    }

    [Fact]
    public async Task A_photo_with_no_dishes_on_it_is_not_blamed_on_gemini()
    {
        var client = Client();
        var (owner, restaurant) = await Upload(client, "kose-lokantasi");

        var at = DateTimeOffset.UtcNow.AddMinutes(1);
        MenuRead Empty(int pass) => new(pass, "p", "m", "h", "{}", new ExtractedMenu([], []), null, TimeSpan.Zero, null, null, at);
        var draft = Drafts.Build(restaurant.Id, Empty(1), Empty(2), new VerificationOptions(), at);
        await using (var db = postgres.CreateDb())
        {
            db.Add(draft.Version);
            db.AddRange(draft.Runs);
            await db.SaveChangesAsync(Ct);
        }

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"{owner}/review", Ct));

        Assert.Contains("Fotoğraftaki yemekler okunamadı", page);
        Assert.DoesNotContain("Gemini", page);
    }

    [Fact]
    public async Task The_order_list_holds_every_order_of_the_day_and_nothing_older()
    {
        var client = Client();
        var (owner, restaurant) = await Upload(client, "kose-lokantasi");
        await Post(client, $"{owner}/publish", []);
        await using (var db = postgres.CreateDb())
        {
            var menu = await db.MenuVersions.Include(v => v.Items).SingleAsync(v => v.RestaurantId == restaurant.Id, Ct);
            var now = DateTimeOffset.UtcNow;
            db.Orders.AddRange(Enumerable.Range(0, 101).Select(_ => new Order(restaurant.Id, menu.Id, 1, null, [(menu.Items[0], 1)], now)));
            db.Orders.Add(new Order(restaurant.Id, menu.Id, 2, null, [(menu.Items[0], 1)], now.AddDays(-2)));
            await db.SaveChangesAsync(Ct);
        }

        var page = await client.GetStringAsync($"{owner}/orders", Ct);

        Assert.Equal(101, Regex.Count(page, "<strong>Masa 1</strong>"));
        Assert.DoesNotContain("<strong>Masa 2</strong>", page);
    }

    [Fact]
    public async Task One_address_cannot_keep_uploading_menus()
    {
        var client = Client();
        var token = Token(await client.GetStringAsync("/", Ct));
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            using var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { new StringContent(""), "name" } };
            statuses.Add((await client.PostAsync("/restaurants", form, Ct)).StatusCode);
        }

        Assert.All(statuses[..10], s => Assert.Equal(HttpStatusCode.BadRequest, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    [Fact]
    public async Task The_same_question_on_several_dishes_is_asked_and_answered_once()
    {
        var client = Client();
        var (owner, restaurant) = await Upload(client, "kose-lokantasi");
        var draft = await SaveDraft(restaurant.Id, Pass(1, "Tek Anayemek", "70 TL"), Pass(2, null, "70 TL"));

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"{owner}/review", Ct));
        var ids = FlagIds().Matches(page).Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.Contains("1 konuda onayınız gerekiyor", page);
        Assert.Equal(draft.Flags.Select(f => f.Id.ToString()).Order(), ids.Order());

        await Post(client, $"{owner}/flags", [.. ids.Select(id => ("ids", id)), ("action", "correct"), ("value", "")], from: $"{owner}/review");

        await using var check = postgres.CreateDb();
        var items = await check.MenuItems.Include(i => i.Flags).Where(i => i.MenuVersionId == draft.Version.Id).ToListAsync(Ct);
        Assert.All(items, i => Assert.Null(i.Category));
        Assert.All(items.SelectMany(i => i.Flags), f => Assert.NotNull(f.ResolvedAt));
    }

    [Fact]
    public async Task Prices_are_never_asked_in_bulk()
    {
        var client = Client();
        var (owner, restaurant) = await Upload(client, "kose-lokantasi");
        await SaveDraft(restaurant.Id, Pass(1, null, "180 TL"), Pass(2, null, "18 TL"));

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"{owner}/review", Ct));

        Assert.Contains("2 konuda onayınız gerekiyor", page);
        Assert.DoesNotContain("yemekte aynı soru", page);
    }

    [Fact]
    public async Task Two_dishes_sharing_a_name_are_asked_one_by_one()
    {
        var client = Client();
        var (owner, _) = await Upload(client, "sahil-kafe");

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"{owner}/review", Ct));

        Assert.Contains("bu bölümde aynı isim iki kez geçiyor", page);
        Assert.DoesNotContain("yemekte aynı soru", page);
    }

    // Two dishes read the same way by both passes except for the given heading and price.
    private static MenuRead Pass(int pass, string? category, string price)
    {
        ExtractedItem Dish(string name) => new(name, category, null, price, []);
        return new(pass, "p", "m", "h", "{}", new ExtractedMenu(category is null ? [] : [category], [Dish("Çorba"), Dish("Pilav")]),
            null, TimeSpan.Zero, null, null, DateTimeOffset.UtcNow);
    }

    // Stored as the newest draft, so the review page shows it instead of the upload's own.
    private async Task<MenuDraft> SaveDraft(Guid restaurantId, MenuRead first, MenuRead second)
    {
        var draft = Drafts.Build(restaurantId, first, second, new VerificationOptions(), DateTimeOffset.UtcNow.AddMinutes(1));
        await using var db = postgres.CreateDb();
        db.Add(draft.Version);
        db.AddRange(draft.Items);
        db.AddRange(draft.Flags);
        db.AddRange(draft.Runs);
        await db.SaveChangesAsync(Ct);
        return draft;
    }

    // A published menu and a reviewed cart, ready to be sent as an order.
    private async Task<(Restaurant Restaurant, string Table, (string, string)[] Order)> Cart(HttpClient client)
    {
        var (owner, restaurant) = await Upload(client, "kose-lokantasi");
        await Post(client, $"{owner}/publish", []);

        var table = $"/m/{restaurant.Slug}/3";
        var menu = await client.GetStringAsync(table, Ct);
        (string, string)[] cart = [("menu", MenuId().Match(menu).Groups[1].Value), ($"qty.{DishField().Match(menu).Groups[1].Value}", "1")];
        var review = await (await Post(client, $"{table}/cart", cart, from: table)).Content.ReadAsStringAsync(Ct);
        return (restaurant, table, [.. cart, ("key", OrderKey().Match(review).Groups[1].Value)]);
    }

    private HttpClient Client() => _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(string Owner, Restaurant Restaurant)> Upload(HttpClient client, string sample)
    {
        var token = Token(await client.GetStringAsync("/", Ct));
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(sample), "name" },
            { new StringContent("4"), "tables" },
            { new ByteArrayContent(await File.ReadAllBytesAsync(Repo.At("samples", "menus", $"{sample}.jpg"), Ct)), "photo", $"{sample}.jpg" },
        };
        var response = await client.PostAsync("/restaurants", form, Ct);
        var owner = response.Headers.Location!.ToString().Replace("/review", "");

        // The read runs in the background; the recorded responses make it quick but not instant.
        await using var db = postgres.CreateDb();
        var restaurant = await db.Restaurants.SingleAsync(r => r.Name == sample && r.OwnerTokenHash == OwnerToken.Hash(owner.Split('/')[2]), Ct);
        for (var i = 0; i < 50 && !await db.MenuVersions.AnyAsync(v => v.RestaurantId == restaurant.Id, Ct); i++)
            await Task.Delay(100, Ct);
        return (owner, restaurant);
    }

    private async Task<HttpResponseMessage> Post(HttpClient client, string url, (string Key, string Value)[] fields, string from = "/")
    {
        var token = Token(await client.GetStringAsync(from, Ct));
        using var form = new FormUrlEncodedContent([new("__RequestVerificationToken", token), .. fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))]);
        return await client.PostAsync(url, form, Ct);
    }

    private static string Token(string html) => AntiforgeryField().Match(html).Groups[1].Value;

    [GeneratedRegex("""name="__RequestVerificationToken" value="([^"]+)" """)]
    private static partial Regex AntiforgeryField();

    [GeneratedRegex("""name="menu" value="([^"]+)" """)]
    private static partial Regex MenuId();

    [GeneratedRegex("""name="qty\.([0-9a-f-]+)" """)]
    private static partial Regex DishField();

    [GeneratedRegex("""name="key" value="([^"]+)" """)]
    private static partial Regex OrderKey();

    [GeneratedRegex("""name="ids" value="([0-9a-f-]{36})" """)]
    private static partial Regex FlagIds();
}
