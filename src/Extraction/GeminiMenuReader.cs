using System.Net.Http.Json;

namespace MenuToOrder.Extraction;

public sealed record GeminiOptions(string ApiKey, string Model = "gemini-3.8-flash");

public sealed class GeminiMenuReader(HttpClient http, GeminiOptions options, TimeProvider time) : IMenuReader
{
    public async Task<MenuRead> ReadAsync(MenuImage image, int pass, CancellationToken ct)
    {
        var prompt = Prompts.For(pass);
        var startedAt = time.GetUtcNow();
        var start = time.GetTimestamp();

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{options.Model}:generateContent")
        {
            Content = JsonContent.Create(Body(image, prompt), options: Json.Options),
        };
        request.Headers.Add("x-goog-api-key", options.ApiKey);

        MenuRead Read(string? raw, GeminiReply reply) => new(
            pass, prompt.Version, reply.ModelVersion ?? options.Model, image.Hash, raw, reply.Menu, reply.Error,
            time.GetElapsedTime(start), reply.InputTokens, reply.OutputTokens, startedAt);

        string raw;
        try
        {
            using var response = await http.SendAsync(request, ct);
            raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var failed = GeminiResponse.Parse(raw);
                return Read(raw, failed with { Menu = null, Error = $"HTTP {(int)response.StatusCode}: {failed.Error}" });
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return Read(null, new GeminiReply(null, $"The request failed: {ex.Message}", null, null, null));
        }

        return Read(raw, GeminiResponse.Parse(raw));
    }

    private static object Body(MenuImage image, Prompt prompt) => new
    {
        contents = new[]
        {
            new
            {
                parts = new object[]
                {
                    new { inlineData = new { mimeType = image.MediaType, data = Convert.ToBase64String(image.Bytes) } },
                    new { text = prompt.Text },
                },
            },
        },
        generationConfig = new { responseMimeType = "application/json", responseSchema = Prompts.Schema },
    };
}
