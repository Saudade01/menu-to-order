using System.Globalization;
using MenuToOrder.Data;
using MenuToOrder.Extraction;
using Microsoft.EntityFrameworkCore;

namespace MenuToOrder.Verification;

public abstract record ReviewOutcome
{
    public sealed record Done : ReviewOutcome;
    public sealed record Rejected(string Reason) : ReviewOutcome;
    public sealed record NotFound : ReviewOutcome;
}

// How the owner answers flags on a draft. A correction is logged; a plain confirmation is not,
// so corrections over flags resolved is the first-pass error rate.
public sealed class Review(MenuDb db, TimeProvider time)
{
    public async Task<ReviewOutcome> ConfirmAsync(Guid flagId, CancellationToken ct)
    {
        var (flag, item, outcome) = await Load(flagId, ct);
        if (outcome is not null)
            return outcome;
        if (flag!.Field == MenuField.Price && item!.Price is null)
            return new ReviewOutcome.Rejected("There is no price to confirm; enter one instead.");

        var now = time.GetUtcNow();
        foreach (var f in item!.Flags.Where(f => f.Field == flag.Field))
            f.Resolve(now);

        await db.SaveChangesAsync(ct);
        return new ReviewOutcome.Done();
    }

    public async Task<ReviewOutcome> CorrectAsync(Guid flagId, string value, CancellationToken ct)
    {
        var (flag, item, outcome) = await Load(flagId, ct);
        if (outcome is not null)
            return outcome;
        if (string.IsNullOrWhiteSpace(value) && flag!.Field is MenuField.Name or MenuField.Price)
            return new ReviewOutcome.Rejected("This field cannot be empty.");

        var now = time.GetUtcNow();
        string? old;
        if (flag!.Field == MenuField.Price)
        {
            var amount = PriceText.Parse(value).Amount;
            if (amount is null)
                return new ReviewOutcome.Rejected("That is not a price we can read. Write it like 180 or 185,50.");

            old = item!.Price?.ToString(CultureInfo.InvariantCulture);
            item.CorrectPrice(amount.Value);
            value = amount.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            old = flag.Field switch { MenuField.Name => item!.Name, MenuField.Category => item!.Category, _ => item!.Description };
            item.Correct(flag.Field, value.Trim());
        }

        db.Corrections.Add(new Correction(item.Id, flag.Field, old, value, now));
        // One answer settles every question about that field.
        foreach (var f in item.Flags.Where(f => f.Field == flag.Field))
            f.Resolve(now);

        await db.SaveChangesAsync(ct);
        return new ReviewOutcome.Done();
    }

    // The owner's single answer to "are the prices on this menu in lira?".
    public async Task<ReviewOutcome> ConfirmLiraAsync(Guid versionId, CancellationToken ct)
    {
        var version = await db.MenuVersions.Include(v => v.Items).ThenInclude(i => i.Flags)
            .SingleOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
            return new ReviewOutcome.NotFound();
        if (version.Status == MenuStatus.Published)
            return new ReviewOutcome.Rejected("A published menu cannot change.");

        var now = time.GetUtcNow();
        foreach (var flag in version.Items.SelectMany(i => i.Flags).Where(f => f.Reason == FlagReason.CurrencyMissing))
            flag.Resolve(now);

        await db.SaveChangesAsync(ct);
        return new ReviewOutcome.Done();
    }

    private async Task<(Flag?, MenuItem?, ReviewOutcome?)> Load(Guid flagId, CancellationToken ct)
    {
        var flag = await db.Flags.SingleOrDefaultAsync(f => f.Id == flagId, ct);
        if (flag is null)
            return (null, null, new ReviewOutcome.NotFound());

        var item = await db.MenuItems.Include(i => i.Flags).SingleAsync(i => i.Id == flag.MenuItemId, ct);
        var status = await db.MenuVersions.Where(v => v.Id == item.MenuVersionId).Select(v => v.Status).SingleAsync(ct);
        return status == MenuStatus.Published
            ? (flag, item, new ReviewOutcome.Rejected("A published menu cannot change."))
            : (flag, item, null);
    }
}
