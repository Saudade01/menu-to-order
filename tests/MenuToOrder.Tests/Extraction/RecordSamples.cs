using MenuToOrder.Extraction;

namespace MenuToOrder.Tests.Extraction;

// Not part of the normal run. Calls Gemini for real and refreshes samples/recorded:
// GEMINI_API_KEY=... dotnet run --project tests/MenuToOrder.Tests -- -explicit only
public class RecordSamples
{
    [Fact(Explicit = true)]
    public async Task Record_live_responses_for_the_sample_menus()
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        Assert.SkipWhen(string.IsNullOrEmpty(key), "Set GEMINI_API_KEY to record.");

        var ct = TestContext.Current.CancellationToken;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
        var reader = new GeminiMenuReader(http, new GeminiOptions(key!), TimeProvider.System);
        var target = Directory.CreateDirectory(Repo.At("samples", "recorded")).FullName;

        foreach (var file in Directory.GetFiles(Repo.At("samples", "menus"), "*.jpg"))
        {
            var image = new MenuImage(await File.ReadAllBytesAsync(file, ct), "image/jpeg");
            foreach (var pass in new[] { 1, 2 })
            {
                var read = await reader.ReadAsync(image, pass, ct);
                // The free tier answers 503 under load; retrying here is fine because this only records samples.
                for (var attempt = 1; read.Menu is null && attempt < 5; attempt++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15 * attempt), ct);
                    read = await reader.ReadAsync(image, pass, ct);
                }

                Assert.True(read.Menu is not null, $"{Path.GetFileName(file)}, pass {pass}: {read.Error}");
                await File.WriteAllTextAsync(Path.Combine(target, RecordedMenuReader.FileName(image, Prompts.For(pass))), read.RawResponse, ct);
            }
        }
    }
}
