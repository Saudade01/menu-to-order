using System.Net;
using System.Text.RegularExpressions;
using MenuToOrder.Api.Shared;
using MenuToOrder.Data;
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

        var response = await Post(client, $"{stranger}/flags/{flag.Id}", [("action", "confirm")]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var check = postgres.CreateDb();
        Assert.Null((await check.Flags.SingleAsync(f => f.Id == flag.Id, Ct)).ResolvedAt);
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
}
