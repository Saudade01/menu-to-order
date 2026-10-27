using System.Text.Json;

namespace MenuToOrder.Extraction;

internal sealed record GeminiReply(ExtractedMenu? Menu, string? Error, string? ModelVersion, int? InputTokens, int? OutputTokens);

public static class GeminiResponse
{
    public const string NoText = "The model returned no text.";

    internal static GeminiReply Parse(string raw)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return new(null, "The response is not JSON.", null, null, null);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var model = String(root, "modelVersion");
            int? input = null, output = null;
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                input = Int(usage, "promptTokenCount");
                // Thinking tokens are billed as output, so they count here.
                output = Int(usage, "candidatesTokenCount") + (Int(usage, "thoughtsTokenCount") ?? 0);
            }

            GeminiReply Fail(string error) => new(null, error, model, input, output);

            if (root.TryGetProperty("error", out var error))
                return Fail(String(error, "message") ?? "The API returned an error.");

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return Fail("No candidates came back; the request was probably blocked.");

            var candidate = candidates[0];
            var finish = String(candidate, "finishReason");
            if (finish != "STOP")
                return Fail($"Generation stopped early ({finish ?? "no reason given"}).");

            // Gemini now and then finishes with STOP and sends no text at all.
            if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
                return Fail(NoText);

            var text = string.Concat(parts.EnumerateArray()
                .Where(p => !(p.TryGetProperty("thought", out var t) && t.GetBoolean()))
                .Select(p => String(p, "text")));

            try
            {
                var menu = JsonSerializer.Deserialize<ExtractedMenu>(text, Json.Options);
                return menu is null ? Fail("The model returned an empty menu.") : new(menu, null, model, input, output);
            }
            catch (JsonException ex)
            {
                return Fail($"The model output does not match the menu schema: {ex.Message}");
            }
        }
    }

    private static string? String(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}
