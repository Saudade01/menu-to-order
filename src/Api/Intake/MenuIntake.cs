using MenuToOrder.Data;
using MenuToOrder.Extraction;
using MenuToOrder.Verification;

namespace MenuToOrder.Api.Intake;

public sealed class MenuIntake(IMenuReader reader, MenuDb db, VerificationOptions options, TimeProvider time)
{
    // Saves the restaurant, both runs and the draft together, whatever the reads returned.
    public async Task<MenuDraft> ReadAsync(Restaurant restaurant, MenuImage image, CancellationToken ct)
    {
        var reads = await Task.WhenAll(reader.ReadAsync(image, 1, ct), reader.ReadAsync(image, 2, ct));
        var draft = Drafts.Build(restaurant.Id, reads[0], reads[1], options, time.GetUtcNow());

        db.Add(restaurant);
        db.Add(draft.Version);
        db.AddRange(draft.Items);
        db.AddRange(draft.Flags);
        db.AddRange(draft.Runs);
        await db.SaveChangesAsync(ct);
        return draft;
    }
}
