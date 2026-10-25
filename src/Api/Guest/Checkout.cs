using MenuToOrder.Data;

namespace MenuToOrder.Api.Guest;

public static class Checkout
{
    public const string MenuChanged = "The menu was just updated. Please check your order again.";

    // Reads "qty.{itemId}" fields. Only ids and counts come from the form, never a price.
    public static (Dictionary<Guid, int>? Cart, string? Error) Read(IFormCollection form)
    {
        var cart = new Dictionary<Guid, int>();
        foreach (var (key, value) in form)
        {
            if (!key.StartsWith("qty.", StringComparison.Ordinal))
                continue;
            if (!Guid.TryParse(key.AsSpan(4), out var id) || !int.TryParse(value, out var quantity) || quantity is < 0 or > Order.MaxQuantity)
                return (null, $"Each dish can be ordered 0 to {Order.MaxQuantity} times.");
            if (quantity > 0)
                cart[id] = quantity;
        }

        return cart.Count == 0 ? (null, "Pick at least one dish.") : (cart, null);
    }

    public static (Order? Order, string? Error) Price(MenuVersion menu, IEnumerable<MenuItem> items, Guid seenMenuId,
        IReadOnlyDictionary<Guid, int> cart, int table, string? note, DateTimeOffset now)
    {
        var byId = items.ToDictionary(i => i.Id);
        if (seenMenuId != menu.Id || cart.Keys.Any(id => !byId.ContainsKey(id)))
            return (null, MenuChanged);

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > Order.MaxNoteLength)
            return (null, $"Keep the note under {Order.MaxNoteLength} characters.");

        var lines = cart.Select(c => (byId[c.Key], c.Value)).OrderBy(l => l.Item1.Position);
        return (new Order(menu.RestaurantId, menu.Id, table, note, lines, now), null);
    }
}
