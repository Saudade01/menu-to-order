using MenuToOrder.Data;
using MenuToOrder.Extraction;
using MenuToOrder.Verification;

namespace MenuToOrder.Api.Intake;

public sealed class MenuIntake(IEnumerable<IMenuReader> readers, MenuDb db, VerificationOptions options, TimeProvider time,
    IReadOnlyList<TimeSpan>? retryDelays = null)
{
    private static readonly TimeSpan[] BusyRetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    private readonly IMenuReader[] _readers = [.. readers];

    // Saves the draft and every model call behind it, whatever the reads returned.
    public async Task<MenuDraft> ReadAsync(Guid restaurantId, MenuImage image, CancellationToken ct)
    {
        var attempts = await Task.WhenAll(Attempts(image, 1, ct), Attempts(image, 2, ct));
        var draft = Drafts.Build(restaurantId, attempts[0][^1], attempts[1][^1], options, time.GetUtcNow());

        db.Add(draft.Version);
        db.AddRange(draft.Items);
        db.AddRange(draft.Flags);
        db.AddRange(draft.Runs);
        db.AddRange(attempts.SelectMany(a => a.SkipLast(1)).Select(read => Drafts.Run(draft.Version, read)));
        await db.SaveChangesAsync(ct);
        return draft;
    }

    private async Task<List<MenuRead>> Attempts(MenuImage image, int pass, CancellationToken ct)
    {
        var reads = new List<MenuRead> { await _readers[0].ReadAsync(image, pass, ct) };
        foreach (var delay in retryDelays ?? BusyRetryDelays)
        {
            if (!Busy(reads[^1]))
                break;
            await Task.Delay(delay, ct);
            // A busy model tends to stay busy for a while, so each retry goes to the next model, staying on the last.
            reads.Add(await _readers[Math.Min(reads.Count, _readers.Length - 1)].ReadAsync(image, pass, ct));
        }

        return reads;
    }

    // Only Gemini saying it is busy, or the network failing, is worth another try; a bad answer is not.
    private static bool Busy(MenuRead read) => read.Menu is null && read.Error is { } error &&
        (error.StartsWith("HTTP 503") || error.StartsWith("HTTP 429") || error.StartsWith("HTTP 500") || error.StartsWith("The request failed"));

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
