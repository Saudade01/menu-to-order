using MenuToOrder.Extraction;

namespace MenuToOrder.Api.Intake;

public static class PhotoCheck
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public static async Task<(MenuImage? Image, string? Error)> ReadAsync(IFormFile? photo, CancellationToken ct)
    {
        if (photo is null || photo.Length == 0)
            return (null, "Menünün fotoğrafını seçin.");
        if (photo.Length > MaxBytes)
            return (null, "Fotoğraf 10 MB'tan büyük. Telefonun kamerasıyla çekip doğrudan yükleyin.");

        using var buffer = new MemoryStream();
        await photo.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (MediaType(bytes) is { } mediaType)
            return (new MenuImage(bytes, mediaType), null);

        return IsHeic(bytes)
            ? (null, "Bu bir iPhone (HEIC) fotoğrafı. Telefondan doğrudan yükleyin ya da JPEG olarak kaydedin.")
            : (null, "Bu dosya JPEG, PNG ya da WebP bir görsel değil.");
    }

    // Judged by the file's first bytes; the browser-supplied content type can say anything.
    public static string? MediaType(ReadOnlySpan<byte> head) => head switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };

    public static bool IsHeic(ReadOnlySpan<byte> head) =>
        head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8) && (head[8..12].SequenceEqual("heic"u8) || head[8..12].SequenceEqual("heix"u8));
}
