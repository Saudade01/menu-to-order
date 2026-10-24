using System.Text.Json;
using System.Text.Json.Serialization;

namespace MenuToOrder.Extraction;

public sealed record ExtractedMenu(IReadOnlyList<string> Categories, IReadOnlyList<ExtractedItem> Items);

public sealed record ExtractedItem(
    string Name,
    string? Category,
    string? Description,
    [property: JsonPropertyName("price")] string? PriceText,
    IReadOnlyList<string> Unclear);

public sealed record MenuRead(
    int Pass,
    string PromptVersion,
    string Model,
    string InputHash,
    string? RawResponse,
    ExtractedMenu? Menu,
    string? Error,
    TimeSpan Duration,
    int? InputTokens,
    int? OutputTokens,
    DateTimeOffset StartedAt);

public interface IMenuReader
{
    Task<MenuRead> ReadAsync(MenuImage image, int pass, CancellationToken ct);
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
}
