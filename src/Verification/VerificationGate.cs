using MenuToOrder.Data;
using Microsoft.EntityFrameworkCore;

namespace MenuToOrder.Verification;

public abstract record PublishOutcome
{
    public sealed record Published(DateTimeOffset At) : PublishOutcome;
    public sealed record Blocked(string Reason) : PublishOutcome;
    public sealed record AlreadyPublished : PublishOutcome;
    public sealed record NotFound : PublishOutcome;
}

// The only way a menu reaches the public page. MenuVersion.Publish is internal to Data and visible here alone.
public sealed class VerificationGate(MenuDb db, TimeProvider time)
{
    public async Task<PublishOutcome> PublishAsync(Guid versionId, CancellationToken ct)
    {
        var version = await db.MenuVersions
            .Include(v => v.Items).ThenInclude(i => i.Flags)
            .SingleOrDefaultAsync(v => v.Id == versionId, ct);

        if (version is null)
            return new PublishOutcome.NotFound();
        if (version.Status == MenuStatus.Published)
            return new PublishOutcome.AlreadyPublished();
        if (version.Items.Count == 0)
            return new PublishOutcome.Blocked("Menüde hiç ürün yok.");

        var open = version.Items.Sum(i => i.Flags.Where(f => f.ResolvedAt is null).DistinctBy(f => f.Field).Count());
        if (open > 0)
            return new PublishOutcome.Blocked($"{open} alan hâlâ sizin onayınızı bekliyor.");

        // Unreadable prices are always flagged, but the gate checks directly instead of trusting that.
        if (version.Items.Any(i => i.Price is null))
            return new PublishOutcome.Blocked("Fiyatı olmayan bir ürün var.");

        var at = time.GetUtcNow();
        version.Publish(at);
        await db.SaveChangesAsync(ct);
        return new PublishOutcome.Published(at);
    }
}
