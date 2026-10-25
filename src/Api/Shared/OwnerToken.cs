using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MenuToOrder.Data;
using Microsoft.EntityFrameworkCore;

namespace MenuToOrder.Api.Shared;

// The owner link is the only credential, so only its hash is stored.
public static class OwnerToken
{
    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static Task<Restaurant?> FindOwnerAsync(this MenuDb db, string token, CancellationToken ct)
    {
        var hash = Hash(token);
        return db.Restaurants.SingleOrDefaultAsync(r => r.OwnerTokenHash == hash, ct);
    }

    public static Task<bool> OwnsFlagAsync(this MenuDb db, Guid restaurantId, Guid flagId, CancellationToken ct) =>
        (from f in db.Flags
         join i in db.MenuItems on f.MenuItemId equals i.Id
         join v in db.MenuVersions on i.MenuVersionId equals v.Id
         where f.Id == flagId && v.RestaurantId == restaurantId
         select f).AnyAsync(ct);
}
