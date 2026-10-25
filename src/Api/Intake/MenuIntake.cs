using MenuToOrder.Data;
using MenuToOrder.Extraction;
using MenuToOrder.Verification;

namespace MenuToOrder.Api.Intake;

public sealed class MenuIntake(IMenuReader reader, MenuDb db, VerificationOptions options, TimeProvider time)
{
    // Saves both runs and the draft together, whatever the reads returned.
    public async Task<MenuDraft> ReadAsync(Guid restaurantId, MenuImage image, CancellationToken ct)
    {
        var reads = await Task.WhenAll(reader.ReadAsync(image, 1, ct), reader.ReadAsync(image, 2, ct));
        var draft = Drafts.Build(restaurantId, reads[0], reads[1], options, time.GetUtcNow());

        db.Add(draft.Version);
        db.AddRange(draft.Items);
        db.AddRange(draft.Flags);
        db.AddRange(draft.Runs);
        await db.SaveChangesAsync(ct);
        return draft;
    }

    // A read takes minutes on the free tier, so it must not depend on the owner's browser staying connected.
    // shortcut: runs inside the web process and is lost if the app stops mid-read; use a queue before deploying.
    public static void Start(IServiceProvider services, Guid restaurantId, MenuImage image)
    {
        // Taken now: the request's own provider is disposed once the redirect goes out.
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        var stopping = services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        var log = services.GetRequiredService<ILogger<MenuIntake>>();
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MenuIntake>().ReadAsync(restaurantId, image, stopping);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Reading the menu for restaurant {RestaurantId} failed", restaurantId);
            }
        });
    }
}
