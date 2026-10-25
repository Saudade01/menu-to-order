using MenuToOrder.Data;

namespace MenuToOrder.Api.Guest;

public static class Checkout
{
    public const string MenuChanged = "Menü az önce güncellendi. Lütfen siparişinize yeniden göz atın.";

    // Reads "qty.{itemId}" fields. Only ids and counts come from the form, never a price.
    public static (Dictionary<Guid, int>? Cart, string? Error) Read(IFormCollection form)
    {
        var cart = new Dictionary<Guid, int>();
        foreach (var (key, value) in form)
        {
            if (!key.StartsWith("qty.", StringComparison.Ordinal))
                continue;
            if (!Guid.TryParse(key.AsSpan(4), out var id) || !int.TryParse(value, out var quantity) || quantity is < 0 or > Order.MaxQuantity)
                return (null, $"Her yemekten 0 ile {Order.MaxQuantity} arasında sipariş verilebilir.");
            if (quantity > 0)
                cart[id] = quantity;
        }

        return cart.Count == 0 ? (null, "En az bir yemek seçin.") : (cart, null);
    }

    public static (Order? Order, string? Error) Price(MenuVersion menu, IEnumerable<MenuItem> items, Guid seenMenuId,
        IReadOnlyDictionary<Guid, int> cart, int table, string? note, DateTimeOffset now, Guid? key = null)
    {
        var byId = items.ToDictionary(i => i.Id);
        if (seenMenuId != menu.Id || cart.Keys.Any(id => !byId.ContainsKey(id)))
            return (null, MenuChanged);

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > Order.MaxNoteLength)
            return (null, $"Not en fazla {Order.MaxNoteLength} karakter olabilir.");

        var lines = cart.Select(c => (byId[c.Key], c.Value)).OrderBy(l => l.Item1.Position);
        return (new Order(menu.RestaurantId, menu.Id, table, note, lines, now, key), null);
    }
}
