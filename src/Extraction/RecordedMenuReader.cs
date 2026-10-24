namespace MenuToOrder.Extraction;

// Replays responses recorded from real Gemini calls, so the app and the tests run without a key.
public sealed class RecordedMenuReader(string directory, TimeProvider time) : IMenuReader
{
    public static string FileName(MenuImage image, Prompt prompt) => $"{image.Hash}.{prompt.Version}.json";

    public async Task<MenuRead> ReadAsync(MenuImage image, int pass, CancellationToken ct)
    {
        var prompt = Prompts.For(pass);
        var path = Path.Combine(directory, FileName(image, prompt));
        if (!File.Exists(path))
        {
            return new MenuRead(pass, prompt.Version, "recorded", image.Hash, null, null,
                $"No recorded response for this image under prompt {prompt.Version}.",
                TimeSpan.Zero, null, null, time.GetUtcNow());
        }

        var raw = await File.ReadAllTextAsync(path, ct);
        var reply = GeminiResponse.Parse(raw);
        return new MenuRead(pass, prompt.Version, $"{reply.ModelVersion ?? "unknown"} (recorded)", image.Hash,
            raw, reply.Menu, reply.Error, TimeSpan.Zero, reply.InputTokens, reply.OutputTokens, time.GetUtcNow());
    }
}
